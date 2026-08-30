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

internal enum WhiteWerewolfRoleState
{
	Awake,
	AwaitingTargetSelection,
	ReadyToSleep,
	Asleep
}

internal sealed class WhiteWerewolfRole
	: RoleHookListener,
		IDeclaredRoleWorkflow
{
	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _workflowRuntime;

	private static readonly RolePowerDefinition SoloAttackPower = new(
		new RolePowerIdentifier("white-werewolf-solo-attack"),
		RolePowerCategory.Chosen);

	internal static RolePowerIdentifier SoloAttackPowerIdentifier =>
		SoloAttackPower.Identifier;

	internal WhiteWerewolfRole(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var identificationWait = RecoverableWait<
				WhiteWerewolfRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				WhiteWerewolfRoleState.Awake,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateIdentificationInstruction,
				static (_, instruction) =>
					instruction is SelectPlayersInstruction
					{
						RoleIdentification: MainRoleType.WhiteWerewolf
					},
				ValidateIdentificationInstruction,
				(_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => WhiteWerewolfRoleState.Awake);
		var wakeWait = RecoverableWait<
				WhiteWerewolfRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				WhiteWerewolfRoleState.Awake,
				ModeratorInstructionSemantic.WakeRole,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateWakeInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.WakeRole &&
					HasExpectedAffectedHolder(session, instruction),
				ValidateWakeInstruction,
				(_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => WhiteWerewolfRoleState.Awake);
		var targetSelectionWait = RecoverableWait<
				WhiteWerewolfRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				WhiteWerewolfRoleState.Awake,
				WhiteWerewolfRoleState.AwaitingTargetSelection,
				ModeratorInstructionSemantic.SelectWhiteWerewolfTarget,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateTargetSelectionInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.SelectWhiteWerewolfTarget &&
					HasExpectedAffectedHolder(session, instruction),
				ValidateTargetSelectionInstruction,
				(session, _, cursor) =>
					ValidateIdentificationHandoff(session, cursor),
				static _ => WhiteWerewolfRoleState.AwaitingTargetSelection);
		var replayableSleepWait = RecoverableWait<
				WhiteWerewolfRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				WhiteWerewolfRoleState.Awake,
				WhiteWerewolfRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.PutRoleToSleep &&
					!GetAttackCommitsThisNight(session).Any() &&
					HasExpectedAffectedHolder(session, instruction),
				ValidateReplayableSleepInstruction,
				(session, _, cursor) =>
					ValidateIdentificationHandoff(session, cursor),
				static _ => WhiteWerewolfRoleState.ReadyToSleep);
		var committedSleepWait = RecoverableWait<
				WhiteWerewolfRoleState,
				ConfirmationInstruction>
			.RecurringDomainDurable(
				Id,
				GameHook.NightMainActionLoop,
				WhiteWerewolfRoleState.AwaitingTargetSelection,
				WhiteWerewolfRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.PutRoleToSleep &&
					GetAttackCommitsThisNight(session).Take(2).Count() == 1 &&
					HasExpectedAffectedHolder(session, instruction),
				ValidateCommittedSleepInstruction,
				ValidateRecurringRecoveryCursor,
				static _ => WhiteWerewolfRoleState.ReadyToSleep,
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
				new RoleWorkflowDecisionStep<WhiteWerewolfRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) => BeginCall(
						session,
						input,
						identificationWait,
						wakeWait)),
				new RoleWorkflowDecisionStep<WhiteWerewolfRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					WhiteWerewolfRoleState.Awake,
					static _ => true,
					(session, input) => PrepareNightPower(
						session,
						input,
						targetSelectionWait,
						replayableSleepWait)),
				new RoleWorkflowDecisionStep<WhiteWerewolfRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					WhiteWerewolfRoleState.AwaitingTargetSelection,
					static _ => true,
					(session, input) => CommitTargetSelection(
						session,
						input,
						replayableSleepWait,
						committedSleepWait)),
				new RoleWorkflowCompletionStep<WhiteWerewolfRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					WhiteWerewolfRoleState.ReadyToSleep,
					WhiteWerewolfRoleState.Asleep,
					static _ => true),
				new RoleWorkflowCompletionStep<WhiteWerewolfRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					WhiteWerewolfRoleState.Asleep,
					WhiteWerewolfRoleState.Asleep,
					static _ => true)
			]);
	}

	internal override string PublicName => GameStrings.WhiteWerewolfRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.WhiteWerewolf);

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_workflowRuntime;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (session.Execution
			    .GetCurrentListenerState<WhiteWerewolfRoleState>(Id) == null &&
		    session.TurnNumber > 1 &&
		    !IsSoloAttackNight(session.TurnNumber))
		{
			return HookListenerActionResult.Skip();
		}

		return base.Execute(session, input);
	}

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		_workflowRuntime.Execute(
			session,
			input,
			session.Execution
				.GetCurrentListenerState<WhiteWerewolfRoleState>(Id));

	private HookListenerActionResult BeginCall(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<WhiteWerewolfRoleState, SelectPlayersInstruction>
			identificationWait,
		RecoverableWait<WhiteWerewolfRoleState, ConfirmationInstruction>
			wakeWait)
	{
		if (!IsCompleteHolderSetKnown(session))
		{
			return identificationWait.Execute(session, input);
		}

		return session.TurnNumber == 1
			? HookListenerActionResult.Complete(WhiteWerewolfRoleState.Asleep)
			: wakeWait.Execute(session, input);
	}

	private HookListenerActionResult PrepareNightPower(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<WhiteWerewolfRoleState, SelectPlayersInstruction>
			targetSelectionWait,
		RecoverableWait<WhiteWerewolfRoleState, ConfirmationInstruction>
			sleepWait)
	{
		if (!IsCompleteHolderSetKnown(session))
		{
			IdentifyCompleteLivingRoleHolderSet(
				session,
				input.SelectedPlayerIds?.ToHashSet()
				?? throw new InvalidOperationException(
					"White Werewolf identification requires one Player selection."));
			_ = InitialBeneficiaryClosureRules.TryCommitCurrentSession(session);
		}

		if (!IsSoloAttackNight(session.TurnNumber))
		{
			return HookListenerActionResult.Complete(
				WhiteWerewolfRoleState.Asleep);
		}

		var holder = GetHolder(session);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				holder,
				MainRoleType.WhiteWerewolf,
				SoloAttackPower,
				RolePowerInstance.CreateCurrent(
					session,
					holder,
					MainRoleType.WhiteWerewolf,
					SoloAttackPower)));
		return availability.AvailabilityResult.IsAvailable &&
		       GetEligibleTargets(session, holder.Id).Count > 0
			? targetSelectionWait.Execute(session, input)
			: sleepWait.Execute(session, input);
	}

	private HookListenerActionResult CommitTargetSelection(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<WhiteWerewolfRoleState, ConfirmationInstruction>
			declinedSleepWait,
		RecoverableWait<WhiteWerewolfRoleState, ConfirmationInstruction>
			committedSleepWait)
	{
		if (input.SelectedPlayerIds is not { Count: <= 1 } selectedPlayerIds)
		{
			throw new InvalidOperationException(
				"The White Werewolf may select at most one Player.");
		}

		if (selectedPlayerIds.Count == 0)
		{
			return declinedSleepWait.Execute(session, input);
		}

		if (GetAttackCommitsThisNight(session).Any())
		{
			throw new InvalidOperationException(
				"Only one White Werewolf attack may be committed per Night.");
		}

		var holder = GetHolder(session);
		var targetId = selectedPlayerIds.Single();
		if (!GetEligibleTargets(session, holder.Id).Contains(targetId))
		{
			throw new InvalidOperationException(
				"The White Werewolf target must be another living known Werewolf Faction Agent.");
		}

		var powerInstance = RolePowerInstance.CreateCurrent(
			session,
			holder,
			MainRoleType.WhiteWerewolf,
			SoloAttackPower);
		session.CommitRecurringRolePowerNightAction(
			NightActionType.WhiteWerewolfVictimSelection,
			targetId,
			new RolePowerInstanceIdentity(
				holder.Id,
				MainRoleType.WhiteWerewolf,
				SoloAttackPower.Identifier.Value,
				powerInstance.Id,
				powerInstance.Origin));
		return committedSleepWait.Execute(session, input);
	}

	private SelectPlayersInstruction CreateIdentificationInstruction(
		GameSession session)
	{
		var selectablePlayerIds = GetIdentificationCandidates(session);
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		if (roleCount <= 0 ||
		    GetCommittedLivingRoleHolderIds(session).Count > roleCount ||
		    selectablePlayerIds.Count < roleCount)
		{
			throw new InvalidOperationException(
				"Confirmed Role knowledge contradicts the required Living Role Holder count.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			selectablePlayerIds,
			NumberRangeConstraint.Exact(roleCount),
			publicAnnouncement: null,
			privateInstruction:
				GameStrings.RoleSingleIdentificationPrompt.Format(PublicName),
			affectedPlayerIds: null,
			roleIdentification: MainRoleType.WhiteWerewolf);
	}

	private ConfirmationInstruction CreateWakeInstruction(
		GameSession session) =>
		new(
			ModeratorInstructionSemantic.WakeRole,
			GameStrings.RoleWakesUp.Format(PublicName),
			affectedPlayerIds: [GetHolder(session).Id]);

	private SelectPlayersInstruction CreateTargetSelectionInstruction(
		GameSession session)
	{
		var holder = GetHolder(session);
		var eligibleTargets = GetEligibleTargets(session, holder.Id);
		if (eligibleTargets.Count == 0)
		{
			throw new InvalidOperationException(
				"White Werewolf target selection requires one eligible living Werewolf Faction Agent.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectWhiteWerewolfTarget,
			eligibleTargets,
			NumberRangeConstraint.SingleOptional,
			publicAnnouncement: null,
			privateInstruction:
				GameStrings.WhiteWerewolfTargetSelectionInstruction,
			affectedPlayerIds: [holder.Id])
		{
			EmptySelectionOptionLabel = GameStrings.DeclineOption
		};
	}

	private ConfirmationInstruction CreateSleepInstruction(
		GameSession session) =>
		new(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleGoesToSleepSingle.Format(PublicName),
			affectedPlayerIds: [GetHolder(session).Id]);

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		if (instruction.RoleIdentification != MainRoleType.WhiteWerewolf ||
		    instruction.AffectedPlayerIds != null ||
		    roleCount <= 0 ||
		    instruction.CountConstraint !=
			    NumberRangeConstraint.Exact(roleCount) ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)))
		{
			throw new InvalidOperationException(
				"The White Werewolf identification instruction has invalid workflow context.");
		}
	}

	private void ValidateWakeInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (session.TurnNumber == 1 ||
		    !HasExpectedAffectedHolder(session, instruction))
		{
			throw new InvalidOperationException(
				"The White Werewolf wake instruction has invalid workflow context.");
		}
	}

	private void ValidateTargetSelectionInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var holder = TryGetHolder(session);
		if (!IsSoloAttackNight(session.TurnNumber) ||
		    GetAttackCommitsThisNight(session).Any() ||
		    instruction.RoleIdentification != null ||
		    instruction.CountConstraint !=
			    NumberRangeConstraint.SingleOptional ||
		    holder == null ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetEligibleTargets(session, holder.Id)) ||
		    instruction.SelectablePlayerIds.Count == 0 ||
		    !HasExpectedAffectedHolder(session, instruction))
		{
			throw new InvalidOperationException(
				"The White Werewolf target selection has invalid workflow context.");
		}
	}

	private void ValidateReplayableSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (GetAttackCommitsThisNight(session).Any() ||
		    !HasExpectedAffectedHolder(session, instruction))
		{
			throw new InvalidOperationException(
				"The White Werewolf replayable sleep has invalid workflow context.");
		}
	}

	private void ValidateCommittedSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		var commits = GetAttackCommitsThisNight(session).ToArray();
		if (commits is not [var commit] ||
		    !HasExpectedAffectedHolder(session, instruction) ||
		    commit.ActingPlayerId != GetHolder(session).Id)
		{
			throw new InvalidOperationException(
				"The pending White Werewolf sleep instruction has invalid solo-attack workflow context.");
		}

		ValidateCommittedAttack(session, commit);
	}

	private static void ValidateCallHandoff(
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.WhiteWerewolf)
		{
			throw new InvalidOperationException(
				"The White Werewolf call has invalid accepted-observation handoff context.");
		}
	}

	private static void ValidateIdentificationHandoff(
		GameSession session,
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.WhiteWerewolf ||
		    cursor.ObservedRole != MainRoleType.WhiteWerewolf ||
		    cursor.AcceptedObservationSemantic !=
			    ModeratorInstructionSemantic.IdentifyRoleHolders ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The White Werewolf continuation has invalid accepted-observation handoff context.");
		}

		var livingHolderIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.WhiteWerewolf)
			.Select(player => player.Id)
			.ToHashSet();
		if (livingHolderIds.Count == 0 ||
		    !session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			    .Any(entry =>
				    entry.TurnNumber == session.TurnNumber &&
				    entry.CurrentPhase == GamePhase.Night &&
				    entry.Role == MainRoleType.WhiteWerewolf &&
				    entry.PlayerIds.SetEquals(livingHolderIds)) ||
		    !InitialBeneficiaryClosureRules
			    .HasConsistentInitialBeneficiaryClosure(session))
		{
			throw new InvalidOperationException(
				"The White Werewolf identification continuation has invalid durable context.");
		}
	}

	private void ValidateRecurringRecoveryCursor(
		GameSession session,
		ConfirmationInstruction instruction,
		DomainRecoveryCursor cursor)
	{
		ValidateRecurringRecoveryCursorIdentity(session, cursor);
		var commits = GetAttackCommitsThisNight(session)
			.Where(commit =>
				commit.PowerIdentity == cursor.PowerIdentity &&
				commit.TargetIds is { Count: 1 } targetIds &&
				cursor.CommittedTargetIds.SequenceEqual(targetIds))
			.ToArray();
		if (commits is not [var committedAction])
		{
			throw new InvalidOperationException(
				"The White Werewolf recovery cursor does not match one recurring solo-attack action.");
		}

		ValidateCommittedAttack(session, committedAction);
	}

	private static void ValidateRecurringRecoveryCursorIdentity(
		GameSession session,
		DomainRecoveryCursor cursor)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(cursor);
		if (cursor.Kind !=
		    DomainRecoveryCursorKind.RecurringNativeRolePowerCommit ||
		    cursor.SourceRole != MainRoleType.WhiteWerewolf ||
		    cursor.CommittedActionType !=
		    NightActionType.WhiteWerewolfVictimSelection ||
		    cursor.ActingPlayerId == Guid.Empty ||
		    !StringComparer.Ordinal.Equals(
			    cursor.SourcePowerIdentifier,
			    SoloAttackPowerIdentifier.Value) ||
		    cursor.PowerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(cursor.ActingPlayerId)) ||
		    cursor.OneUseResourceId != Guid.Empty ||
		    cursor.NextInstructionSemantic !=
			    ModeratorInstructionSemantic.PutRoleToSleep)
		{
			throw new InvalidOperationException(
				"The White Werewolf recovery cursor has an invalid recurring Role Power identity.");
		}
	}

	private static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		RecurringRolePowerCommittedLogEntry committedEntry,
		ConfirmationInstruction nextInstruction)
	{
		if (committedEntry.ActionType !=
		    NightActionType.WhiteWerewolfVictimSelection)
		{
			return false;
		}

		if (committedEntry.TargetIds is not [var committedTargetId] ||
		    startingInstruction is not SelectPlayersInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.SelectWhiteWerewolfTarget,
			    CountConstraint: var countConstraint,
			    AffectedPlayerIds: { Count: 1 } affectedPlayerIds,
			    RoleIdentification: null
		    } targetSelection ||
		    countConstraint != NumberRangeConstraint.SingleOptional ||
		    input.SelectedPlayerIds is not
			    { Count: 1 } selectedPlayerIds ||
		    selectedPlayerIds.Single() != committedTargetId ||
		    !targetSelection.SelectablePlayerIds.Contains(committedTargetId) ||
		    nextInstruction.AffectedPlayerIds is not
			    { Count: 1 } sleepAffectedPlayerIds ||
		    sleepAffectedPlayerIds.Single() != affectedPlayerIds.Single())
		{
			throw new InvalidOperationException(
				"The White Werewolf commit must correlate to its accepted target and exact sleep continuation.");
		}

		if (committedEntry.ActingPlayerId != affectedPlayerIds.Single())
		{
			throw new InvalidOperationException(
				"The White Werewolf commit does not belong to the instructed Role holder.");
		}

		ValidateCommittedAttack(session, committedEntry);
		return true;
	}

	private bool IsCompleteHolderSetKnown(GameSession session) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			MainRoleType.WhiteWerewolf);

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.WhiteWerewolf ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == MainRoleType.WhiteWerewolf ||
				  player.State.ModeratorKnownRole == null &&
				  RoleFactionKnowledge.GetPossibleRoles(session, player.Id)
					  .Contains(MainRoleType.WhiteWerewolf))))
			.ToIdSet();

	private IPlayer GetHolder(GameSession session) =>
		TryGetHolder(session)
		?? throw new InvalidOperationException(
			"No living White Werewolf is available.");

	private IPlayer? TryGetHolder(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault();

	private bool HasExpectedAffectedHolder(
		GameSession session,
		ModeratorInstruction instruction) =>
		TryGetHolder(session) is { } holder &&
		instruction.AffectedPlayerIds is [var affectedPlayerId] &&
		affectedPlayerId == holder.Id;

	private static HashSet<Guid> GetEligibleTargets(
		GameSession session,
		Guid holderId) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.Id != holderId &&
				player.State.GetFactionAgentKnowledge(Faction.Werewolf) ==
				FactionAgentKnowledge.KnownAgent)
			.ToIdSet();

	private static IEnumerable<RecurringRolePowerCommittedLogEntry>
		GetAttackCommitsThisNight(GameSession session) =>
		GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				[NightActionType.WhiteWerewolfVictimSelection])
			.OfType<RecurringRolePowerCommittedLogEntry>();

	private static bool IsSoloAttackNight(int turnNumber) =>
		turnNumber > 1 && turnNumber % 2 == 0;

	private static void ValidateCommittedAttack(
		GameSession session,
		RecurringRolePowerCommittedLogEntry committedEntry)
	{
		if (committedEntry.ActionType !=
			    NightActionType.WhiteWerewolfVictimSelection ||
		    committedEntry.TargetIds is not [var committedTargetId] ||
		    committedEntry.SourceRole != MainRoleType.WhiteWerewolf ||
		    !StringComparer.Ordinal.Equals(
			    committedEntry.SourcePowerIdentifier,
			    SoloAttackPowerIdentifier.Value) ||
		    committedEntry.ActingPlayerId == Guid.Empty ||
		    committedEntry.PowerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(committedEntry.ActingPlayerId)) ||
		    committedEntry.CurrentPhase != GamePhase.Night ||
		    committedEntry.TurnNumber != session.TurnNumber ||
		    !IsSoloAttackNight(committedEntry.TurnNumber))
		{
			throw new InvalidOperationException(
				"The White Werewolf recovery boundary requires one owned recurring solo-attack action.");
		}

		var holder = session.GetPlayer(committedEntry.ActingPlayerId);
		if (holder.State.Health != PlayerHealth.Alive ||
		    holder.State.MainRole != MainRoleType.WhiteWerewolf)
		{
			throw new InvalidOperationException(
				"The White Werewolf attack commit does not belong to the living Role holder.");
		}

		if (!GetEligibleTargets(session, holder.Id)
			    .Contains(committedTargetId))
		{
			throw new InvalidOperationException(
				"The White Werewolf attack target must be another living known Werewolf Faction Agent.");
		}
	}

	private static RolePowerInstanceIdentity CreateCurrentPowerIdentity(
		GameSession session,
		IPlayer holder) =>
		RolePowerInstance.CreateCurrentIdentity(
			session,
			holder,
			MainRoleType.WhiteWerewolf,
			SoloAttackPower);
}
