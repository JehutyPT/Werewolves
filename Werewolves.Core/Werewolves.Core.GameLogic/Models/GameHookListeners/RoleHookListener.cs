using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using static Werewolves.Core.StateModels.Enums.PlayerHealth;

namespace Werewolves.Core.GameLogic.Models.GameHookListeners;

/// <summary>
/// The universal base for all role listeners.
/// Implement this class for roles with stateless or atomic "fire-and-forget" logic that
/// completes in a single transaction without needing to pause for moderator input.
/// </summary>
internal abstract class RoleHookListener : IGameHookListener
{
	internal abstract string PublicName { get; }
	
	public abstract ListenerIdentifier Id { get; }

	public virtual HookListenerActionResult Execute(GameSession session, ModeratorResponse input)
	{
		var roleCount = session.RoleInPlayCount(Id);

		if (roleCount == 0 ||   //if role is not in play, skip
			session.GetPlayers().WithRole(Id).WithHealth(Dead).Count() == roleCount) // if all players with this role are dead, skip
		{
			return HookListenerActionResult.Skip();
		}

		// otherwise, advance the core state machine
		return ExecuteCore(session, input);
	}

	protected abstract HookListenerActionResult ExecuteCore(GameSession session,
		ModeratorResponse input);

	#region MainRole Helper functions

	protected HashSet<IPlayer>? GetAliveRolePlayers(GameSession session) =>
		session.GetPlayers().WithRole(Id).WithHealth(Alive).ToHashSet();

	protected HashSet<Guid> GetPotentialTargets(GameSession session, bool canTargetSelf)
	{
		var potentialTargets = session.GetPlayers()
			.WithHealth(Alive);

		if (canTargetSelf == false)
			potentialTargets = potentialTargets.WithoutRole(Id);

		var hashSet = potentialTargets.ToIdSet();

		if (!hashSet.Any())
		{
			throw new InvalidOperationException($"No valid targets available for {Id} to select.");
		}

		return hashSet;
	}

	#endregion
}


/// <summary>
/// The base for roles requiring a stateful, interactive workflow.
/// The generic parameter T must be a state enum, enabling the listener to pause
/// for moderator input and correctly resume its multi-step state machine.
/// </summary>
/// <typeparam name="TRoleStateEnum"></typeparam>
internal abstract class RoleHookListener<TRoleStateEnum> : RoleHookListener where TRoleStateEnum : struct, Enum
{
	private class RoleStageExecutionKey : IRoleStageExecutionKey { }
	private static readonly RoleStageExecutionKey Key = new();
	//for when the listener starts to respond to a hook and it has no current state
	private Dictionary<GameHook, RoleStateMachineStage> InitialStateMachineStages { get; set; } = new();
	private Dictionary<GameHook, Dictionary<TRoleStateEnum, RoleStateMachineStage>>? StateMachineStagesDictionary { get; set; }

	protected abstract List<RoleStateMachineStage> DefineStateMachineStages();

	protected sealed override HookListenerActionResult ExecuteCore(GameSession session,
		ModeratorResponse input)
	{
		if (StateMachineStagesDictionary == null)
			InitStateMachineStages();

		if (session.TryGetActiveGameHook(out var currentHook) == false)
		{
			throw new InvalidOperationException(
				$"{Id}: Tried to advance role state machine without an active game hook");
		}
		var currentState = GetCurrentListenerState(session);

		RoleStateMachineStage? stageToExecute = null;

		if (currentState != null)
		{
			//we have a current state, so look up the appropriate stage
			if (StateMachineStagesDictionary!.TryGetValue(currentHook, out var stagesForHook) &&
			    stagesForHook.TryGetValue((TRoleStateEnum)currentState, out var stage))
			{
				stageToExecute = stage;
			}
		}
		else
		{
			//no current state, so look for an initial stage for this hook
			if (InitialStateMachineStages.TryGetValue(currentHook, out var initialStage))
			{
				stageToExecute = initialStage;
			}
		}

		if (stageToExecute == null)
		{
			throw new InvalidOperationException(
				$"{Id}: machine state stuck, cannot advance from {currentHook}:{currentState}");
		}

		//execute the stage
		var result = stageToExecute.Execute(Key, session, input, currentState);

		return result;
	}

