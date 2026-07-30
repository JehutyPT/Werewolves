using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

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
	: RoleHookListener<CardinalityRoleHolderNightState>
{
	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	protected CardinalityRoleHolderNightHookListener(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	protected abstract int InitialRoleHolderCardinality { get; }
	protected abstract int MinimumCommunicationParticipants { get; }
	protected abstract RolePowerDefinition RecognitionPower { get; }
	protected abstract RolePowerDefinition CommunicationPower { get; }
	protected abstract bool HasCommunicationInterval(int turnNumber);

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		listenerState = string.Empty;
		if (hook != GameHook.NightMainActionLoop)
		{
			return false;
		}

		switch (pendingInstruction)
		{
			case SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.IdentifyRoleHolders,
				RoleIdentification: { } role
			} when role == (MainRoleType)Id:
				listenerState =
					CardinalityRoleHolderNightState.Identification.ToString();
				return true;
			case ConfirmationInstruction
			{
				Semantic: ModeratorInstructionSemantic.RecognizeRoleHolders
			} when HasExpectedAffectedPlayers(session, pendingInstruction):
				listenerState =
					CardinalityRoleHolderNightState
						.RecognitionConfirmation
						.ToString();
				return true;
			case ConfirmationInstruction
			{
				Semantic: ModeratorInstructionSemantic.CommunicateAsRoleHolders
			} when HasExpectedAffectedPlayers(session, pendingInstruction):
				listenerState =
					CardinalityRoleHolderNightState
						.CommunicationConfirmation
						.ToString();
				return true;
			case ConfirmationInstruction
			{
				Semantic: ModeratorInstructionSemantic.PutRoleToSleep
			} when HasExpectedAffectedPlayers(session, pendingInstruction):
				listenerState =
					CardinalityRoleHolderNightState.SleepConfirmation.ToString();
				return true;
			default:
				return false;
		}
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			null,
			[
				CardinalityRoleHolderNightState.Identification,
				CardinalityRoleHolderNightState.RecognitionConfirmation,
				CardinalityRoleHolderNightState.CommunicationConfirmation,
				CardinalityRoleHolderNightState.Asleep
			],
			BeginInterval),
		CreateStage(
			GameHook.NightMainActionLoop,
			CardinalityRoleHolderNightState.Identification,
			[
				CardinalityRoleHolderNightState.RecognitionConfirmation,
				CardinalityRoleHolderNightState.SleepConfirmation
			],
			AcceptIdentification),
		CreateStage(
			GameHook.NightMainActionLoop,
			CardinalityRoleHolderNightState.RecognitionConfirmation,
			CardinalityRoleHolderNightState.SleepConfirmation,
			(session, _) => PrepareSleepInstruction(session)),
		CreateStage(
			GameHook.NightMainActionLoop,
			CardinalityRoleHolderNightState.CommunicationConfirmation,
			CardinalityRoleHolderNightState.SleepConfirmation,
			(session, _) => PrepareSleepInstruction(session)),
		CreateStage(
			GameHook.NightMainActionLoop,
			CardinalityRoleHolderNightState.SleepConfirmation,
			CardinalityRoleHolderNightState.Asleep,
			(_, _) => HookListenerActionResult.Complete(
				CardinalityRoleHolderNightState.Asleep)),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			CardinalityRoleHolderNightState.Asleep,
			(_, _) => HookListenerActionResult.Complete(
				CardinalityRoleHolderNightState.Asleep))
	];

	private HookListenerActionResult BeginInterval(
		GameSession session,
		ModeratorResponse _)
	{
		if (session.TurnNumber == 1)
		{
			if (!IsCompleteHolderSetKnown(session))
			{
				return PrepareIdentificationInstruction(session);
			}

			var participants = GetLivingCurrentRoleHolders(session);
			return AreAllPowersAvailable(participants, RecognitionPower)
				? PrepareRecognitionInstruction(participants)
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
		    !AreAllPowersAvailable(currentParticipants, CommunicationPower))
		{
			return HookListenerActionResult.Complete(
				CardinalityRoleHolderNightState.Asleep);
		}

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.CommunicateAsRoleHolders,
				GameStrings.RoleHoldersCommunicationPrompt.Format(PublicName),
				affectedPlayerIds: GetOrderedIds(currentParticipants)),
			CardinalityRoleHolderNightState.CommunicationConfirmation);
	}

	private HookListenerActionResult AcceptIdentification(
		GameSession session,
		ModeratorResponse input)
	{
		var selectedPlayerIds = input.SelectedPlayerIds?.ToHashSet()
			?? throw new InvalidOperationException(
				"Role Holder Identification requires a Player selection.");
		IdentifyCompleteLivingRoleHolderSet(session, selectedPlayerIds);

		var participants = GetLivingCurrentRoleHolders(session);
		return AreAllPowersAvailable(participants, RecognitionPower)
			? PrepareRecognitionInstruction(participants)
			: PrepareSleepInstruction(session);
	}

	private HookListenerActionResult PrepareIdentificationInstruction(GameSession session)
	{
		var selectablePlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == (MainRoleType)Id ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == null ||
				  player.State.ModeratorKnownRole == (MainRoleType)Id)))
			.ToIdSet();

		return HookListenerActionResult.NeedInput(
			new SelectPlayersInstruction(
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				selectablePlayerIds,
				NumberRangeConstraint.Exact(InitialRoleHolderCardinality),
				publicAnnouncement: GameStrings.RoleHoldersWakeUp.Format(PublicName),
				privateInstruction:
					GameStrings.RoleMultipleIdentificationPrompt.Format(PublicName),
				roleIdentification: (MainRoleType)Id),
			CardinalityRoleHolderNightState.Identification);
	}

	private HookListenerActionResult PrepareRecognitionInstruction(
		IReadOnlyList<IPlayer> participants) =>
		HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.RecognizeRoleHolders,
				GameStrings.RoleHoldersRecognitionPrompt.Format(PublicName),
				affectedPlayerIds: GetOrderedIds(participants)),
			CardinalityRoleHolderNightState.RecognitionConfirmation);

	private HookListenerActionResult PrepareSleepInstruction(GameSession session) =>
		HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleHoldersGoToSleep.Format(PublicName),
				affectedPlayerIds: GetOrderedIds(
					GetLivingCurrentRoleHolders(session))),
			CardinalityRoleHolderNightState.SleepConfirmation);

	private bool IsCompleteHolderSetKnown(GameSession session)
	{
		var knownLivingHolders = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Count(player =>
				player.State.CurrentRole == (MainRoleType)Id &&
				player.State.ModeratorKnownRole == (MainRoleType)Id);

		return GetExpectedLivingRoleHolderCount(session) == InitialRoleHolderCardinality &&
		       knownLivingHolders == InitialRoleHolderCardinality;
	}

	private List<IPlayer> GetLivingCurrentRoleHolders(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player => player.State.CurrentRole == (MainRoleType)Id)
			.OrderBy(player => player.Id)
			.ToList();

	private bool HasExpectedAffectedPlayers(
		GameSession session,
		ModeratorInstruction pendingInstruction) =>
		pendingInstruction.AffectedPlayerIds is { } affectedPlayerIds &&
		affectedPlayerIds.ToHashSet().SetEquals(
			GetLivingCurrentRoleHolders(session).Select(player => player.Id));

	private bool AreAllPowersAvailable(
		IReadOnlyList<IPlayer> participants,
		RolePowerDefinition power)
	{
		var results = participants
			.Select(participant =>
			{
				var instance = RolePowerInstance.CreateNative(
					participant,
					(MainRoleType)Id,
					power);
				return _availabilityGateway.Evaluate(
					new RolePowerAttempt(
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
