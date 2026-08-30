using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum SeerRoleState
{
	Awake,
	AwaitingTargetSelection,
	AwaitingFeedbackAcknowledgement,
	ReadyToSleep,
	Asleep
}

/// <summary>
/// Seer role implementation using declared recoverable waits.
/// </summary>
internal sealed class SeerRole
	: RoleHookListener,
		IDeclaredRoleWorkflow
{
	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		bool IsBorrowed);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _workflowRuntime;

	private static readonly RolePowerDefinition WerewolfDetectionPower = new(
		new RolePowerIdentifier("seer-werewolf-detection"),
		RolePowerCategory.Chosen);

	internal SeerRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var identificationWait = RecoverableWait<
				SeerRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				SeerRoleState.Awake,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateIdentificationInstruction,
				static (_, instruction) =>
					instruction is SelectPlayersInstruction
					{
						RoleIdentification: MainRoleType.Seer
					},
				ValidateIdentificationInstruction,
				(_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => SeerRoleState.Awake);
		var wakeWait = RecoverableWait<
				SeerRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				SeerRoleState.Awake,
				ModeratorInstructionSemantic.WakeRole,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateWakeInstruction,
				ClaimsWakeRecoveryCandidate,
				ValidateWakeInstruction,
				(_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => SeerRoleState.Awake);
		var targetSelectionWait = RecoverableWait<
				SeerRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				SeerRoleState.Awake,
				SeerRoleState.AwaitingTargetSelection,
				ModeratorInstructionSemantic.SelectSeerTarget,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateTargetSelectionInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.SelectSeerTarget &&
					HasExpectedAffectedActingPlayer(session, instruction),
				ValidateTargetSelectionInstruction,
				(session, _, cursor) =>
					ValidateIdentificationHandoff(session, cursor),
				static _ => SeerRoleState.AwaitingTargetSelection);
		var unavailableSleepWait = RecoverableWait<
				SeerRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				SeerRoleState.Awake,
				SeerRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateUnavailableSleepInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.PutRoleToSleep &&
					CountCheckCommitsThisNight(session, 2) == 0 &&
					HasExpectedAffectedActingPlayer(session, instruction),
				ValidateUnavailableSleepInstruction,
				(session, _, cursor) =>
					ValidateIdentificationHandoff(session, cursor),
				static _ => SeerRoleState.ReadyToSleep);
		var feedbackWait = RecoverableWait<
				SeerRoleState,
				ConfirmationInstruction>
			.DomainDurable(
				Id,
				GameHook.NightMainActionLoop,
				SeerRoleState.AwaitingTargetSelection,
				SeerRoleState.AwaitingFeedbackAcknowledgement,
				ModeratorInstructionSemantic.RevealSeerResult,
				ExpectedInputType.Continue,
				static _ => false,
				CommitTargetSelection,
				CreateFeedbackInstruction,
				ClaimsCommittedFeedback,
				ValidateFeedbackInstruction,
				ValidateFeedbackRecoveryContext,
				static _ => SeerRoleState.AwaitingFeedbackAcknowledgement,
				ValidateCommittedRecoveryBoundary);
		var feedbackSleepWait = RecoverableWait<
				SeerRoleState,
				ConfirmationInstruction>
			.Replayable(
				Id,
				GameHook.NightMainActionLoop,
				SeerRoleState.AwaitingFeedbackAcknowledgement,
				SeerRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => true,
				static (_, _) => { },
				CreateFeedbackSleepInstruction,
				static (_, _) => false,
				ValidateFeedbackSleepInstruction);

		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				identificationWait,
				wakeWait,
				targetSelectionWait,
				unavailableSleepWait,
				feedbackWait,
				feedbackSleepWait,
				new RoleWorkflowDecisionStep<SeerRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) => BeginCall(
						session,
						input,
						identificationWait,
						wakeWait)),
				new RoleWorkflowDecisionStep<SeerRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					SeerRoleState.Awake,
					static _ => true,
					(session, input) => PrepareNightPower(
						session,
						input,
						targetSelectionWait,
						unavailableSleepWait)),
				new RoleWorkflowDecisionStep<SeerRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					SeerRoleState.AwaitingTargetSelection,
					static _ => true,
					(session, input) =>
						feedbackWait.Execute(session, input)),
				new RoleWorkflowCompletionStep<SeerRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					SeerRoleState.ReadyToSleep,
					SeerRoleState.Asleep,
					static _ => true),
				new RoleWorkflowCompletionStep<SeerRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					SeerRoleState.Asleep,
					SeerRoleState.Asleep,
					static _ => true)
			]);
	}

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Seer);

	internal override string PublicName => GameStrings.SeerRoleName;

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_workflowRuntime;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedExecution(session, out _))
		{
			return ExecuteCore(session, input);
		}

		return base.Execute(session, input);
	}

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		_workflowRuntime.Execute(
			session,
			input,
			session.Execution.GetCurrentListenerState<SeerRoleState>(Id));

	private bool ValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		TargetPrivateRolePowerRecoveryBoundary committedBoundary,
		ConfirmationInstruction nextInstruction)
	{
		if (committedBoundary.ActionType != NightActionType.SeerCheck)
		{
			return false;
		}

		var execution = ResolveBorrowedExecution(session);
		var commit = GetBorrowedCommit(session, execution);
		ValidateBorrowedBoundary(session, execution, commit, committedBoundary);
		if (startingInstruction is not SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.SelectSeerTarget
			} selection ||
			input.InstructionId != selection.InstructionId ||
			input.Type != ExpectedInputType.PlayerSelection ||
			input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds ||
			selectedPlayerIds.Single() != commit.TargetPlayerId ||
			!selection.SelectablePlayerIds.Contains(commit.TargetPlayerId) ||
			nextInstruction.Semantic !=
				ModeratorInstructionSemantic.RevealSeerResult)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Seer commit does not match its accepted target and feedback continuation.");
		}

		ValidateBorrowedSelectionInstruction(session, execution, selection);
		ValidateBorrowedFeedback(session, execution, commit, nextInstruction);
		return true;
	}

	private void ValidateFeedbackRecoveryContext(
		GameSession session,
		ConfirmationInstruction instruction,
		DomainRecoveryCursor cursor)
	{
		ArgumentNullException.ThrowIfNull(cursor);
		var execution = ResolveBorrowedExecution(session);
		var commit = GetBorrowedCommit(session, execution);
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		if (cursor.Kind !=
				DomainRecoveryCursorKind.TargetPrivateRolePowerCommit ||
			cursor.SourceRole != MainRoleType.Seer ||
			cursor.CommittedActionType != NightActionType.SeerCheck ||
			!StringComparer.Ordinal.Equals(
				cursor.SourcePowerIdentifier,
				WerewolfDetectionPower.Identifier.Value) ||
			cursor.PowerIdentity != CreatePowerIdentity(execution) ||
			cursor.OneUseResourceId != Guid.Empty ||
			cursor.ActorSetupCardId != activation.SelectedCardId ||
			cursor.ActorBorrowedActivationId != activation.ActivationId ||
			cursor.CommittedTargetIds is not { Count: 1 } targetIds ||
			targetIds.Single() != commit.TargetPlayerId ||
			cursor.NextInstructionSemantic !=
				ModeratorInstructionSemantic.RevealSeerResult)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Seer recovery cursor has an invalid target-private Role Power identity.");
		}

		ValidateBorrowedFeedback(session, execution, commit, instruction);
	}

	private HookListenerActionResult BeginCall(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<SeerRoleState, SelectPlayersInstruction>
			identificationWait,
		RecoverableWait<SeerRoleState, ConfirmationInstruction> wakeWait) =>
		!TryResolveBorrowedExecution(session, out _) &&
		!IsCompleteHolderSetKnown(session)
			? identificationWait.Execute(session, input)
			: wakeWait.Execute(session, input);

	private HookListenerActionResult PrepareNightPower(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<SeerRoleState, SelectPlayersInstruction>
			targetSelectionWait,
		RecoverableWait<SeerRoleState, ConfirmationInstruction> sleepWait)
	{
		if (!TryResolveBorrowedExecution(session, out _) &&
		    !IsCompleteHolderSetKnown(session))
		{
			IdentifyCompleteLivingRoleHolderSet(
				session,
				input.SelectedPlayerIds?.ToHashSet()
				?? throw new InvalidOperationException(
					"Seer identification requires a Player selection."));
		}

		var execution = ResolveExecution(session);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				execution.ActingPlayer,
				MainRoleType.Seer,
				WerewolfDetectionPower,
				execution.PowerInstance));
		if (!availability.AvailabilityResult.IsAvailable)
		{
			return sleepWait.Execute(session, input);
		}

		return execution.IsBorrowed &&
		       GetBorrowedPotentialTargets(
			       session,
			       execution.ActingPlayer.Id).Count == 0
			? sleepWait.Execute(session, input)
			: targetSelectionWait.Execute(session, input);
	}

	private void CommitTargetSelection(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = ResolveExecution(session);
		if (!execution.IsBorrowed)
		{
			session.PerformNightAction(
				NightActionType.SeerCheck,
				input.SelectedPlayerIds!.First());
			return;
		}

		if (input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds)
		{
			throw new InvalidOperationException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}

		var targetId = selectedPlayerIds.Single();
		if (!GetBorrowedPotentialTargets(session, execution.ActingPlayer.Id)
			    .Contains(targetId))
		{
			throw new InvalidOperationException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}

		var targetKnowledge = session.GetFactionAgentKnowledge(
			targetId,
			Faction.Werewolf);
		if (targetKnowledge is not
		    (FactionAgentKnowledge.KnownAgent or
		     FactionAgentKnowledge.KnownNonAgent))
		{
			throw new InvalidOperationException(
				"The current Werewolf Faction Agent fact is incomplete.");
		}

		session.CommitActorBorrowedSeerCheck(
			CreatePowerIdentity(execution),
			targetId,
			targetKnowledge);
	}

	private SelectPlayersInstruction CreateIdentificationInstruction(
		GameSession session)
	{
		var selectablePlayerIds = GetIdentificationCandidates(session);
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		var committedLivingRoleHolderCount =
			GetCommittedLivingRoleHolderIds(session).Count;
		if (roleCount <= 0 ||
		    committedLivingRoleHolderCount > roleCount ||
		    selectablePlayerIds.Count < roleCount)
		{
			throw new InvalidOperationException(
				"Confirmed Role knowledge contradicts the required Living Role Holder count.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			selectablePlayerIds: selectablePlayerIds,
			countConstraint: NumberRangeConstraint.Exact(roleCount),
			publicAnnouncement: GameStrings.RoleWakesUp.Format(PublicName),
			privateInstruction: roleCount == 1
				? GameStrings.RoleSingleIdentificationPrompt.Format(PublicName)
				: GameStrings.RoleMultipleIdentificationPrompt.Format(
					PublicName),
			affectedPlayerIds: null,
			roleIdentification: MainRoleType.Seer);
	}

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

	private SelectPlayersInstruction CreateTargetSelectionInstruction(
		GameSession session)
	{
		var execution = ResolveExecution(session);
		if (execution.IsBorrowed)
		{
			return new SelectPlayersInstruction(
				ModeratorInstructionSemantic.SelectSeerTarget,
				publicAnnouncement:
					GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName),
				countConstraint: NumberRangeConstraint.Single,
				selectablePlayerIds: GetBorrowedPotentialTargets(
					session,
					execution.ActingPlayer.Id),
				privateInstruction: GameStrings.SeerNightActionPrompt,
				affectedPlayerIds: [execution.ActingPlayer.Id]);
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectSeerTarget,
			publicAnnouncement: GameStrings.SeerNightActionPrompt,
			countConstraint: NumberRangeConstraint.Single,
			selectablePlayerIds: GetPotentialTargets(session, false),
			affectedPlayerIds: [execution.ActingPlayer.Id]);
	}

	private ConfirmationInstruction CreateFeedbackInstruction(
		GameSession session)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			var commit = GetBorrowedCommit(session, borrowedExecution);
			return new ConfirmationInstruction(
				ModeratorInstructionSemantic.RevealSeerResult,
				privateInstruction: FormatSeerFeedback(
					session.GetPlayer(commit.TargetPlayerId).Name,
					commit.TargetAgentKnowledge),
				affectedPlayerIds: [borrowedExecution.ActingPlayer.Id]);
		}

		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.RevealSeerResult,
			privateInstruction: FormatSeerFeedback(
				session,
				GetCommittedNativeCheckTargetId(session)));
	}

	private ConfirmationInstruction CreateUnavailableSleepInstruction(
		GameSession session)
	{
		var execution = ResolveExecution(session);
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleGoesToSleepSingle.Format(
				execution.IsBorrowed
					? GameStrings.ActorRoleName
					: PublicName),
			affectedPlayerIds: [execution.ActingPlayer.Id]);
	}

	private ConfirmationInstruction CreateFeedbackSleepInstruction(
		GameSession session)
	{
		if (!TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			return new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(PublicName));
		}

		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleGoesToSleepSingle.Format(
				GameStrings.ActorRoleName),
			affectedPlayerIds: [borrowedExecution.ActingPlayer.Id]);
	}

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		if (TryResolveBorrowedExecution(session, out _) ||
		    instruction.RoleIdentification != MainRoleType.Seer ||
		    instruction.AffectedPlayerIds != null ||
		    roleCount <= 0 ||
		    instruction.CountConstraint !=
			    NumberRangeConstraint.Exact(roleCount) ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)))
		{
			throw new InvalidOperationException(
				"The Seer identification instruction has invalid workflow context.");
		}
	}

	private bool ClaimsWakeRecoveryCandidate(
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

	private void ValidateWakeInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			ValidateBorrowedWake(borrowedExecution, instruction);
			return;
		}

		if (instruction.PublicAnnouncement !=
			    GameStrings.RoleWakesUp.Format(PublicName) ||
		    instruction.PrivateInstruction != null ||
		    instruction.AffectedPlayerIds != null ||
		    GetLivingHolderIds(session).Count == 0)
		{
			throw new InvalidOperationException(
				"The Seer wake instruction has invalid workflow context.");
		}
	}

	private void ValidateTargetSelectionInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var execution = ResolveExecution(session);
		if (execution.IsBorrowed)
		{
			ValidateBorrowedSelectionInstruction(
				session,
				execution,
				instruction);
			return;
		}

		if (instruction.RoleIdentification != null ||
		    instruction.PublicAnnouncement !=
			    GameStrings.SeerNightActionPrompt ||
		    instruction.PrivateInstruction != null ||
		    instruction.CountConstraint != NumberRangeConstraint.Single ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetPotentialTargets(session, false)) ||
		    !HasExpectedAffectedActingPlayer(session, instruction))
		{
			throw new InvalidOperationException(
				"The Seer target selection has invalid workflow context.");
		}
	}

	private bool ClaimsCommittedFeedback(
		GameSession session,
		ModeratorInstruction instruction) =>
		instruction.Semantic ==
		ModeratorInstructionSemantic.RevealSeerResult &&
		TryResolveBorrowedExecution(session, out _);

	private void ValidateFeedbackInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			ValidateBorrowedFeedback(
				session,
				borrowedExecution,
				GetBorrowedCommit(session, borrowedExecution),
				instruction);
			return;
		}

		if (instruction.PublicAnnouncement != null ||
		    instruction.AffectedPlayerIds != null ||
		    instruction.PrivateInstruction != FormatSeerFeedback(
			    session,
			    GetCommittedNativeCheckTargetId(session)))
		{
			throw new InvalidOperationException(
				"The Seer feedback does not match its committed check.");
		}
	}

	private void ValidateUnavailableSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		var execution = ResolveExecution(session);
		if (execution.IsBorrowed)
		{
			ValidateBorrowedSleep(execution, instruction);
		}
		else if (instruction.PublicAnnouncement !=
				 GameStrings.RoleGoesToSleepSingle.Format(PublicName) ||
		         instruction.PrivateInstruction != null ||
		         !HasExpectedAffectedActingPlayer(session, instruction))
		{
			throw new InvalidOperationException(
				"The Seer sleep instruction has invalid workflow context.");
		}

		if (CountCheckCommitsThisNight(session, 1) != 0)
		{
			throw new InvalidOperationException(
				"The Seer unavailable sleep has invalid workflow context.");
		}
	}

	private void ValidateFeedbackSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			ValidateBorrowedSleep(borrowedExecution, instruction);
		}
		else if (instruction.PublicAnnouncement !=
				 GameStrings.RoleGoesToSleepSingle.Format(PublicName) ||
		         instruction.PrivateInstruction != null ||
		         instruction.AffectedPlayerIds != null)
		{
			throw new InvalidOperationException(
				"The Seer sleep instruction has invalid workflow context.");
		}

		if (CountCheckCommitsThisNight(session, 2) != 1)
		{
			throw new InvalidOperationException(
				"The Seer feedback sleep requires its committed check.");
		}
	}

	private static void ValidateCallHandoff(
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.Seer)
		{
			throw new InvalidOperationException(
				"The Seer call has invalid accepted-observation handoff context.");
		}
	}

	private void ValidateIdentificationHandoff(
		GameSession session,
		AcceptedObservationRecoveryCursor cursor)
	{
		if (TryResolveBorrowedExecution(session, out _) ||
		    cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.Seer ||
		    cursor.ObservedRole != MainRoleType.Seer ||
		    cursor.AcceptedObservationSemantic !=
			    ModeratorInstructionSemantic.IdentifyRoleHolders ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The Seer continuation has invalid accepted-observation handoff context.");
		}

		var livingHolderIds = GetLivingHolderIds(session);
		if (livingHolderIds.Count == 0 ||
		    !RoleFactionKnowledge.HasAcceptedRoleIdentification(
			    session,
			    MainRoleType.Seer))
		{
			throw new InvalidOperationException(
				"The Seer identification continuation has invalid durable context.");
		}
	}

	private bool IsCompleteHolderSetKnown(GameSession session) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			MainRoleType.Seer);

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.Seer ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == MainRoleType.Seer ||
				  player.State.ModeratorKnownRole == null &&
				  RoleFactionKnowledge.GetPossibleRoles(session, player.Id)
					  .Contains(MainRoleType.Seer))))
			.ToIdSet();

	private HashSet<Guid> GetLivingHolderIds(GameSession session) =>
		GetAliveRolePlayers(session)?.Select(player => player.Id).ToHashSet()
		?? [];

	private bool HasExpectedAffectedActingPlayer(
		GameSession session,
		ModeratorInstruction instruction)
	{
		if (instruction.AffectedPlayerIds is not { Count: 1 } affectedPlayerIds)
		{
			return false;
		}

		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			return affectedPlayerIds.Single() ==
			       borrowedExecution.ActingPlayer.Id;
		}

		var livingHolderIds = GetLivingHolderIds(session);
		return livingHolderIds.Count > 0 &&
		       livingHolderIds.SetEquals(affectedPlayerIds);
	}

	private int CountCheckCommitsThisNight(GameSession session, int limit) =>
		TryResolveBorrowedExecution(session, out var borrowedExecution)
			? GetBorrowedCommitsThisNight(session, borrowedExecution)
				.Take(limit)
				.Count()
			: GameSessionQueries
				.GetOrderedNightActionsThisNight(
					session,
					[NightActionType.SeerCheck])
				.Take(limit)
				.Count();

	private static Guid GetCommittedNativeCheckTargetId(GameSession session)
	{
		var commits = GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				[NightActionType.SeerCheck])
			.ToArray();
		if (commits is not [{ TargetIds: [var targetId] }])
		{
			throw new InvalidOperationException(
				"The Seer feedback requires exactly one committed check.");
		}

		return targetId;
	}

	private ExecutionContext ResolveExecution(GameSession session) =>
		TryResolveBorrowedExecution(session, out var borrowed)
			? borrowed
			: ResolveNativeExecution(session);

	private ExecutionContext ResolveNativeExecution(GameSession session)
	{
		var seer = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No alive Seer found for Role Power availability.");
		return new ExecutionContext(
			seer,
			RolePowerInstance.CreateCurrent(
				session,
				seer,
				MainRoleType.Seer,
				WerewolfDetectionPower),
			IsBorrowed: false);
	}

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.Seer)
		{
			execution = null!;
			return false;
		}

		execution = ResolveBorrowedExecution(session);
		return true;
	}

	private static ExecutionContext ResolveBorrowedExecution(
		GameSession session)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.Seer)
		{
			throw new InvalidOperationException(
				"No active Actor borrowed Seer Role Power is available.");
		}

		var actor = session.GetPlayer(activation.ActingPlayerId);
		return new ExecutionContext(
			actor,
			RolePowerInstance.CreateBorrowed(
				session,
				actor,
				MainRoleType.Seer,
				WerewolfDetectionPower),
			IsBorrowed: true);
	}

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		ExecutionContext execution) => new(
		execution.ActingPlayer.Id,
		MainRoleType.Seer,
		WerewolfDetectionPower.Identifier.Value,
		execution.PowerInstance.Id,
		execution.PowerInstance.Origin);

	private static HashSet<Guid> GetBorrowedPotentialTargets(
		GameSession session,
		Guid actorId) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player => player.Id != actorId)
			.ToIdSet();

	private static IEnumerable<ActorBorrowedSeerCheckCommit>
		GetBorrowedCommitsThisNight(
			GameSession session,
			ExecutionContext execution)
	{
		var identity = CreatePowerIdentity(execution);
		return session.GetActorBorrowedSeerCheckCommits()
			.Where(commit =>
				commit.PowerIdentity == identity &&
				commit.TurnNumber == session.TurnNumber &&
				commit.CurrentPhase == GamePhase.Night);
	}

	private static ActorBorrowedSeerCheckCommit GetBorrowedCommit(
		GameSession session,
		ExecutionContext execution)
	{
		var commits = GetBorrowedCommitsThisNight(session, execution).ToArray();
		if (commits is not [var commit])
		{
			throw new InvalidOperationException(
				"The Actor borrowed Seer continuation requires exactly one private commit.");
		}

		return commit;
	}

	private static void ValidateBorrowedBoundary(
		GameSession session,
		ExecutionContext execution,
		ActorBorrowedSeerCheckCommit commit,
		TargetPrivateRolePowerRecoveryBoundary boundary)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		if (boundary.CurrentPhase != GamePhase.Night ||
			boundary.TurnNumber != session.TurnNumber ||
			boundary.ActionType != NightActionType.SeerCheck ||
			boundary.PowerIdentity != CreatePowerIdentity(execution) ||
			boundary.PowerIdentity != commit.PowerIdentity ||
			boundary.SpentResourceIdentity is not null ||
			commit.ActorSetupCardId != activation.SelectedCardId)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Seer target-private commit has an invalid Role Power identity.");
		}
	}

	private static void ValidateBorrowedWake(
		ExecutionContext execution,
		ConfirmationInstruction wake)
	{
		if (wake.PublicAnnouncement !=
				GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName) ||
			wake.PrivateInstruction is not null ||
			wake.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id)
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	private static void ValidateBorrowedSelectionInstruction(
		GameSession session,
		ExecutionContext execution,
		SelectPlayersInstruction selection)
	{
		var potentialTargets = GetBorrowedPotentialTargets(
			session,
			execution.ActingPlayer.Id);
		if (potentialTargets.Count == 0 ||
			selection.CountConstraint != NumberRangeConstraint.Single ||
			selection.RoleIdentification is not null ||
			selection.PublicAnnouncement !=
				GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName) ||
			selection.PrivateInstruction != GameStrings.SeerNightActionPrompt ||
			selection.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id ||
			!selection.SelectablePlayerIds.ToHashSet().SetEquals(
				potentialTargets))
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	private static void ValidateBorrowedFeedback(
		GameSession session,
		ExecutionContext execution,
		ActorBorrowedSeerCheckCommit commit,
		ConfirmationInstruction feedback)
	{
		if (feedback.PublicAnnouncement is not null ||
			feedback.PrivateInstruction != FormatSeerFeedback(
				session.GetPlayer(commit.TargetPlayerId).Name,
				commit.TargetAgentKnowledge) ||
			feedback.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id)
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	private static void ValidateBorrowedSleep(
		ExecutionContext execution,
		ConfirmationInstruction sleep)
	{
		if (sleep.PublicAnnouncement !=
				GameStrings.RoleGoesToSleepSingle.Format(
					GameStrings.ActorRoleName) ||
			sleep.PrivateInstruction is not null ||
			sleep.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id)
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	private static string FormatSeerFeedback(
		GameSession session,
		Guid targetId) =>
		(session.GetFactionAgentKnowledge(targetId, Faction.Werewolf) ==
		 FactionAgentKnowledge.KnownAgent
			? GameStrings.SeerResultWerewolfTeam
			: GameStrings.SeerResultNotWerewolfTeam)
		.Format(session.GetPlayer(targetId).Name);

	private static string FormatSeerFeedback(
		string targetName,
		FactionAgentKnowledge targetKnowledge) =>
		(targetKnowledge == FactionAgentKnowledge.KnownAgent
			? GameStrings.SeerResultWerewolfTeam
			: GameStrings.SeerResultNotWerewolfTeam).Format(targetName);
}
