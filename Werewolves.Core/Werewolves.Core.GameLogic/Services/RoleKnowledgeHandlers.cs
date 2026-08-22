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

			var playersWithUnknownRoles = requestedPlayers
				.Where(player => GameSessionQueries.GetEstablishedRole(player) is null)
				.ToArray();
        var affectedPlayerIds = requestedPlayers
            .Select(player => player.Id)
            .ToArray();
		if (playersWithUnknownRoles.Length == 0)
		{
			return new ConfirmationInstruction(
				semantic,
				publicAnnouncement: publicAnnouncement,
				privateInstruction:
					CreatePublicRoleRevealPrivateInstruction(requestedPlayers),
                affectedPlayerIds: affectedPlayerIds);
        }

		var selectableRolesForPlayers = CreateSelectableRolesForPlayers(
			session,
			playersWithUnknownRoles);
		var playersForAssignment = selectableRolesForPlayers
			.Where(entry => entry.Value.Distinct().Skip(1).Any())
			.Select(entry => entry.Key)
			.ToImmutableHashSet();
		var privateInstruction = CreatePublicRoleRevealPrivateInstruction(
			requestedPlayers,
			selectableRolesForPlayers);

		return new AssignRolesInstruction(
			semantic,
			playersForAssignment,
			selectableRolesForPlayers,
            publicAnnouncement: publicAnnouncement,
			privateInstruction: privateInstruction,
			affectedPlayerIds: affectedPlayerIds);
	}

	private static IReadOnlyDictionary<Guid, IReadOnlyList<MainRoleType>>
		CreateSelectableRolesForPlayers(
			GameSession session,
			IReadOnlyCollection<IPlayer> players)
	{
		var roleOptions = players.ToDictionary(
			player => player.Id,
			player => GameSessionQueries.GetPossibleRoles(session, player.Id)
				.ToList());
		if (roleOptions.Any(entry => entry.Value.Count == 0))
		{
			throw new InvalidOperationException(
				"An unknown Player has no possible Role for the pending public reveal.");
		}

		var reservedPlayers = new HashSet<Guid>();
		while (roleOptions
			.Where(entry => !reservedPlayers.Contains(entry.Key))
			.FirstOrDefault(entry => entry.Value.Distinct().Take(2).Count() == 1)
			is { Key: var playerId, Value: not null } singleton)
		{
			var role = singleton.Value[0];
			reservedPlayers.Add(playerId);
			foreach (var other in roleOptions.Where(entry =>
				!reservedPlayers.Contains(entry.Key)))
			{
				other.Value.Remove(role);
				if (other.Value.Count == 0)
				{
					throw new InvalidOperationException(
						"Public Role Reveal singleton reservations exhausted a Player's possible Roles.");
				}
			}
		}

		return roleOptions.ToDictionary(
			entry => entry.Key,
			entry => (IReadOnlyList<MainRoleType>)Array.AsReadOnly(
				entry.Value.ToArray()));
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
		var playersWithUnknownRoles = requestedPlayers
			.Where(player => GameSessionQueries.GetEstablishedRole(player) is null)
			.ToArray();
		AssignRolesInstruction? assignmentInstruction = null;
		IReadOnlyDictionary<Guid, IReadOnlyList<MainRoleType>>?
			currentRoleOptions = null;
		if (playersWithUnknownRoles.Length > 0)
		{
			assignmentInstruction = session.Execution.PendingInstruction
				as AssignRolesInstruction
				?? throw new InvalidOperationException(
					"An unknown public Role Reveal has no pending assignment instruction.");
			if (assignmentInstruction.InstructionId != input.InstructionId ||
				!assignmentInstruction.SelectableRolesForPlayers.Keys.ToHashSet()
					.SetEquals(playersWithUnknownRoles.Select(player => player.Id)))
			{
				throw new InvalidOperationException(
					"The public Role Reveal response is not correlated with its offered Player Roles.");
			}

			currentRoleOptions = CreateSelectableRolesForPlayers(
				session,
				playersWithUnknownRoles);
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
				if (assignedRole is null)
				{
					var offeredRoles = assignmentInstruction!
						.SelectableRolesForPlayers[player.Id];
					if (assignmentInstruction.PlayersForAssignment.Contains(
							player.Id))
					{
						if (input.AssignedPlayerRoles?.TryGetValue(
								player.Id,
								out var mappedRole) != true ||
							!offeredRoles.Contains(mappedRole) ||
							!currentRoleOptions![player.Id].Contains(mappedRole))
						{
							throw new InvalidOperationException(
								$"No available Deal Pool card matches the accepted Role Reveal for Player {player.Id}.");
						}

						assignedRole = mappedRole;
					}
					else
					{
						var offeredRole = offeredRoles.Distinct().SingleOrDefault();
						var currentRole = currentRoleOptions![player.Id]
							.Distinct()
							.SingleOrDefault();
						if (offeredRole != currentRole ||
							offeredRoles.Distinct().Count() != 1 ||
							currentRoleOptions[player.Id].Distinct().Count() != 1)
						{
							throw new InvalidOperationException(
								$"The confirmed Role Reveal for Player {player.Id} is no longer entailed.");
						}

						assignedRole = offeredRole;
					}
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
		IReadOnlyCollection<IPlayer> requestedPlayers,
		IReadOnlyDictionary<Guid, IReadOnlyList<MainRoleType>>?
			selectableRolesForPlayers = null)
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
		var entailedRoleDescriptions = requestedPlayers
			.Where(player =>
				selectableRolesForPlayers?.TryGetValue(
					player.Id,
					out _) == true)
			.Select(player => (
				Player: player,
				Roles: selectableRolesForPlayers![player.Id]))
			.Where(entry => entry.Roles.Distinct().Take(2).Count() == 1)
			.Select(entry =>
				$"{entry.Player.Name}: {entry.Roles[0].GetPublicName()}")
			.ToArray();
		if (knownRoleDescriptions.Length == 0 &&
			entailedRoleDescriptions.Length == 0)
		{
			return GameStrings.PublicRoleRevealInstruction;
		}

		var instructions = new List<string>
		{
			GameStrings.PublicRoleRevealInstruction
		};
		if (knownRoleDescriptions.Length > 0)
		{
			instructions.Add(
				GameStrings.PublicRoleRevealKnownRolesInstruction.Format(
					string.Join("; ", knownRoleDescriptions)));
		}
		if (entailedRoleDescriptions.Length > 0)
		{
			instructions.Add(
				GameStrings.PublicRoleRevealEntailedRolesInstruction.Format(
					string.Join("; ", entailedRoleDescriptions)));
		}

		return string.Join(" ", instructions);
	}

    internal static ModeratorInstruction? RequestVillagerVillagerPublicFromDealObservation(
        GameSession session,
        ModeratorResponse input)
    {
        if (!NeedsVillagerVillagerPublicFromDealObservation(session))
        {
            return null;
        }

		var candidateIds = session.GetPlayers()
			.Where(player => player.State.Health == PlayerHealth.Alive)
			.Where(player =>
				GameSessionQueries.GetEstablishedRole(player) is { } establishedRole
					? establishedRole == MainRoleType.VillagerVillager
					: GameSessionQueries.GetPossibleRoles(session, player.Id)
						.Contains(MainRoleType.VillagerVillager))
			.Select(player => player.Id)
			.ToHashSet();

        return new SelectPlayersInstruction(
            ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal,
			candidateIds,
            NumberRangeConstraint.Single,
            privateInstruction: GameStrings.VillagerVillagerPublicFromDealInstruction,
			affectedPlayerIds: candidateIds.ToArray());
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