	private void InitStateMachineStages()
	{
		StateMachineStagesDictionary = new();

		var stages = DefineStateMachineStages();

		foreach (var stage in stages)
		{
			if (stage.StartStage == null)
			{
				if (stage.ShouldOverwriteStartStage == false &&
				    InitialStateMachineStages.ContainsKey(stage.GameHook))
				{
					throw new InvalidOperationException(
						$"{Id}: Illegal overwrite of initial stage handler for {stage.GameHook} game hook");
				}

				InitialStateMachineStages[stage.GameHook] = stage;
			}
			else
			{
				if (StateMachineStagesDictionary.ContainsKey(stage.GameHook) == false)
				{
					StateMachineStagesDictionary[stage.GameHook] = new();
				}

				var startStage = (TRoleStateEnum)stage.StartStage;

				if (stage.ShouldOverwriteStartStage == false &&
				    StateMachineStagesDictionary[stage.GameHook].ContainsKey(startStage))
				{
					throw new InvalidOperationException(
						$"{Id}: Illegal overwrite of {startStage} stage handler");
				}
				

				StateMachineStagesDictionary[stage.GameHook][startStage] = stage;
			}

			
		}
	}

	protected TRoleStateEnum? GetCurrentListenerState(GameSession session)
	{
		return session.GetCurrentListenerState<TRoleStateEnum>(this.Id);
	}

	#region RoleStateMachineStage Wrappers
	internal RoleStateMachineStage CreateStage(
		GameHook gameHook,
		TRoleStateEnum? startStage,
		TRoleStateEnum endStage,
		Func<GameSession, ModeratorResponse, HookListenerActionResult> actionToPerform,
		bool shouldOverwriteStartStage = false
		)
		=> new(
			Id,
			gameHook,
			startStage,
			actionToPerform,
			[endStage],
			shouldOverwriteStartStage
		);

	internal RoleStateMachineStage CreateStage(
		GameHook gameHook,
		TRoleStateEnum? startStage,
		HashSet<TRoleStateEnum> possibleEndStages,
		Func<GameSession, ModeratorResponse, HookListenerActionResult> actionToPerform,
		bool shouldOverwriteStartStage = false
		)
		=> new(
			Id,
			gameHook,
			startStage,
			actionToPerform,
			possibleEndStages,
			shouldOverwriteStartStage
		);

	/// <summary>
	/// Used for stages that are defined at abstract parents, and navigation is left to the devices of the child class.
	/// </summary>
	/// <param name="gameHook"></param>
	/// <param name="startStage"></param>
	/// <param name="actionToPerform"></param>
	/// <param name="shouldOverwriteStartStage"></param>
	/// <returns></returns>
	internal RoleStateMachineStage CreateOpenEndedStage(
		GameHook gameHook,
		TRoleStateEnum? startStage,
		Func<GameSession, ModeratorResponse, HookListenerActionResult> actionToPerform,
		bool shouldOverwriteStartStage = false
		)
		=> new(
			Id,
			gameHook,
			startStage,
			actionToPerform,
			null,
			shouldOverwriteStartStage
		);

	/// <summary>
	/// Same as open ended but explicitly is not meant to advance state.
	/// </summary>
	/// <param name="gameHook"></param>
	/// <param name="startStage"></param>
	/// <param name="actionToPerform"></param>
	/// <param name="shouldOverwriteStartStage"></param>
	/// <returns></returns>
	internal RoleStateMachineStage CreateEndStage(
		GameHook gameHook,
		TRoleStateEnum startStage,
		Func<GameSession, ModeratorResponse, HookListenerActionResult> actionToPerform,
		bool shouldOverwriteStartStage = false
	)
		=> new(
			Id,
			gameHook,
			startStage,
			actionToPerform,
			[startStage],
			shouldOverwriteStartStage,
			ShouldAdvanceState: false
		);

	#endregion

	#region RoleStateMachineStage Definition

	/// <summary>
	/// Used to grant access to the Execute method of RoleStateMachineStage only to RoleHookListener<TRoleStateEnum>
	/// </summary>
	internal interface IRoleStageExecutionKey { }

