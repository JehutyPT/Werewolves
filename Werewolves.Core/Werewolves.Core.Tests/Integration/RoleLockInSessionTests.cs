using System.Text.Json.Nodes;
using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Serialization;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public class RoleLockInSessionTests
{
	[Fact]
	public void RoleLockInAuthority_OfferOnlyVillagerVillagerIsNotInitiallyLive()
	{
		var cards = new[]
		{
			Card("91000000-0000-0000-0000-000000000001", MainRoleType.Thief),
			Card("91000000-0000-0000-0000-000000000002", MainRoleType.SimpleWerewolf),
			Card("91000000-0000-0000-0000-000000000003", MainRoleType.SimpleVillager),
			Card("91000000-0000-0000-0000-000000000004", MainRoleType.SimpleVillager),
			Card("91000000-0000-0000-0000-000000000005", MainRoleType.SimpleVillager),
			Card("91000000-0000-0000-0000-000000000006", MainRoleType.VillagerVillager),
			Card("91000000-0000-0000-0000-000000000007", MainRoleType.Seer)
		};
		var lockIn = new RoleLockIn(
			version: 11,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Take(5).Select(card => card.Id),
			offer1CardId: cards[5].Id,
			offer2CardId: cards[6].Id);
		var config = new GameSessionConfig(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			lockIn);
		config.Roles.Should().Equal(
			lockIn.DealPool.Select(card => card.PrintedRole));
		var gameId = Guid.NewGuid();
		var session = new GameSession(
			gameId,
			new StartGameConfirmationInstruction(gameId),
			config);

		session.RoleInPlayCount(MainRoleType.VillagerVillager).Should().Be(0);
		RoleKnowledgeHandlers.RequestVillagerVillagerPublicFromDealObservation(
			session,
			new ModeratorResponse()).Should().BeNull();
	}

	[Fact]
	public void RoleLockInAuthority_LegacySerializedRolesCannotOverrideActiveZones()
	{
		var roles = new[]
		{
			MainRoleType.BigBadWolf,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.Hunter,
			MainRoleType.SimpleVillager
		};
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			roles.ToList()));
		var tampered = RecoveryPayloadTestDriver
			.Parse(service.SerializeSession(start.GameGuid))
			.RewriteRolesInPlay(
				Enumerable.Repeat(MainRoleType.SimpleWerewolf, 5))
			.Serialize();
		var recoveredService = new GameService();

		var recoveredId = recoveredService.RehydrateSession(tampered);
		var recovered = recoveredService.GetGameStateView(recoveredId)!;

		recovered.RoleInPlayCount(MainRoleType.BigBadWolf).Should().Be(1);
		recovered.RoleInPlayCount(MainRoleType.SimpleWerewolf).Should().Be(0);
	}

	[Fact]
	public void StartNewGame_WithAcceptedRoleLockIn_ExposesLockedDealPoolWithoutOwnership()
	{
		var cards = new[]
		{
			Card("a0000000-0000-0000-0000-000000000001", MainRoleType.BigBadWolf),
			Card("a0000000-0000-0000-0000-000000000002", MainRoleType.Seer),
			Card("a0000000-0000-0000-0000-000000000003", MainRoleType.Witch),
			Card("a0000000-0000-0000-0000-000000000004", MainRoleType.Hunter),
			Card("a0000000-0000-0000-0000-000000000005", MainRoleType.SimpleVillager)
		};
		var lockIn = new RoleLockIn(
			version: 3,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Select(card => card.Id));
		var service = new GameService();

		var start = service.StartNewGame(new GameSessionConfig(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			lockIn));
		var session = service.GetGameStateView(start.GameGuid)!;

		session.RoleLockIn.Version.Should().Be(3);
		session.RoleLockIn.RoleComposition.Should().Equal(cards);
		session.GetModeratorPhysicalCharacterCards().Should().Equal(
			cards.Select(card => new PhysicalCharacterCardState(
				card,
				PhysicalCharacterCardZone.DealPool,
				OwnerPlayerId: null)));
		session.GetPlayers().Should().OnlyContain(player =>
			player.State.PhysicalCharacterCardId == null &&
			player.State.PhysicalCharacterCardRole == null &&
			player.State.CurrentRole == null);
	}

	[Fact]
	public void SerializeAndRehydrate_PreservesStableCardIdentitiesAndLockedZones()
	{
		var cards = new[]
		{
			Card("b0000000-0000-0000-0000-000000000001", MainRoleType.BigBadWolf),
			Card("b0000000-0000-0000-0000-000000000002", MainRoleType.Seer),
			Card("b0000000-0000-0000-0000-000000000003", MainRoleType.Witch),
			Card("b0000000-0000-0000-0000-000000000004", MainRoleType.Hunter),
			Card("b0000000-0000-0000-0000-000000000005", MainRoleType.SimpleVillager)
		};
		var lockIn = new RoleLockIn(
			version: 9,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Select(card => card.Id));
		var originalService = new GameService();
		var start = originalService.StartNewGame(new GameSessionConfig(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			lockIn));
		var serialized = originalService.SerializeSession(start.GameGuid);
		var recoveredService = new GameService();

		var recoveredId = recoveredService.RehydrateSession(serialized);
		var recovered = recoveredService.GetGameStateView(recoveredId)!;

		recovered.RoleLockIn.Version.Should().Be(9);
		recovered.RoleLockIn.RoleComposition.Should().Equal(cards);
		recovered.GetModeratorPhysicalCharacterCards().Should().Equal(
			cards.Select(card => new PhysicalCharacterCardState(
				card,
				PhysicalCharacterCardZone.DealPool,
				OwnerPlayerId: null)));
	}

	[Fact]
	public void AcceptedOwnershipObservation_MovesOneDealPoolCardWithoutInferringRoleKnowledge()
	{
		var cards = new[]
		{
			Card("c0000000-0000-0000-0000-000000000001", MainRoleType.BigBadWolf),
			Card("c0000000-0000-0000-0000-000000000002", MainRoleType.Seer),
			Card("c0000000-0000-0000-0000-000000000003", MainRoleType.Witch),
			Card("c0000000-0000-0000-0000-000000000004", MainRoleType.Hunter),
			Card("c0000000-0000-0000-0000-000000000005", MainRoleType.SimpleVillager)
		};
		var lockIn = new RoleLockIn(
			version: 6,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Select(card => card.Id));
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			lockIn));
		var session = service.GetGameStateView(start.GameGuid)!;
		var player = session.GetPlayers().First();

		var accepted = service.TryRecordPhysicalCharacterCardOwnership(
			start.GameGuid,
			expectedRoleLockInVersion: 6,
			player.Id,
			cards[1].Id);

		accepted.Should().BeTrue();
		var playerState = session.GetPlayerState(player.Id);
		playerState.PhysicalCharacterCardId.Should().Be(cards[1].Id);
		playerState.PhysicalCharacterCardRole.Should().Be(MainRoleType.Seer);
		playerState.CurrentRole.Should().BeNull();
		playerState.ModeratorKnownRole.Should().BeNull();
		session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.Id == cards[1].Id)
			.Should().Be(new PhysicalCharacterCardState(
				cards[1],
				PhysicalCharacterCardZone.PlayerOwned,
				player.Id));
	}

	[Fact]
	public void Rehydrate_WhenCardOwnershipProjectionContradictsHistory_IsRejected()
	{
		var cards = new[]
		{
			Card("c1000000-0000-0000-0000-000000000001", MainRoleType.BigBadWolf),
			Card("c1000000-0000-0000-0000-000000000002", MainRoleType.Seer),
			Card("c1000000-0000-0000-0000-000000000003", MainRoleType.Witch),
			Card("c1000000-0000-0000-0000-000000000004", MainRoleType.Hunter),
			Card("c1000000-0000-0000-0000-000000000005", MainRoleType.SimpleVillager)
		};
		var lockIn = new RoleLockIn(
			version: 6,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Select(card => card.Id));
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			lockIn));
		var session = service.GetGameStateView(start.GameGuid)!;
		var players = session.GetPlayers().ToArray();
		var observedOwner = players[0];
		var contradictoryOwner = players[1];
		service.TryRecordPhysicalCharacterCardOwnership(
			start.GameGuid,
			lockIn.Version,
			observedOwner.Id,
			cards[1].Id).Should().BeTrue();
		service.ProcessInstruction(start.GameGuid, start.CreateResponse())
			.IsSuccess.Should().BeTrue();
		var snapshot = JsonNode.Parse(
			service.SerializeSession(start.GameGuid))!.AsObject();
		var cardState = snapshot["PhysicalCharacterCards"]!.AsArray()
			.Select(node => node!.AsObject())
			.Single(node => node["CardId"]!.GetValue<Guid>() == cards[1].Id);
		cardState["Zone"] = PhysicalCharacterCardZone.PlayerOwned.ToString();
		cardState["OwnerPlayerId"] = contradictoryOwner.Id;
		var playerStates = snapshot[nameof(GameSessionDto.Players)]!.AsArray()
			.Select(node => node!.AsObject())
			.ToDictionary(node => node["Id"]!.GetValue<Guid>());
		playerStates[observedOwner.Id]["PhysicalCharacterCardId"] = null;
		playerStates[observedOwner.Id]["PhysicalCharacterCardRole"] = null;
		playerStates[contradictoryOwner.Id]["PhysicalCharacterCardId"] = cards[1].Id;
		playerStates[contradictoryOwner.Id]["PhysicalCharacterCardRole"] =
			MainRoleType.Seer.ToString();

		var recover = () => new GameService().RehydrateSession(snapshot.ToJsonString());

		recover.Should().Throw<InvalidOperationException>()
			.WithMessage("*Physical Character Card ownership*history*");
	}

	[Fact]
	public void StaleOwnershipObservation_IsRejectedWithoutMutation()
	{
		var cards = new[]
		{
			Card("d0000000-0000-0000-0000-000000000001", MainRoleType.BigBadWolf),
			Card("d0000000-0000-0000-0000-000000000002", MainRoleType.Seer),
			Card("d0000000-0000-0000-0000-000000000003", MainRoleType.Witch),
			Card("d0000000-0000-0000-0000-000000000004", MainRoleType.Hunter),
			Card("d0000000-0000-0000-0000-000000000005", MainRoleType.SimpleVillager)
		};
		var lockIn = new RoleLockIn(
			version: 2,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Select(card => card.Id));
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			lockIn));
		var session = service.GetGameStateView(start.GameGuid)!;
		var player = session.GetPlayers().First();
		var beforeCards = session.GetModeratorPhysicalCharacterCards().ToArray();
		var beforeLogCount = session.GameHistoryLog.Count();

		var accepted = service.TryRecordPhysicalCharacterCardOwnership(
			start.GameGuid,
			expectedRoleLockInVersion: 1,
			player.Id,
			cards[1].Id);

		accepted.Should().BeFalse();
		session.GetModeratorPhysicalCharacterCards().Should().Equal(beforeCards);
		session.GetPlayerState(player.Id).PhysicalCharacterCardId.Should().BeNull();
		session.GameHistoryLog.Should().HaveCount(beforeLogCount);
	}

	private static PhysicalCharacterCard Card(string id, MainRoleType printedRole) =>
		new(Guid.Parse(id), printedRole);
}
