using System.Collections.Immutable;
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
        => session.GetPlayersEliminatedThisDawn().Any();

    internal static ModeratorInstruction AnnounceVictimsAndRequestRoles(GameSession session, ModeratorResponse input)
    {
        var victimList = session.GetPlayersEliminatedThisDawn().ToImmutableHashSet();
        var victimNameList = string.Join(Environment.NewLine, victimList.Select(p => p.Name));
        var announcement = GameStrings.MultipleVictimEliminatedAnnounce.Format(victimNameList);
        var victimsNeedingRoles = victimList
            .Where(p => p.State.MainRole == null)
            .ToImmutableHashSet();

        if (victimsNeedingRoles.Count == 0)
        {
            return new ConfirmationInstruction(publicAnnouncement: announcement);
        }

        return new AssignRolesInstruction(
            publicAnnouncement: announcement,
            privateInstruction: GameStrings.RevealRolePromptSpecify,
            playersForAssignment: victimsNeedingRoles.Select(p => p.Id).ToImmutableHashSet(),
            rolesForAssignment: session.GetUnassignedRoles());
    }

    internal static void AssignVictimRoles(GameSession session, ModeratorResponse input)
    {
        var victimsNeedingRoles = session.GetPlayersEliminatedThisDawn()
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
