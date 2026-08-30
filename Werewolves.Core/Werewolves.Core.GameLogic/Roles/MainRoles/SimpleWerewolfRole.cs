using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
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

internal enum SimpleWerewolfRoleState
{
	AwaitingGroupObservation,
	AwaitingWakeAcknowledgement,
	AwaitingVictimAfterObservation,
	AwaitingVictimAfterWake,
	ReadyToSleepAfterObservation,
	ReadyToSleepAfterWake,
	ReadyToSleepAfterObservedVictim,
	ReadyToSleepAfterWakeVictim,
	Asleep
}

/// <summary>
/// The centrally scheduled collective Werewolf Faction Agent interval.
/// </summary>
internal class SimpleWerewolfRole
	: RoleHookListener,
		IDeclaredRoleWorkflow
{
	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _workflowRuntime;
	private bool? _littleGirlGuidanceAllowed;
	private bool _littleGirlGuidanceEvaluated;

	internal SimpleWerewolfRole(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var observationWait = RecoverableWait<
			SimpleWerewolfRoleState,
			SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				SimpleWerewolfRoleState.AwaitingGroupObservation,
				ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateObservationInstruction,
				static (_, instruction) =>
					instruction is SelectPlayersInstruction
					{
						Semantic: ModeratorInstructionSemantic
							.ObserveWerewolfFactionAgentGroup,
						RoleIdentification: null,
						AffectedPlayerIds: null
					},
				ValidateObservationInstruction,
				ValidateAcceptedObservationHandoff,
				static _ =>
					SimpleWerewolfRoleState.AwaitingGroupObservation);
		var wakeWait = RecoverableWait<
			SimpleWerewolfRoleState,
			ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				SimpleWerewolfRoleState.AwaitingWakeAcknowledgement,
				ModeratorInstructionSemantic.WakeRole,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateWakeInstruction,
				static (session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.WakeRole &&
					HasExpectedAffectedWerewolfAgents(session, instruction),
				ValidateWakeInstruction,
				ValidateAcceptedObservationHandoff,
				static _ =>
					SimpleWerewolfRoleState.AwaitingWakeAcknowledgement);
		var observedVictimWait = RecoverableWait<
			SimpleWerewolfRoleState,
			SelectPlayersInstruction>.Durable(
				Id,
				GameHook.NightMainActionLoop,
				SimpleWerewolfRoleState.AwaitingGroupObservation,
				SimpleWerewolfRoleState.AwaitingVictimAfterObservation,
				ModeratorInstructionSemantic.SelectWerewolfVictim,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateVictimSelectionInstruction,
				static (session, instruction) =>
					instruction.Semantic == ModeratorInstructionSemantic
						.SelectWerewolfVictim &&
					HasExpectedAffectedWerewolfAgents(session, instruction),
				ValidateVictimSelectionInstruction,
				ValidateAgentObservationRecoveryBoundary,
				static _ =>
					SimpleWerewolfRoleState.AwaitingVictimAfterObservation);
		var observedSleepWait = RecoverableWait<
			SimpleWerewolfRoleState,
			ConfirmationInstruction>.Durable(
				Id,
				GameHook.NightMainActionLoop,
				SimpleWerewolfRoleState.AwaitingGroupObservation,
				SimpleWerewolfRoleState.ReadyToSleepAfterObservation,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				static (session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.PutRoleToSleep &&
					HasExpectedAffectedWerewolfAgents(session, instruction),
				ValidateSleepInstruction,
				ValidateAgentObservationRecoveryBoundary,
				static _ =>
					SimpleWerewolfRoleState.ReadyToSleepAfterObservation);
		var wakeVictimWait = CreateReplayableVictimWait(
			SimpleWerewolfRoleState.AwaitingWakeAcknowledgement,
			SimpleWerewolfRoleState.AwaitingVictimAfterWake);
		var wakeSleepWait = CreateReplayableSleepWait(
			SimpleWerewolfRoleState.AwaitingWakeAcknowledgement,
			SimpleWerewolfRoleState.ReadyToSleepAfterWake,
			expectsCommittedObservation: false,
			expectsCommittedVictim: false);
		var observedVictimSleepWait = CreateReplayableSleepWait(
			SimpleWerewolfRoleState.AwaitingVictimAfterObservation,
			SimpleWerewolfRoleState.ReadyToSleepAfterObservedVictim,
			expectsCommittedObservation: true,
			expectsCommittedVictim: true);
		var wakeVictimSleepWait = CreateReplayableSleepWait(
			SimpleWerewolfRoleState.AwaitingVictimAfterWake,
			SimpleWerewolfRoleState.ReadyToSleepAfterWakeVictim,
			expectsCommittedObservation: false,
			expectsCommittedVictim: true);

		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				observationWait,
				wakeWait,
				observedVictimWait,
				observedSleepWait,
				wakeVictimWait,
				wakeSleepWait,
				observedVictimSleepWait,
				wakeVictimSleepWait,
				new RoleWorkflowDecisionStep<SimpleWerewolfRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) => BeginCollectiveInterval(
						session,
						input,
						observationWait,
						wakeWait)),
				new RoleWorkflowDecisionStep<SimpleWerewolfRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					SimpleWerewolfRoleState.AwaitingGroupObservation,
					static _ => true,
					(session, input) => ContinueAfterObservation(
						session,
						input,
						observedVictimWait,
						observedSleepWait)),
				new RoleWorkflowDecisionStep<SimpleWerewolfRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					SimpleWerewolfRoleState.AwaitingWakeAcknowledgement,
					static _ => true,
					(session, input) => ContinueAfterWake(
						session,
						input,
						wakeVictimWait,
						wakeSleepWait)),
				new RoleWorkflowDecisionStep<SimpleWerewolfRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					SimpleWerewolfRoleState.AwaitingVictimAfterObservation,
					static _ => true,
					(session, input) => CommitVictimAndSleep(
						session,
						input,
						observedVictimSleepWait)),
				new RoleWorkflowDecisionStep<SimpleWerewolfRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					SimpleWerewolfRoleState.AwaitingVictimAfterWake,
					static _ => true,
					(session, input) => CommitVictimAndSleep(
						session,
						input,
						wakeVictimSleepWait)),
				CreateSleepCompletion(
					SimpleWerewolfRoleState.ReadyToSleepAfterObservation),
				CreateSleepCompletion(
					SimpleWerewolfRoleState.ReadyToSleepAfterWake),
				CreateSleepCompletion(
					SimpleWerewolfRoleState.ReadyToSleepAfterObservedVictim),
				CreateSleepCompletion(
					SimpleWerewolfRoleState.ReadyToSleepAfterWakeVictim),
				new RoleWorkflowCompletionStep<SimpleWerewolfRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					SimpleWerewolfRoleState.Asleep,
					SimpleWerewolfRoleState.Asleep,
					static _ => true)
			]);
	}

	internal override string PublicName => GameStrings.SimpleWerewolfRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf);

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_workflowRuntime;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (session.Execution.GetCurrentListenerState<SimpleWerewolfRoleState>(Id)
				is null &&
		    TryGetKnownLivingWerewolfAgents(session, out var agents) &&
		    agents.Count == 0)
		{
			TryCommitInitialBeneficiaryClosure(session);
			return HookListenerActionResult.Skip();
		}

		try
		{
			return ExecuteCore(session, input);
		}
		catch (InvalidOperationException) when (
			UsesBorrowedLittleGirlPrivacy(session))
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		_workflowRuntime.Execute(
			session,
			input,
			session.Execution.GetCurrentListenerState<SimpleWerewolfRoleState>(Id));

	internal bool? LittleGirlGuidanceDecision =>
		_littleGirlGuidanceAllowed;

	internal void RestoreLittleGirlGuidanceDecision(bool? isAllowed)
	{
		_littleGirlGuidanceAllowed = isAllowed;
		_littleGirlGuidanceEvaluated = true;
	}

	private RecoverableWait<SimpleWerewolfRoleState, SelectPlayersInstruction>
		CreateReplayableVictimWait(
			SimpleWerewolfRoleState startState,
			SimpleWerewolfRoleState continuationState) =>
			RecoverableWait<SimpleWerewolfRoleState,
				SelectPlayersInstruction>.Replayable(
				Id,
				GameHook.NightMainActionLoop,
				startState,
				continuationState,
				ModeratorInstructionSemantic.SelectWerewolfVictim,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateVictimSelectionInstruction,
				static (session, instruction) =>
					instruction.Semantic == ModeratorInstructionSemantic
						.SelectWerewolfVictim &&
					HasExpectedAffectedWerewolfAgents(session, instruction),
				ValidateVictimSelectionInstruction);

	private RecoverableWait<SimpleWerewolfRoleState, ConfirmationInstruction>
		CreateReplayableSleepWait(
			SimpleWerewolfRoleState startState,
			SimpleWerewolfRoleState continuationState,
			bool expectsCommittedObservation,
			bool expectsCommittedVictim) =>
			RecoverableWait<SimpleWerewolfRoleState,
				ConfirmationInstruction>.Replayable(
				Id,
				GameHook.NightMainActionLoop,
				startState,
				continuationState,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.PutRoleToSleep &&
					HasExpectedAffectedWerewolfAgents(session, instruction) &&
					HasExpectedReplayableSleepBoundary(
						session,
						expectsCommittedObservation,
						expectsCommittedVictim),
				ValidateSleepInstruction);

	private RoleWorkflowDecisionStep<SimpleWerewolfRoleState>
		CreateSleepCompletion(SimpleWerewolfRoleState startState) =>
			new(
				Id,
				GameHook.NightMainActionLoop,
				startState,
				static _ => true,
				CompleteCollectiveInterval);

	private HookListenerActionResult BeginCollectiveInterval(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<SimpleWerewolfRoleState,
			SelectPlayersInstruction> observationWait,
		RecoverableWait<SimpleWerewolfRoleState,
			ConfirmationInstruction> wakeWait)
	{
		EvaluateLittleGirlGuidance(session);
		return TryGetKnownLivingWerewolfAgents(session, out _)
			? wakeWait.Execute(session, input)
			: observationWait.Execute(session, input);
	}

	private HookListenerActionResult ContinueAfterObservation(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<SimpleWerewolfRoleState,
			SelectPlayersInstruction> victimWait,
		RecoverableWait<SimpleWerewolfRoleState,
			ConfirmationInstruction> sleepWait)
	{
		EnsureLittleGirlGuidanceEvaluated(session);
		var boundary = CommitWerewolfAgentGroupObservation(session, input);
		TryCommitInitialBeneficiaryClosure(session, boundary);
		return GetLivingKnownNonAgents(session).Count == 0
			? sleepWait.Execute(session, input)
			: victimWait.Execute(session, input);
	}

	private HookListenerActionResult ContinueAfterWake(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<SimpleWerewolfRoleState,
			SelectPlayersInstruction> victimWait,
		RecoverableWait<SimpleWerewolfRoleState,
			ConfirmationInstruction> sleepWait)
	{
		EnsureLittleGirlGuidanceEvaluated(session);
		TryCommitInitialBeneficiaryClosure(session);
		return GetLivingKnownNonAgents(session).Count == 0
			? sleepWait.Execute(session, input)
			: victimWait.Execute(session, input);
	}

	private HookListenerActionResult CommitVictimAndSleep(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<SimpleWerewolfRoleState,
			ConfirmationInstruction> sleepWait)
	{
		CommitVictimSelection(session, input);
		return sleepWait.Execute(session, input);
	}

	private HookListenerActionResult CompleteCollectiveInterval(
		GameSession session,
		ModeratorResponse input)
	{
		_littleGirlGuidanceAllowed = null;
		_littleGirlGuidanceEvaluated = false;
		return HookListenerActionResult.Complete(
			SimpleWerewolfRoleState.Asleep);
	}

	private SelectPlayersInstruction CreateObservationInstruction(
		GameSession session)
	{
		var opportunity = RoleFactionKnowledge
			.RequireInitialWerewolfAgentGroupOpportunity(session);
		var observationPrompt = GameStrings.WerewolfFactionAgentObservationPrompt
			.Format(opportunity.RequiredCount);
		var privateInstruction = _littleGirlGuidanceAllowed == true
			? string.Join(
				Environment.NewLine + Environment.NewLine,
				observationPrompt,
				GameStrings.LittleGirlOpeningGuidance)
			: observationPrompt;
		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup,
			opportunity.CandidatePlayerIds.ToHashSet(),
			NumberRangeConstraint.Exact(opportunity.RequiredCount),
			publicAnnouncement: GameStrings.RoleHoldersWakeUp.Format(
				GameStrings.WerewolvesGroupName),
			privateInstruction: privateInstruction);
	}

	private ConfirmationInstruction CreateWakeInstruction(GameSession session)
	{
		var agents = RequireKnownNonemptyLivingWerewolfAgents(session);
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.WakeRole,
			GameStrings.RoleHoldersWakeUp.Format(GameStrings.WerewolvesGroupName),
			privateInstruction: _littleGirlGuidanceAllowed == true
				? GameStrings.LittleGirlOpeningGuidance
				: null,
			affectedPlayerIds: agents.Select(player => player.Id).ToArray());
	}

	private SelectPlayersInstruction CreateVictimSelectionInstruction(
		GameSession session)
	{
		var agents = RequireKnownNonemptyLivingWerewolfAgents(session);
		var targets = GetLivingKnownNonAgents(session);
		if (targets.Count == 0)
		{
			throw new InvalidOperationException(
				"Werewolf victim selection requires a living known non-Agent.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectWerewolfVictim,
			targets,
			NumberRangeConstraint.Single,
			publicAnnouncement: GameStrings.WerewolvesChooseVictimPrompt,
			affectedPlayerIds: agents.Select(player => player.Id).ToArray());
	}

	private ConfirmationInstruction CreateSleepInstruction(GameSession session)
	{
		var agents = RequireKnownNonemptyLivingWerewolfAgents(session);
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleHoldersGoToSleep.Format(
				GameStrings.WerewolvesGroupName),
			privateInstruction: _littleGirlGuidanceAllowed == true
				? GameStrings.LittleGirlClosingGuidance
				: null,
			affectedPlayerIds: agents.Select(player => player.Id).ToArray());
	}

	private void CommitVictimSelection(
		GameSession session,
		ModeratorResponse input)
	{
		if (input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds ||
		    !GetLivingKnownNonAgents(session).Contains(selectedPlayerIds.Single()))
		{
			throw new InvalidOperationException(
				GetInvalidModeratorResponseMessage(
					session,
					"The Werewolf victim must be one living known non-Agent."));
		}

		session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			selectedPlayerIds.Single());
	}

	private void ValidateObservationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var opportunity = RoleFactionKnowledge
			.RequireInitialWerewolfAgentGroupOpportunity(session);
		if (instruction.RoleIdentification != null ||
		    instruction.AffectedPlayerIds != null ||
		    instruction.CountConstraint !=
		    NumberRangeConstraint.Exact(opportunity.RequiredCount) ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    opportunity.CandidatePlayerIds))
		{
			throw new InvalidOperationException(
				"The Werewolf Agent-group observation has invalid workflow context.");
		}
	}

	private static void ValidateWakeInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!HasExpectedAffectedWerewolfAgents(session, instruction))
		{
			throw new InvalidOperationException(
				"The Werewolf collective wake has invalid workflow context.");
		}
	}

	private static void ValidateVictimSelectionInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		if (instruction.RoleIdentification != null ||
		    instruction.CountConstraint != NumberRangeConstraint.Single ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetLivingKnownNonAgents(session)) ||
		    !HasExpectedAffectedWerewolfAgents(session, instruction))
		{
			throw new InvalidOperationException(
				"The Werewolf victim selection has invalid workflow context.");
		}
	}

	private static void ValidateSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!HasExpectedAffectedWerewolfAgents(session, instruction))
		{
			throw new InvalidOperationException(
				"The Werewolf collective sleep has invalid workflow context.");
		}
	}

	private void ValidateAcceptedObservationHandoff<TInstruction>(
		GameSession session,
		TInstruction instruction,
		AcceptedObservationRecoveryCursor cursor)
		where TInstruction : ModeratorInstruction
	{
		if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.SimpleWerewolf ||
		    !LittleGirlRole.HasValidRetainedGuidanceDecision(
			    session,
			    continuationRetainsGuidanceDecision: true,
			    cursor.RetainedLittleGirlGuidanceDecision))
		{
			throw new InvalidOperationException(
				"The Werewolf collective has invalid accepted-observation handoff context.");
		}
	}

	private void ValidateAgentObservationRecoveryBoundary<TInstruction>(
		GameSession session,
		TInstruction instruction,
		AcceptedObservationRecoveryCursor cursor)
		where TInstruction : ModeratorInstruction
	{
		if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.AcceptedObservationSemantic !=
		    ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup ||
		    cursor.ObservedRole != MainRoleType.SimpleWerewolf ||
		    cursor.ContinuationRole != MainRoleType.SimpleWerewolf ||
		    !LittleGirlRole.HasValidRetainedGuidanceDecision(
			    session,
			    continuationRetainsGuidanceDecision: true,
			    cursor.RetainedLittleGirlGuidanceDecision) ||
		    !HasExpectedAgentObservationBoundary(session, expected: true) ||
		    !InitialBeneficiaryClosureRules
			    .HasConsistentInitialBeneficiaryClosure(session))
		{
			throw new InvalidOperationException(
				"The Werewolf Agent-group continuation has invalid durable context.");
		}
	}

	private static bool HasExpectedReplayableSleepBoundary(
		GameSession session,
		bool expectsCommittedObservation,
		bool expectsCommittedVictim) =>
		HasExpectedAgentObservationBoundary(
			session,
			expectsCommittedObservation) &&
		HasExpectedVictimBoundary(session, expectsCommittedVictim);

	private static bool HasExpectedAgentObservationBoundary(
		GameSession session,
		bool expected)
	{
		var observations = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Where(entry =>
				entry.TurnNumber == session.TurnNumber &&
				entry.CurrentPhase == GamePhase.Night &&
				entry.Source.Kind ==
					FactionFactSourceKind.ScheduledObservation &&
				entry.Source.Identifier == FactionFactSource
					.WerewolfFactionAgentGroupObservationIdentifier)
			.ToArray();
		if (!expected)
		{
			return observations.Length == 0;
		}

		var livingPlayers = GetLivingPlayers(session);
		var livingIds = livingPlayers.Select(player => player.Id).ToHashSet();
		var knownAgentIds = livingPlayers
			.Where(player =>
				session.GetFactionAgentKnowledge(
					player.Id,
					Faction.Werewolf) == FactionAgentKnowledge.KnownAgent)
			.Select(player => player.Id)
			.ToHashSet();
		return observations is [var observation] &&
		       observation.Facts.Length == livingIds.Count &&
		       observation.Facts.All(fact =>
			       fact.Type == FactionFactType.Agent &&
			       fact.Faction == Faction.Werewolf &&
			       livingIds.Contains(fact.PlayerId)) &&
		       observation.Facts
			       .Where(fact =>
				       fact.AgentKnowledge ==
				       FactionAgentKnowledge.KnownAgent)
			       .Select(fact => fact.PlayerId)
			       .ToHashSet()
			       .SetEquals(knownAgentIds);
	}

	private static bool HasExpectedVictimBoundary(
		GameSession session,
		bool expected)
	{
		var victimActions = session.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Where(entry =>
				entry.TurnNumber == session.TurnNumber &&
				entry.CurrentPhase == GamePhase.Night &&
				entry.ActionType == NightActionType.WerewolfVictimSelection)
			.ToArray();
		if (!expected)
		{
			return victimActions.Length == 0;
		}

		return victimActions is [{ TargetIds: [var targetId] }] &&
		       GetLivingKnownNonAgents(session).Contains(targetId);
	}

	private void EvaluateLittleGirlGuidance(GameSession session)
	{
		_littleGirlGuidanceAllowed =
			EvaluateLittleGirlGuidanceAvailability(session);
		_littleGirlGuidanceEvaluated = true;
	}

	private void EnsureLittleGirlGuidanceEvaluated(GameSession session)
	{
		if (!_littleGirlGuidanceEvaluated)
		{
			EvaluateLittleGirlGuidance(session);
		}
	}

	private bool? EvaluateLittleGirlGuidanceAvailability(GameSession session)
	{
		if (!LittleGirlRole.TryCreateSpyingAttempt(session, out var attempt))
		{
			return null;
		}

		return _availabilityGateway.Evaluate(attempt)
			.AvailabilityResult.IsAvailable;
	}

	private static IReadOnlyList<IPlayer> GetLivingPlayers(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.ToArray();

	private static bool TryGetKnownLivingWerewolfAgents(
		GameSession session,
		out IReadOnlyList<IPlayer> agents)
	{
		var livingPlayers = GetLivingPlayers(session);
		if (livingPlayers.Any(player =>
			    session.GetFactionAgentKnowledge(
				    player.Id,
				    Faction.Werewolf) == FactionAgentKnowledge.Unknown))
		{
			agents = [];
			return false;
		}

		agents = livingPlayers
			.Where(player =>
				session.GetFactionAgentKnowledge(
					player.Id,
					Faction.Werewolf) == FactionAgentKnowledge.KnownAgent)
			.ToArray();
		return true;
	}

	private static IReadOnlyList<IPlayer>
		RequireKnownNonemptyLivingWerewolfAgents(GameSession session)
	{
		if (!TryGetKnownLivingWerewolfAgents(session, out var agents) ||
		    agents.Count == 0)
		{
			throw new InvalidOperationException(
				"The Werewolf collective requires a known nonempty living Agent group.");
		}

		return agents;
	}

	private static bool HasExpectedAffectedWerewolfAgents(
		GameSession session,
		ModeratorInstruction pendingInstruction) =>
		TryGetKnownLivingWerewolfAgents(session, out var agents) &&
		agents.Count > 0 &&
		pendingInstruction.AffectedPlayerIds is { } affectedPlayerIds &&
		affectedPlayerIds.SequenceEqual(
			agents.Select(player => player.Id));

	private static HashSet<Guid> GetLivingKnownNonAgents(GameSession session) =>
		GetLivingPlayers(session)
			.Where(player =>
				session.GetFactionAgentKnowledge(
					player.Id,
					Faction.Werewolf) ==
				FactionAgentKnowledge.KnownNonAgent)
			.Select(player => player.Id)
			.ToHashSet();

	private FactionFactEffectiveBoundary CommitWerewolfAgentGroupObservation(
		GameSession session,
		ModeratorResponse input)
	{
		if (input.SelectedPlayerIds is not { } observedPlayerIds)
		{
			throw new InvalidOperationException(
				GetInvalidModeratorResponseMessage(
					session,
					"Werewolf Agent-group observation requires a complete Player selection."));
		}

		try
		{
			return RoleFactionKnowledge.CommitInitialWerewolfAgentGroupObservation(
				session,
				observedPlayerIds);
		}
		catch (InvalidOperationException exception)
			when (UsesBorrowedLittleGirlPrivacy(session))
		{
			throw new InvalidOperationException(
				GetInvalidModeratorResponseMessage(session, exception.Message));
		}
	}

	private string GetInvalidModeratorResponseMessage(
		GameSession session,
		string nativeMessage) =>
		UsesBorrowedLittleGirlPrivacy(session)
			? GameStrings.ActorBorrowedRolePowerInvalidResponse
			: nativeMessage;

	private bool UsesBorrowedLittleGirlPrivacy(GameSession session) =>
		_littleGirlGuidanceAllowed == true &&
		session.GetModeratorActiveActorBorrowedRolePowerActivation()
			?.SourceRole == MainRoleType.LittleGirl;

	private static void TryCommitInitialBeneficiaryClosure(
		GameSession session,
		FactionFactEffectiveBoundary?
			earliestCompleteWerewolfAgentPartitionBoundary = null)
	{
		if (session.TurnNumber == 1)
		{
			_ = InitialBeneficiaryClosureRules.TryCommitCurrentSession(
				session,
				earliestCompleteWerewolfAgentPartitionBoundary);
		}
	}
}
