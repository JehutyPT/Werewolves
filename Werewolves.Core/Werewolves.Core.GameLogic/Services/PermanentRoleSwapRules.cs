using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Services;

internal static class PermanentRoleSwapRules
{
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
				IsValidThiefExchange(session, request));
	}

	private static bool IsValidThiefExchange(
		GameSession session,
		PermanentRoleSwapRequest request)
	{
		var offer1 = session.RoleLockIn.Offer1;
		var offer2 = session.RoleLockIn.Offer2;
		if (offer1 is null || offer2 is null)
		{
			return false;
		}

		var acquiredCardId = request.PhysicalCards.AcquiredCardId;
		var otherOfferCardId = acquiredCardId == offer1.Id
			? offer2.Id
			: acquiredCardId == offer2.Id
				? offer1.Id
				: Guid.Empty;
		var outgoingCard = session.RoleLockIn.RoleComposition
			.SingleOrDefault(card =>
				card.Id == request.PhysicalCards.OutgoingOwnedCardId);
		return IsThiefExchangePolicy(request.Policy) &&
			request.StateChanges.IsEmpty &&
			outgoingCard?.PrintedRole == MainRoleType.Thief &&
			otherOfferCardId != Guid.Empty &&
			request.PhysicalCards.AdditionalSetAsideCardIds.Count == 1 &&
			request.PhysicalCards.AdditionalSetAsideCardIds[0] ==
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

	private static bool HasExpectedFactionReplacement(
		PermanentRoleSwapRequest request)
	{
		var expectedBeneficiary = request.NewCurrentRole switch
		{
			MainRoleType.SimpleWerewolf or MainRoleType.BigBadWolf or
				MainRoleType.AccursedWolfFather => Faction.Werewolf,
			MainRoleType.WhiteWerewolf => Faction.WhiteWerewolf,
			MainRoleType.Piper => Faction.Piper,
			_ => Faction.Villager
		};
		var expectedWerewolfAgent = request.NewCurrentRole is
			MainRoleType.SimpleWerewolf or MainRoleType.BigBadWolf or
			MainRoleType.AccursedWolfFather or MainRoleType.WhiteWerewolf;
		return (request.Policy.FactionBeneficiary !=
				PermanentRoleSwapDisposition.Change ||
			request.Factions.BeneficiaryCandidate == expectedBeneficiary) &&
			(request.Policy.FactionAgents !=
				PermanentRoleSwapDisposition.Change ||
			Enum.GetValues<Faction>().All(faction =>
				request.Factions.AgentFacts[faction] ==
				(expectedWerewolfAgent && faction == Faction.Werewolf
					? FactionAgentKnowledge.KnownAgent
					: FactionAgentKnowledge.KnownNonAgent)));
	}
}
