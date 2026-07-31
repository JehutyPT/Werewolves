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
			.Where(player => player.State.PhysicalCharacterCardId is null)
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
                revealedRoles[player.Id] = assignedRole;
                continue;
            }

			var physicalRole = player.State.PhysicalCharacterCardId is { } cardId
				? session.GetModeratorPhysicalCharacterCards()
					.Single(cardState => cardState.Card.Id == cardId)
					.Card.PrintedRole
				: player.State.ModeratorKnownRole ?? player.State.CurrentRole;
			if (physicalRole is not { } revealedRole)
            {
                throw new InvalidOperationException(
					$"The accepted Role Reveal response did not establish a physical Role for Player {player.Id}.");
            }

			revealedRoles[player.Id] = revealedRole;
        }

		foreach (var roleGroup in requestedPlayers
			.Where(player =>
				player.State.CurrentRole is null ||
				player.State.ModeratorKnownRole is null &&
				player.State.CurrentRole == revealedRoles[player.Id])
			.GroupBy(player => player.State.CurrentRole ?? revealedRoles[player.Id]))
		{
			session.IdentifyRole(
				roleGroup.Select(player => player.Id).ToHashSet(),
				roleGroup.Key);
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
