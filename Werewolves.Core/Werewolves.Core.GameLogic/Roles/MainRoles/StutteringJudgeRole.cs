using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum StutteringJudgeRoleState
{
	Awake,
	AwaitingSignalSetup,
	NightComplete,
	AwaitingVoteConductConfirmation,
	AwaitingSignalObservation,
	DayComplete
}

internal sealed class StutteringJudgeRole
	: RoleHookListener, IDeclaredRoleWorkflow
{
	private sealed record BorrowedExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance);

	private static readonly RolePowerDefinition ConsecutiveVotePower = new(
		new RolePowerIdentifier("stuttering-judge-consecutive-vote"),
		RolePowerCategory.Chosen);

	private static readonly Guid ConsecutiveVoteResourceId =
		Guid.Parse("85ff5eb7-61cf-4b33-894c-b9c37d58bace");

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _nightWorkflowRuntime;
	private readonly RoleWorkflowRuntime _voteWorkflowRuntime;

	internal StutteringJudgeRole(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var identificationWait = RecoverableWait<
				StutteringJudgeRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				StutteringJudgeRoleState.Awake,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateIdentificationInstruction,
				static (_, instruction) =>
					instruction is SelectPlayersInstruction
					{
						Semantic:
							ModeratorInstructionSemantic.IdentifyRoleHolders,
						RoleIdentification: MainRoleType.StutteringJudge
					},
				ValidateIdentificationInstruction,
				static (_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => StutteringJudgeRoleState.Awake);
		var wakeWait = RecoverableWait<
				StutteringJudgeRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				StutteringJudgeRoleState.Awake,
				ModeratorInstructionSemantic.WakeRole,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateWakeInstruction,
				ClaimsWake,
				ValidateWakeInstruction,
				static (_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => StutteringJudgeRoleState.Awake);
		var signalSetupWait = RecoverableWait<
				StutteringJudgeRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				StutteringJudgeRoleState.Awake,
				StutteringJudgeRoleState.AwaitingSignalSetup,
				ModeratorInstructionSemantic.EstablishStutteringJudgeSignal,
				ExpectedInputType.Continue,
				static _ => true,
				AcceptIdentificationIfNeeded,
				CreateSignalSetupInstruction,
				ClaimsSignalSetup,
				ValidateSignalSetupInstruction,
				(session, _, cursor) =>
					ValidateIdentificationHandoff(session, cursor),
				static _ => StutteringJudgeRoleState.AwaitingSignalSetup);
		var borrowedSleepWait = RecoverableWait<
				StutteringJudgeRoleState,
				ConfirmationInstruction>
			.Durable(
				Id,
				GameHook.NightMainActionLoop,
				StutteringJudgeRoleState.AwaitingSignalSetup,
				StutteringJudgeRoleState.NightComplete,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				CommitBorrowedSignalSetup,
				CreateBorrowedSleepInstruction,
				static (_, _) => false,
				ValidateBorrowedSleepInstruction,
				static (_, _, cursor) => ValidateSignalSetupHandoff(cursor),
				static _ => StutteringJudgeRoleState.NightComplete);

		_nightWorkflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				identificationWait,
				wakeWait,
				signalSetupWait,
				borrowedSleepWait,
				new RoleWorkflowDecisionStep<StutteringJudgeRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) => BeginCall(
						session,
						input,
						identificationWait,
						wakeWait)),
				new RoleWorkflowDecisionStep<StutteringJudgeRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					StutteringJudgeRoleState.AwaitingSignalSetup,
					static _ => true,
					(session, input) => RecordSignalSetup(
						session,
						input,
						borrowedSleepWait)),
				new RoleWorkflowCompletionStep<StutteringJudgeRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					StutteringJudgeRoleState.NightComplete,
					StutteringJudgeRoleState.NightComplete,
					static _ => true)
			]);

		var voteConductWait = RecoverableWait<
				StutteringJudgeRoleState,
				ConfirmationInstruction>
			.Replayable(
				Id,
				GameHook.OnVoteConducted,
				startState: null,
				StutteringJudgeRoleState.AwaitingVoteConductConfirmation,
				ModeratorInstructionSemantic.ConductDayVote,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				static _ => CreateVoteConductInstruction(),
				static (_, instruction) => instruction.Semantic ==
					ModeratorInstructionSemantic.ConductDayVote,
				ValidateVoteConductInstruction);
		var signalObservationWait = RecoverableWait<
				StutteringJudgeRoleState,
				SelectOptionsInstruction>
			.Replayable(
				Id,
				GameHook.OnVoteConducted,
				StutteringJudgeRoleState.AwaitingVoteConductConfirmation,
				StutteringJudgeRoleState.AwaitingSignalObservation,
				ModeratorInstructionSemantic.ObserveStutteringJudgeSignal,
				ExpectedInputType.OptionSelection,
				static _ => true,
				static (_, _) => { },
				CreateSignalObservationInstruction,
				static (_, instruction) => instruction.Semantic ==
					ModeratorInstructionSemantic
						.ObserveStutteringJudgeSignal,
				ValidateSignalObservationInstruction);

		_voteWorkflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.OnVoteConducted,
			[
				voteConductWait,
				signalObservationWait,
				new RoleWorkflowDecisionStep<StutteringJudgeRoleState>(
					Id,
					GameHook.OnVoteConducted,
					startState: null,
					static _ => true,
					(session, input) => BeginVote(
						session,
						input,
						voteConductWait)),
				new RoleWorkflowDecisionStep<StutteringJudgeRoleState>(
					Id,
					GameHook.OnVoteConducted,
					StutteringJudgeRoleState.AwaitingSignalObservation,
					static _ => true,
					RecordSignalObservation)
			]);
	}

	internal override string PublicName => GameStrings.StutteringJudgeRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.StutteringJudge);

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_nightWorkflowRuntime;

	RoleWorkflowRuntime? IDeclaredRoleWorkflow.GetWorkflowRuntime(
		GameHook hook) => hook switch
	{
		GameHook.NightMainActionLoop => _nightWorkflowRuntime,
		GameHook.OnVoteConducted => _voteWorkflowRuntime,
		_ => null
	};

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (!session.Execution.TryGetActiveGameHook(out var hook))
		{
			return HookListenerActionResult.Skip();
		}

		if (hook == GameHook.NightMainActionLoop)
		{
			if (TryResolveBorrowedExecution(session, out var borrowedExecution))
			{
				var currentState = GetCurrentListenerState(session);
				if (GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
					    session,
					    CreatePowerIdentity(borrowedExecution)))
				{
					return currentState == StutteringJudgeRoleState.NightComplete
						? ExecuteCore(session, input)
						: HookListenerActionResult.Skip();
				}

				return ExecuteCore(session, input);
			}

			if (session.TurnNumber != 1 ||
			    HasEstablishedSignal(session))
			{
				return HookListenerActionResult.Skip();
			}

			return base.Execute(session, input);
		}

		if (hook == GameHook.OnVoteConducted)
		{
			if (GameSessionQueries.GetCurrentDayVoteOutcome(session) != null)
			{
				return HookListenerActionResult.Skip();
			}

			if (TryResolveBorrowedDayExecution(
					session,
					out var borrowedExecution))
			{
				return GameSessionQueries.HasStutteringJudgeSignalBeenObserved(
							session,
							CreatePowerIdentity(borrowedExecution))
					? HookListenerActionResult.Skip()
					: ExecuteCore(session, input);
			}

			if (!HasEstablishedSignal(session))
			{
				return HookListenerActionResult.Skip();
			}

			return base.Execute(session, input);
		}

		return HookListenerActionResult.Skip();
	}

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input)
	{
		if (!session.Execution.TryGetActiveGameHook(out var hook))
		{
			throw new InvalidOperationException(
				"The Stuttering Judge requires an active Role hook.");
		}

		var currentState = GetCurrentListenerState(session);
		return hook switch
		{
			GameHook.NightMainActionLoop => _nightWorkflowRuntime.Execute(
				session,
				input,
				currentState),
			GameHook.OnVoteConducted => _voteWorkflowRuntime.Execute(
				session,
				input,
				currentState),
			_ => throw new InvalidOperationException(
				$"The Stuttering Judge does not declare the '{hook}' hook.")
		};
	}

	#region Night workflow

	private HookListenerActionResult BeginCall(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<StutteringJudgeRoleState, SelectPlayersInstruction>
			identificationWait,
		RecoverableWait<StutteringJudgeRoleState, ConfirmationInstruction>
			wakeWait) =>
		TryResolveBorrowedExecution(session, out _) ||
		IsCompleteHolderSetKnown(session)
			? wakeWait.Execute(session, input)
			: identificationWait.Execute(session, input);

	private HookListenerActionResult RecordSignalSetup(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<StutteringJudgeRoleState, ConfirmationInstruction>
			borrowedSleepWait)
	{
		if (TryResolveBorrowedExecution(session, out _))
		{
			return borrowedSleepWait.Execute(session, input);
		}

		var judge = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Stuttering Judge is available to complete signal setup.");
		RecordSignalEstablished(session, judge.Id);
		return HookListenerActionResult.Complete(
			StutteringJudgeRoleState.NightComplete);
	}

	private void AcceptIdentificationIfNeeded(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedExecution(session, out _) ||
		    IsCompleteHolderSetKnown(session))
		{
			return;
		}

		IdentifyCompleteLivingRoleHolderSet(
			session,
			input.SelectedPlayerIds?.ToHashSet()
			?? throw new InvalidOperationException(
				"Stuttering Judge identification requires a Player selection."));
	}

	private void CommitBorrowedSignalSetup(
		GameSession session,
		ModeratorResponse input)
	{
		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Stuttering Judge signal setup has no active execution.");
		}

		var powerIdentity = CreatePowerIdentity(execution);
		if (GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
				session,
				powerIdentity))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Stuttering Judge signal is already established for this activation.");
		}

		session.CommitActorBorrowedStutteringJudgeSignalSetup(powerIdentity);
	}

	private SelectPlayersInstruction CreateIdentificationInstruction(
		GameSession session)
	{
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		var committedHolderCount =
			GetCommittedLivingRoleHolderIds(session).Count;
		var selectablePlayerIds = GetIdentificationCandidates(session);
		if (roleCount <= 0 ||
		    committedHolderCount > roleCount ||
		    selectablePlayerIds.Count < roleCount)
		{
			throw new InvalidOperationException(
				"Confirmed Role knowledge contradicts the required Living Role Holder count.");
		}

		var privateInstruction = roleCount == 1
			? GameStrings.RoleSingleIdentificationPrompt.Format(PublicName)
			: GameStrings.RoleMultipleIdentificationPrompt.Format(PublicName);
		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			selectablePlayerIds: selectablePlayerIds,
			countConstraint: NumberRangeConstraint.Exact(roleCount),
			publicAnnouncement: GameStrings.RoleWakesUp.Format(PublicName),
			privateInstruction: privateInstruction,
			affectedPlayerIds: null,
			roleIdentification: MainRoleType.StutteringJudge);
	}

	/// <summary>
	/// A Borrowed Role Power wakes under the Actor's own public name and
	/// audience, so the native and borrowed calls share this one declaration
	/// and never identify or reveal the source Role holder.
	/// </summary>
	private ConfirmationInstruction CreateWakeInstruction(GameSession session)
	{
		if (!TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			return new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName));
		}

		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.WakeRole,
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName),
			affectedPlayerIds: [borrowedExecution.ActingPlayer.Id]);
	}

	private ConfirmationInstruction CreateSignalSetupInstruction(
		GameSession session) =>
		new(
			ModeratorInstructionSemantic.EstablishStutteringJudgeSignal,
			privateInstruction:
				GameStrings.StutteringJudgeSignalSetupInstruction,
			affectedPlayerIds: [ResolveSignalSetupActingPlayer(session).Id]);

	private static ConfirmationInstruction CreateBorrowedSleepInstruction(
		GameSession session)
	{
		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Stuttering Judge sleep has no active execution.");
		}

		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName),
			affectedPlayerIds: [execution.ActingPlayer.Id]);
	}

	private IPlayer ResolveSignalSetupActingPlayer(GameSession session) =>
		TryResolveBorrowedExecution(session, out var execution)
			? execution.ActingPlayer
			: GetAliveRolePlayers(session)?.SingleOrDefault()
			  ?? throw new InvalidOperationException(
				  "No living Stuttering Judge is available for signal setup.");

	private bool ClaimsWake(
		GameSession session,
		ModeratorInstruction instruction)
	{
		if (instruction.Semantic != ModeratorInstructionSemantic.WakeRole)
		{
			return false;
		}

		return TryResolveBorrowedExecution(session, out var borrowedExecution)
			? instruction.AffectedPlayerIds is { Count: 1 } affectedPlayerIds &&
			  affectedPlayerIds.Single() == borrowedExecution.ActingPlayer.Id
			: instruction.AffectedPlayerIds == null;
	}

	private bool ClaimsSignalSetup(
		GameSession session,
		ModeratorInstruction instruction) =>
		instruction.Semantic ==
			ModeratorInstructionSemantic.EstablishStutteringJudgeSignal &&
		instruction.AffectedPlayerIds is { Count: 1 };

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		if (instruction.RoleIdentification != MainRoleType.StutteringJudge ||
		    instruction.AffectedPlayerIds != null ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)) ||
		    instruction.CountConstraint != NumberRangeConstraint.Exact(
			    GetExpectedLivingRoleHolderCount(session)))
		{
			throw new InvalidOperationException(
				"The Stuttering Judge identification instruction has invalid workflow context.");
		}
	}

	private void ValidateWakeInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			if (!StringComparer.Ordinal.Equals(
				    instruction.PublicAnnouncement,
				    GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName)) ||
			    instruction.PrivateInstruction != null ||
			    instruction.AffectedPlayerIds is not [var borrowedAffectedId] ||
			    borrowedAffectedId != borrowedExecution.ActingPlayer.Id)
			{
				throw new RoleWorkflowInputRejectionException(
					GameStrings.ActorBorrowedRolePowerInvalidResponse);
			}

			return;
		}

		if (!StringComparer.Ordinal.Equals(
			    instruction.PublicAnnouncement,
			    GameStrings.RoleWakesUp.Format(PublicName)) ||
		    instruction.PrivateInstruction != null ||
		    instruction.AffectedPlayerIds != null ||
		    GetLivingHolderIds(session).Count == 0)
		{
			throw new InvalidOperationException(
				"The Stuttering Judge wake instruction has invalid workflow context.");
		}
	}

	private void ValidateSignalSetupInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			if (session.Execution.CurrentPhase != GamePhase.Night ||
			    GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
				    session,
				    CreatePowerIdentity(borrowedExecution)) ||
			    instruction.PublicAnnouncement != null ||
			    !StringComparer.Ordinal.Equals(
				    instruction.PrivateInstruction,
				    GameStrings.StutteringJudgeSignalSetupInstruction) ||
			    instruction.AffectedPlayerIds is not [var borrowedAffectedId] ||
			    borrowedAffectedId != borrowedExecution.ActingPlayer.Id)
			{
				throw new RoleWorkflowInputRejectionException(
					GameStrings.ActorBorrowedRolePowerInvalidResponse);
			}

			return;
		}

		if (instruction.PublicAnnouncement != null ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PrivateInstruction,
			    GameStrings.StutteringJudgeSignalSetupInstruction) ||
		    !HasExpectedAffectedRoleHolders(session, instruction))
		{
			throw new InvalidOperationException(
				"The pending Stuttering Judge signal instruction is structurally invalid.");
		}
	}

	private void ValidateBorrowedSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!HasValidBorrowedSignalSetupSleep(session, instruction))
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	private static void ValidateCallHandoff(
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.StutteringJudge)
		{
			throw new InvalidOperationException(
				"The Stuttering Judge call has invalid accepted-observation handoff context.");
		}
	}

	private void ValidateIdentificationHandoff(
		GameSession session,
		AcceptedObservationRecoveryCursor cursor)
	{
		if (TryResolveBorrowedExecution(session, out _))
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}

		ValidateJudgeCursor(
			cursor,
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		var livingHolderIds = GetLivingHolderIds(session);
		if (livingHolderIds.Count == 0 ||
		    !session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Any(
			    entry =>
				    entry.TurnNumber == session.TurnNumber &&
				    entry.CurrentPhase == GamePhase.Night &&
				    entry.Role == MainRoleType.StutteringJudge &&
				    entry.PlayerIds.SetEquals(livingHolderIds)))
		{
			throw new InvalidOperationException(
				"The Stuttering Judge signal setup has no committed identification.");
		}
	}

	private static void ValidateSignalSetupHandoff(
		AcceptedObservationRecoveryCursor cursor) =>
		ValidateJudgeCursor(
			cursor,
			ModeratorInstructionSemantic.EstablishStutteringJudgeSignal);

	private static void ValidateJudgeCursor(
		AcceptedObservationRecoveryCursor cursor,
		ModeratorInstructionSemantic acceptedSemantic)
	{
		if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.AcceptedObservationSemantic != acceptedSemantic ||
		    cursor.ObservedRole != MainRoleType.StutteringJudge ||
		    cursor.ContinuationRole != MainRoleType.StutteringJudge ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The Stuttering Judge continuation cursor has invalid workflow context.");
		}
	}

	#endregion

	#region Vote workflow

	private HookListenerActionResult BeginVote(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<StutteringJudgeRoleState, ConfirmationInstruction>
			voteConductWait) =>
		IsSignalOpportunityAvailable(session)
			? voteConductWait.Execute(session, input)
			: HookListenerActionResult.Complete(
				StutteringJudgeRoleState.DayComplete);

	private static ConfirmationInstruction CreateVoteConductInstruction() =>
		new(
			ModeratorInstructionSemantic.ConductDayVote,
			publicAnnouncement: GameStrings.VoteStartsPublicInstruction,
			privateInstruction: GameStrings.DayVoteConductInstruction);

	private SelectOptionsInstruction CreateSignalObservationInstruction(
		GameSession session)
	{
		var actingPlayer = TryResolveBorrowedDayExecution(
				session,
				out var borrowedExecution)
			? borrowedExecution.ActingPlayer
			: GetAliveRolePlayers(session)?.SingleOrDefault()
			  ?? throw new InvalidOperationException(
				  "No living Stuttering Judge is available for signal observation.");

		return new SelectOptionsInstruction(
			ModeratorInstructionSemantic.ObserveStutteringJudgeSignal,
			[
				new ModeratorOption(
					StutteringJudgeSignalOptionIds.Occurred,
					GameStrings.StutteringJudgeSignalOccurredOption),
				new ModeratorOption(
					StutteringJudgeSignalOptionIds.DidNotOccur,
					GameStrings.StutteringJudgeSignalDidNotOccurOption)
			],
			NumberRangeConstraint.Single,
			privateInstruction:
				GameStrings.StutteringJudgeSignalObservationInstruction,
			affectedPlayerIds: [actingPlayer.Id]);
	}

	private void ValidateVoteConductInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		var execution = session.Execution;
		if (execution.CurrentPhase != GamePhase.Day ||
		    execution.GetSubPhase<DaySubPhases>() !=
			    DaySubPhases.NormalVoting ||
		    GameSessionQueries.GetCurrentDayVoteOutcome(session) != null ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PublicAnnouncement,
			    GameStrings.VoteStartsPublicInstruction) ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PrivateInstruction,
			    GameStrings.DayVoteConductInstruction) ||
		    instruction.AffectedPlayerIds != null)
		{
			throw new InvalidOperationException(
				"The pending Stuttering Judge vote conduct instruction is structurally invalid.");
		}
	}

	private void ValidateSignalObservationInstruction(
		GameSession session,
		SelectOptionsInstruction instruction)
	{
		var execution = session.Execution;
		if (execution.CurrentPhase != GamePhase.Day ||
		    execution.GetSubPhase<DaySubPhases>() !=
			    DaySubPhases.NormalVoting ||
		    GameSessionQueries.GetCurrentDayVoteOutcome(session) != null ||
		    instruction.SelectionRange != NumberRangeConstraint.Single ||
		    !instruction.Options
			    .Select(option => option.Id)
			    .SequenceEqual(
				    [
					    StutteringJudgeSignalOptionIds.Occurred,
					    StutteringJudgeSignalOptionIds.DidNotOccur
				    ],
				    StringComparer.Ordinal))
		{
			throw new InvalidOperationException(
				"The pending Stuttering Judge signal instruction is structurally invalid.");
		}

		IPlayer signalObserver;
		if (session.GetModeratorActiveActorBorrowedRolePowerActivation()
			    ?.SourceRole == MainRoleType.StutteringJudge)
		{
			if (!TryResolveBorrowedDayExecution(
				    session,
				    out var borrowedExecution))
			{
				throw new InvalidOperationException(
					"The pending Actor borrowed Stuttering Judge signal instruction is stale.");
			}

			signalObserver = borrowedExecution.ActingPlayer;
		}
		else
		{
			var nativeObserver =
				GetAliveRolePlayers(session)?.SingleOrDefault();
			if (nativeObserver is null ||
			    !GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
				    session,
				    nativeObserver.Id))
			{
				throw new InvalidOperationException(
					"The pending Stuttering Judge signal instruction has no valid execution.");
			}

			signalObserver = nativeObserver;
		}

		if (instruction.PublicAnnouncement != null ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PrivateInstruction,
			    GameStrings.StutteringJudgeSignalObservationInstruction) ||
		    instruction.AffectedPlayerIds is not [var affectedPlayerId] ||
		    affectedPlayerId != signalObserver.Id)
		{
			throw new InvalidOperationException(
				"The pending Stuttering Judge signal instruction is structurally invalid.");
		}
	}

	private HookListenerActionResult RecordSignalObservation(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedDayExecution(session, out var borrowedExecution))
		{
			if (!IsSignalOpportunityAvailable(session, borrowedExecution))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Stuttering Judge signal opportunity is no longer available.");
			}

			var borrowedOptionId = GetSignalObservationOption(input);
			var signalOccurred = StringComparer.Ordinal.Equals(
				borrowedOptionId,
				StutteringJudgeSignalOptionIds.Occurred);
			session.CommitActorBorrowedStutteringJudgeSignalObservation(
				CreatePowerIdentity(borrowedExecution),
				signalOccurred,
				signalOccurred
					? CreateResourceIdentity(borrowedExecution)
					: null);
			return HookListenerActionResult.Complete(
				StutteringJudgeRoleState.DayComplete);
		}

		var judge = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Stuttering Judge is available for signal observation.");
		if (!IsSignalOpportunityAvailable(session, judge))
		{
			throw new InvalidOperationException(
				"The Stuttering Judge signal opportunity is no longer available.");
		}

		var selectedOptionId = GetSignalObservationOption(input);
		if (StringComparer.Ordinal.Equals(
			    selectedOptionId,
			    StutteringJudgeSignalOptionIds.DidNotOccur))
		{
			RecordSignalDidNotOccur(session, judge.Id);
			return HookListenerActionResult.Complete(
				StutteringJudgeRoleState.DayComplete);
		}

		if (!StringComparer.Ordinal.Equals(
			    selectedOptionId,
			    StutteringJudgeSignalOptionIds.Occurred))
		{
			throw new InvalidOperationException(
				"The Stuttering Judge signal option is unknown.");
		}

		DayVoteRules.CommitOneUseDayAction(
			session,
			DayPowerType.JudgeExtraVote,
			CreateResourceIdentity(session, judge));
		return HookListenerActionResult.Complete(
			StutteringJudgeRoleState.DayComplete);
	}

	#endregion

	#region Helpers

	private StutteringJudgeRoleState? GetCurrentListenerState(
		GameSession session) =>
		session.Execution.GetCurrentListenerState<StutteringJudgeRoleState>(Id);

	private bool IsCompleteHolderSetKnown(GameSession session) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			MainRoleType.StutteringJudge);

	private HashSet<Guid> GetLivingHolderIds(GameSession session) =>
		GetAliveRolePlayers(session)?.Select(player => player.Id).ToHashSet()
		?? [];

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.StutteringJudge ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == null ||
				  player.State.ModeratorKnownRole ==
					  MainRoleType.StutteringJudge)))
			.ToIdSet();

	private bool HasExpectedAffectedRoleHolders(
		GameSession session,
		ModeratorInstruction instruction)
	{
		var holders = GetLivingHolderIds(session);
		return holders.Count > 0 &&
		       instruction.AffectedPlayerIds is { } affectedPlayerIds &&
		       affectedPlayerIds.ToHashSet().SetEquals(holders);
	}

	private static string GetSignalObservationOption(ModeratorResponse input)
	{
		var selectedOptionId = input.SelectedOptionIds?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"Stuttering Judge signal observation requires one semantic option.");
		if (!StringComparer.Ordinal.Equals(
				selectedOptionId,
				StutteringJudgeSignalOptionIds.Occurred) &&
			!StringComparer.Ordinal.Equals(
				selectedOptionId,
				StutteringJudgeSignalOptionIds.DidNotOccur))
		{
			throw new InvalidOperationException(
				"The Stuttering Judge signal option is unknown.");
		}

		return selectedOptionId;
	}

	private bool IsSignalOpportunityAvailable(GameSession session)
	{
		if (TryResolveBorrowedDayExecution(session, out var borrowedExecution))
		{
			return IsSignalOpportunityAvailable(session, borrowedExecution);
		}

		var judge = GetAliveRolePlayers(session)?.SingleOrDefault();
		return judge != null && IsSignalOpportunityAvailable(session, judge);
	}

	private bool IsSignalOpportunityAvailable(
		GameSession session,
		IPlayer judge)
	{
		var instance = RolePowerInstance.CreateCurrent(
			session,
			judge,
			MainRoleType.StutteringJudge,
			ConsecutiveVotePower);
		if (GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			    session,
			    CreateResourceIdentity(judge, instance)))
		{
			return false;
		}
		return _availabilityGateway.Evaluate(
				new RolePowerAttempt(
					session,
					judge,
					MainRoleType.StutteringJudge,
					ConsecutiveVotePower,
					instance,
					new OneUseRolePowerResource(
						ConsecutiveVoteResourceId,
						instance)))
			.AvailabilityResult.IsAvailable;
	}

	private bool IsSignalOpportunityAvailable(
		GameSession session,
		BorrowedExecutionContext execution)
	{
		var powerIdentity = CreatePowerIdentity(execution);
		var resourceIdentity = CreateResourceIdentity(execution);
		if (GameSessionQueries.HasStutteringJudgeSignalBeenObserved(
				session,
				powerIdentity) ||
			GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				session,
				resourceIdentity))
		{
			return false;
		}

		return _availabilityGateway.Evaluate(
				new RolePowerAttempt(
					session,
					execution.ActingPlayer,
					MainRoleType.StutteringJudge,
					ConsecutiveVotePower,
					execution.PowerInstance,
					new OneUseRolePowerResource(
						ConsecutiveVoteResourceId,
						execution.PowerInstance)))
			.AvailabilityResult.IsAvailable;
	}

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		GameSession session,
		IPlayer judge)
	{
		var instance = RolePowerInstance.CreateCurrent(
			session,
			judge,
			MainRoleType.StutteringJudge,
			ConsecutiveVotePower);
		return CreateResourceIdentity(judge, instance);
	}

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		IPlayer judge,
		RolePowerInstance instance) => new(
		judge.Id,
		MainRoleType.StutteringJudge,
		ConsecutiveVotePower.Identifier.Value,
		instance.Id,
		instance.Origin,
		ConsecutiveVoteResourceId);

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		BorrowedExecutionContext execution) =>
		CreateResourceIdentity(execution.ActingPlayer, execution.PowerInstance);

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out BorrowedExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.StutteringJudge)
		{
			execution = null!;
			return false;
		}

		var actor = session.GetPlayer(activation.ActingPlayerId);
		execution = new BorrowedExecutionContext(
			actor,
			RolePowerInstance.CreateBorrowed(
				session,
				actor,
				MainRoleType.StutteringJudge,
				ConsecutiveVotePower));
		return true;
	}

	private static bool TryResolveBorrowedDayExecution(
		GameSession session,
		out BorrowedExecutionContext execution)
	{
		execution = null!;
		return session.Execution.CurrentPhase == GamePhase.Day &&
			TryResolveBorrowedExecution(session, out execution) &&
			GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
				session,
				CreatePowerIdentity(execution));
	}

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		BorrowedExecutionContext execution) => new(
			execution.ActingPlayer.Id,
			MainRoleType.StutteringJudge,
			ConsecutiveVotePower.Identifier.Value,
			execution.PowerInstance.Id,
			execution.PowerInstance.Origin);

	private static void RecordSignalEstablished(
		GameSession session,
		Guid judgePlayerId)
	{
		EnsureJudgePlayerId(judgePlayerId);
		session.CommitGameFact(context =>
			new StutteringJudgeSignalEstablishedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				JudgePlayerId = judgePlayerId
			});
	}

	private static void RecordSignalDidNotOccur(
		GameSession session,
		Guid judgePlayerId)
	{
		EnsureJudgePlayerId(judgePlayerId);
		session.CommitGameFact(context =>
			new StutteringJudgeSignalDidNotOccurLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				JudgePlayerId = judgePlayerId
			});
	}

	private static void EnsureJudgePlayerId(Guid judgePlayerId)
	{
		if (judgePlayerId == Guid.Empty)
		{
			throw new ArgumentException(
				"The Stuttering Judge holder identity is required.",
				nameof(judgePlayerId));
		}
	}

	private bool HasEstablishedSignal(GameSession session)
	{
		var judge = GetAliveRolePlayers(session)?.SingleOrDefault();
		return judge is not null &&
		       GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
			       session,
			       judge.Id);
	}

	/// <summary>
	/// Authenticates the native committed signal for the central
	/// accepted-observation contract, which owns the cross-listener handoff the
	/// Judge's own declared workflow never resolves.
	/// </summary>
	internal static bool HasValidEstablishedSignal(GameSession session)
	{
		var judges = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.State.CurrentRole == MainRoleType.StutteringJudge)
			.ToArray();
		var entries = session.GameHistoryLog
			.OfType<StutteringJudgeSignalEstablishedLogEntry>()
			.ToArray();
		return judges is [var judge] &&
		       entries is [var entry] &&
		       entry.JudgePlayerId == judge.Id;
	}

	private static bool HasValidBorrowedSignalSetupSleep(
		GameSession session,
		ModeratorInstruction? pendingInstruction)
	{
		var expectedPublicAnnouncement =
			GameStrings.RoleGoesToSleepSingle.Format(
				GameStrings.ActorRoleName);
		if (pendingInstruction is not ConfirmationInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.PutRoleToSleep,
			    PublicAnnouncement: var publicAnnouncement,
			    PrivateInstruction: null,
			    AffectedPlayerIds: [var affectedPlayerId]
		    } ||
		    !StringComparer.Ordinal.Equals(
			    publicAnnouncement,
			    expectedPublicAnnouncement))
		{
			return false;
		}

		if (!TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			return false;
		}

		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		var powerIdentity = CreatePowerIdentity(borrowedExecution);
		var matchingSetups = session
			.GetActorBorrowedStutteringJudgeSignalSetupCommits()
			.Where(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.ActorSetupCardId == activation.SelectedCardId)
			.ToArray();
		return matchingSetups is [var setup] &&
		       setup.TurnNumber == session.TurnNumber &&
		       setup.CurrentPhase == GamePhase.Night &&
		       affectedPlayerId == borrowedExecution.ActingPlayer.Id;
	}

	#endregion
}
