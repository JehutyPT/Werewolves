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

		var availableDealPoolCards = session
			.GetModeratorPhysicalCharacterCards()
			.Where(state => state.Zone == PhysicalCharacterCardZone.DealPool)
			.Select(state => state.Card)
			.ToList();
		var ownershipsToRecord = new List<(Guid PlayerId, Guid CardId)>();
        var revealedRoles = new Dictionary<Guid, MainRoleType>();
        foreach (var player in requestedPlayers)
        {
			if (player.State.PhysicalCharacterCardId is { } cardId)
			{
				revealedRoles[player.Id] = session
					.GetModeratorPhysicalCharacterCards()
					.Single(cardState => cardState.Card.Id == cardId)
					.Card.PrintedRole;
				continue;
			}

			if (input.AssignedPlayerRoles?.TryGetValue(
					player.Id,
					out var assignedRole) != true)
            {
                throw new InvalidOperationException(
					$"The accepted Role Reveal response did not establish a physical Role for Player {player.Id}.");
            }

			var card = availableDealPoolCards.FirstOrDefault(candidate =>
				candidate.PrintedRole == assignedRole)
				?? throw new InvalidOperationException(
					$"No available Deal Pool card matches the accepted Role Reveal for Player {player.Id}.");
			availableDealPoolCards.Remove(card);
			ownershipsToRecord.Add((player.Id, card.Id));
			revealedRoles[player.Id] = assignedRole;
        }
		var initialRoleIdentifications = requestedPlayers
			.Where(player => player.State.CurrentRole == null)
			.GroupBy(player => revealedRoles[player.Id])
			.Select(group => (
				Role: group.Key,
				PlayerIds: group.Select(player => player.Id).ToHashSet()))
			.ToArray();

		foreach (var (playerId, cardId) in ownershipsToRecord)
		{
			if (!session.TryRecordPhysicalCharacterCardOwnership(
					session.RoleLockIn.Version,
					playerId,
					cardId))
			{
				throw new InvalidOperationException(
					"The accepted Role Reveal physical-card mapping became stale.");
			}
		}

		foreach (var (role, playerIds) in initialRoleIdentifications)
		{
			session.IdentifyRole(playerIds, role);
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
