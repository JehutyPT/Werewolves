using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
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
			.Where(player => GameSessionQueries.GetEstablishedRole(player) is null)
			.ToArray();
        var affectedPlayerIds = requestedPlayers
            .Select(player => player.Id)
            .ToArray();
		var privateInstruction = CreatePublicRoleRevealPrivateInstruction(
			requestedPlayers);

        if (playersNeedingMapping.Length == 0)
        {
            return new ConfirmationInstruction(
                semantic,
                publicAnnouncement: publicAnnouncement,
				privateInstruction: privateInstruction,
                affectedPlayerIds: affectedPlayerIds);
        }

		var rolesForAssignment = GameSessionQueries.GetUnclaimedRoles(session);

        return new AssignRolesInstruction(
            semantic,
            playersForAssignment: playersNeedingMapping
                .Select(player => player.Id)
                .ToImmutableHashSet(),
            rolesForAssignment: rolesForAssignment,
            publicAnnouncement: publicAnnouncement,
			privateInstruction: privateInstruction,
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
		var requestedPlayerIds = requestedPlayers
			.Select(player => player.Id)
			.ToHashSet();
		foreach (var player in session.GetPlayers())
		{
			if (requestedPlayerIds.Contains(player.Id) ||
				player.State.PhysicalCharacterCardId is not null ||
				GameSessionQueries.GetEstablishedRole(player) is not { } establishedRole)
			{
				continue;
			}

			var claimedCard = availableDealPoolCards.FirstOrDefault(candidate =>
				candidate.PrintedRole == establishedRole)
				?? throw new InvalidOperationException(
					$"No available Deal Pool card remains for Player {player.Id}'s established Role.");
			availableDealPoolCards.Remove(claimedCard);
		}
		var ownershipsToRecord = new List<(Guid PlayerId, Guid CardId)>();
        var revealedRoles = new Dictionary<Guid, MainRoleType>();
        foreach (var player in requestedPlayers)
        {
			if (player.State.PhysicalCharacterCardId is not null)
			{
				revealedRoles[player.Id] =
					GameSessionQueries.GetEstablishedRole(player)!.Value;
				continue;
			}

			var assignedRole = GameSessionQueries.GetEstablishedRole(player);
			if (assignedRole is null &&
				input.AssignedPlayerRoles?.TryGetValue(
					player.Id,
					out var mappedRole) == true)
			{
				if (!GameSessionQueries.GetPossibleRoles(session, player.Id)
					.Contains(mappedRole))
				{
					throw new InvalidOperationException(
						$"No available Deal Pool card matches the accepted Role Reveal for Player {player.Id}.");
				}
				assignedRole = mappedRole;
			}
			if (assignedRole is null)
			{
				throw new InvalidOperationException(
					$"The accepted Role Reveal response did not establish a physical Role for Player {player.Id}.");
			}

			var card = availableDealPoolCards.FirstOrDefault(candidate =>
				candidate.PrintedRole == assignedRole.Value)
				?? throw new InvalidOperationException(
					$"No available Deal Pool card matches the accepted Role Reveal for Player {player.Id}.");
			availableDealPoolCards.Remove(card);
			ownershipsToRecord.Add((player.Id, card.Id));
			revealedRoles[player.Id] = assignedRole.Value;
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
		AngelLifecycleRules.ApplyExpiredHolderProjection(session, revealedRoles);
    }

	internal static string CreatePublicRoleRevealPrivateInstruction(
		IReadOnlyCollection<IPlayer> requestedPlayers)
	{
		var knownRoleDescriptions = requestedPlayers
			.Select(player => (
				Player: player,
				Role: GameSessionQueries.GetEstablishedRole(player)))
			.Where(established => established.Role.HasValue)
			.Select(established =>
				$"{established.Player.Name}: " +
				$"{established.Role!.Value.GetPublicName()}")
			.ToArray();
		if (knownRoleDescriptions.Length == 0)
		{
			return GameStrings.PublicRoleRevealInstruction;
		}

		return string.Concat(
			GameStrings.PublicRoleRevealInstruction,
			" ",
			GameStrings.PublicRoleRevealKnownRolesInstruction.Format(
				string.Join("; ", knownRoleDescriptions)));
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
