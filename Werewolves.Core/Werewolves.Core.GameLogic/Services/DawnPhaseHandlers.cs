using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Services;

internal static class DawnPhaseHandlers
{
    internal static void CalculateVictims(GameSession session, ModeratorResponse input)
        => NightInteractionResolver.ResolveNightPhase(session);

    internal static bool HasVictimsToAnnounce(GameSession session)
        => GameSessionQueries.GetPendingDawnEliminations(session).Any();
}
