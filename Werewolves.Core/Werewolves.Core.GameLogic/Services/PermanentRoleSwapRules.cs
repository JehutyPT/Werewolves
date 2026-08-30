using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Services;

internal static class PermanentRoleSwapRules
{
	internal static PermanentRoleSwapRequest CreateThiefExchangeRequest(
		GameSession session,
		Guid playerId,
		PhysicalCharacterCard outgoingThiefCard,
		PhysicalCharacterCard selectedOffer,
		PhysicalCharacterCard unselectedOffer)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(outgoingThiefCard);
		ArgumentNullException.ThrowIfNull(selectedOffer);
		ArgumentNullException.ThrowIfNull(unselectedOffer);

		var policy = new PermanentRoleSwapPolicy(
			PrivateRoleKnowledge: PermanentRoleSwapDisposition.Change,
			PublicRevealHistory: PermanentRoleSwapDisposition.Preserve,
			FactionBeneficiary: PermanentRoleSwapDisposition.Change,
			FactionAgents: PermanentRoleSwapDisposition.Change,
			Relationships: PermanentRoleSwapDisposition.Preserve,
			StatusEffects: PermanentRoleSwapDisposition.Preserve,
			VotingState: PermanentRoleSwapDisposition.Preserve,
			Restrictions: PermanentRoleSwapDisposition.Preserve,
			Assignments: PermanentRoleSwapDisposition.Preserve,
			RolePowerState: PermanentRoleSwapDisposition.Change);
		var factions = new PermanentRoleSwapFactionReplacement(
			ExpectedBeneficiary(selectedOffer.PrintedRole),
			FactionFactFactions.All.ToDictionary(
				faction => faction,
				faction => ExpectedAgentKnowledge(
					selectedOffer.PrintedRole,
					faction)));
		return new PermanentRoleSwapRequest(
			session.RoleLockIn.Version,
			playerId,
			MainRoleType.Thief,
			selectedOffer.PrintedRole,
			new PermanentRoleSwapCardMovement(
				outgoingThiefCard.Id,
				selectedOffer.Id,
				[unselectedOffer.Id]),
			policy,
			factions,
			PermanentRoleSwapStateChanges.None);
	}

	internal static DevotedServantRoleTakeRequest
		CreateDevotedServantRoleTakeRequest(
			GameSession session,
			Guid actingPlayerId,
			Guid voteTargetId,
			MainRoleType observedPrintedRole,
			MainRoleType newCurrentRole)
	{
		ArgumentNullException.ThrowIfNull(session);
		var actor = session.GetPlayers().SingleOrDefault(player =>
			player.Id == actingPlayerId) ??
			throw new InvalidOperationException(
				"The Devoted Servant actor is unavailable.");
		var target = session.GetPlayers().SingleOrDefault(player =>
			player.Id == voteTargetId) ??
			throw new InvalidOperationException(
				"The Devoted Servant Vote Target is unavailable.");
		var cardStates = session.GetModeratorPhysicalCharacterCards();
		var outgoing = cardStates.SingleOrDefault(state =>
			state.Card.Id == actor.State.PhysicalCharacterCardId &&
			state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
			state.OwnerPlayerId == actingPlayerId &&
			state.Card.PrintedRole == MainRoleType.DevotedServant) ??
			throw new InvalidOperationException(
				"The Devoted Servant outgoing Character Card is unavailable.");
		var acquired = target.State.PhysicalCharacterCardId is { } targetCardId
			? cardStates.SingleOrDefault(state =>
				state.Card.Id == targetCardId &&
				state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
				state.OwnerPlayerId == voteTargetId &&
				state.Card.PrintedRole == observedPrintedRole)
			: cardStates.FirstOrDefault(state =>
				state.Zone == PhysicalCharacterCardZone.DealPool &&
				state.OwnerPlayerId is null &&
				state.Card.PrintedRole == observedPrintedRole);
		if (acquired is null)
		{
			throw new InvalidOperationException(
				"The observed acquired Character Card is unavailable.");
		}

		var policy = DevotedServantRoleTakePolicy();
		var effectsToClear = new[]
			{
				StatusEffectTypes.Charmed,
				StatusEffectTypes.Sheriff,
				StatusEffectTypes.TownCrier
			}
			.Where(actor.State.HasStatusEffect)
			.ToHashSet();
		var durableVotingPower = actor.State.DurableVotingPower -
			(effectsToClear.Contains(StatusEffectTypes.Sheriff) ? 1 : 0);
		var stateChanges = new PermanentRoleSwapStateChanges(
			new HashSet<StatusEffectTypes>(),
			effectsToClear,
			new PermanentRoleSwapVotingState(
				actor.State.HasVotingRight,
				Math.Max(0, durableVotingPower)),
			new HashSet<string>(),
			new HashSet<string>());
		var isInfected = actor.State.HasStatusEffect(
			StatusEffectTypes.LycanthropyInfection);
		var factions = new PermanentRoleSwapFactionReplacement(
			isInfected
				? Faction.Werewolf
				: ExpectedBeneficiary(newCurrentRole),
			FactionFactFactions.All.ToDictionary(
				faction => faction,
				faction =>
					faction == Faction.Werewolf && isInfected
						? FactionAgentKnowledge.KnownAgent
						: ExpectedAgentKnowledge(newCurrentRole, faction)));

		return new DevotedServantRoleTakeRequest(
			session.RoleLockIn.Version,
			actingPlayerId,
			voteTargetId,
			observedPrintedRole,
			newCurrentRole,
			target.State.CurrentRole,
			new PermanentRoleSwapCardMovement(
				outgoing.Card.Id,
				acquired.Card.Id,
				[],
				target.State.PhysicalCharacterCardId is null
					? null
					: voteTargetId),
			policy,
			factions,
			stateChanges);
	}

	internal static void EnforceValidHistory(GameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		var swaps = GameSessionQueries.GetCommittedPermanentRoleSwaps(session);
		if (swaps.Any(entry => !HasExpectedFactionFacts(entry)))
		{
			throw new InvalidOperationException(
				"Permanent Role Swap Faction defaults are invalid.");
		}
		if (swaps.Any(entry =>
			entry.ExpectedCurrentRole == MainRoleType.Thief &&
			!IsValidCommittedThiefExchange(session, entry)))
		{
			throw new InvalidOperationException(
				"Permanent Role Swap Thief exchange history is invalid.");
		}
	}

	internal static bool IsValidCommittedThiefExchange(
		GameSession session,
		PermanentRoleSwapCommittedLogEntry entry)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(entry);
		return HasExpectedFactionFacts(entry) && IsValidThiefExchange(
			session,
			entry.RoleLockInVersion,
			entry.ExpectedCurrentRole,
			entry.NewCurrentRole,
			entry.PhysicalCards,
			entry.Policy,
			entry.StateChanges);
	}

	internal static bool CanCommit(
		GameSession session,
		PermanentRoleSwapRequest request)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(request);
		if (request.PhysicalCards is null ||
			request.Policy is null ||
			request.Factions is null ||
			request.StateChanges is null ||
			!Enum.IsDefined(request.ExpectedCurrentRole) ||
			!Enum.IsDefined(request.NewCurrentRole))
		{
			return false;
		}

		return HasExpectedFactionReplacement(request) &&
			(request.ExpectedCurrentRole != MainRoleType.Thief ||
				IsValidThiefExchange(
					session,
					request.ExpectedRoleLockInVersion,
					request.ExpectedCurrentRole,
					request.NewCurrentRole,
					request.PhysicalCards,
					request.Policy,
					request.StateChanges));
	}

	private static bool IsValidThiefExchange(
		GameSession session,
		long roleLockInVersion,
		MainRoleType expectedCurrentRole,
		MainRoleType newCurrentRole,
		PermanentRoleSwapCardMovement physicalCards,
		PermanentRoleSwapPolicy policy,
		PermanentRoleSwapStateChanges stateChanges)
	{
		var offer1 = session.RoleLockIn.Offer1;
		var offer2 = session.RoleLockIn.Offer2;
		if (roleLockInVersion != session.RoleLockIn.Version ||
			expectedCurrentRole != MainRoleType.Thief ||
			offer1 is null || offer2 is null)
		{
			return false;
		}

		var acquiredCardId = physicalCards.AcquiredCardId;
		var otherOfferCardId = acquiredCardId == offer1.Id
			? offer2.Id
			: acquiredCardId == offer2.Id
				? offer1.Id
				: Guid.Empty;
		var acquiredOffer = acquiredCardId == offer1.Id
			? offer1
			: acquiredCardId == offer2.Id
				? offer2
				: null;
		var outgoingCard = session.RoleLockIn.RoleComposition
			.SingleOrDefault(card =>
				card.Id == physicalCards.OutgoingOwnedCardId);
		return IsThiefExchangePolicy(policy) &&
			stateChanges.IsEmpty &&
			outgoingCard?.PrintedRole == MainRoleType.Thief &&
			acquiredOffer?.PrintedRole == newCurrentRole &&
			physicalCards.ExpectedAcquiredCardOwnerPlayerId is null &&
			otherOfferCardId != Guid.Empty &&
			physicalCards.AdditionalSetAsideCardIds.Count == 1 &&
			physicalCards.AdditionalSetAsideCardIds[0] ==
				otherOfferCardId;
	}

	private static bool IsThiefExchangePolicy(
		PermanentRoleSwapPolicy policy) =>
		policy.IsExplicit &&
		policy.PrivateRoleKnowledge == PermanentRoleSwapDisposition.Change &&
		policy.PublicRevealHistory == PermanentRoleSwapDisposition.Preserve &&
		policy.FactionBeneficiary == PermanentRoleSwapDisposition.Change &&
		policy.FactionAgents == PermanentRoleSwapDisposition.Change &&
		policy.Relationships == PermanentRoleSwapDisposition.Preserve &&
		policy.StatusEffects == PermanentRoleSwapDisposition.Preserve &&
		policy.VotingState == PermanentRoleSwapDisposition.Preserve &&
		policy.Restrictions == PermanentRoleSwapDisposition.Preserve &&
		policy.Assignments == PermanentRoleSwapDisposition.Preserve &&
		policy.RolePowerState == PermanentRoleSwapDisposition.Change;

	private static PermanentRoleSwapPolicy DevotedServantRoleTakePolicy() => new(
		PrivateRoleKnowledge: PermanentRoleSwapDisposition.Change,
		PublicRevealHistory: PermanentRoleSwapDisposition.Preserve,
		FactionBeneficiary: PermanentRoleSwapDisposition.Change,
		FactionAgents: PermanentRoleSwapDisposition.Change,
		Relationships: PermanentRoleSwapDisposition.Preserve,
		StatusEffects: PermanentRoleSwapDisposition.Change,
		VotingState: PermanentRoleSwapDisposition.Change,
		Restrictions: PermanentRoleSwapDisposition.Preserve,
		Assignments: PermanentRoleSwapDisposition.Preserve,
		RolePowerState: PermanentRoleSwapDisposition.Change);

	private static bool HasExpectedFactionReplacement(
		PermanentRoleSwapRequest request) =>
		(request.Policy.FactionBeneficiary !=
				PermanentRoleSwapDisposition.Change ||
			request.Factions.BeneficiaryCandidate ==
				ExpectedBeneficiary(request.NewCurrentRole)) &&
		(request.Policy.FactionAgents != PermanentRoleSwapDisposition.Change ||
			FactionFactFactions.All.All(faction =>
				request.Factions.AgentFacts[faction] ==
					ExpectedAgentKnowledge(request.NewCurrentRole, faction)));

	private static bool HasExpectedFactionFacts(
		PermanentRoleSwapCommittedLogEntry entry) =>
		(entry.Policy.FactionBeneficiary !=
				PermanentRoleSwapDisposition.Change ||
			entry.Facts.Any(fact =>
				fact.Type == FactionFactType.Beneficiary &&
				fact.Faction == ExpectedBeneficiary(entry.NewCurrentRole))) &&
		(entry.Policy.FactionAgents != PermanentRoleSwapDisposition.Change ||
			FactionFactFactions.All.All(faction =>
				entry.Facts.Any(fact =>
					fact.Type == FactionFactType.Agent &&
					fact.Faction == faction &&
					fact.AgentKnowledge == ExpectedAgentKnowledge(
						entry.NewCurrentRole,
						faction))));

	private static Faction ExpectedBeneficiary(MainRoleType role) => role switch
	{
		MainRoleType.SimpleWerewolf or MainRoleType.BigBadWolf or
			MainRoleType.AccursedWolfFather => Faction.Werewolf,
		MainRoleType.WhiteWerewolf => Faction.WhiteWerewolf,
		MainRoleType.Piper => Faction.Piper,
		MainRoleType.PrejudicedManipulator => Faction.PrejudicedManipulator,
		_ => Faction.Villager
	};

	private static FactionAgentKnowledge ExpectedAgentKnowledge(
		MainRoleType role,
		Faction faction) =>
			RoleFactionKnowledge.EstablishesInitialWerewolfAgency(role) &&
			faction == Faction.Werewolf
				? FactionAgentKnowledge.KnownAgent
			: FactionAgentKnowledge.KnownNonAgent;
}
