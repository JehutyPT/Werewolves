using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
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
        => GameSessionQueries.GetPlayersEliminatedThisDawn(session).Any();

    internal static ModeratorInstruction AnnounceVictimsAndRequestRoles(GameSession session, ModeratorResponse input)
    {
        var victimList = GameSessionQueries.GetPlayersEliminatedThisDawn(session).ToImmutableHashSet();
        var victimNameList = string.Join(Environment.NewLine, victimList.Select(p => p.Name));
        var announcement = GameStrings.MultipleVictimEliminatedAnnounce.Format(victimNameList);
        var victimsNeedingRoles = victimList
            .Where(p => p.State.MainRole == null)
            .ToImmutableHashSet();

        if (victimsNeedingRoles.Count == 0)
        {
            return new ConfirmationInstruction(publicAnnouncement: announcement);
        }

        if (GameSessionQueries.TryGetOnlyPossibleUnassignedRole(session, victimsNeedingRoles.Count, out var role))
        {
            session.AssignRole(victimsNeedingRoles.Select(p => p.Id).ToHashSet(), role);
            return new ConfirmationInstruction(publicAnnouncement: announcement);
        }

        return new AssignRolesInstruction(
            publicAnnouncement: announcement,
            privateInstruction: GameStrings.RevealRolePromptSpecify,
            playersForAssignment: victimsNeedingRoles.Select(p => p.Id).ToImmutableHashSet(),
            rolesForAssignment: GameSessionQueries.GetUnassignedRoles(session));
    }

    internal static void AssignVictimRoles(GameSession session, ModeratorResponse input)
    {
        var victimsNeedingRoles = GameSessionQueries.GetPlayersEliminatedThisDawn(session)
            .Where(p => p.State.MainRole == null)
            .ToList();

        if (victimsNeedingRoles.Count == 0)
        {
            return;
        }

        foreach (var entry in input.AssignedPlayerRoles!)
        {
            session.AssignRole(entry.Key, entry.Value);
        }
    }
}
