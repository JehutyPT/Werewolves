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

internal enum WitchRoleState
{
	Awake,
	AwaitingHealingSelection,
	AwaitingPoisonSelection,
	AwaitingPoisonSelectionAfterCommit,
	ReadyToSleep,
	ReadyToSleepAfterCommit,
	Asleep
}

internal sealed class WitchRole
	: RoleHookListener,
		IDeclaredRoleWorkflow
{
	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		bool IsBorrowed);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _workflowRuntime;

	private static readonly RolePowerDefinition PotionsPower = new(
		new RolePowerIdentifier("witch-potions"),
		RolePowerCategory.Chosen);

	internal static readonly Guid HealingResourceId =
		Guid.Parse("a9b9d885-3edc-4671-bec8-1ddabbe4de3e");

	internal static readonly Guid PoisonResourceId =
		Guid.Parse("da29bd31-bbe8-4abc-bb12-87b15df6df38");

	internal WitchRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var identificationWait = RecoverableWait<
				WitchRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				WitchRoleState.Awake,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateIdentificationInstruction,
				static (_, instruction) =>
					instruction is SelectPlayersInstruction
					{
						RoleIdentification: MainRoleType.Witch
					},
				ValidateIdentificationInstruction,
				(_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => WitchRoleState.Awake);
		var wakeWait = RecoverableWait<
				WitchRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				WitchRoleState.Awake,
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
				static _ => WitchRoleState.Awake);
		var healingWait = RecoverableWait<
				WitchRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				WitchRoleState.Awake,
				WitchRoleState.AwaitingHealingSelection,
				ModeratorInstructionSemantic.SelectWitchHealingTarget,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateHealingInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.SelectWitchHealingTarget &&
					HasExpectedAffectedActingPlayer(session, instruction),
				ValidateHealingInstruction,
				(session, _, cursor) =>
					ValidateIdentificationHandoff(session, cursor),
				static _ => WitchRoleState.AwaitingHealingSelection);
		var replayablePoisonWait = RecoverableWait<
				WitchRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				WitchRoleState.Awake,
				WitchRoleState.AwaitingPoisonSelection,
				ModeratorInstructionSemantic.SelectWitchPoisonTarget,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateReplayablePoisonInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.SelectWitchPoisonTarget &&
					HasExpectedAffectedActingPlayer(session, instruction),
				ValidateReplayablePoisonInstruction,
				(session, _, cursor) =>
					ValidateIdentificationHandoff(session, cursor),
				static _ => WitchRoleState.AwaitingPoisonSelection);
		var committedPoisonWait = RecoverableWait<
				WitchRoleState,
				SelectPlayersInstruction>
			.OneUseDomainDurable(
				Id,
				GameHook.NightMainActionLoop,
				WitchRoleState.AwaitingHealingSelection,
				WitchRoleState.AwaitingPoisonSelectionAfterCommit,
				ModeratorInstructionSemantic.SelectWitchPoisonTarget,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateCommittedPoisonInstruction,
				static (_, _) => false,
				ValidateCommittedPoisonInstruction,
				(session, instruction, cursor) => ValidateDomainRecoveryCursor(
					session,
					instruction,
					cursor,
					allowsCommittedPoisonDecision: false),
				static _ => WitchRoleState.AwaitingPoisonSelectionAfterCommit,
				(session, startingInstruction, input, committedEntry, next) =>
					TryValidateCommittedRecoveryBoundary(
						session,
						startingInstruction,
						input,
						committedEntry,
						next));
		var replayableSleepWait = RecoverableWait<
				WitchRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				WitchRoleState.Awake,
				WitchRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.PutRoleToSleep &&
					HasExpectedAffectedActingPlayer(session, instruction),
				ValidateSleepInstruction,
				(session, _, cursor) =>
					ValidateIdentificationHandoff(session, cursor),
				static _ => WitchRoleState.ReadyToSleep);
		var committedSleepWait = RecoverableWait<
				WitchRoleState,
				ConfirmationInstruction>
			.OneUseDomainDurable(
				Id,
				GameHook.NightMainActionLoop,
				WitchRoleState.AwaitingPoisonSelection,
				WitchRoleState.ReadyToSleepAfterCommit,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				static (_, _) => false,
				ValidateSleepInstruction,
				(session, instruction, cursor) => ValidateDomainRecoveryCursor(
					session,
					instruction,
					cursor,
					allowsCommittedPoisonDecision: true),
				static _ => WitchRoleState.ReadyToSleepAfterCommit,
				(session, startingInstruction, input, committedEntry, next) =>
					TryValidateCommittedRecoveryBoundary(
						session,
						startingInstruction,
						input,
						committedEntry,
						next));

		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				identificationWait,
				wakeWait,
				healingWait,
				replayablePoisonWait,
				committedPoisonWait,
				replayableSleepWait,
				committedSleepWait,
				new RoleWorkflowDecisionStep<WitchRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) => BeginCall(
						session,
						input,
						identificationWait,
						wakeWait)),
				new RoleWorkflowDecisionStep<WitchRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					WitchRoleState.Awake,
					static _ => true,
					(session, input) => PrepareNightPower(
						session,
						input,
						healingWait,
						replayablePoisonWait,
						replayableSleepWait)),
				new RoleWorkflowDecisionStep<WitchRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					WitchRoleState.AwaitingHealingSelection,
					static _ => true,
					(session, input) => CommitHealingSelection(
						session,
						input,
						committedPoisonWait,
						replayablePoisonWait,
						committedSleepWait,
						replayableSleepWait)),
				new RoleWorkflowDecisionStep<WitchRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					WitchRoleState.AwaitingPoisonSelection,
					static _ => true,
					(session, input) => CommitPoisonSelection(
						session,
						input,
						committedSleepWait,
						replayableSleepWait)),
				new RoleWorkflowDecisionStep<WitchRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					WitchRoleState.AwaitingPoisonSelectionAfterCommit,
					static _ => true,
					(session, input) => CommitPoisonSelection(
						session,
						input,
						committedSleepWait,
						replayableSleepWait)),
				new RoleWorkflowCompletionStep<WitchRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					WitchRoleState.ReadyToSleep,
					WitchRoleState.Asleep,
					static _ => true),
				new RoleWorkflowCompletionStep<WitchRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					WitchRoleState.ReadyToSleepAfterCommit,
					WitchRoleState.Asleep,
					static _ => true),
				new RoleWorkflowCompletionStep<WitchRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					WitchRoleState.Asleep,
					WitchRoleState.Asleep,
					static _ => true)
			]);
	}

	internal override string PublicName => GameStrings.WitchRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Witch);

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_workflowRuntime;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		var hasBorrowedExecution =
			TryResolveBorrowedExecution(session, out var execution);
		if (session.Execution.GetCurrentListenerState<WitchRoleState>(Id) ==
		    null)
		{
			if (!hasBorrowedExecution &&
			    GetAliveRolePlayers(session)?.SingleOrDefault() is { } witch)
			{
				execution = new ExecutionContext(
					witch,
					CreatePowerInstance(session, witch),
					IsBorrowed: false);
			}

			if (execution is not null &&
			    IsSpent(
				    session,
				    CreateResourceIdentity(
					    execution.ActingPlayer,
					    execution.PowerInstance,
					    HealingResourceId)) &&
			    IsSpent(
				    session,
				    CreateResourceIdentity(
					    execution.ActingPlayer,
					    execution.PowerInstance,
					    PoisonResourceId)))
			{
				return HookListenerActionResult.Skip();
			}
		}

		if (hasBorrowedExecution)
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
			session.Execution.GetCurrentListenerState<WitchRoleState>(Id));

	private bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		OneUseRolePowerCommittedLogEntry committedEntry,
		ModeratorInstruction nextInstruction)
	{
		if (committedEntry.ActionType is not
		    (NightActionType.WitchSave or NightActionType.WitchKill))
		{
			return false;
		}

		if (!TryResolveExecution(session, out var execution) ||
		    execution.IsBorrowed)
		{
			throw new InvalidOperationException(
				"No native Witch potion execution is available for recovery.");
		}

		var isHealing = committedEntry.ActionType == NightActionType.WitchSave;
		var expectedResourceIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			isHealing ? HealingResourceId : PoisonResourceId);
		var expectedSemantic = isHealing
			? ModeratorInstructionSemantic.SelectWitchHealingTarget
			: ModeratorInstructionSemantic.SelectWitchPoisonTarget;
		if (committedEntry.ResourceIdentity != expectedResourceIdentity ||
		    committedEntry.TargetIds is not [var committedTargetId] ||
		    startingInstruction is not SelectPlayersInstruction selection ||
		    selection.Semantic != expectedSemantic ||
		    selection.CountConstraint !=
			    NumberRangeConstraint.SingleOptional ||
		    !selection.SelectablePlayerIds.Contains(committedTargetId) ||
		    input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds ||
		    selectedPlayerIds.Single() != committedTargetId ||
		    nextInstruction.AffectedPlayerIds is not
			    { Count: 1 } affectedPlayerIds ||
		    affectedPlayerIds.Single() != committedEntry.ActingPlayerId)
		{
			throw new InvalidOperationException(
				"The Witch commit must correlate to its accepted potion target and exact continuation.");
		}

		return true;
	}

	/// <summary>
	/// Authenticates a committed potion decision cursor. Only a healing
	/// decision can precede the poison selection, so the poison continuation
	/// refuses a cursor that claims a committed poison use.
	/// </summary>
	private void ValidateDomainRecoveryCursor(
		GameSession session,
		ModeratorInstruction instruction,
		DomainRecoveryCursor cursor,
		bool allowsCommittedPoisonDecision)
	{
		ArgumentNullException.ThrowIfNull(cursor);
		var execution = ResolveExecution(session);
		var committedResourceId = cursor.CommittedActionType switch
		{
			NightActionType.WitchSave => HealingResourceId,
			NightActionType.WitchKill when allowsCommittedPoisonDecision =>
				PoisonResourceId,
			_ => (Guid?)null
		};
		if (cursor.SourceRole != MainRoleType.Witch ||
		    committedResourceId is not { } resourceId ||
		    cursor.ResourceIdentity is not { } resourceIdentity ||
		    resourceIdentity != CreateResourceIdentity(
			    execution.ActingPlayer,
			    execution.PowerInstance,
			    resourceId))
		{
			throw new InvalidOperationException(
				"The Witch recovery cursor has an invalid One-Use Role Power identity.");
		}

		var matchesCommittedDecision = cursor.Kind switch
		{
			DomainRecoveryCursorKind.OneUseRolePowerCommit =>
				!execution.IsBorrowed &&
				cursor.ActorSetupCardId == Guid.Empty &&
				cursor.ActorBorrowedActivationId == Guid.Empty &&
				cursor.CommittedTargetIds is [var committedTargetId] &&
				GetNativePotionCommitsThisNight(session).Any(entry =>
					entry.ActionType == cursor.CommittedActionType &&
					entry.ResourceIdentity == resourceIdentity &&
					entry.TargetIds is [var targetId] &&
					targetId == committedTargetId),
			DomainRecoveryCursorKind.ActorBorrowedWitchPotionUseCommit =>
				execution.IsBorrowed &&
				cursor.CommittedTargetIds is [var borrowedTargetId] &&
				GetBorrowedPotionUseCommitsThisNight(session, execution).Any(
					commit =>
						commit.SpentResourceIdentity == resourceIdentity &&
						commit.TargetPlayerId == borrowedTargetId),
			DomainRecoveryCursorKind.ActorBorrowedWitchPotionDeclineCommit =>
				execution.IsBorrowed &&
				cursor.CommittedTargetIds.Count == 0 &&
				GetBorrowedPotionDeclineCommitsThisNight(session, execution)
					.Any(commit =>
						commit.OfferedResourceIdentity == resourceIdentity),
			_ => false
		};
		if (!matchesCommittedDecision)
		{
			throw new InvalidOperationException(
				"The Witch recovery cursor does not match one committed potion decision.");
		}
	}

	private HookListenerActionResult BeginCall(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<WitchRoleState, SelectPlayersInstruction>
			identificationWait,
		RecoverableWait<WitchRoleState, ConfirmationInstruction> wakeWait) =>
		!TryResolveBorrowedExecution(session, out _) &&
		!IsCompleteHolderSetKnown(session)
			? identificationWait.Execute(session, input)
			: wakeWait.Execute(session, input);

	private HookListenerActionResult PrepareNightPower(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<WitchRoleState, SelectPlayersInstruction> healingWait,
		RecoverableWait<WitchRoleState, SelectPlayersInstruction> poisonWait,
		RecoverableWait<WitchRoleState, ConfirmationInstruction> sleepWait)
	{
		if (!TryResolveBorrowedExecution(session, out _) &&
		    !IsCompleteHolderSetKnown(session))
		{
			IdentifyCompleteLivingRoleHolderSet(
				session,
				input.SelectedPlayerIds?.ToHashSet()
				?? throw new InvalidOperationException(
					"Witch identification requires a Player selection."));
		}

		var execution = ResolveExecution(session);
		var attackTargets =
			GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		if (attackTargets.Count > 0 &&
		    TryEvaluateAvailableResource(
			    session,
			    execution.ActingPlayer,
			    execution.PowerInstance,
			    HealingResourceId))
		{
			return healingWait.Execute(session, input);
		}

		return IsPoisonSelectionOffered(session, execution, healedTargetId: null)
			? poisonWait.Execute(session, input)
			: sleepWait.Execute(session, input);
	}

	private HookListenerActionResult CommitHealingSelection(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<WitchRoleState, SelectPlayersInstruction>
			committedPoisonWait,
		RecoverableWait<WitchRoleState, SelectPlayersInstruction>
			replayablePoisonWait,
		RecoverableWait<WitchRoleState, ConfirmationInstruction>
			committedSleepWait,
		RecoverableWait<WitchRoleState, ConfirmationInstruction>
			replayableSleepWait)
	{
		var execution = ResolveExecution(session);
		var selectedPlayerIds = input.SelectedPlayerIds
			?? throw new InvalidOperationException(
				execution.IsBorrowed
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "Witch healing requires a Player selection response.");
		if (selectedPlayerIds.Count > 1)
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "The Witch may heal at most one attacked Player.");
		}

		var targetId = selectedPlayerIds.SingleOrDefault();
		if (execution.IsBorrowed &&
		    selectedPlayerIds.Count == 1 &&
		    !GameSessionQueries.GetPhysicalAttackTargetsThisNight(session)
			    .Any(player => player.Id == targetId))
		{
			throw new InvalidOperationException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}

		Guid? healedTargetId = selectedPlayerIds.Count == 0 ? null : targetId;
		var hasCommittedDecision =
			selectedPlayerIds.Count > 0 || execution.IsBorrowed;
		if (selectedPlayerIds.Count > 0)
		{
			CommitPotion(
				session,
				execution,
				HealingResourceId,
				NightActionType.WitchSave,
				targetId);
		}
		else if (execution.IsBorrowed)
		{
			CommitPotionDecline(session, execution, HealingResourceId);
		}

		if (IsPoisonSelectionOffered(session, execution, healedTargetId))
		{
			return hasCommittedDecision
				? committedPoisonWait.Execute(session, input)
				: replayablePoisonWait.Execute(session, input);
		}

		return hasCommittedDecision
			? committedSleepWait.Execute(session, input)
			: replayableSleepWait.Execute(session, input);
	}

	private HookListenerActionResult CommitPoisonSelection(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<WitchRoleState, ConfirmationInstruction>
			committedSleepWait,
		RecoverableWait<WitchRoleState, ConfirmationInstruction>
			replayableSleepWait)
	{
		var execution = ResolveExecution(session);
		var selectedPlayerIds = input.SelectedPlayerIds
			?? throw new InvalidOperationException(
				execution.IsBorrowed
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "Witch poison requires a Player selection response.");
		if (selectedPlayerIds.Count > 1)
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "The Witch may poison at most one living Player.");
		}

		if (execution.IsBorrowed && selectedPlayerIds.Count == 1)
		{
			var borrowedTargetId = selectedPlayerIds.Single();
			var (healedTargetId, _) = ValidateBorrowedPotionUseCommits(
				session,
				execution);
			var target = session.GetPlayer(borrowedTargetId);
			if (target.State.Health != PlayerHealth.Alive ||
			    borrowedTargetId == execution.ActingPlayer.Id ||
			    borrowedTargetId == healedTargetId)
			{
				throw new InvalidOperationException(
					GameStrings.ActorBorrowedRolePowerInvalidResponse);
			}
		}

		var hasCommittedDecision =
			selectedPlayerIds.Count > 0 || execution.IsBorrowed;
		if (selectedPlayerIds.Count > 0)
		{
			CommitPotion(
				session,
				execution,
				PoisonResourceId,
				NightActionType.WitchKill,
				selectedPlayerIds.Single());
		}
		else if (execution.IsBorrowed)
		{
			CommitPotionDecline(session, execution, PoisonResourceId);
		}

		return hasCommittedDecision
			? committedSleepWait.Execute(session, input)
			: replayableSleepWait.Execute(session, input);
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
			roleIdentification: MainRoleType.Witch);
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

	private SelectPlayersInstruction CreateHealingInstruction(
		GameSession session)
	{
		var execution = ResolveExecution(session);
		var attackTargets =
			GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		if (attackTargets.Count == 0)
		{
			throw new InvalidOperationException(
				"Witch healing requires one attacked Player.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectWitchHealingTarget,
			attackTargets.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.SingleOptional,
			privateInstruction:
				GameStrings.WitchHealingSelectionInstruction.Format(
					string.Join(
						", ",
						attackTargets.Select(player => player.Name))),
			affectedPlayerIds: [execution.ActingPlayer.Id])
		{
			EmptySelectionOptionLabel = GameStrings.DeclineOption
		};
	}

	private SelectPlayersInstruction CreateReplayablePoisonInstruction(
		GameSession session) =>
		CreatePoisonInstruction(session, healedTargetId: null);

	private SelectPlayersInstruction CreateCommittedPoisonInstruction(
		GameSession session) =>
		CreatePoisonInstruction(
			session,
			GetHealedTargetId(session, ResolveExecution(session)));

	private SelectPlayersInstruction CreatePoisonInstruction(
		GameSession session,
		Guid? healedTargetId)
	{
		var execution = ResolveExecution(session);
		var poisonCandidates =
			GetPoisonCandidates(session, execution, healedTargetId);
		if (poisonCandidates.Count == 0)
		{
			throw new InvalidOperationException(
				"Witch poison requires one legal living Player.");
		}

		var attackTargets =
			GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var attackRosterWasDisclosed =
			session.Execution.PendingInstruction?.Semantic ==
			ModeratorInstructionSemantic.SelectWitchHealingTarget;
		var privateInstruction =
			attackRosterWasDisclosed || attackTargets.Count == 0
				? GameStrings.WitchPoisonSelectionInstruction
				: GameStrings.WitchAttackTargetsAndPoisonSelectionInstruction
					.Format(
						string.Join(
							", ",
							attackTargets.Select(player => player.Name)));
		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget,
			poisonCandidates,
			NumberRangeConstraint.SingleOptional,
			privateInstruction: privateInstruction,
			affectedPlayerIds: [execution.ActingPlayer.Id])
		{
			EmptySelectionOptionLabel = GameStrings.DeclineOption
		};
	}

	private ConfirmationInstruction CreateSleepInstruction(GameSession session)
	{
		var execution = ResolveExecution(session);
		var attackTargets =
			GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var privateInstruction =
			!WasAttackRosterDisclosed(session, execution) &&
			attackTargets.Count > 0
				? GameStrings.WitchAttackTargetsInstruction.Format(
					string.Join(
						", ",
						attackTargets.Select(player => player.Name)))
				: null;
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleGoesToSleepSingle.Format(
				execution.IsBorrowed
					? GameStrings.ActorRoleName
					: PublicName),
			privateInstruction,
			[execution.ActingPlayer.Id]);
	}

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		if (TryResolveBorrowedExecution(session, out _) ||
		    instruction.RoleIdentification != MainRoleType.Witch ||
		    instruction.AffectedPlayerIds != null ||
		    roleCount <= 0 ||
		    instruction.CountConstraint !=
			    NumberRangeConstraint.Exact(roleCount) ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)))
		{
			throw new InvalidOperationException(
				"The Witch identification instruction has invalid workflow context.");
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
				"The Witch wake instruction has invalid workflow context.");
		}
	}

	private void ValidateHealingInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var execution = ResolveExecution(session);
		if (execution.IsBorrowed)
		{
			ValidateBorrowedHealingInstruction(
				session,
				execution,
				instruction);
			return;
		}

		if (!HasExpectedAffectedActingPlayer(session, instruction))
		{
			throw new InvalidOperationException(
				"The Witch healing selection has invalid workflow context.");
		}
	}

	private void ValidateReplayablePoisonInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var execution = ResolveExecution(session);
		if (execution.IsBorrowed)
		{
			ValidateBorrowedPoisonInstruction(
				session,
				execution,
				instruction,
				healedTargetId: null);
			return;
		}

		if (!HasExpectedAffectedActingPlayer(session, instruction))
		{
			throw new InvalidOperationException(
				"The Witch poison selection has invalid workflow context.");
		}
	}

	private void ValidateCommittedPoisonInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var execution = ResolveExecution(session);
		if (execution.IsBorrowed)
		{
			var (healedTargetId, poisonedTargetId) =
				ValidateBorrowedPotionUseCommits(session, execution);
			if (poisonedTargetId.HasValue)
			{
				throw new RoleWorkflowInputRejectionException(
					GameStrings.ActorBorrowedRolePowerInvalidResponse);
			}

			ValidateBorrowedPoisonInstruction(
				session,
				execution,
				instruction,
				healedTargetId);
			return;
		}

		if (!HasExpectedAffectedActingPlayer(session, instruction))
		{
			throw new InvalidOperationException(
				"The Witch poison selection has invalid workflow context.");
		}
	}

	private void ValidateSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		var execution = ResolveExecution(session);
		if (execution.IsBorrowed)
		{
			ValidateBorrowedPotionUseCommits(session, execution);
			ValidateBorrowedSleep(session, execution, instruction);
			return;
		}

		if (!HasExpectedAffectedActingPlayer(session, instruction))
		{
			throw new InvalidOperationException(
				"The Witch sleep instruction has invalid workflow context.");
		}
	}

	private static void ValidateCallHandoff(
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.Witch)
		{
			throw new InvalidOperationException(
				"The Witch call has invalid accepted-observation handoff context.");
		}
	}

	private void ValidateIdentificationHandoff(
		GameSession session,
		AcceptedObservationRecoveryCursor cursor)
	{
		if (TryResolveBorrowedExecution(session, out _) ||
		    cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.Witch ||
		    cursor.ObservedRole != MainRoleType.Witch ||
		    cursor.AcceptedObservationSemantic !=
			    ModeratorInstructionSemantic.IdentifyRoleHolders ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The Witch continuation has invalid accepted-observation handoff context.");
		}

		var livingHolderIds = GetLivingHolderIds(session);
		if (livingHolderIds.Count == 0 ||
		    !session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			    .Any(entry =>
				    entry.TurnNumber == session.TurnNumber &&
				    entry.CurrentPhase == GamePhase.Night &&
				    entry.Role == MainRoleType.Witch &&
				    entry.PlayerIds.SetEquals(livingHolderIds)))
		{
			throw new InvalidOperationException(
				"The Witch identification continuation has invalid durable context.");
		}
	}

	private bool IsCompleteHolderSetKnown(GameSession session) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			MainRoleType.Witch);

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.Witch ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == MainRoleType.Witch ||
				  player.State.ModeratorKnownRole == null &&
				  GameSessionQueries.GetPossibleRoles(session, player.Id)
					  .Contains(MainRoleType.Witch))))
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

	private bool IsPoisonSelectionOffered(
		GameSession session,
		ExecutionContext execution,
		Guid? healedTargetId) =>
		GetPoisonCandidates(session, execution, healedTargetId).Count > 0 &&
		TryEvaluateAvailableResource(
			session,
			execution.ActingPlayer,
			execution.PowerInstance,
			PoisonResourceId);

	private static HashSet<Guid> GetPoisonCandidates(
		GameSession session,
		ExecutionContext execution,
		Guid? healedTargetId) =>
		session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.Id != execution.ActingPlayer.Id &&
				player.Id != healedTargetId)
			.Select(player => player.Id)
			.ToHashSet();

	private static Guid? GetHealedTargetId(
		GameSession session,
		ExecutionContext execution)
	{
		var healingIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			HealingResourceId);
		if (execution.IsBorrowed)
		{
			return GetBorrowedPotionUseCommitsThisNight(session, execution)
				.Where(commit =>
					commit.SpentResourceIdentity == healingIdentity)
				.Select(commit => (Guid?)commit.TargetPlayerId)
				.FirstOrDefault();
		}

		return GetNativePotionCommitsThisNight(session)
			.Where(entry => entry.ResourceIdentity == healingIdentity)
			.SelectMany(entry => entry.TargetIds ?? [])
			.Select(targetId => (Guid?)targetId)
			.FirstOrDefault();
	}

	/// <summary>
	/// Re-derives whether the attacked-Player roster has already been
	/// disclosed to this Witch execution from Session facts alone, so the
	/// disclosure survives Rehydration without any Listener state.
	/// </summary>
	private static bool WasAttackRosterDisclosed(
		GameSession session,
		ExecutionContext execution)
	{
		var healingIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			HealingResourceId);
		var poisonIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			PoisonResourceId);
		return session.Execution.PendingInstruction?.Semantic is
			       ModeratorInstructionSemantic.SelectWitchHealingTarget or
			       ModeratorInstructionSemantic.SelectWitchPoisonTarget ||
		       (execution.IsBorrowed
			       ? GetBorrowedPotionUseCommitsThisNight(session, execution)
				       .Any(commit =>
					       commit.SpentResourceIdentity == healingIdentity ||
					       commit.SpentResourceIdentity == poisonIdentity)
			       : GetNativePotionCommitsThisNight(session)
				       .Any(entry =>
					       entry.ResourceIdentity == healingIdentity ||
					       entry.ResourceIdentity == poisonIdentity));
	}

	private static IEnumerable<OneUseRolePowerCommittedLogEntry>
		GetNativePotionCommitsThisNight(GameSession session) =>
		session.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Where(entry =>
				entry.TurnNumber == session.TurnNumber &&
				entry.CurrentPhase == GamePhase.Night);

	private static IEnumerable<ActorBorrowedWitchPotionUseCommit>
		GetBorrowedPotionUseCommitsThisNight(
			GameSession session,
			ExecutionContext execution)
	{
		var powerIdentity = CreatePowerIdentity(execution);
		return session.GetActorBorrowedWitchPotionUseCommits()
			.Where(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.TurnNumber == session.TurnNumber &&
				commit.CurrentPhase == GamePhase.Night);
	}

	private static IEnumerable<ActorBorrowedWitchPotionDeclineCommit>
		GetBorrowedPotionDeclineCommitsThisNight(
			GameSession session,
			ExecutionContext execution)
	{
		var powerIdentity = CreatePowerIdentity(execution);
		return session.GetActorBorrowedWitchPotionDeclineCommits()
			.Where(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.TurnNumber == session.TurnNumber &&
				commit.CurrentPhase == GamePhase.Night);
	}

	private bool TryEvaluateAvailableResource(
		GameSession session,
		IPlayer witch,
		RolePowerInstance instance,
		Guid resourceId)
	{
		var resourceIdentity = CreateResourceIdentity(
			witch,
			instance,
			resourceId);
		if (IsSpent(session, resourceIdentity))
		{
			return false;
		}

		var resource = new OneUseRolePowerResource(resourceId, instance);
		return _availabilityGateway.Evaluate(
				new RolePowerAttempt(
					session,
					witch,
					MainRoleType.Witch,
					PotionsPower,
					instance,
					resource))
			.AvailabilityResult.IsAvailable;
	}

	private static bool IsSpent(
		GameSession session,
		OneUseRolePowerResourceIdentity resourceIdentity) =>
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			session,
			resourceIdentity);

	private ExecutionContext ResolveExecution(GameSession session) =>
		TryResolveBorrowedExecution(session, out var borrowedExecution)
			? borrowedExecution
			: ResolveNativeExecution(session);

	private bool TryResolveExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		if (TryResolveBorrowedExecution(session, out execution))
		{
			return true;
		}

		if (GetAliveRolePlayers(session)?.SingleOrDefault() is not { } witch)
		{
			execution = null!;
			return false;
		}

		execution = new ExecutionContext(
			witch,
			CreatePowerInstance(session, witch),
			IsBorrowed: false);
		return true;
	}

	private ExecutionContext ResolveNativeExecution(GameSession session)
	{
		var witch = GetWitch(session);
		return new ExecutionContext(
			witch,
			CreatePowerInstance(session, witch),
			IsBorrowed: false);
	}

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.Witch)
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
				MainRoleType.Witch,
				PotionsPower),
			IsBorrowed: true);
		return true;
	}

	private static RolePowerInstance CreatePowerInstance(
		GameSession session,
		IPlayer witch) =>
		RolePowerInstance.CreateCurrent(
			session,
			witch,
			MainRoleType.Witch,
			PotionsPower);

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		ExecutionContext execution) => new(
			execution.ActingPlayer.Id,
			MainRoleType.Witch,
			PotionsPower.Identifier.Value,
			execution.PowerInstance.Id,
			execution.PowerInstance.Origin);

	private static void ValidateBorrowedWake(
		ExecutionContext execution,
		ConfirmationInstruction wake)
	{
		if (wake.AffectedPlayerIds is not { Count: 1 } affectedPlayerIds ||
		    affectedPlayerIds.Single() != execution.ActingPlayer.Id ||
		    wake.PrivateInstruction is not null ||
		    wake.SoundEffects.Count != 0 ||
		    !StringComparer.Ordinal.Equals(
			    wake.PublicAnnouncement,
			    GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName)))
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	private static void ValidateBorrowedHealingInstruction(
		GameSession session,
		ExecutionContext execution,
		SelectPlayersInstruction selection)
	{
		var (healedTargetId, poisonedTargetId) =
			ValidateBorrowedPotionUseCommits(session, execution);
		var attackTargets =
			GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var expectedTargets = attackTargets
			.Select(player => player.Id)
			.ToHashSet();
		var expectedPrivateInstruction =
			GameStrings.WitchHealingSelectionInstruction.Format(
				string.Join(", ", attackTargets.Select(player => player.Name)));
		if (healedTargetId.HasValue ||
		    poisonedTargetId.HasValue ||
		    selection.CountConstraint != NumberRangeConstraint.SingleOptional ||
		    selection.RoleIdentification.HasValue ||
		    selection.PublicAnnouncement is not null ||
		    !StringComparer.Ordinal.Equals(
			    selection.PrivateInstruction,
			    expectedPrivateInstruction) ||
		    !StringComparer.Ordinal.Equals(
			    selection.EmptySelectionOptionLabel,
			    GameStrings.DeclineOption) ||
		    selection.SoundEffects.Count != 0 ||
		    selection.AffectedPlayerIds is not { Count: 1 } affectedPlayerIds ||
		    affectedPlayerIds.Single() != execution.ActingPlayer.Id ||
		    !selection.SelectablePlayerIds.SetEquals(expectedTargets))
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	private static void ValidateBorrowedPoisonInstruction(
		GameSession session,
		ExecutionContext execution,
		SelectPlayersInstruction selection,
		Guid? healedTargetId)
	{
		var expectedTargets =
			GetPoisonCandidates(session, execution, healedTargetId);
		var attackTargets =
			GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var attackRosterWasDisclosed = HasBorrowedPotionDecision(
			session,
			execution,
			HealingResourceId);
		var expectedPrivateInstruction =
			attackRosterWasDisclosed || attackTargets.Count == 0
				? GameStrings.WitchPoisonSelectionInstruction
				: GameStrings.WitchAttackTargetsAndPoisonSelectionInstruction.Format(
					string.Join(", ", attackTargets.Select(player => player.Name)));
		if (selection.CountConstraint != NumberRangeConstraint.SingleOptional ||
		    selection.RoleIdentification.HasValue ||
		    selection.PublicAnnouncement is not null ||
		    !StringComparer.Ordinal.Equals(
			    selection.PrivateInstruction,
			    expectedPrivateInstruction) ||
		    !StringComparer.Ordinal.Equals(
			    selection.EmptySelectionOptionLabel,
			    GameStrings.DeclineOption) ||
		    selection.SoundEffects.Count != 0 ||
		    selection.AffectedPlayerIds is not { Count: 1 } affectedPlayerIds ||
		    affectedPlayerIds.Single() != execution.ActingPlayer.Id ||
		    !selection.SelectablePlayerIds.SetEquals(expectedTargets))
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	private static void ValidateBorrowedSleep(
		GameSession session,
		ExecutionContext execution,
		ConfirmationInstruction sleep)
	{
		var attackTargets =
			GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var attackRosterWasDisclosed = HasBorrowedPotionDecision(
			session,
			execution,
			HealingResourceId,
			PoisonResourceId);
		var expectedPrivateInstruction =
			!attackRosterWasDisclosed && attackTargets.Count > 0
				? GameStrings.WitchAttackTargetsInstruction.Format(
					string.Join(", ", attackTargets.Select(player => player.Name)))
				: null;
		if (sleep.AffectedPlayerIds is not { Count: 1 } affectedPlayerIds ||
		    affectedPlayerIds.Single() != execution.ActingPlayer.Id ||
		    !StringComparer.Ordinal.Equals(
			    sleep.PrivateInstruction,
			    expectedPrivateInstruction) ||
		    sleep.SoundEffects.Count != 0 ||
		    !StringComparer.Ordinal.Equals(
			    sleep.PublicAnnouncement,
			    GameStrings.RoleGoesToSleepSingle.Format(
				    GameStrings.ActorRoleName)))
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	private static bool HasBorrowedPotionDecision(
		GameSession session,
		ExecutionContext execution,
		params Guid[] resourceIds)
	{
		var powerIdentity = CreatePowerIdentity(execution);
		return session.GetActorBorrowedWitchPotionUseCommits().Any(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.TurnNumber == session.TurnNumber &&
				commit.CurrentPhase == GamePhase.Night &&
				resourceIds.Contains(
					commit.SpentResourceIdentity.OneUseResourceId)) ||
		       session.GetActorBorrowedWitchPotionDeclineCommits().Any(commit =>
				       commit.PowerIdentity == powerIdentity &&
				       commit.TurnNumber == session.TurnNumber &&
				       commit.CurrentPhase == GamePhase.Night &&
				       resourceIds.Contains(
					       commit.OfferedResourceIdentity.OneUseResourceId));
	}

	private static (Guid? HealedTargetId, Guid? PoisonedTargetId)
		ValidateBorrowedPotionUseCommits(
			GameSession session,
			ExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation()
			?? throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		var powerIdentity = CreatePowerIdentity(execution);
		var healingIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			HealingResourceId);
		var poisonIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			PoisonResourceId);
		var commits = session.GetActorBorrowedWitchPotionUseCommits()
			.Where(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.TurnNumber == session.TurnNumber &&
				commit.CurrentPhase == GamePhase.Night)
			.ToArray();
		Guid? healedTargetId = null;
		Guid? poisonedTargetId = null;
		foreach (var commit in commits)
		{
			if (commit.ActorSetupCardId != activation.SelectedCardId ||
			    commit.TargetPlayerId == Guid.Empty)
			{
				throw new RoleWorkflowInputRejectionException(
					GameStrings.ActorBorrowedRolePowerInvalidResponse);
			}

			if (commit.SpentResourceIdentity == healingIdentity)
			{
				if (healedTargetId.HasValue)
				{
					throw new RoleWorkflowInputRejectionException(
						GameStrings.ActorBorrowedRolePowerInvalidResponse);
				}

				healedTargetId = commit.TargetPlayerId;
			}
			else if (commit.SpentResourceIdentity == poisonIdentity)
			{
				if (poisonedTargetId.HasValue)
				{
					throw new RoleWorkflowInputRejectionException(
						GameStrings.ActorBorrowedRolePowerInvalidResponse);
				}

				poisonedTargetId = commit.TargetPlayerId;
			}
			else
			{
				throw new RoleWorkflowInputRejectionException(
					GameStrings.ActorBorrowedRolePowerInvalidResponse);
			}
		}

		if (healedTargetId is { } healed &&
		    !GameSessionQueries.GetPhysicalAttackTargetsThisNight(session)
			    .Any(player => player.Id == healed))
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}

		if (poisonedTargetId is { } poisoned)
		{
			var target = session.GetPlayer(poisoned);
			if (target.State.Health != PlayerHealth.Alive ||
			    poisoned == execution.ActingPlayer.Id ||
			    poisoned == healedTargetId)
			{
				throw new RoleWorkflowInputRejectionException(
					GameStrings.ActorBorrowedRolePowerInvalidResponse);
			}
		}

		return (healedTargetId, poisonedTargetId);
	}

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		IPlayer witch,
		RolePowerInstance instance,
		Guid resourceId) => new(
			witch.Id,
			MainRoleType.Witch,
			PotionsPower.Identifier.Value,
			instance.Id,
			instance.Origin,
			resourceId);

	private static void CommitPotion(
		GameSession session,
		ExecutionContext execution,
		Guid resourceId,
		NightActionType actionType,
		Guid targetId)
	{
		var resourceIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			resourceId);
		var powerIdentity = CreatePowerIdentity(execution);
		var isSpent = execution.IsBorrowed
			? session.GetActorBorrowedWitchPotionUseCommits().Any(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.SpentResourceIdentity == resourceIdentity)
			: IsSpent(session, resourceIdentity);
		if (isSpent)
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
				? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "The selected Witch potion resource is already spent.");
		}

		if (execution.IsBorrowed)
		{
			session.CommitActorBorrowedWitchPotionUse(
				powerIdentity,
				resourceIdentity,
				targetId);
		}
		else
		{
			session.CommitOneUseRolePowerNightAction(
				actionType,
				targetId,
				resourceIdentity);
		}
	}

	private static void CommitPotionDecline(
		GameSession session,
		ExecutionContext execution,
		Guid resourceId)
	{
		var resourceIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			resourceId);
		session.CommitActorBorrowedWitchPotionDecline(
			CreatePowerIdentity(execution),
			resourceIdentity);
	}

	private IPlayer GetWitch(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException(
			"No alive Witch found for potion selection.");
}
