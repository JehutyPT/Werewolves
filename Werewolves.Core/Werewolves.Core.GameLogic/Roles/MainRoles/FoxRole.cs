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

internal enum FoxRoleState
{
	Awake,
	AwaitingWakeAcknowledgement,
	AwaitingCenterSelection,
	AwaitingResultAcknowledgement,
	ReadyToSleep,
	Asleep
}

internal sealed class FoxRole
	: RoleHookListener,
		IDeclaredRoleWorkflow
{
	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		bool IsBorrowed);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _workflowRuntime;
	private bool? _powerIsAvailable;

	private static readonly RolePowerDefinition NeighborhoodCheckPower = new(
		new RolePowerIdentifier("fox-neighborhood-check"),
		RolePowerCategory.Chosen);

	private static readonly Guid NeighborhoodCheckResourceId =
		Guid.Parse("dadbf4d0-fcb8-4e1b-857d-326634230227");

	internal FoxRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var identificationWait =
			RecoverableWait<FoxRoleState, SelectPlayersInstruction>.Replayable(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				FoxRoleState.Awake,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateIdentificationInstruction,
				static (_, instruction) =>
					instruction is SelectPlayersInstruction
					{
						RoleIdentification: MainRoleType.Fox
					},
				ValidateIdentificationInstruction);
		var directWakeWait =
			RecoverableWait<FoxRoleState, ConfirmationInstruction>.Replayable(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				FoxRoleState.Awake,
				ModeratorInstructionSemantic.WakeRole,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateWakeInstruction,
				ClaimsWakeRecoveryCandidate,
				ValidateWakeInstruction);
		var identifiedWakeWait =
			RecoverableWait<FoxRoleState, ConfirmationInstruction>.Durable(
				Id,
				GameHook.NightMainActionLoop,
				FoxRoleState.Awake,
				FoxRoleState.AwaitingWakeAcknowledgement,
				ModeratorInstructionSemantic.WakeRole,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateWakeInstruction,
				static (_, _) => false,
				ValidateWakeInstruction,
				ValidateIdentificationRecoveryBoundary,
				static _ => FoxRoleState.AwaitingWakeAcknowledgement);
		var centerSelectionWait =
			RecoverableWait<FoxRoleState, SelectPlayersInstruction>.Replayable(
				Id,
				GameHook.NightMainActionLoop,
				FoxRoleState.AwaitingWakeAcknowledgement,
				FoxRoleState.AwaitingCenterSelection,
				ModeratorInstructionSemantic.SelectFoxCenter,
				ExpectedInputType.PlayerSelection,
				static _ => true,
				static (_, _) => { },
				CreateCenterSelectionInstruction,
				static (_, _) => false,
				ValidateCenterSelectionInstruction);
		var feedbackWait =
			RecoverableWait<FoxRoleState, ConfirmationInstruction>.DomainDurable(
				Id,
				GameHook.NightMainActionLoop,
				FoxRoleState.AwaitingCenterSelection,
				FoxRoleState.AwaitingResultAcknowledgement,
				ModeratorInstructionSemantic.RevealFoxResult,
				ExpectedInputType.Continue,
				static _ => false,
				CommitCenterSelection,
				CreateFeedbackInstruction,
				ClaimsCommittedFeedback,
				ValidateFeedbackInstruction,
				ValidateFeedbackRecoveryContext,
				static _ => FoxRoleState.AwaitingResultAcknowledgement,
				ValidateCommittedRecoveryBoundary);
		var sleepWait =
			RecoverableWait<FoxRoleState, ConfirmationInstruction>.Replayable(
				Id,
				GameHook.NightMainActionLoop,
				FoxRoleState.AwaitingResultAcknowledgement,
				FoxRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => true,
				static (_, _) => { },
				CreateSleepInstruction,
				static (_, _) => false,
				ValidateSleepInstruction);

		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				identificationWait,
				directWakeWait,
				identifiedWakeWait,
				centerSelectionWait,
				feedbackWait,
				sleepWait,
				new RoleWorkflowDecisionStep<FoxRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) => BeginNightAction(
						session,
						input,
						identificationWait,
						directWakeWait)),
				new RoleWorkflowDecisionStep<FoxRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					FoxRoleState.Awake,
					static _ => true,
					(session, input) => ContinueAfterWakeOrIdentification(
						session,
						input,
						identifiedWakeWait,
						centerSelectionWait)),
				new RoleWorkflowDecisionStep<FoxRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					FoxRoleState.AwaitingCenterSelection,
					static _ => true,
					(session, input) => ContinueAfterCenterSelection(
						session,
						input,
						feedbackWait,
						sleepWait)),
				new RoleWorkflowCompletionStep<FoxRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					FoxRoleState.ReadyToSleep,
					FoxRoleState.Asleep,
					static _ => true)
			]);
	}

	internal override string PublicName => GameStrings.FoxRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Fox);

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
			session.Execution.GetCurrentListenerState<FoxRoleState>(Id));

	private bool ValidateCommittedRecoveryBoundary(
			GameSession session,
			ModeratorInstruction? startingInstruction,
			ModeratorResponse input,
			TargetPrivateRolePowerRecoveryBoundary committedBoundary,
			ConfirmationInstruction nextInstruction)
	{
		if (committedBoundary.ActionType != NightActionType.FoxCheck)
		{
			return false;
		}

		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			return TryValidateCommittedRecoveryBoundary(
				session,
				startingInstruction,
				input,
				committedBoundary,
				nextInstruction);
		}

		var commit = GetBorrowedFoxCheckCommit(session, execution);
		ValidateBorrowedRecoveryBoundary(
			session,
			execution,
			commit,
			committedBoundary);
		if (startingInstruction is not SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.SelectFoxCenter
			} centerSelection ||
			input.InstructionId != centerSelection.InstructionId ||
			input.Type != ExpectedInputType.PlayerSelection ||
			input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds ||
			selectedPlayerIds.Single() != commit.CenterPlayerId ||
			!centerSelection.SelectablePlayerIds.Contains(
				commit.CenterPlayerId) ||
			nextInstruction.Semantic !=
				ModeratorInstructionSemantic.RevealFoxResult)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox commit does not match its accepted center and feedback continuation.");
		}

		ValidateBorrowedCenterSelectionInstruction(
			session,
			execution,
			centerSelection);
		ValidateBorrowedFeedback(execution, nextInstruction);
		return true;
	}

	private static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		TargetPrivateRolePowerRecoveryBoundary committedBoundary,
		ConfirmationInstruction nextInstruction)
	{
		if (committedBoundary.ActionType != NightActionType.FoxCheck)
		{
			return false;
		}

		ValidateOwnedRecoveryBoundary(session, committedBoundary);
		if (startingInstruction is not SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.SelectFoxCenter,
				CountConstraint: var countConstraint,
				AffectedPlayerIds: { Count: 1 } affectedPlayerIds,
				RoleIdentification: null
			} centerSelection ||
			countConstraint != NumberRangeConstraint.SingleOptional ||
			input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds ||
			!centerSelection.SelectablePlayerIds.Contains(
				selectedPlayerIds.Single()) ||
			selectedPlayerIds.Single() == Guid.Empty ||
			committedBoundary.ActingPlayerId != affectedPlayerIds.Single() ||
			nextInstruction.Semantic !=
				ModeratorInstructionSemantic.RevealFoxResult)
		{
			throw new InvalidOperationException(
				"The Fox target-private commit does not match its accepted check and feedback continuation.");
		}

		ValidateFeedback(committedBoundary, nextInstruction);
		return true;
	}

	private void ValidateFeedbackRecoveryContext(
		GameSession session,
		ConfirmationInstruction instruction,
		DomainRecoveryCursor cursor)
	{
		if (TryResolveBorrowedExecution(session, out var execution))
		{
			ValidateBorrowedRecoveryCursorIdentity(
				session,
				execution,
				cursor);
			return;
		}

		ValidateTargetPrivateRecoveryCursorIdentity(session, cursor);
		ValidateFeedbackInstruction(session, instruction);
	}

	private static void ValidateTargetPrivateRecoveryCursorIdentity(
		GameSession session,
		DomainRecoveryCursor cursor)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(cursor);
		var commits = GetFoxCheckCommitsThisNight(session).ToArray();
		if (commits is not [var commit])
		{
			throw new InvalidOperationException(
				"The Fox recovery cursor requires exactly one committed target-private check.");
		}

		ValidateOwnedCommit(session, commit);
		var expectedResourceId =
			commit.SpentResourceIdentity?.OneUseResourceId ?? Guid.Empty;
		if (cursor.Kind !=
				DomainRecoveryCursorKind.TargetPrivateRolePowerCommit ||
			cursor.SourceRole != MainRoleType.Fox ||
			cursor.CommittedActionType != NightActionType.FoxCheck ||
			cursor.ActingPlayerId == Guid.Empty ||
			!StringComparer.Ordinal.Equals(
				cursor.SourcePowerIdentifier,
				NeighborhoodCheckPower.Identifier.Value) ||
			cursor.PowerIdentity != CreatePowerIdentity(
				session.GetPlayer(cursor.ActingPlayerId),
				CreatePowerInstance(
					session,
					session.GetPlayer(cursor.ActingPlayerId))) ||
			cursor.PowerIdentity != commit.PowerIdentity ||
			cursor.OneUseResourceId != expectedResourceId ||
			cursor.CommittedTargetIds.Count != 0 ||
			cursor.NextInstructionSemantic !=
				ModeratorInstructionSemantic.RevealFoxResult)
		{
			throw new InvalidOperationException(
				"The Fox recovery cursor has an invalid target-private Role Power identity.");
		}
	}

	private HookListenerActionResult BeginNightAction(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<FoxRoleState, SelectPlayersInstruction>
			identificationWait,
		RecoverableWait<FoxRoleState, ConfirmationInstruction>
			directWakeWait)
	{
		_powerIsAvailable = null;
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			if (!EvaluateAvailability(session, borrowedExecution))
			{
				return HookListenerActionResult.Complete(FoxRoleState.Asleep);
			}

			return directWakeWait.Execute(session, input);
		}

		if (!IsCompleteHolderSetKnown(session))
		{
			return identificationWait.Execute(session, input);
		}

		var execution = ResolveNativeExecution(session);
		if (!EvaluateAvailability(session, execution))
		{
			return HookListenerActionResult.Complete(FoxRoleState.Asleep);
		}

		return directWakeWait.Execute(session, input);
	}

	private HookListenerActionResult ContinueAfterWakeOrIdentification(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<FoxRoleState, ConfirmationInstruction>
			identifiedWakeWait,
		RecoverableWait<FoxRoleState, SelectPlayersInstruction>
			centerSelectionWait)
	{
		var pendingInstruction = session.Execution.PendingInstruction
			?? throw new InvalidOperationException(
				"The Fox workflow requires one Pending Instruction.");
		if (pendingInstruction.Semantic ==
		    ModeratorInstructionSemantic.WakeRole)
		{
			return centerSelectionWait.Execute(session, input);
		}

		if (pendingInstruction.Semantic !=
		    ModeratorInstructionSemantic.IdentifyRoleHolders)
		{
			throw new InvalidOperationException(
				$"Unsupported Fox continuation '{pendingInstruction.Semantic}'.");
		}

		AcceptIdentification(session, input);
		var identifiedExecution = ResolveNativeExecution(session);
		if (!EvaluateAvailability(session, identifiedExecution))
		{
			return HookListenerActionResult.Complete(FoxRoleState.Asleep);
		}

		return identifiedWakeWait.Execute(session, input);
	}

	private ConfirmationInstruction CreateWakeInstruction(
		GameSession session)
	{
		var execution = ResolveExecution(session);
		return new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(
					execution.IsBorrowed
						? GameStrings.ActorRoleName
						: PublicName),
				affectedPlayerIds: [execution.ActingPlayer.Id]);
	}

	private bool ClaimsWakeRecoveryCandidate(
		GameSession session,
		ModeratorInstruction instruction)
	{
		if (instruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.WakeRole,
			    AffectedPlayerIds: { Count: 1 } affectedPlayerIds
		    })
		{
			return false;
		}

		var livingHolderIds = GetLivingHolderIds(session);
		var actingPlayerId = TryResolveBorrowedExecution(
			session,
			out var borrowedExecution)
			? borrowedExecution.ActingPlayer.Id
			: livingHolderIds.Count == 1
				? livingHolderIds.Single()
				: Guid.Empty;
		return actingPlayerId != Guid.Empty &&
		       affectedPlayerIds.Single() == actingPlayerId;
	}

	private SelectPlayersInstruction CreateCenterSelectionInstruction(
		GameSession session)
	{
		var execution = ResolveExecution(session);
		return new SelectPlayersInstruction(
				ModeratorInstructionSemantic.SelectFoxCenter,
				session.GetPlayers()
					.WithHealth(PlayerHealth.Alive)
					.ToIdSet(),
				NumberRangeConstraint.SingleOptional,
				publicAnnouncement: null,
				privateInstruction: GameStrings.FoxCenterSelectionInstruction,
				affectedPlayerIds: [execution.ActingPlayer.Id])
			{
				EmptySelectionOptionLabel = GameStrings.DeclineOption
			};
	}

	private HookListenerActionResult ContinueAfterCenterSelection(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<FoxRoleState, ConfirmationInstruction> feedbackWait,
		RecoverableWait<FoxRoleState, ConfirmationInstruction> sleepWait)
	{
		if (input.SelectedPlayerIds is not { Count: <= 1 } selectedPlayerIds)
		{
			throw new InvalidOperationException(
				TryResolveBorrowedExecution(session, out _)
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "The Fox may select at most one living Player.");
		}

		return selectedPlayerIds.Count == 0
			? sleepWait.Execute(session, input)
			: feedbackWait.Execute(session, input);
	}

	private void CommitCenterSelection(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = ResolveExecution(session);
		if (input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds)
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "The Fox check requires exactly one living Player.");
		}

		var livingPlayers = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.ToArray();
		var centerId = selectedPlayerIds.Single();
		var center = livingPlayers.SingleOrDefault(player =>
			player.Id == centerId);
		if (center is null)
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "The Fox center selection is unavailable.");
		}

		if (livingPlayers.Any(player =>
				player.State.GetFactionAgentKnowledge(Faction.Werewolf) ==
				FactionAgentKnowledge.Unknown))
		{
			throw new InvalidOperationException(
				"The current living Werewolf Faction Agent facts are incomplete.");
		}

		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			center.Id);
		var checkedPlayerIds = new HashSet<Guid> { center.Id };
		if (neighbors.Clockwise is { } clockwise)
		{
			checkedPlayerIds.Add(clockwise.Id);
		}

		if (neighbors.Counterclockwise is { } counterclockwise)
		{
			checkedPlayerIds.Add(counterclockwise.Id);
		}

		var isAffirmative = checkedPlayerIds.Any(playerId =>
			session.GetFactionAgentKnowledge(playerId, Faction.Werewolf) ==
			FactionAgentKnowledge.KnownAgent);
		var powerIdentity = CreatePowerIdentity(execution);
		var spentResourceIdentity = isAffirmative
			? (OneUseRolePowerResourceIdentity?)null
			: CreateResourceIdentity(
				execution.ActingPlayer,
				execution.PowerInstance);
		if (execution.IsBorrowed)
		{
			session.CommitActorBorrowedFoxCheck(
				powerIdentity,
				center.Id,
				isAffirmative
					? FactionAgentKnowledge.KnownAgent
					: FactionAgentKnowledge.KnownNonAgent,
				spentResourceIdentity);
		}
		else
		{
			session.CommitTargetPrivateRolePowerNightAction(
				NightActionType.FoxCheck,
				powerIdentity,
				spentResourceIdentity);
		}
	}

	private ConfirmationInstruction CreateFeedbackInstruction(
		GameSession session)
	{
		var execution = ResolveExecution(session);
		bool isAffirmative;
		if (execution.IsBorrowed)
		{
			var commit = GetBorrowedFoxCheckCommit(session, execution);
			ValidateBorrowedCommit(session, execution, commit);
			isAffirmative = commit.NeighborhoodAgentKnowledge ==
				FactionAgentKnowledge.KnownAgent;
		}
		else
		{
			var commits = GetFoxCheckCommitsThisNight(session).ToArray();
			if (commits is not [var commit])
			{
				throw new InvalidOperationException(
					"The Fox feedback requires one committed target-private check.");
			}

			ValidateOwnedCommit(session, commit);
			isAffirmative = commit.SpentResourceIdentity == null;
		}

		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.RevealFoxResult,
			privateInstruction: isAffirmative
				? GameStrings.FoxAffirmativeFeedbackInstruction
				: GameStrings.FoxNegativeFeedbackInstruction,
			affectedPlayerIds: [execution.ActingPlayer.Id]);
	}

	private bool EvaluateAvailability(
		GameSession session,
		ExecutionContext execution)
	{
		if (_powerIsAvailable is { } knownAvailability)
		{
			return knownAvailability;
		}

		if (GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				session,
				CreateResourceIdentity(
					execution.ActingPlayer,
					execution.PowerInstance)))
		{
			_powerIsAvailable = false;
			return false;
		}

		var executionContext = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				execution.ActingPlayer,
				MainRoleType.Fox,
				NeighborhoodCheckPower,
				execution.PowerInstance,
				new OneUseRolePowerResource(
					NeighborhoodCheckResourceId,
					execution.PowerInstance)));
		_powerIsAvailable =
			executionContext.AvailabilityResult.IsAvailable;
		return _powerIsAvailable.Value;
	}

	private ConfirmationInstruction CreateSleepInstruction(
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

	private bool IsCompleteHolderSetKnown(GameSession session) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			MainRoleType.Fox);

	private SelectPlayersInstruction CreateIdentificationInstruction(
		GameSession session)
	{
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		var committedHolderCount = GetCommittedLivingRoleHolderIds(session).Count;
		var selectablePlayerIds = GetIdentificationCandidates(session);
		if (roleCount <= 0 ||
		    committedHolderCount > roleCount ||
		    selectablePlayerIds.Count < roleCount)
		{
			throw new InvalidOperationException(
				"Confirmed Role knowledge contradicts the required Fox holder count.");
		}

		var privateInstruction = roleCount == 1
			? GameStrings.RoleSingleIdentificationPrompt.Format(PublicName)
			: GameStrings.RoleMultipleIdentificationPrompt.Format(PublicName);
		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			selectablePlayerIds,
			NumberRangeConstraint.Exact(roleCount),
			publicAnnouncement: GameStrings.RoleWakesUp.Format(PublicName),
			privateInstruction: privateInstruction,
			affectedPlayerIds: null,
			roleIdentification: MainRoleType.Fox);
	}

	private void AcceptIdentification(
		GameSession session,
		ModeratorResponse input)
	{
		if (IsCompleteHolderSetKnown(session))
		{
			return;
		}

		IdentifyCompleteLivingRoleHolderSet(
			session,
			input.SelectedPlayerIds?.ToHashSet()
			?? throw new InvalidOperationException(
				"Fox identification requires a Player selection."));
	}

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		if (instruction.RoleIdentification != MainRoleType.Fox ||
		    instruction.AffectedPlayerIds != null ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)) ||
		    instruction.CountConstraint != NumberRangeConstraint.Exact(
			    GetExpectedLivingRoleHolderCount(session)))
		{
			throw new InvalidOperationException(
				"The Fox identification instruction has invalid workflow context.");
		}
	}

	private void ValidateWakeInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		var execution = ResolveExecution(session);
		if (string.IsNullOrWhiteSpace(instruction.PublicAnnouncement) ||
		    instruction.PrivateInstruction != null ||
		    instruction.AffectedPlayerIds is not { Count: 1 } affectedIds ||
		    affectedIds.Single() != execution.ActingPlayer.Id)
		{
			throw new InvalidOperationException(
				"The Fox wake instruction has invalid workflow context.");
		}
	}

	private void ValidateIdentificationRecoveryBoundary(
		GameSession session,
		ConfirmationInstruction instruction,
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.AcceptedObservationSemantic !=
			    ModeratorInstructionSemantic.IdentifyRoleHolders ||
		    cursor.ObservedRole != MainRoleType.Fox ||
		    cursor.ContinuationRole != MainRoleType.Fox ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The Fox identification cursor has invalid workflow context.");
		}

		var livingHolderIds = GetLivingHolderIds(session);
		if (livingHolderIds.Count == 0 ||
		    !session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Any(entry =>
			    entry.TurnNumber == session.TurnNumber &&
			    entry.CurrentPhase == GamePhase.Night &&
			    entry.Role == MainRoleType.Fox &&
			    entry.PlayerIds.SetEquals(livingHolderIds)))
		{
			throw new InvalidOperationException(
				"The Fox wake wait has no committed identification.");
		}
	}

	private void ValidateCenterSelectionInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var execution = ResolveExecution(session);
		if (instruction.RoleIdentification != null ||
		    instruction.PublicAnnouncement != null ||
		    string.IsNullOrWhiteSpace(instruction.PrivateInstruction) ||
		    instruction.AffectedPlayerIds is not { Count: 1 } affectedIds ||
		    affectedIds.Single() != execution.ActingPlayer.Id ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    session.GetPlayers().WithHealth(PlayerHealth.Alive).ToIdSet()) ||
		    instruction.CountConstraint != NumberRangeConstraint.SingleOptional ||
		    string.IsNullOrWhiteSpace(instruction.EmptySelectionOptionLabel))
		{
			if (execution.IsBorrowed)
			{
				throw new RoleWorkflowInputRejectionException(
					GameStrings.ActorBorrowedRolePowerInvalidResponse);
			}

			throw new InvalidOperationException(
				"The Fox center-selection instruction has invalid workflow context.");
		}
	}

	private void ValidateFeedbackInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		var execution = ResolveExecution(session);
		if (execution.IsBorrowed)
		{
			var commit = GetBorrowedFoxCheckCommit(session, execution);
			ValidateBorrowedCommit(session, execution, commit);
		}
		else
		{
			var commits = GetFoxCheckCommitsThisNight(session).ToArray();
			if (commits is not [var commit])
			{
				throw new InvalidOperationException(
					"The Fox feedback requires one committed target-private check.");
			}

			ValidateOwnedCommit(session, commit);
		}

		if (instruction.PublicAnnouncement != null ||
		    string.IsNullOrWhiteSpace(instruction.PrivateInstruction) ||
		    instruction.AffectedPlayerIds is not { Count: 1 } affectedIds ||
		    affectedIds.Single() != execution.ActingPlayer.Id)
		{
			throw new InvalidOperationException(
				"The Fox feedback has invalid private workflow context.");
		}
	}

	private static bool ClaimsCommittedFeedback(
		GameSession session,
		ModeratorInstruction instruction)
	{
		if (instruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.RevealFoxResult,
			    AffectedPlayerIds: [var affectedPlayerId]
		    })
		{
			return false;
		}

		return GetFoxCheckCommitsThisNight(session).Any(commit =>
			       commit.ActingPlayerId == affectedPlayerId) ||
		       session.GetActorBorrowedFoxCheckCommits().Any(commit =>
			       commit.TurnNumber == session.TurnNumber &&
			       commit.CurrentPhase == GamePhase.Night &&
			       commit.PowerIdentity.ActingPlayerId == affectedPlayerId);
	}

	private void ValidateSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		var execution = ResolveExecution(session);
		if (execution.IsBorrowed)
		{
			var commits = GetBorrowedFoxCheckCommitsThisNight(
					session,
					CreatePowerIdentity(execution))
				.ToArray();
			if (commits.Length > 1)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Fox sleep wait has multiple committed checks.");
			}

			if (commits is [var commit])
			{
				ValidateBorrowedCommit(session, execution, commit);
			}
		}
		else
		{
			var commits = GetFoxCheckCommitsThisNight(session).ToArray();
			if (commits.Length > 1)
			{
				throw new InvalidOperationException(
					"The Fox sleep wait has multiple committed checks.");
			}

			if (commits is [var commit])
			{
				ValidateOwnedCommit(session, commit);
			}
		}

		if (string.IsNullOrWhiteSpace(instruction.PublicAnnouncement) ||
		    instruction.PrivateInstruction != null ||
		    instruction.AffectedPlayerIds is not { Count: 1 } affectedIds ||
		    affectedIds.Single() != execution.ActingPlayer.Id)
		{
			throw new InvalidOperationException(
				"The Fox sleep instruction has invalid workflow context.");
		}
	}

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.Fox ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == null ||
				  player.State.ModeratorKnownRole == MainRoleType.Fox)))
			.ToIdSet();

	private HashSet<Guid> GetLivingHolderIds(GameSession session) =>
		GetAliveRolePlayers(session)?.Select(player => player.Id).ToHashSet()
		?? [];

	private ExecutionContext ResolveExecution(GameSession session) =>
		TryResolveBorrowedExecution(session, out var borrowed)
			? borrowed
			: ResolveNativeExecution(session);

	private ExecutionContext ResolveNativeExecution(GameSession session)
	{
		var fox = GetFox(session);
		return new ExecutionContext(
			fox,
			CreatePowerInstance(session, fox),
			IsBorrowed: false);
	}

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.Fox)
		{
			execution = null!;
			return false;
		}

		var actor = session.GetPlayer(activation.ActingPlayerId);
		execution = new ExecutionContext(
			actor,
			RolePowerInstance.CreateBorrowed(
				session,
				actor,
				MainRoleType.Fox,
				NeighborhoodCheckPower),
			IsBorrowed: true);
		return true;
	}

	private static RolePowerInstance CreatePowerInstance(
		GameSession session,
		IPlayer fox) =>
		RolePowerInstance.CreateCurrent(
			session,
			fox,
			MainRoleType.Fox,
			NeighborhoodCheckPower);

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		IPlayer fox,
		RolePowerInstance powerInstance) =>
		new(
			fox.Id,
			MainRoleType.Fox,
			NeighborhoodCheckPower.Identifier.Value,
			powerInstance.Id,
			powerInstance.Origin);

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		ExecutionContext execution) =>
		CreatePowerIdentity(
			execution.ActingPlayer,
			execution.PowerInstance);

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		IPlayer fox,
		RolePowerInstance powerInstance) =>
		new(
			fox.Id,
			MainRoleType.Fox,
			NeighborhoodCheckPower.Identifier.Value,
			powerInstance.Id,
			powerInstance.Origin,
			NeighborhoodCheckResourceId);

	private static IEnumerable<ActorBorrowedFoxCheckCommit>
		GetBorrowedFoxCheckCommitsThisNight(
			GameSession session,
			RolePowerInstanceIdentity powerIdentity) =>
		session.GetActorBorrowedFoxCheckCommits()
			.Where(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.TurnNumber == session.TurnNumber &&
				commit.CurrentPhase == GamePhase.Night);

	private static ActorBorrowedFoxCheckCommit GetBorrowedFoxCheckCommit(
		GameSession session,
		ExecutionContext execution)
	{
		var commits = GetBorrowedFoxCheckCommitsThisNight(
				session,
				CreatePowerIdentity(execution))
			.ToArray();
		if (commits is not [var commit])
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox continuation requires exactly one private commit.");
		}

		return commit;
	}

	private static void ValidateBorrowedRecoveryBoundary(
		GameSession session,
		ExecutionContext execution,
		ActorBorrowedFoxCheckCommit commit,
		TargetPrivateRolePowerRecoveryBoundary boundary)
	{
		ValidateBorrowedCommit(session, execution, commit);
		if (boundary.CurrentPhase != GamePhase.Night ||
			boundary.TurnNumber != session.TurnNumber ||
			boundary.ActionType != NightActionType.FoxCheck ||
			boundary.PowerIdentity != CreatePowerIdentity(execution) ||
			boundary.PowerIdentity != commit.PowerIdentity ||
			boundary.SpentResourceIdentity != commit.SpentResourceIdentity)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox target-private commit has an invalid Role Power identity.");
		}
	}

	private static void ValidateBorrowedRecoveryCursorIdentity(
		GameSession session,
		ExecutionContext execution,
		DomainRecoveryCursor cursor)
	{
		ArgumentNullException.ThrowIfNull(cursor);
		var commit = GetBorrowedFoxCheckCommit(session, execution);
		ValidateBorrowedCommit(session, execution, commit);
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		var expectedResourceId =
			commit.SpentResourceIdentity?.OneUseResourceId ?? Guid.Empty;
		if (cursor.Kind != DomainRecoveryCursorKind.TargetPrivateRolePowerCommit ||
			cursor.SourceRole != MainRoleType.Fox ||
			cursor.CommittedActionType != NightActionType.FoxCheck ||
			!StringComparer.Ordinal.Equals(
				cursor.SourcePowerIdentifier,
				NeighborhoodCheckPower.Identifier.Value) ||
			cursor.PowerIdentity != CreatePowerIdentity(execution) ||
			cursor.OneUseResourceId != expectedResourceId ||
			cursor.ActorSetupCardId != activation.SelectedCardId ||
			cursor.ActorBorrowedActivationId != activation.ActivationId ||
			cursor.CommittedTargetIds is not { Count: 1 } centerIds ||
			centerIds.Single() != commit.CenterPlayerId ||
			cursor.NextInstructionSemantic !=
				ModeratorInstructionSemantic.RevealFoxResult)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox recovery cursor has an invalid target-private Role Power identity.");
		}
	}

	private static void ValidateBorrowedCommit(
		GameSession session,
		ExecutionContext execution,
		ActorBorrowedFoxCheckCommit commit)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		var expectedPowerIdentity = CreatePowerIdentity(execution);
		var expectedSpentResource = commit.NeighborhoodAgentKnowledge ==
			FactionAgentKnowledge.KnownNonAgent
				? CreateResourceIdentity(
					execution.ActingPlayer,
					execution.PowerInstance)
				: (OneUseRolePowerResourceIdentity?)null;
		var publicMarker = commit.PublicMarkerLogIndex >= 0
			? session.GameHistoryLog.ElementAtOrDefault(
				commit.PublicMarkerLogIndex)
			: null;
		if (!execution.IsBorrowed ||
			activation?.SourceRole != MainRoleType.Fox ||
			activation.ActingPlayerId != execution.ActingPlayer.Id ||
			commit.PowerIdentity != expectedPowerIdentity ||
			commit.ActorSetupCardId != activation.SelectedCardId ||
			commit.CenterPlayerId == Guid.Empty ||
			commit.NeighborhoodAgentKnowledge is not
				(FactionAgentKnowledge.KnownAgent or
				 FactionAgentKnowledge.KnownNonAgent) ||
			commit.SpentResourceIdentity != expectedSpentResource ||
			commit.TurnNumber != session.TurnNumber ||
			commit.CurrentPhase != GamePhase.Night ||
			publicMarker is not ActorBorrowedRolePowerCommittedLogEntry marker ||
			marker.Timestamp != commit.Timestamp ||
			marker.TurnNumber != commit.TurnNumber ||
			marker.CurrentPhase != commit.CurrentPhase)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox recovery boundary requires one correlated private check and sanitized public marker.");
		}

		if (execution.ActingPlayer.State.Health != PlayerHealth.Alive ||
			execution.ActingPlayer.State.CurrentRole != MainRoleType.Actor ||
			session.GetPlayer(commit.CenterPlayerId).State.Health !=
				PlayerHealth.Alive)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox check does not belong to the living Actor or a living center.");
		}
	}

	private static void ValidateBorrowedCenterSelectionInstruction(
		GameSession session,
		ExecutionContext execution,
		SelectPlayersInstruction selection)
	{
		if (selection.CountConstraint != NumberRangeConstraint.SingleOptional ||
			selection.RoleIdentification is not null ||
			selection.PublicAnnouncement is not null ||
			string.IsNullOrWhiteSpace(selection.PrivateInstruction) ||
			string.IsNullOrWhiteSpace(selection.EmptySelectionOptionLabel) ||
			selection.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id ||
			!selection.SelectablePlayerIds.ToHashSet().SetEquals(
				session.GetPlayers()
					.WithHealth(PlayerHealth.Alive)
					.ToIdSet()))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox center-selection instruction is invalid.");
		}
	}

	private static void ValidateBorrowedFeedback(
		ExecutionContext execution,
		ConfirmationInstruction feedback)
	{
		if (feedback.PublicAnnouncement is not null ||
			string.IsNullOrWhiteSpace(feedback.PrivateInstruction) ||
			feedback.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox feedback does not match its private commit.");
		}
	}

	private static IEnumerable<TargetPrivateRolePowerCommittedLogEntry>
		GetFoxCheckCommitsThisNight(GameSession session) =>
		GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				[NightActionType.FoxCheck])
			.OfType<TargetPrivateRolePowerCommittedLogEntry>();

	private static void ValidateOwnedCommit(
		GameSession session,
		TargetPrivateRolePowerCommittedLogEntry commit)
	{
		if (commit.TargetIds is { Count: > 0 })
		{
			throw new InvalidOperationException(
				"The Fox target-private check commit has an invalid Role Power identity.");
		}

		ValidateOwnedRecoveryBoundary(
			session,
			TargetPrivateRolePowerRecoveryBoundary.FromCommittedEntry(commit));
	}

	private static void ValidateOwnedRecoveryBoundary(
		GameSession session,
		TargetPrivateRolePowerRecoveryBoundary boundary)
	{
		if (boundary.ActionType != NightActionType.FoxCheck ||
			boundary.SourceRole != MainRoleType.Fox ||
			!StringComparer.Ordinal.Equals(
				boundary.SourcePowerIdentifier,
				NeighborhoodCheckPower.Identifier.Value) ||
			boundary.PowerIdentity != CreatePowerIdentity(
				session.GetPlayer(boundary.ActingPlayerId),
				CreatePowerInstance(
					session,
					session.GetPlayer(boundary.ActingPlayerId))) ||
			boundary.CurrentPhase != GamePhase.Night ||
			boundary.TurnNumber != session.TurnNumber)
		{
			throw new InvalidOperationException(
				"The Fox target-private check commit has an invalid Role Power identity.");
		}

		var fox = session.GetPlayer(boundary.ActingPlayerId);
		if (fox.State.Health != PlayerHealth.Alive ||
			fox.State.CurrentRole != MainRoleType.Fox)
		{
			throw new InvalidOperationException(
				"The Fox target-private check commit does not belong to the living Role holder.");
		}

		if (boundary.SpentResourceIdentity is { } spentResource &&
			spentResource != CreateResourceIdentity(
				fox,
				CreatePowerInstance(session, fox)))
		{
			throw new InvalidOperationException(
				"The Fox target-private check commit has an invalid spent Resource.");
		}
	}

	private static void ValidateFeedback(
		TargetPrivateRolePowerRecoveryBoundary boundary,
		ConfirmationInstruction feedback)
	{
		if (feedback.PublicAnnouncement != null ||
			string.IsNullOrWhiteSpace(feedback.PrivateInstruction) ||
			feedback.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != boundary.ActingPlayerId)
		{
			throw new InvalidOperationException(
				"The Fox feedback does not match its committed target-private check.");
		}
	}

	private IPlayer GetFox(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException(
			"No living Fox is available.");
}
