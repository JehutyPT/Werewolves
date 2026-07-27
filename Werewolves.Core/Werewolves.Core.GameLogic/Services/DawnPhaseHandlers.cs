using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Services;

internal static class DawnPhaseHandlers
{
    internal static void CalculateVictims(GameSession session, ModeratorResponse input)
        => NightInteractionResolver.ResolveNightPhase(session);

    internal static bool HasVictimsToAnnounce(GameSession session)
        => GameSessionQueries.GetPendingDawnEliminations(session).Any();

    internal static ModeratorInstruction AnnounceVictimsAndRequestRoles(GameSession session, ModeratorResponse input)
    {
        var victimList = GameSessionQueries.GetPendingDawnEliminations(session)
            .Select(consequence => consequence.Player)
            .ToImmutableHashSet();
        var victimNameList = string.Join(Environment.NewLine, victimList.Select(p => p.Name));
        var announcement = GameStrings.MultipleVictimEliminatedAnnounce.Format(victimNameList);

        return RoleKnowledgeHandlers.RequestPublicRoleReveal(
                   session,
                   victimList,
                   ModeratorInstructionSemantic.AssignDawnVictimRoles,
                   announcement)
               ?? new ConfirmationInstruction(
                   ModeratorInstructionSemantic.AnnounceDawnVictims,
                   publicAnnouncement: announcement);
    }

    internal static void AssignVictimRoles(GameSession session, ModeratorResponse input)
    {
        var pendingEliminations = GameSessionQueries.GetPendingDawnEliminations(session).ToArray();
        RoleKnowledgeHandlers.RecordPublicRoleReveal(
            session,
            pendingEliminations.Select(consequence => consequence.Player).ToArray(),
            input);

        foreach (var (player, reason) in pendingEliminations)
        {
            session.EliminatePlayer(player.Id, reason);
        }
    }
}
