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

internal enum PiperRoleState
{
	AwaitingIdentification,
	Awake,
	AwaitingTargetSelection,
	ReadyToSleep,
	AwaitingCharmedRecognition,
	Asleep
}

internal sealed class PiperRole
	: RoleHookListener,
		IDeclaredRoleWorkflow
{
	private static readonly RolePowerDefinition CharmPower = new(
		new RolePowerIdentifier("piper-charm"),
		RolePowerCategory.Chosen);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _workflowRuntime;

	internal static RolePowerIdentifier CharmPowerIdentifier =>
		CharmPower.Identifier;

	internal PiperRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var identificationWait = RecoverableWait<
			PiperRoleState,
			SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				PiperRoleState.AwaitingIdentification,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateIdentificationInstruction,
				static (_, instruction) =>
					instruction is SelectPlayersInstruction
					{
						RoleIdentification: MainRoleType.Piper
					},
				ValidateIdentificationInstruction,
				ValidateAcceptedObservationHandoff,
				static _ => PiperRoleState.AwaitingIdentification);
		var wakeWait = RecoverableWait<
			PiperRoleState,
			ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				PiperRoleState.Awake,
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
				ValidateWakeRecoveryHandoff,
				static _ => PiperRoleState.Awake);
		var targetSelectionWait = RecoverableWait<
			PiperRoleState,
			SelectPlayersInstruction>.Replayable(
			Id,
			GameHook.NightMainActionLoop,
			PiperRoleState.Awake,
			PiperRoleState.AwaitingTargetSelection,
			ModeratorInstructionSemantic.SelectPiperTargets,
			ExpectedInputType.PlayerSelection,
			static _ => false,
				static (_, _) => { },
				CreateTargetSelectionInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.SelectPiperTargets &&
					HasExpectedAffectedHolder(session, instruction),
				ValidateTargetSelectionInstruction);
		var replayableSleepWait = RecoverableWait<
			PiperRoleState,
			ConfirmationInstruction>.Replayable(
			Id,
			GameHook.NightMainActionLoop,
			PiperRoleState.Awake,
			PiperRoleState.ReadyToSleep,
			ModeratorInstructionSemantic.PutRoleToSleep,
			ExpectedInputType.Continue,
			static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.PutRoleToSleep &&
					!GetCharmCommitsThisNight(session).Any() &&
					HasExpectedAffectedHolder(session, instruction),
				ValidateReplayableSleepInstruction);
		var committedSleepWait = RecoverableWait<
			PiperRoleState,
			ConfirmationInstruction>.RecurringDomainDurable(
			Id,
			GameHook.NightMainActionLoop,
			PiperRoleState.AwaitingTargetSelection,
			PiperRoleState.ReadyToSleep,
			ModeratorInstructionSemantic.PutRoleToSleep,
			ExpectedInputType.Continue,
			static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.PutRoleToSleep &&
					GetCharmCommitsThisNight(session).Take(2).Count() == 1 &&
					HasExpectedAffectedHolder(session, instruction),
				ValidateCommittedSleepInstruction,
			ValidateRecurringRecoveryCursor,
			static _ => PiperRoleState.ReadyToSleep,
			TryValidateCommittedRecoveryBoundary);
		var recognitionWait = RecoverableWait<
			PiperRoleState,
			ConfirmationInstruction>.Replayable(
			Id,
			GameHook.NightMainActionLoop,
			PiperRoleState.ReadyToSleep,
			PiperRoleState.AwaitingCharmedRecognition,
			ModeratorInstructionSemantic.RecognizeCharmedPlayers,
			ExpectedInputType.Continue,
			static _ => false,
				static (_, _) => { },
				CreateRecognitionInstruction,
				static (session, instruction) =>
					instruction.Semantic == ModeratorInstructionSemantic
						.RecognizeCharmedPlayers &&
					HasExpectedCharmedRoster(session, instruction),
				ValidateRecognitionInstruction);

		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				identificationWait,
				wakeWait,
				targetSelectionWait,
				replayableSleepWait,
				committedSleepWait,
				recognitionWait,
				new RoleWorkflowDecisionStep<PiperRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) => BeginCall(
						session,
						input,
						identificationWait,
						wakeWait)),
				new RoleWorkflowDecisionStep<PiperRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					PiperRoleState.AwaitingIdentification,
					static _ => true,
					(session, input) => CommitIdentificationAndWake(
						session,
						input,
						wakeWait)),
				new RoleWorkflowDecisionStep<PiperRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					PiperRoleState.Awake,
					static _ => true,
					(session, input) => PrepareNightPower(
						session,
						input,
						targetSelectionWait,
						replayableSleepWait)),
				new RoleWorkflowDecisionStep<PiperRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					PiperRoleState.AwaitingTargetSelection,
					static _ => true,
					(session, input) => CommitTargetSelection(
						session,
						input,
						committedSleepWait)),
				new RoleWorkflowDecisionStep<PiperRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					PiperRoleState.ReadyToSleep,
					static _ => true,
					(session, input) => ContinueAfterSleep(
						session,
						input,
						recognitionWait)),
				new RoleWorkflowCompletionStep<PiperRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					PiperRoleState.AwaitingCharmedRecognition,
					PiperRoleState.Asleep,
					static _ => true),
				new RoleWorkflowCompletionStep<PiperRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					PiperRoleState.Asleep,
					PiperRoleState.Asleep,
					static _ => true)
			]);
	}

	internal override string PublicName => GameStrings.PiperRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Piper);

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_workflowRuntime;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input) =>
		session.Execution.GetCurrentListenerState<PiperRoleState>(Id) != null
			? ExecuteCore(session, input)
			: base.Execute(session, input);

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		_workflowRuntime.Execute(
			session,
			input,
			session.Execution.GetCurrentListenerState<PiperRoleState>(Id));

	private HookListenerActionResult BeginCall(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<PiperRoleState, SelectPlayersInstruction>
			identificationWait,
		RecoverableWait<PiperRoleState, ConfirmationInstruction> wakeWait) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			MainRoleType.Piper)
			? wakeWait.Execute(session, input)
			: identificationWait.Execute(session, input);

	private HookListenerActionResult CommitIdentificationAndWake(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<PiperRoleState, ConfirmationInstruction> wakeWait)
	{
		IdentifyCompleteLivingRoleHolderSet(
			session,
			input.SelectedPlayerIds?.ToHashSet()
			?? throw new InvalidOperationException(
				"Piper identification requires one Player selection."));
		_ = InitialBeneficiaryClosureRules.TryCommitCurrentSession(session);
		return wakeWait.Execute(session, input);
	}

	private HookListenerActionResult PrepareNightPower(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<PiperRoleState, SelectPlayersInstruction>
			targetSelectionWait,
		RecoverableWait<PiperRoleState, ConfirmationInstruction>
			sleepWait)
	{
		var holder = GetHolder(session);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				holder,
				MainRoleType.Piper,
				CharmPower,
				RolePowerInstance.CreateCurrent(
					session,
					holder,
					MainRoleType.Piper,
					CharmPower)));
		return availability.AvailabilityResult.IsAvailable &&
		       GetEligibleTargets(session, holder.Id).Count > 0
			? targetSelectionWait.Execute(session, input)
			: sleepWait.Execute(session, input);
	}

	private HookListenerActionResult CommitTargetSelection(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<PiperRoleState, ConfirmationInstruction>
			committedSleepWait)
	{
		if (GetCharmCommitsThisNight(session).Any())
		{
			throw new InvalidOperationException(
				"Only one Piper charm action may be committed per Night.");
		}

		var holder = GetHolder(session);
		var eligibleTargets = GetEligibleTargets(session, holder.Id);
		var expectedCount = Math.Min(2, eligibleTargets.Count);
		if (input.SelectedPlayerIds is not { } selectedPlayerIds ||
		    selectedPlayerIds.Count != expectedCount ||
		    !selectedPlayerIds.IsSubsetOf(eligibleTargets))
		{
			throw new InvalidOperationException(
				"The Piper must select the exact required set of legal living Players.");
		}

		var powerIdentity = CreateCurrentPowerIdentity(session, holder);
		session.CommitRecurringRolePowerNightAction(
			NightActionType.PiperCharm,
			selectedPlayerIds,
			powerIdentity);
		foreach (var targetId in session.GetPlayers()
			         .Select(player => player.Id)
			         .Where(selectedPlayerIds.Contains))
		{
			session.ApplyStatusEffect(StatusEffectTypes.Charmed, targetId);
		}

		return committedSleepWait.Execute(session, input);
	}

	private HookListenerActionResult ContinueAfterSleep(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<PiperRoleState, ConfirmationInstruction>
			recognitionWait) =>
		GetLivingCharmedPlayers(session).Count == 0
			? HookListenerActionResult.Complete(PiperRoleState.Asleep)
			: recognitionWait.Execute(session, input);

	private SelectPlayersInstruction CreateIdentificationInstruction(
		GameSession session)
	{
		var selectablePlayerIds = GetIdentificationCandidates(session);
		if (GetExpectedLivingRoleHolderCount(session) != 1 ||
		    selectablePlayerIds.Count == 0)
		{
			throw new InvalidOperationException(
				"Piper identification requires exactly one possible living holder.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			selectablePlayerIds,
			NumberRangeConstraint.Single,
			publicAnnouncement: null,
			privateInstruction:
				GameStrings.RoleSingleIdentificationPrompt.Format(PublicName),
			affectedPlayerIds: null,
			roleIdentification: MainRoleType.Piper);
	}

	private ConfirmationInstruction CreateWakeInstruction(GameSession session) =>
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
				"Piper target selection requires one eligible living Player.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectPiperTargets,
			selectablePlayerIds: eligibleTargets,
			countConstraint: NumberRangeConstraint.Exact(
				Math.Min(2, eligibleTargets.Count)),
			privateInstruction: GameStrings.PiperTargetSelectionInstruction,
			affectedPlayerIds: [holder.Id]);
	}

	private ConfirmationInstruction CreateSleepInstruction(GameSession session) =>
		new(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleGoesToSleepSingle.Format(PublicName),
			affectedPlayerIds: [GetHolder(session).Id]);

	private static ConfirmationInstruction CreateRecognitionInstruction(
		GameSession session)
	{
		var livingCharmedPlayers = GetLivingCharmedPlayers(session);
		if (livingCharmedPlayers.Count == 0)
		{
			throw new InvalidOperationException(
				"Piper recognition requires one living Charmed Player.");
		}

		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.RecognizeCharmedPlayers,
			GameStrings.PiperCharmedRecognitionAnnouncement,
			GameStrings.PiperLivingCharmedRosterInstruction,
			livingCharmedPlayers.Select(player => player.Id).ToArray());
	}

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		if (instruction.RoleIdentification != MainRoleType.Piper ||
		    instruction.AffectedPlayerIds != null ||
		    instruction.CountConstraint != NumberRangeConstraint.Single ||
		    GetExpectedLivingRoleHolderCount(session) != 1 ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)))
		{
			throw new InvalidOperationException(
				"The Piper identification instruction has invalid workflow context.");
		}
	}

	private void ValidateWakeInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!HasExpectedAffectedHolder(session, instruction))
		{
			throw new InvalidOperationException(
				"The Piper wake instruction has invalid workflow context.");
		}
	}

	private void ValidateTargetSelectionInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var holder = GetHolder(session);
		var eligibleTargets = GetEligibleTargets(session, holder.Id);
		var possibleTargetIds = session.GetPlayers()
			.Where(player => player.Id != holder.Id)
			.Select(player => player.Id)
			.ToHashSet();
		if (GetCharmCommitsThisNight(session).Any() ||
		    instruction.RoleIdentification != null ||
		    instruction.CountConstraint != NumberRangeConstraint.Exact(
			    Math.Min(2, instruction.SelectablePlayerIds.Count)) ||
		    instruction.SelectablePlayerIds.Count == 0 ||
		    !eligibleTargets.IsSubsetOf(instruction.SelectablePlayerIds) ||
		    !instruction.SelectablePlayerIds.IsSubsetOf(possibleTargetIds) ||
		    !HasExpectedAffectedHolder(session, instruction))
		{
			throw new InvalidOperationException(
				"The Piper target selection has invalid workflow context.");
		}
	}

	private void ValidateReplayableSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (GetCharmCommitsThisNight(session).Any() ||
		    !HasExpectedAffectedHolder(session, instruction))
		{
			throw new InvalidOperationException(
				"The Piper replayable sleep has invalid workflow context.");
		}
	}

	private void ValidateCommittedSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		var commits = GetCharmCommitsThisNight(session).ToArray();
		if (commits is not [var commit] ||
		    !HasExpectedAffectedHolder(session, instruction))
		{
			throw new InvalidOperationException(
				"The Piper committed sleep has invalid workflow context.");
		}

		ValidateCommittedCharm(session, commit);
	}

	private static void ValidateRecognitionInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!HasExpectedCharmedRoster(session, instruction))
		{
			throw new InvalidOperationException(
				"The Piper recognition instruction has invalid workflow context.");
		}
	}

	private void ValidateAcceptedObservationHandoff(
		GameSession session,
		SelectPlayersInstruction instruction,
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.Piper ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The Piper identification has invalid accepted-observation handoff context.");
		}
	}

	private void ValidateWakeRecoveryHandoff(
		GameSession session,
		ConfirmationInstruction instruction,
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.Piper ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The Piper wake has invalid accepted-observation handoff context.");
		}

		if (cursor.AcceptedObservationSemantic ==
		    ModeratorInstructionSemantic.IdentifyRoleHolders &&
		    cursor.ObservedRole == MainRoleType.Piper)
		{
			ValidateIdentificationRecoveryBoundary(session);
		}
	}

	private static void ValidateIdentificationRecoveryBoundary(
		GameSession session)
	{
		var livingHolderIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player => player.State.CurrentRole == MainRoleType.Piper)
			.Select(player => player.Id)
			.ToHashSet();
		if (livingHolderIds is not { Count: 1 } ||
		    !session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Any(entry =>
			    entry.TurnNumber == session.TurnNumber &&
			    entry.CurrentPhase == GamePhase.Night &&
			    entry.Role == MainRoleType.Piper &&
			    entry.PlayerIds.SetEquals(livingHolderIds)) ||
		    !InitialBeneficiaryClosureRules
			    .HasConsistentInitialBeneficiaryClosure(session) ||
		    session.RequireKnownFactionBeneficiary(livingHolderIds.Single()) !=
		    Faction.Piper)
		{
			throw new InvalidOperationException(
				"The Piper identification continuation has invalid durable context.");
		}
	}

	private void ValidateRecurringRecoveryCursor(
		GameSession session,
		ConfirmationInstruction instruction,
		DomainRecoveryCursor cursor)
	{
		if (cursor.Kind !=
		    DomainRecoveryCursorKind.RecurringNativeRolePowerCommit ||
		    cursor.SourceRole != MainRoleType.Piper ||
		    cursor.CommittedActionType != NightActionType.PiperCharm ||
		    cursor.ActingPlayerId == Guid.Empty ||
		    !StringComparer.Ordinal.Equals(
			    cursor.SourcePowerIdentifier,
			    CharmPowerIdentifier.Value) ||
		    cursor.PowerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(cursor.ActingPlayerId)) ||
		    cursor.OneUseResourceId != Guid.Empty)
		{
			throw new InvalidOperationException(
				"The Piper recovery cursor has an invalid recurring Role Power identity.");
		}

		var commits = GetCharmCommitsThisNight(session)
			.Where(commit =>
				commit.PowerIdentity == cursor.PowerIdentity &&
				commit.TargetIds is { Count: > 0 } targetIds &&
				cursor.CommittedTargetIds.SequenceEqual(targetIds))
			.ToArray();
		if (commits is not [var commit])
		{
			throw new InvalidOperationException(
				"The Piper recovery cursor does not match one recurring charm action.");
		}

		ValidateCommittedCharm(session, commit);
	}

	private static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		RecurringRolePowerCommittedLogEntry committedEntry,
		ConfirmationInstruction nextInstruction)
	{
		if (committedEntry.ActionType != NightActionType.PiperCharm)
		{
			return false;
		}

		if (committedEntry.TargetIds is not { Count: > 0 } committedTargetIds ||
		    startingInstruction is not SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectPiperTargets,
			    CountConstraint: var countConstraint,
			    AffectedPlayerIds: { Count: 1 } affectedPlayerIds,
			    RoleIdentification: null
		    } targetSelection ||
		    countConstraint !=
		    NumberRangeConstraint.Exact(committedTargetIds.Count) ||
		    input.SelectedPlayerIds is not { } selectedPlayerIds ||
		    !selectedPlayerIds.SetEquals(committedTargetIds) ||
		    !selectedPlayerIds.IsSubsetOf(targetSelection.SelectablePlayerIds) ||
		    nextInstruction.AffectedPlayerIds is not
			    { Count: 1 } sleepAffectedPlayerIds ||
		    sleepAffectedPlayerIds.Single() != affectedPlayerIds.Single() ||
		    committedEntry.ActingPlayerId != affectedPlayerIds.Single())
		{
			throw new InvalidOperationException(
				"The Piper commit must correlate to its accepted targets and exact sleep continuation.");
		}

		ValidateCommittedCharm(session, committedEntry);
		return true;
	}

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.Piper ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == null ||
				  player.State.ModeratorKnownRole == MainRoleType.Piper)))
			.ToIdSet();

	private IPlayer GetHolder(GameSession session) =>
		TryGetHolder(session)
		?? throw new InvalidOperationException("No living Piper is available.");

	private static IPlayer? TryGetHolder(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.SingleOrDefault(player =>
				player.State.CurrentRole == MainRoleType.Piper);

	private bool HasExpectedAffectedHolder(
		GameSession session,
		ModeratorInstruction instruction) =>
		TryGetHolder(session) is { } holder &&
		instruction.AffectedPlayerIds is [var affectedPlayerId] &&
		affectedPlayerId == holder.Id;

	private static RolePowerInstanceIdentity CreateCurrentPowerIdentity(
		GameSession session,
		IPlayer holder) =>
		RolePowerInstance.CreateCurrentIdentity(
			session,
			holder,
			MainRoleType.Piper,
			CharmPower);

	private static HashSet<Guid> GetEligibleTargets(
		GameSession session,
		Guid holderId) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.Id != holderId &&
				!player.State.HasStatusEffect(StatusEffectTypes.Charmed))
			.ToIdSet();

	private static IReadOnlyList<IPlayer> GetLivingCharmedPlayers(
		GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.WithStatusEffect(StatusEffectTypes.Charmed)
			.ToArray();

	private static bool HasExpectedCharmedRoster(
		GameSession session,
		ModeratorInstruction instruction) =>
		instruction.AffectedPlayerIds is { Count: > 0 } affectedPlayerIds &&
		affectedPlayerIds.SequenceEqual(
			GetLivingCharmedPlayers(session).Select(player => player.Id));

	private static IEnumerable<RecurringRolePowerCommittedLogEntry>
		GetCharmCommitsThisNight(GameSession session) =>
		GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				[NightActionType.PiperCharm])
			.OfType<RecurringRolePowerCommittedLogEntry>();

	private static void ValidateCommittedCharm(
		GameSession session,
		RecurringRolePowerCommittedLogEntry committedEntry)
	{
		if (committedEntry.ActionType != NightActionType.PiperCharm ||
		    committedEntry.TargetIds is not { Count: 1 or 2 } targetIds ||
		    targetIds.Distinct().Count() != targetIds.Count ||
		    committedEntry.SourceRole != MainRoleType.Piper ||
		    !StringComparer.Ordinal.Equals(
			    committedEntry.SourcePowerIdentifier,
			    CharmPowerIdentifier.Value) ||
		    committedEntry.PowerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(committedEntry.ActingPlayerId)))
		{
			throw new InvalidOperationException(
				"The Piper recovery boundary requires one owned recurring charm action.");
		}

		var holder = session.GetPlayer(committedEntry.ActingPlayerId);
		if (holder.State.Health != PlayerHealth.Alive ||
		    holder.State.CurrentRole != MainRoleType.Piper ||
		    targetIds.Any(targetId =>
			    !session.GetPlayerState(targetId)
				    .HasStatusEffect(StatusEffectTypes.Charmed)))
		{
			throw new InvalidOperationException(
				"The Piper charm commit does not match the living holder and Charmed targets.");
		}
	}
}
