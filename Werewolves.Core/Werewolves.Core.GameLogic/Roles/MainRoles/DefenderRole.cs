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

internal enum DefenderRoleState
{
	Awake,
	AwaitingTargetSelection,
	ReadyToSleep,
	Asleep
}

internal sealed class DefenderRole
	: RoleHookListener,
		IDeclaredRoleWorkflow
{
	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		bool IsBorrowed);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _workflowRuntime;

	private static readonly RolePowerDefinition ProtectionPower = new(
		new RolePowerIdentifier("defender-protection"),
		RolePowerCategory.Chosen);

	internal static RolePowerIdentifier ProtectionPowerIdentifier =>
		ProtectionPower.Identifier;

	internal DefenderRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var identificationWait = RecoverableWait<
				DefenderRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				DefenderRoleState.Awake,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateIdentificationInstruction,
				static (_, instruction) =>
					instruction is SelectPlayersInstruction
					{
						RoleIdentification: MainRoleType.Defender
					},
				ValidateIdentificationInstruction,
				(_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => DefenderRoleState.Awake);
		var wakeWait = RecoverableWait<
				DefenderRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				DefenderRoleState.Awake,
				ModeratorInstructionSemantic.WakeRole,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateWakeInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.WakeRole &&
					HasExpectedAffectedActingPlayer(session, instruction),
				ValidateWakeInstruction,
				(_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => DefenderRoleState.Awake);
		var targetSelectionWait = RecoverableWait<
				DefenderRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				DefenderRoleState.Awake,
				DefenderRoleState.AwaitingTargetSelection,
				ModeratorInstructionSemantic.SelectDefenderTarget,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateTargetSelectionInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.SelectDefenderTarget &&
					HasExpectedAffectedActingPlayer(session, instruction),
				ValidateTargetSelectionInstruction,
				(session, _, cursor) =>
					ValidateIdentificationHandoff(session, cursor),
				static _ => DefenderRoleState.AwaitingTargetSelection);
		var replayableSleepWait = RecoverableWait<
				DefenderRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				DefenderRoleState.Awake,
				DefenderRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.PutRoleToSleep &&
					CountProtectionCommitsThisNight(session, 2) == 0 &&
					HasExpectedAffectedActingPlayer(session, instruction),
				ValidateReplayableSleepInstruction,
				(session, _, cursor) =>
					ValidateIdentificationHandoff(session, cursor),
				static _ => DefenderRoleState.ReadyToSleep);
		var committedSleepWait = RecoverableWait<
				DefenderRoleState,
				ConfirmationInstruction>
			.RecurringDomainDurable(
				Id,
				GameHook.NightMainActionLoop,
				DefenderRoleState.AwaitingTargetSelection,
				DefenderRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.PutRoleToSleep &&
					CountProtectionCommitsThisNight(session, 2) == 1 &&
					HasExpectedAffectedActingPlayer(session, instruction),
				ValidateCommittedSleepInstruction,
				ValidateRecurringRecoveryCursor,
				static _ => DefenderRoleState.ReadyToSleep,
				TryValidateCommittedRecoveryBoundary);

		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				identificationWait,
				wakeWait,
				targetSelectionWait,
				replayableSleepWait,
				committedSleepWait,
				new RoleWorkflowDecisionStep<DefenderRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) => BeginCall(
						session,
						input,
						identificationWait,
						wakeWait)),
				new RoleWorkflowDecisionStep<DefenderRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					DefenderRoleState.Awake,
					static _ => true,
					(session, input) => PrepareNightPower(
						session,
						input,
						targetSelectionWait,
						replayableSleepWait)),
				new RoleWorkflowDecisionStep<DefenderRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					DefenderRoleState.AwaitingTargetSelection,
					static _ => true,
					(session, input) => CommitTargetSelection(
						session,
						input,
						committedSleepWait)),
				new RoleWorkflowCompletionStep<DefenderRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					DefenderRoleState.ReadyToSleep,
					DefenderRoleState.Asleep,
					static _ => true),
				new RoleWorkflowCompletionStep<DefenderRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					DefenderRoleState.Asleep,
					DefenderRoleState.Asleep,
					static _ => true)
			]);
	}

	internal override string PublicName => GameStrings.DefenderRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Defender);

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
			session.Execution.GetCurrentListenerState<DefenderRoleState>(Id));

	internal static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		ActorBorrowedDefenderProtectionCommit committedProtection,
		ModeratorInstruction nextInstruction)
	{
		if (committedProtection.PowerIdentity.SourceRole !=
		    MainRoleType.Defender)
		{
			return false;
		}

		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			throw new InvalidOperationException(
				"No active Actor borrowed Defender Role Power is available for recovery.");
		}

		ValidateCommittedBorrowedProtection(
			session,
			execution,
			committedProtection);
		if (startingInstruction is not SelectPlayersInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.SelectDefenderTarget
		    } targetSelection ||
		    input.InstructionId != targetSelection.InstructionId ||
		    input.Type != ExpectedInputType.PlayerSelection ||
		    input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds ||
		    selectedPlayerIds.Single() != committedProtection.TargetPlayerId ||
		    !targetSelection.SelectablePlayerIds.Contains(
			    committedProtection.TargetPlayerId) ||
		    nextInstruction is not ConfirmationInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.PutRoleToSleep
		    } sleep)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Defender commit must correlate to its accepted target and exact Actor sleep continuation.");
		}

		ValidateBorrowedSelectionInstruction(
			session,
			execution,
			targetSelection);
		ValidateBorrowedSleep(execution, sleep);
		return true;
	}

	private static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		RecurringRolePowerCommittedLogEntry committedEntry,
		ConfirmationInstruction nextInstruction)
	{
		if (committedEntry.ActionType !=
		    NightActionType.DefenderProtect)
		{
			return false;
		}

		if (committedEntry.TargetIds is not [var committedTargetId] ||
		    startingInstruction is not SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectDefenderTarget,
			    CountConstraint: var countConstraint,
			    AffectedPlayerIds: { Count: 1 } affectedPlayerIds,
			    RoleIdentification: null
		    } targetSelection ||
		    countConstraint != NumberRangeConstraint.Single ||
		    input.SelectedPlayerIds is not
			    { Count: 1 } selectedPlayerIds ||
		    selectedPlayerIds.Single() != committedTargetId ||
		    !targetSelection.SelectablePlayerIds.Contains(
			    committedTargetId) ||
		    nextInstruction.AffectedPlayerIds is not
			    { Count: 1 } sleepAffectedPlayerIds ||
		    sleepAffectedPlayerIds.Single() !=
			    affectedPlayerIds.Single())
		{
			throw new InvalidOperationException(
				"The Defender commit must correlate to its accepted target and exact sleep continuation.");
		}

		ValidateCommittedProtection(session, committedEntry);
		if (committedEntry.ActingPlayerId !=
		    affectedPlayerIds.Single())
		{
			throw new InvalidOperationException(
				"The Defender commit does not belong to the instructed Role holder.");
		}

		return true;
	}

	private static void ValidateRecurringRecoveryCursorIdentity(
		GameSession session,
		DomainRecoveryCursor cursor)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(cursor);
		if (cursor.Kind !=
		    DomainRecoveryCursorKind.RecurringNativeRolePowerCommit ||
		    cursor.SourceRole != MainRoleType.Defender ||
		    cursor.CommittedActionType !=
		    NightActionType.DefenderProtect ||
		    cursor.ActingPlayerId == Guid.Empty ||
		    !StringComparer.Ordinal.Equals(
			    cursor.SourcePowerIdentifier,
			    ProtectionPowerIdentifier.Value) ||
		    cursor.PowerIdentity is not { } powerIdentity ||
		    cursor.OneUseResourceId != Guid.Empty)
		{
			throw new InvalidOperationException(
				"The Defender recovery cursor has an invalid recurring Role Power identity.");
		}

		if (powerIdentity.PowerInstanceOrigin ==
		    RolePowerInstanceOrigin.Borrowed)
		{
			ValidateBorrowedRecoveryCursorIdentity(
				session,
				cursor,
				powerIdentity);
			return;
		}

		if (cursor.ActorSetupCardId != Guid.Empty ||
		    cursor.ActorBorrowedActivationId != Guid.Empty ||
		    powerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(cursor.ActingPlayerId)))
		{
			throw new InvalidOperationException(
				"The Defender recovery cursor has an invalid recurring Role Power identity.");
		}

		var commits = GetProtectionCommitsThisNight(session)
			.Where(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.TargetIds is { Count: 1 } targetIds &&
				cursor.CommittedTargetIds.SequenceEqual(targetIds))
			.ToArray();
		if (commits is not [var committedAction])
		{
			throw new InvalidOperationException(
				"The Defender recovery cursor does not match one recurring protection action.");
		}

		ValidateCommittedProtection(session, committedAction);
	}

	private void ValidateRecurringRecoveryCursor(
		GameSession session,
		ConfirmationInstruction instruction,
		DomainRecoveryCursor cursor) =>
		ValidateRecurringRecoveryCursorIdentity(session, cursor);

	private HookListenerActionResult BeginCall(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<DefenderRoleState, SelectPlayersInstruction>
			identificationWait,
		RecoverableWait<DefenderRoleState, ConfirmationInstruction>
			wakeWait) =>
		!TryResolveBorrowedExecution(session, out _) &&
		!IsCompleteHolderSetKnown(session)
			? identificationWait.Execute(session, input)
			: wakeWait.Execute(session, input);

	private HookListenerActionResult PrepareNightPower(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<DefenderRoleState, SelectPlayersInstruction>
			targetSelectionWait,
		RecoverableWait<DefenderRoleState, ConfirmationInstruction>
			sleepWait)
	{
		if (!TryResolveBorrowedExecution(session, out _) &&
		    !IsCompleteHolderSetKnown(session))
		{
			IdentifyCompleteLivingRoleHolderSet(
				session,
				input.SelectedPlayerIds?.ToHashSet()
				?? throw new InvalidOperationException(
					"Defender identification requires a Player selection."));
		}

		var execution = ResolveExecution(session);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				execution.ActingPlayer,
				MainRoleType.Defender,
				ProtectionPower,
				execution.PowerInstance));
		return availability.AvailabilityResult.IsAvailable &&
		       GetEligibleTargets(
			       session,
			       CreatePowerIdentity(execution)).Count > 0
			? targetSelectionWait.Execute(session, input)
			: sleepWait.Execute(session, input);
	}

	private HookListenerActionResult CommitTargetSelection(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<DefenderRoleState, ConfirmationInstruction>
			committedSleepWait)
	{
		var execution = ResolveExecution(session);
		if (input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds)
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "The Defender must select exactly one Player.");
		}

		var powerIdentity = CreatePowerIdentity(execution);
		var hasCommittedProtection = execution.IsBorrowed
			? GetBorrowedProtectionCommitsThisNight(session, powerIdentity).Any()
			: GetProtectionCommitsThisNight(session).Any();
		if (hasCommittedProtection)
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "Only one Defender protection may be committed per Night.");
		}

		var targetId = selectedPlayerIds.Single();
		if (!GetEligibleTargets(session, powerIdentity).Contains(targetId))
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
				? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "The Defender target must be one legal living Player.");
		}

		if (execution.IsBorrowed)
		{
			session.CommitActorBorrowedDefenderProtection(
				powerIdentity,
				targetId);
		}
		else
		{
			session.CommitRecurringRolePowerNightAction(
				NightActionType.DefenderProtect,
				targetId,
				powerIdentity);
		}

		return committedSleepWait.Execute(session, input);
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
			roleIdentification: MainRoleType.Defender);
	}

	private ConfirmationInstruction CreateWakeInstruction(GameSession session)
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

	private SelectPlayersInstruction CreateTargetSelectionInstruction(
		GameSession session)
	{
		var execution = ResolveExecution(session);
		var eligibleTargets = GetEligibleTargets(
			session,
			CreatePowerIdentity(execution));
		if (eligibleTargets.Count == 0)
		{
			throw new InvalidOperationException(
				"Defender target selection requires one legal living Player.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectDefenderTarget,
			selectablePlayerIds: eligibleTargets,
			countConstraint: NumberRangeConstraint.Single,
			privateInstruction:
				GameStrings.DefenderTargetSelectionInstruction,
			affectedPlayerIds: [execution.ActingPlayer.Id]);
	}

	private ConfirmationInstruction CreateSleepInstruction(GameSession session)
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

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		if (TryResolveBorrowedExecution(session, out _) ||
		    instruction.RoleIdentification != MainRoleType.Defender ||
		    instruction.AffectedPlayerIds != null ||
		    roleCount <= 0 ||
		    instruction.CountConstraint !=
			    NumberRangeConstraint.Exact(roleCount) ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)))
		{
			throw new InvalidOperationException(
				"The Defender identification instruction has invalid workflow context.");
		}
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
		    !HasExpectedAffectedActingPlayer(session, instruction))
		{
			throw new InvalidOperationException(
				"The Defender wake instruction has invalid workflow context.");
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
		    instruction.PublicAnnouncement != null ||
		    instruction.PrivateInstruction !=
			    GameStrings.DefenderTargetSelectionInstruction ||
		    instruction.CountConstraint != NumberRangeConstraint.Single ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetEligibleTargets(
				    session,
				    CreatePowerIdentity(execution))) ||
		    instruction.SelectablePlayerIds.Count == 0 ||
		    !HasExpectedAffectedActingPlayer(session, instruction))
		{
			throw new InvalidOperationException(
				"The Defender target selection has invalid workflow context.");
		}
	}

	private void ValidateReplayableSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		var execution = ResolveExecution(session);
		ValidateSleepInstructionShape(session, execution, instruction);
		if (CountProtectionCommitsThisNight(session, 1) != 0)
		{
			throw new InvalidOperationException(
				"The Defender replayable sleep has invalid workflow context.");
		}
	}

	private void ValidateCommittedSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		var execution = ResolveExecution(session);
		ValidateSleepInstructionShape(session, execution, instruction);
		if (execution.IsBorrowed)
		{
			var borrowedCommits = GetBorrowedProtectionCommitsThisNight(
					session,
					CreatePowerIdentity(execution))
				.ToArray();
			if (borrowedCommits.Length > 1)
			{
				throw new InvalidOperationException(
					"The pending Actor borrowed Defender sleep instruction has multiple private protection commits.");
			}

			if (borrowedCommits is not [var borrowedCommit])
			{
				throw new InvalidOperationException(
					"The pending Actor borrowed Defender sleep instruction requires its committed private protection.");
			}

			ValidateCommittedBorrowedProtection(
				session,
				execution,
				borrowedCommit);
			return;
		}

		var commits = GetProtectionCommitsThisNight(session).ToArray();
		if (commits.Length > 1)
		{
			throw new InvalidOperationException(
				"The pending Defender sleep instruction has multiple protection commits.");
		}

		if (commits is not [var commit])
		{
			throw new InvalidOperationException(
				"The pending Defender sleep instruction requires its committed protection.");
		}

		ValidateCommittedProtection(session, commit);
		if (instruction.AffectedPlayerIds is not { Count: 1 } affectedPlayerIds ||
		    affectedPlayerIds.Single() != commit.ActingPlayerId)
		{
			throw new InvalidOperationException(
				"The pending Defender sleep instruction does not belong to the committed protection.");
		}
	}

	private void ValidateSleepInstructionShape(
		GameSession session,
		ExecutionContext execution,
		ConfirmationInstruction instruction)
	{
		if (execution.IsBorrowed)
		{
			ValidateBorrowedSleep(execution, instruction);
			return;
		}

		if (instruction.PublicAnnouncement !=
			    GameStrings.RoleGoesToSleepSingle.Format(PublicName) ||
		    instruction.PrivateInstruction != null ||
		    !HasExpectedAffectedActingPlayer(session, instruction))
		{
			throw new InvalidOperationException(
				"The Defender sleep instruction has invalid workflow context.");
		}
	}

	private static void ValidateCallHandoff(
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.Defender)
		{
			throw new InvalidOperationException(
				"The Defender call has invalid accepted-observation handoff context.");
		}
	}

	private void ValidateIdentificationHandoff(
		GameSession session,
		AcceptedObservationRecoveryCursor cursor)
	{
		if (TryResolveBorrowedExecution(session, out _) ||
		    cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.Defender ||
		    cursor.ObservedRole != MainRoleType.Defender ||
		    cursor.AcceptedObservationSemantic !=
			    ModeratorInstructionSemantic.IdentifyRoleHolders ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The Defender continuation has invalid accepted-observation handoff context.");
		}

		if (!RoleFactionKnowledge.HasAcceptedRoleIdentification(
			    session,
			    MainRoleType.Defender))
		{
			throw new InvalidOperationException(
				"The Defender identification continuation has invalid durable context.");
		}
	}

	private bool IsCompleteHolderSetKnown(GameSession session) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			MainRoleType.Defender);

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.Defender ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == MainRoleType.Defender ||
				  player.State.ModeratorKnownRole == null &&
				  RoleFactionKnowledge.GetPossibleRoles(session, player.Id)
					  .Contains(MainRoleType.Defender))))
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

	private int CountProtectionCommitsThisNight(
		GameSession session,
		int limit) =>
		TryResolveBorrowedExecution(session, out var borrowedExecution)
			? GetBorrowedProtectionCommitsThisNight(
					session,
					CreatePowerIdentity(borrowedExecution))
				.Take(limit)
				.Count()
			: GetProtectionCommitsThisNight(session).Take(limit).Count();

	private ExecutionContext ResolveExecution(GameSession session) =>
		TryResolveBorrowedExecution(session, out var borrowed)
			? borrowed
			: ResolveNativeExecution(session);

	private ExecutionContext ResolveNativeExecution(GameSession session)
	{
		var holder = GetHolder(session);
		return new ExecutionContext(
			holder,
			RolePowerInstance.CreateCurrent(
				session,
				holder,
				MainRoleType.Defender,
				ProtectionPower),
			IsBorrowed: false);
	}

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.Defender)
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
				MainRoleType.Defender,
				ProtectionPower),
			IsBorrowed: true);
		return true;
	}

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		ExecutionContext execution) => new(
			execution.ActingPlayer.Id,
			MainRoleType.Defender,
			ProtectionPower.Identifier.Value,
			execution.PowerInstance.Id,
			execution.PowerInstance.Origin);

	private IPlayer GetHolder(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException(
			"No living Defender is available.");

	private static HashSet<Guid> GetEligibleTargets(
		GameSession session,
		RolePowerInstanceIdentity powerIdentity)
	{
		var eligibleTargets = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole != MainRoleType.LittleGirl)
			.Select(player => player.Id)
			.ToHashSet();
		if (session.TurnNumber <= 1)
		{
			return eligibleTargets;
		}

		var previousMatchingTargetIds =
			powerIdentity.PowerInstanceOrigin ==
			RolePowerInstanceOrigin.Borrowed
				? session.GetActorBorrowedDefenderProtectionCommits()
					.Where(commit =>
						commit.CurrentPhase == GamePhase.Night &&
						commit.TurnNumber == session.TurnNumber - 1 &&
						commit.PowerIdentity == powerIdentity)
					.Select(commit => commit.TargetPlayerId)
					.ToArray()
				: session.GameHistoryLog
					.OfType<RecurringRolePowerCommittedLogEntry>()
					.Where(entry =>
						entry.CurrentPhase == GamePhase.Night &&
						entry.TurnNumber == session.TurnNumber - 1 &&
						entry.ActionType == NightActionType.DefenderProtect &&
						entry.PowerIdentity == powerIdentity)
					.SelectMany(entry => entry.TargetIds ?? [])
					.ToArray();
		if (previousMatchingTargetIds.Length > 1)
		{
			throw new InvalidOperationException(
				"The immediately preceding Night contains multiple protections for one Defender power instance.");
		}

		if (previousMatchingTargetIds is [var previousTargetId])
		{
			eligibleTargets.Remove(previousTargetId);
		}

		return eligibleTargets;
	}

	private static IEnumerable<RecurringRolePowerCommittedLogEntry>
		GetProtectionCommitsThisNight(GameSession session) =>
		GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				[NightActionType.DefenderProtect])
			.OfType<RecurringRolePowerCommittedLogEntry>();

	private static IEnumerable<ActorBorrowedDefenderProtectionCommit>
		GetBorrowedProtectionCommitsThisNight(
			GameSession session,
			RolePowerInstanceIdentity powerIdentity) =>
		session.GetActorBorrowedDefenderProtectionCommits()
			.Where(commit =>
				commit.CurrentPhase == GamePhase.Night &&
				commit.TurnNumber == session.TurnNumber &&
				commit.PowerIdentity == powerIdentity);

	private static void ValidateBorrowedRecoveryCursorIdentity(
		GameSession session,
		DomainRecoveryCursor cursor,
		RolePowerInstanceIdentity cursorPowerIdentity)
	{
		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Defender recovery cursor has no active borrowed execution.");
		}

		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		var expectedPowerIdentity = CreatePowerIdentity(execution);
		var commits = GetBorrowedProtectionCommitsThisNight(
				session,
				expectedPowerIdentity)
			.ToArray();
		if (cursorPowerIdentity != expectedPowerIdentity ||
		    cursor.ActorSetupCardId != activation.SelectedCardId ||
		    cursor.ActorBorrowedActivationId != activation.ActivationId ||
		    cursor.CommittedTargetIds is not [var committedTargetId] ||
		    cursor.NextInstructionSemantic !=
		    ModeratorInstructionSemantic.PutRoleToSleep ||
		    cursor.NextInstructionId == Guid.Empty ||
		    commits is not [var commit] ||
		    commit.TargetPlayerId != committedTargetId)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Defender recovery cursor has an invalid borrowed Role Power identity.");
		}

		ValidateCommittedBorrowedProtection(session, execution, commit);
	}

	private static void ValidateCommittedBorrowedProtection(
		GameSession session,
		ExecutionContext execution,
		ActorBorrowedDefenderProtectionCommit committedProtection)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		var expectedPowerIdentity = CreatePowerIdentity(execution);
		var publicMarker = committedProtection.PublicMarkerLogIndex >= 0
			? session.GameHistoryLog.ElementAtOrDefault(
				committedProtection.PublicMarkerLogIndex)
			: null;
		if (!execution.IsBorrowed ||
		    activation?.SourceRole != MainRoleType.Defender ||
		    committedProtection.PowerIdentity != expectedPowerIdentity ||
		    committedProtection.ActorSetupCardId != activation.SelectedCardId ||
		    committedProtection.TargetPlayerId == Guid.Empty ||
		    committedProtection.TurnNumber != session.TurnNumber ||
		    committedProtection.CurrentPhase != GamePhase.Night ||
		    publicMarker is not ActorBorrowedRolePowerCommittedLogEntry marker ||
		    marker.Timestamp != committedProtection.Timestamp ||
		    marker.TurnNumber != committedProtection.TurnNumber ||
		    marker.CurrentPhase != committedProtection.CurrentPhase)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Defender recovery boundary requires one correlated private protection commit and sanitized public marker.");
		}

		var actor = session.GetPlayer(expectedPowerIdentity.ActingPlayerId);
		if (actor.State.Health != PlayerHealth.Alive ||
		    actor.State.CurrentRole != MainRoleType.Actor ||
		    !GetEligibleTargets(session, expectedPowerIdentity).Contains(
			    committedProtection.TargetPlayerId))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Defender protection does not belong to the living Actor or a legal target.");
		}
	}

	private static void ValidateBorrowedWake(
		ExecutionContext execution,
		ConfirmationInstruction wake)
	{
		if (wake.PublicAnnouncement !=
			    GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName) ||
		    wake.PrivateInstruction is not null ||
		    wake.AffectedPlayerIds is not [var affectedPlayerId] ||
		    affectedPlayerId != execution.ActingPlayer.Id)
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
		if (selection.CountConstraint != NumberRangeConstraint.Single ||
		    selection.RoleIdentification is not null ||
		    selection.PublicAnnouncement is not null ||
		    selection.PrivateInstruction !=
		    GameStrings.DefenderTargetSelectionInstruction ||
		    selection.AffectedPlayerIds is not [var affectedPlayerId] ||
		    affectedPlayerId != execution.ActingPlayer.Id ||
		    !selection.SelectablePlayerIds.ToHashSet().SetEquals(
			    GetEligibleTargets(session, CreatePowerIdentity(execution))))
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
		    sleep.AffectedPlayerIds is not [var affectedPlayerId] ||
		    affectedPlayerId != execution.ActingPlayer.Id)
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	private static void ValidateCommittedProtection(
		GameSession session,
		RecurringRolePowerCommittedLogEntry committedEntry)
	{
		if (committedEntry.ActionType !=
			    NightActionType.DefenderProtect ||
		    committedEntry.TargetIds is not [var targetId] ||
		    committedEntry.SourceRole != MainRoleType.Defender ||
		    !StringComparer.Ordinal.Equals(
			    committedEntry.SourcePowerIdentifier,
			    ProtectionPowerIdentifier.Value) ||
		    committedEntry.PowerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(committedEntry.ActingPlayerId)))
		{
			throw new InvalidOperationException(
				"The Defender recovery boundary requires one owned recurring protection action.");
		}

		var holder = session.GetPlayer(committedEntry.ActingPlayerId);
		if (holder.State.Health != PlayerHealth.Alive ||
		    holder.State.CurrentRole != MainRoleType.Defender ||
		    !GetEligibleTargets(
			    session,
			    committedEntry.PowerIdentity).Contains(targetId))
		{
			throw new InvalidOperationException(
				"The Defender protection does not belong to the living current holder or a legal target.");
		}
	}

	private static RolePowerInstanceIdentity CreateCurrentPowerIdentity(
		GameSession session,
		IPlayer holder) =>
		RolePowerInstance.CreateCurrentIdentity(
			session,
			holder,
			MainRoleType.Defender,
			ProtectionPower);
}
