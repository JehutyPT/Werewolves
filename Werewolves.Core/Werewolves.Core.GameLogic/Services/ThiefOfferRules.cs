using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;

namespace Werewolves.Core.GameLogic.Services;

internal static class ThiefOfferRules
{
	internal static bool IsDeclineLegal(
		MainRoleType offer1Role,
		MainRoleType offer2Role) =>
		!offer1Role.IsHardAlignedWerewolf() ||
		!offer2Role.IsHardAlignedWerewolf();

	internal static bool TryCommitDecline(
		GameSession session,
		Guid playerId)
	{
		ArgumentNullException.ThrowIfNull(session);
		var offer1 = session.RoleLockIn.Offer1;
		var offer2 = session.RoleLockIn.Offer2;
		return offer1 is not null &&
			offer2 is not null &&
			IsDeclineLegal(offer1.PrintedRole, offer2.PrintedRole) &&
			session.TryCommitThiefOfferDecline(playerId);
	}

	internal static bool HasValidCommittedChoice(
		GameSession session,
		Guid playerId)
	{
		ArgumentNullException.ThrowIfNull(session);
		if (session.TurnNumber != 1 ||
		    session.GetCurrentPhase() != GamePhase.Night ||
		    playerId == Guid.Empty)
		{
			return false;
		}

		var swaps = GameSessionQueries.GetCommittedThiefExchanges(
			session,
			playerId,
			session.RoleLockIn.Version);
		var validSwapCount = swaps.Count(entry =>
			PermanentRoleSwapRules.IsValidCommittedThiefExchange(
				session,
				entry));
		var declines = GameSessionQueries.GetCommittedThiefOfferDeclines(
			session,
			playerId,
			session.RoleLockIn.Version);
		var validDeclineCount = declines.Count(entry =>
			IsValidCommittedDecline(session, entry));
		return validSwapCount + validDeclineCount == 1;
	}

	internal static void EnforceValidHistory(GameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		if (GameSessionQueries.GetAllCommittedThiefOfferDeclines(session)
			.Any(entry => !IsValidCommittedDecline(session, entry)))
		{
			throw new InvalidOperationException(
				"Thief offer decline history is invalid.");
		}
	}

	private static bool IsValidCommittedDecline(
		GameSession session,
		ThiefOfferDeclinedLogEntry entry)
	{
		var offer1 = session.RoleLockIn.Offer1;
		var offer2 = session.RoleLockIn.Offer2;
		return offer1 is not null &&
			offer2 is not null &&
			entry.RoleLockInVersion == session.RoleLockIn.Version &&
			entry.TurnNumber == 1 &&
			entry.CurrentPhase == GamePhase.Night &&
			session.GetPlayers().Any(player => player.Id == entry.PlayerId) &&
			session.RoleLockIn.RoleComposition.Any(card =>
				card.Id == entry.ThiefCardId &&
				card.PrintedRole == MainRoleType.Thief) &&
			entry.Offer1CardId == offer1.Id &&
			entry.Offer2CardId == offer2.Id &&
			IsDeclineLegal(offer1.PrintedRole, offer2.PrintedRole);
	}
}
