using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class BearTamerRole : NightRoleIdOnlyHookListener
{
	private static readonly RolePowerDefinition GrowlPower = new(
		new RolePowerIdentifier("bear-tamer-growl"),
		RolePowerCategory.Automatic);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	internal BearTamerRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.BearTamerRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.BearTamer);

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		return session.GetCurrentPhase() switch
		{
			GamePhase.Night when session.TurnNumber == 1 =>
				base.Execute(session, input),
			GamePhase.Dawn => base.Execute(session, input),
			_ => HookListenerActionResult.Skip()
		};
	}

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		if (hook == GameHook.DawnMainActionLoop &&
		    pendingInstruction is ConfirmationInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.AnnounceBearTamerGrowl
		    })
		{
			listenerState = NightRoleIdOnlyState.Awake.ToString();
			return true;
		}

		return base.TryResolvePendingInstructionContinuation(
			hook,
			session,
			pendingInstruction,
			out listenerState);
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages()
	{
		var stages = base.DefineStateMachineStages();
		stages.Add(
			CreateStage(
				GameHook.DawnMainActionLoop,
				startStage: null,
				[
					NightRoleIdOnlyState.Awake,
					NightRoleIdOnlyState.Asleep
				],
				PrepareGrowl));
		stages.Add(
			CreateStage(
				GameHook.DawnMainActionLoop,
				NightRoleIdOnlyState.Awake,
				NightRoleIdOnlyState.Asleep,
				CommitGrowl));
		stages.Add(
			CreateEndStage(
				GameHook.DawnMainActionLoop,
				NightRoleIdOnlyState.Asleep,
				(_, _) => HookListenerActionResult.Complete(
					NightRoleIdOnlyState.Asleep)));
		return stages;
	}

	private HookListenerActionResult PrepareGrowl(
		GameSession session,
		ModeratorResponse input)
	{
		if (GameSessionQueries.HasBearTamerGrowlOccurredThisDawn(session))
		{
			return HookListenerActionResult.Complete(
				NightRoleIdOnlyState.Asleep);
		}

		var holder = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Bear Tamer is available for the Dawn growl.");
		var powerInstance = RolePowerInstance.CreateCurrent(
			session,
			holder,
			MainRoleType.BearTamer,
			GrowlPower);
		var execution = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				holder,
				MainRoleType.BearTamer,
				GrowlPower,
				powerInstance));
		if (!execution.AvailabilityResult.IsAvailable)
		{
			return HookListenerActionResult.Complete(
				NightRoleIdOnlyState.Asleep);
		}

		var werewolfAgentIds = session
			.RequireKnownFactionAgents(Faction.Werewolf)
			.Select(player => player.Id)
			.ToHashSet();
		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			holder.Id);
		var distinctNeighborIds = new[]
			{
				neighbors.Clockwise?.Id,
				neighbors.Counterclockwise?.Id
			}
			.Where(playerId => playerId.HasValue)
			.Select(playerId => playerId!.Value)
			.ToHashSet();
		if (!distinctNeighborIds.Overlaps(werewolfAgentIds))
		{
			return HookListenerActionResult.Complete(
				NightRoleIdOnlyState.Asleep);
		}

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.AnnounceBearTamerGrowl,
				publicAnnouncement: null,
				privateInstruction: GameStrings.BearTamerGrowlInstruction,
				affectedPlayerIds: null,
				soundEffects: [SoundEffectsEnum.BearGrowl]),
			NightRoleIdOnlyState.Awake);
	}

	private static HookListenerActionResult CommitGrowl(
		GameSession session,
		ModeratorResponse input)
	{
		session.CommitGameFact(context =>
			new BearTamerGrowlOccurredLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase
			});
		return HookListenerActionResult.Complete(
			NightRoleIdOnlyState.Asleep);
	}
}
