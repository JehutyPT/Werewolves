using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Services;

internal static class DawnPhaseHandlers
{
	internal static ModeratorInstruction? RequestElderRoleIdentification(
		GameSession session,
		ModeratorResponse input) =>
		NightInteractionResolver.RequiresElderRoleIdentification(session)
			? NightInteractionResolver.GetElderRole(session)
				.CreateResistanceIdentificationInstruction(session)
			: null;

	internal static void RecordElderRoleIdentification(
		GameSession session,
		ModeratorResponse input)
	{
		if (!NightInteractionResolver.RequiresElderRoleIdentification(session))
		{
			return;
		}

		NightInteractionResolver.GetElderRole(session)
			.RecordResistanceHolderIdentification(session, input);
	}

	    internal static void CalculateVictims(GameSession session, ModeratorResponse input)
	        => NightInteractionResolver.ResolveNightPhase(session);

    internal static bool HasVictimsToAnnounce(GameSession session)
        => GameSessionQueries.GetPendingDawnEliminations(session).Any();
}
