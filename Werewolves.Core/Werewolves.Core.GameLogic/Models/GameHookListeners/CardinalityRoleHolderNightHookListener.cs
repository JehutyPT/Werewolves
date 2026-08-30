using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Models.GameHookListeners;

internal enum CardinalityRoleHolderNightState
{
	Identification,
	RecognitionConfirmation,
	CommunicationConfirmation,
	SleepConfirmation,
	Asleep
}

/// <summary>
/// Implements the cardinality-driven role-holder cadence shared by Roles whose complete
/// holder set recognizes one another on Night 1 and whose surviving quorum can
/// communicate together on selected later Nights.
/// </summary>
internal abstract class CardinalityRoleHolderNightHookListener
	: RoleHookListener,
		IDeclaredRoleWorkflow
{
	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _workflowRuntime;

	protected CardinalityRoleHolderNightHookListener(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var identificationWait =
			RecoverableWait<CardinalityRoleHolderNightState,
				SelectPlayersInstruction>
				.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				CardinalityRoleHolderNightState.Identification,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateIdentificationInstruction,
				(_, instruction) =>
					instruction is SelectPlayersInstruction
					{
						RoleIdentification: { } role
					} && role == (MainRoleType)Id,
				ValidateIdentificationInstruction,
				ValidateAcceptedObservationHandoff,
				static _ => CardinalityRoleHolderNightState.Identification);
		var recognitionWait =
			RecoverableWait<CardinalityRoleHolderNightState,
				ConfirmationInstruction>
				.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				CardinalityRoleHolderNightState.RecognitionConfirmation,
				ModeratorInstructionSemantic.RecognizeRoleHolders,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateRecognitionInstruction,
				ClaimsRecognition,
				ValidateRecognitionInstruction,
				ValidateAcceptedObservationHandoff,
				static _ =>
					CardinalityRoleHolderNightState.RecognitionConfirmation);
		var communicationWait =
			RecoverableWait<CardinalityRoleHolderNightState,
				ConfirmationInstruction>
				.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				CardinalityRoleHolderNightState.CommunicationConfirmation,
				ModeratorInstructionSemantic.CommunicateAsRoleHolders,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateCommunicationInstruction,
				ClaimsCommunication,
				ValidateCommunicationInstruction,
				ValidateAcceptedObservationHandoff,
				static _ =>
					CardinalityRoleHolderNightState.CommunicationConfirmation);
		var sleepWait = CreateSleepWait();

		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				identificationWait,
				recognitionWait,
				communicationWait,
				sleepWait,
				new RoleWorkflowDecisionStep<CardinalityRoleHolderNightState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) => BeginInterval(
						session,
						input,
						identificationWait,
						recognitionWait,
						communicationWait)),
				new RoleWorkflowDecisionStep<CardinalityRoleHolderNightState>(
					Id,
					GameHook.NightMainActionLoop,
					CardinalityRoleHolderNightState.Identification,
					static _ => true,
					(session, input) => ContinueAfterIdentification(
						session,
						input,
						recognitionWait,
						sleepWait)),
				new RoleWorkflowDecisionStep<CardinalityRoleHolderNightState>(
					Id,
					GameHook.NightMainActionLoop,
					CardinalityRoleHolderNightState.CommunicationConfirmation,
					static _ => true,
					(session, input) =>
						sleepWait.Execute(session, input)),
				new RoleWorkflowCompletionStep<CardinalityRoleHolderNightState>(
					Id,
					GameHook.NightMainActionLoop,
					CardinalityRoleHolderNightState.SleepConfirmation,
					CardinalityRoleHolderNightState.Asleep,
					static _ => true),
				new RoleWorkflowCompletionStep<CardinalityRoleHolderNightState>(
					Id,
					GameHook.NightMainActionLoop,
					CardinalityRoleHolderNightState.Asleep,
					CardinalityRoleHolderNightState.Asleep,
					static _ => true)
			]);
	}

	protected abstract int InitialRoleHolderCardinality { get; }
	protected abstract int MinimumCommunicationParticipants { get; }
	protected abstract RolePowerDefinition RecognitionPower { get; }
	protected abstract RolePowerDefinition CommunicationPower { get; }
	protected abstract bool HasCommunicationInterval(int turnNumber);

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_workflowRuntime;

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		_workflowRuntime.Execute(
			session,
			input,
			session.Execution.GetCurrentListenerState<
				CardinalityRoleHolderNightState>(Id));

	private RecoverableWait<CardinalityRoleHolderNightState,
		ConfirmationInstruction> CreateSleepWait() =>
		RecoverableWait<CardinalityRoleHolderNightState,
			ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
			Id,
			GameHook.NightMainActionLoop,
			CardinalityRoleHolderNightState.RecognitionConfirmation,
			CardinalityRoleHolderNightState.SleepConfirmation,
			ModeratorInstructionSemantic.PutRoleToSleep,
			ExpectedInputType.Continue,
			static _ => true,
			static (_, _) => { },
			CreateSleepInstruction,
			ClaimsSleep,
			ValidateSleepInstruction,
			ValidateAcceptedObservationHandoff,
			static _ => CardinalityRoleHolderNightState.SleepConfirmation);

	private HookListenerActionResult BeginInterval(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<CardinalityRoleHolderNightState,
			SelectPlayersInstruction> identificationWait,
		RecoverableWait<CardinalityRoleHolderNightState,
			ConfirmationInstruction> recognitionWait,
		RecoverableWait<CardinalityRoleHolderNightState,
			ConfirmationInstruction> communicationWait)
	{
		if (session.TurnNumber == 1)
		{
			if (!IsCompleteHolderSetKnown(session))
			{
				return identificationWait.Execute(session, input);
			}

			var participants = GetLivingCurrentRoleHolders(session);
			return AreAllPowersAvailable(session, participants, RecognitionPower)
				? recognitionWait.Execute(session, input)
				: HookListenerActionResult.Complete(
					CardinalityRoleHolderNightState.Asleep);
		}

		if (!HasCommunicationInterval(session.TurnNumber))
		{
			return HookListenerActionResult.Complete(
				CardinalityRoleHolderNightState.Asleep);
		}

		var currentParticipants = GetLivingCurrentRoleHolders(session);
		if (currentParticipants.Count < MinimumCommunicationParticipants ||
		    !AreAllPowersAvailable(
			    session,
			    currentParticipants,
			    CommunicationPower))
		{
			return HookListenerActionResult.Complete(
				CardinalityRoleHolderNightState.Asleep);
		}

		return communicationWait.Execute(session, input);
	}

	private HookListenerActionResult ContinueAfterIdentification(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<CardinalityRoleHolderNightState,
			ConfirmationInstruction> recognitionWait,
		RecoverableWait<CardinalityRoleHolderNightState,
			ConfirmationInstruction> sleepWait)
	{
		IdentifyCompleteLivingRoleHolderSet(
			session,
			input.SelectedPlayerIds?.ToHashSet()
			?? throw new InvalidOperationException(
				"Role Holder Identification requires a Player selection."));
		var participants = GetLivingCurrentRoleHolders(session);
		return AreAllPowersAvailable(session, participants, RecognitionPower)
			? recognitionWait.Execute(session, input)
			: sleepWait.Execute(session, input);
	}

	private SelectPlayersInstruction CreateIdentificationInstruction(
		GameSession session) =>
		new(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			GetIdentificationCandidates(session),
			NumberRangeConstraint.Exact(InitialRoleHolderCardinality),
			publicAnnouncement: GameStrings.RoleHoldersWakeUp.Format(PublicName),
			privateInstruction:
				GameStrings.RoleMultipleIdentificationPrompt.Format(PublicName),
			roleIdentification: (MainRoleType)Id);

	private ConfirmationInstruction CreateRecognitionInstruction(
		GameSession session) =>
		new(
			ModeratorInstructionSemantic.RecognizeRoleHolders,
			GameStrings.RoleHoldersRecognitionPrompt.Format(PublicName),
			affectedPlayerIds: GetOrderedIds(
				GetLivingCurrentRoleHolders(session)));

	private ConfirmationInstruction CreateCommunicationInstruction(
		GameSession session) =>
		new(
			ModeratorInstructionSemantic.CommunicateAsRoleHolders,
			GameStrings.RoleHoldersCommunicationPrompt.Format(PublicName),
			affectedPlayerIds: GetOrderedIds(
				GetLivingCurrentRoleHolders(session)));

	private ConfirmationInstruction CreateSleepInstruction(GameSession session) =>
		new(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleHoldersGoToSleep.Format(PublicName),
			affectedPlayerIds: GetOrderedIds(
				GetLivingCurrentRoleHolders(session)));

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		if (session.TurnNumber != 1 ||
		    instruction.RoleIdentification != (MainRoleType)Id ||
		    instruction.AffectedPlayerIds != null ||
		    instruction.CountConstraint !=
		    NumberRangeConstraint.Exact(InitialRoleHolderCardinality) ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)))
		{
			throw new InvalidOperationException(
				$"The {PublicName} identification instruction has invalid workflow context.");
		}
	}

	private void ValidateRecognitionInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (session.TurnNumber != 1 ||
		    !IsCompleteHolderSetKnown(session) ||
		    !HasExactOrderedAffectedPlayers(session, instruction))
		{
			throw new InvalidOperationException(
				$"The {PublicName} recognition instruction has invalid workflow context.");
		}
	}

	private void ValidateCommunicationInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		var participants = GetLivingCurrentRoleHolders(session);
		if (!HasCommunicationInterval(session.TurnNumber) ||
		    participants.Count < MinimumCommunicationParticipants ||
		    !HasExactOrderedAffectedPlayers(session, instruction))
		{
			throw new InvalidOperationException(
				$"The {PublicName} communication instruction has invalid workflow context.");
		}
	}

	private void ValidateSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!IsSleepCadence(session) ||
		    GetLivingCurrentRoleHolders(session).Count == 0 ||
		    !HasExactOrderedAffectedPlayers(session, instruction))
		{
			throw new InvalidOperationException(
				$"The {PublicName} sleep instruction has invalid workflow context.");
		}
	}

	private void ValidateAcceptedObservationHandoff<TInstruction>(
		GameSession session,
		TInstruction instruction,
		AcceptedObservationRecoveryCursor cursor)
		where TInstruction : ModeratorInstruction
	{
		var role = (MainRoleType)Id;
		var continuesOwnIdentification =
			cursor.AcceptedObservationSemantic ==
			ModeratorInstructionSemantic.IdentifyRoleHolders &&
			cursor.ObservedRole == role;
		if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != role ||
		    cursor.RetainedLittleGirlGuidanceDecision != null ||
		    (instruction.Semantic ==
		     ModeratorInstructionSemantic.PutRoleToSleep &&
		     !continuesOwnIdentification) ||
		    (continuesOwnIdentification &&
		     (!HasCurrentTurnIdentificationBoundary(session) ||
		      instruction.Semantic is not
			      (ModeratorInstructionSemantic.RecognizeRoleHolders or
			       ModeratorInstructionSemantic.PutRoleToSleep))))
		{
			throw new InvalidOperationException(
				$"The {PublicName} workflow has invalid accepted-observation handoff context.");
		}
	}

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == (MainRoleType)Id ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == (MainRoleType)Id ||
				  player.State.ModeratorKnownRole == null &&
				  RoleFactionKnowledge.GetPossibleRoles(session, player.Id)
					  .Contains((MainRoleType)Id))))
			.ToIdSet();

	private bool IsCompleteHolderSetKnown(GameSession session) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			(MainRoleType)Id);

	private List<IPlayer> GetLivingCurrentRoleHolders(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player => player.State.CurrentRole == (MainRoleType)Id)
			.OrderBy(player => player.Id)
			.ToList();

	private bool HasExactOrderedAffectedPlayers(
		GameSession session,
		ModeratorInstruction instruction) =>
		instruction.AffectedPlayerIds is { } affectedPlayerIds &&
		affectedPlayerIds.SequenceEqual(
			GetOrderedIds(GetLivingCurrentRoleHolders(session)));

	private bool ClaimsRecognition(
		GameSession session,
		ModeratorInstruction instruction) =>
		instruction.Semantic ==
			ModeratorInstructionSemantic.RecognizeRoleHolders &&
		session.TurnNumber == 1 &&
		HasExactOrderedAffectedPlayers(session, instruction);

	private bool ClaimsCommunication(
		GameSession session,
		ModeratorInstruction instruction) =>
		instruction.Semantic ==
			ModeratorInstructionSemantic.CommunicateAsRoleHolders &&
		session.TurnNumber > 1 &&
		HasCommunicationInterval(session.TurnNumber) &&
		GetLivingCurrentRoleHolders(session).Count >=
		MinimumCommunicationParticipants &&
		HasExactOrderedAffectedPlayers(session, instruction);

	private bool ClaimsSleep(
		GameSession session,
		ModeratorInstruction instruction) =>
		instruction.Semantic == ModeratorInstructionSemantic.PutRoleToSleep &&
		IsSleepCadence(session) &&
		HasExactOrderedAffectedPlayers(session, instruction);

	private bool IsSleepCadence(GameSession session) =>
		session.TurnNumber == 1 ||
		session.TurnNumber > 1 &&
		HasCommunicationInterval(session.TurnNumber) &&
		GetLivingCurrentRoleHolders(session).Count >=
		MinimumCommunicationParticipants;

	private bool HasCurrentTurnIdentificationBoundary(GameSession session)
	{
		var role = (MainRoleType)Id;
		var livingHolderIds = GetLivingCurrentRoleHolders(session)
			.Select(player => player.Id)
			.ToHashSet();
		return livingHolderIds.Count == InitialRoleHolderCardinality &&
		       session.GameHistoryLog
			       .OfType<RoleIdentificationLogEntry>()
			       .Any(entry =>
				       entry.TurnNumber == session.TurnNumber &&
				       entry.CurrentPhase == GamePhase.Night &&
				       entry.Role == role &&
				       entry.PlayerIds.SetEquals(livingHolderIds));
	}

	private bool AreAllPowersAvailable(
		GameSession session,
		IReadOnlyList<IPlayer> participants,
		RolePowerDefinition power)
	{
		var results = participants
			.Select(participant =>
			{
				var instance = RolePowerInstance.CreateCurrent(
					session,
					participant,
					(MainRoleType)Id,
					power);
				return _availabilityGateway.Evaluate(
					new RolePowerAttempt(
						session,
						participant,
						(MainRoleType)Id,
						power,
						instance));
			})
			.ToArray();

		return results.All(result => result.AvailabilityResult.IsAvailable);
	}

	private static IReadOnlyList<Guid> GetOrderedIds(
		IEnumerable<IPlayer> participants) =>
		participants.Select(player => player.Id).Order().ToArray();
}