	/// <summary>
	/// 
	/// </summary>
	internal record RoleStateMachineStage
	{
		// Ensure this is only visible to the class RoleHookListener<TRoleStateEnum>
		internal HookListenerActionResult Execute(IRoleStageExecutionKey key, GameSession session, ModeratorResponse input, TRoleStateEnum? currentState)
		{
			if (currentState?.Equals(StartStage) == false)
			{
				throw new InvalidOperationException(
					$"State Machine Error: MainRole '{MainRoleType}' attempted to execute stage for '{currentState} but " +
					$"executed {StartStage} instead'.");
			}

			var output = ActionToPerform(session, input);
			

			if (output.Outcome != HookListenerOutcome.Skip)
			{
				var newState = output.NextListenerPhase!;

				//if we are in a stage that should advance state, but we didn't, throw
				if (ShouldAdvanceState && currentState != null &&
				    newState.Equals(currentState.ToString()))
				{
					throw new InvalidOperationException(
						$"State Machine Error: MainRole '{MainRoleType}' attempted to remain in state '{newState}' " +
						$"but this stage requires state advancement.");
				}

				//if we didn't error out and we ended up in a state not in PossibleEndStages, throw
				if (PossibleEndStages != null &&
				    PossibleEndStages.Contains(newState) == false)
				{
					var joinedPossibleStateList = string.Join(", ", PossibleEndStages);
					throw new InvalidOperationException(
						$"State Machine Error: MainRole '{MainRoleType}' attempted to transition to invalid state '{newState}'. " +
						$"Valid end states from this stage are: {joinedPossibleStateList}."
					);
				}
			}

			return output;
		}


		/// <summary>
		/// 
		/// </summary>
		/// <param name="MainRoleType"></param>
		/// <param name="GameHook"></param>
		/// <param name="StartStage"></param>
		/// <param name="ActionToPerform">
		/// Whatever happens inside of this function, it must result in the state being set to one of the PossibleEndStages.
		/// </param>
		/// <param name="PossibleEndStages"></param>
		/// <param name="ShouldAdvanceState">
		/// Used for open ended stages, to ensure they at least advanced state.
		/// If false, the stage is allowed to leave the state unchanged.
		/// Otherwise, throw an error if the state is unchanged after execution.
		/// </param>
		/// <param name="ShouldOverwriteStartStage">While false, will throw an exception if it attempts to replace a handler for the same start stage.
		/// Otherwise, will proceed with replacing it</param>
		public RoleStateMachineStage(MainRoleType MainRoleType,
			GameHook GameHook,
			TRoleStateEnum? StartStage,
			Func<GameSession, ModeratorResponse, HookListenerActionResult> ActionToPerform,
			HashSet<TRoleStateEnum>? PossibleEndStages,
			bool ShouldOverwriteStartStage = false,
			bool ShouldAdvanceState = true)
		{
			this.MainRoleType = MainRoleType;
			this.GameHook = GameHook;
			this.StartStage = StartStage;
			this.ActionToPerform = ActionToPerform;
			this.PossibleEndStages = PossibleEndStages?.Select(s => s.ToString()).ToHashSet();
			this.ShouldOverwriteStartStage = ShouldOverwriteStartStage;
		}

		private class HookStateMachineStageKey : IHookSubPhaseKey{}
		private static readonly HookStateMachineStageKey Key = new();

		/// <summary></summary>
		public MainRoleType MainRoleType { get; init; }

		/// <summary></summary>
		public GameHook GameHook { get; init; }

		/// <summary></summary>
		public TRoleStateEnum? StartStage { get; init; }

		/// <summary>
		/// Whatever happens inside of this function, it must result in the state being set to one of the PossibleEndStages.
		/// </summary>
		public Func<GameSession, ModeratorResponse, HookListenerActionResult> ActionToPerform { get; init; }

		/// <summary></summary>
		public HashSet<string>? PossibleEndStages { get; init; }

		/// <summary>
		/// Used for end stages, to indicate that the stage does not advance state.
		/// </summary>
		public bool ShouldAdvanceState { get; init; }

		/// <summary>While false, will throw an exception if it attempts to replace a handler for the same start stage.
		/// Otherwise, will proceed with replacing it</summary>
		public bool ShouldOverwriteStartStage { get; init; }

	}
	#endregion
}
