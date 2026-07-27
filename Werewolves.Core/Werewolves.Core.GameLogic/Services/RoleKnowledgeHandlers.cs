using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Services;

internal static class RoleKnowledgeHandlers
{
    internal static ModeratorInstruction? RequestPublicRoleReveal(
        GameSession session,
        IReadOnlyCollection<IPlayer> players,
        ModeratorInstructionSemantic semantic,
        string? publicAnnouncement = null)
    {
        var requestedPlayers = players
            .Where(player => player.State.PubliclyRevealedRole == null)
            .ToArray();
        if (requestedPlayers.Length == 0)
        {
            return null;
        }

        var playersNeedingMapping = requestedPlayers
            .Where(player =>
                player.State.CurrentRole == null ||
                player.State.ModeratorKnownRole != player.State.CurrentRole)
            .ToArray();
        var affectedPlayerIds = requestedPlayers
            .Select(player => player.Id)
            .ToArray();

        if (playersNeedingMapping.Length == 0)
        {
            return new ConfirmationInstruction(
                semantic,
                publicAnnouncement: publicAnnouncement,
                privateInstruction: GameStrings.PublicRoleRevealInstruction,
                affectedPlayerIds: affectedPlayerIds);
        }

        var rolesForAssignment = GameSessionQueries.GetUnassignedRoles(session);
        rolesForAssignment.AddRange(playersNeedingMapping
            .Where(player => player.State.CurrentRole.HasValue)
            .Select(player => player.State.CurrentRole!.Value));

        return new AssignRolesInstruction(
            semantic,
            playersForAssignment: playersNeedingMapping
                .Select(player => player.Id)
                .ToImmutableHashSet(),
            rolesForAssignment: rolesForAssignment,
            publicAnnouncement: publicAnnouncement,
            privateInstruction: GameStrings.PublicRoleRevealInstruction,
            affectedPlayerIds: affectedPlayerIds);
    }

    internal static void RecordPublicRoleReveal(
        GameSession session,
        IReadOnlyCollection<IPlayer> players,
        ModeratorResponse input)
    {
        var requestedPlayers = players
            .Where(player => player.State.PubliclyRevealedRole == null)
            .ToArray();
        if (requestedPlayers.Length == 0)
        {
            return;
        }

        var revealedRoles = new Dictionary<Guid, MainRoleType>();
        foreach (var player in requestedPlayers)
        {
            if (input.AssignedPlayerRoles?.TryGetValue(player.Id, out var assignedRole) == true)
            {
                if (player.State.CurrentRole is { } currentRole && assignedRole != currentRole)
                {
                    throw new InvalidOperationException(
                        $"The accepted Role Reveal response for Player {player.Id} contradicts their committed current Role.");
                }

                revealedRoles[player.Id] = assignedRole;
                continue;
            }

            if (player.State.CurrentRole is not { } knownRole)
            {
                throw new InvalidOperationException(
                    $"The accepted Role Reveal response did not establish a Role for Player {player.Id}.");
            }

            revealedRoles[player.Id] = knownRole;
        }

        session.RevealRoles(revealedRoles);
    }

    internal static ModeratorInstruction? RequestVillagerVillagerPublicFromDealObservation(
        GameSession session,
        ModeratorResponse input)
    {
        if (!NeedsVillagerVillagerPublicFromDealObservation(session))
        {
            return null;
        }

        return new SelectPlayersInstruction(
            ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal,
            session.GetPlayers().Select(player => player.Id).ToHashSet(),
            NumberRangeConstraint.Single,
            privateInstruction: GameStrings.VillagerVillagerPublicFromDealInstruction,
            affectedPlayerIds: session.GetPlayers().Select(player => player.Id).ToArray());
    }

    internal static void RecordVillagerVillagerPublicFromDealObservation(
        GameSession session,
        ModeratorResponse input)
    {
        if (!NeedsVillagerVillagerPublicFromDealObservation(session))
        {
            return;
        }

        session.ObserveVillagerVillagerFromDeal(input.SelectedPlayerIds!.Single());
    }

    private static bool NeedsVillagerVillagerPublicFromDealObservation(GameSession session)
        => session.RoleInPlayCount(MainRoleType.VillagerVillager) == 1 &&
           session.GetPlayers().All(player =>
               player.State.PubliclyRevealedRole != MainRoleType.VillagerVillager);
}
