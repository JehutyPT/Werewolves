using System.Text.Json.Nodes;
using FluentAssertions;
using Werewolves.Core.GameLogic;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Serialization;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class PrejudicedManipulatorRoleTests
{
	[Fact]
	public void AcceptedIdentificationAndBeneficiaryClosure_RehydrateWithPartitionAndSchemaTwo()
	{
		var roster = Enumerable.Range(1, 5)
			.Select(index => new GameSessionPlayerConfig(
				Guid.NewGuid(),
				$"Player{index}"))
			.ToArray();
		var roles = new List<MainRoleType>
		{
			MainRoleType.PrejudicedManipulator,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			roster.Take(2).Select(player => player.Id),
			roster.Skip(2).Select(player => player.Id));
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			roster,
			roles,
			publicGroupPartition: partition));
		var nightStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					start.CreateResponse()));
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					nightStart.CreateResponse()));
		var werewolfObservation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					identification.CreateResponse([roster[0].Id])));
		service.GetGameStateView(start.GameGuid)!
			.GetFactionAgentKnowledge(roster[0].Id, Faction.Werewolf).Should()
			.Be(FactionAgentKnowledge.KnownNonAgent);
		var committedNext =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					werewolfObservation.CreateResponse([roster[1].Id])));
		committedNext.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var serialized = service.SerializeSession(start.GameGuid);
		var payload = JsonNode.Parse(serialized)!.AsObject();
		var recoveredService = new GameService();

		var recoveredId = recoveredService.RehydrateSession(
			serialized);

		var recoveredNext = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			recoveredService.GetCurrentInstruction(recoveredId));
		recoveredNext.Should().BeEquivalentTo(committedNext);
		payload[nameof(GameSessionDto.FactionFactSchemaVersion)]!
			.GetValue<int>().Should().Be(FactionFactSchema.CurrentVersion);
		var recovered = recoveredService.GetGameStateView(recoveredId)!;
		recovered.PublicGroupPartition.Should().Be(partition);
		recovered.GetPlayerState(roster[0].Id).PhysicalCharacterCardRole
			.Should().Be(MainRoleType.PrejudicedManipulator);
		recovered.GetFactionAgentKnowledge(roster[0].Id, Faction.Werewolf).Should()
			.Be(FactionAgentKnowledge.KnownNonAgent);
		recovered.RequireKnownFactionBeneficiary(roster[0].Id)
			.Should().Be(Faction.PrejudicedManipulator);
		recovered.RequireKnownFactionBeneficiary(roster[1].Id)
			.Should().Be(Faction.Werewolf);
		foreach (var villager in roster.Skip(2))
		{
			recovered.RequireKnownFactionBeneficiary(villager.Id)
				.Should().Be(Faction.Villager);
		}
		recovered.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.PrejudicedManipulator &&
				entry.PlayerIds.SetEquals(new[] { roster[0].Id }));
	}

	[Fact]
	public void NightOne_KnownEmptyAfterThiefDecline_SkipsTheWholeIdentificationSlot()
	{
		var roster = Enumerable.Range(1, 5)
			.Select(index => new GameSessionPlayerConfig(
				Guid.NewGuid(),
				$"Player{index}"))
			.ToArray();
		var cards = new[]
		{
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Thief),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(
				Guid.NewGuid(),
				MainRoleType.PrejudicedManipulator),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Seer)
		};
		var lockIn = new RoleLockIn(
			version: 1,
			playerCount: roster.Length,
			cards,
			cards.Take(roster.Length).Select(card => card.Id),
			cards[5].Id,
			cards[6].Id);
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			roster.Take(2).Select(player => player.Id),
			roster.Skip(2).Select(player => player.Id));
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			roster,
			lockIn,
			publicGroupPartition: partition));
		var session = service.GetGameStateView(start.GameGuid)
			.Should().BeOfType<GameSession>().Subject;
		session.TryRecordPhysicalCharacterCardOwnership(
			lockIn.Version,
			roster[0].Id,
			cards[0].Id).Should().BeTrue();
		session.AssignRole(roster[0].Id, MainRoleType.Thief);
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { roster[0].Id },
			MainRoleType.Thief);
		var nightStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					start.CreateResponse()));
		var thiefWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					nightStart.CreateResponse()));
		var choice =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					thiefWake.CreateResponse()));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					choice.CreateResponse(ThiefOfferOptionIds.Decline)));

		var next = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			service.ProcessInstruction(
				start.GameGuid,
				sleep.CreateResponse()));

		next.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		service.GetGameStateView(start.GameGuid)!
			.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.Id == cards[5].Id)
			.Zone.Should().Be(PhysicalCharacterCardZone.SetAside);
	}

	[Fact]
	public void NightOne_UnknownDealPoolHolder_RunsAfterThiefBeforeActor()
	{
		var roster = Enumerable.Range(1, 5)
			.Select(index => new GameSessionPlayerConfig(
				Guid.NewGuid(),
				$"Player{index}"))
			.ToArray();
		var cards = new[]
		{
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Thief),
			new PhysicalCharacterCard(
				Guid.NewGuid(),
				MainRoleType.PrejudicedManipulator),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Actor),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Seer),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Cupid)
		};
		var lockIn = new RoleLockIn(
			version: 1,
			playerCount: roster.Length,
			cards,
			cards.Take(roster.Length).Select(card => card.Id),
			cards[5].Id,
			cards[6].Id);
		var actorSetupCards = new ActorSetupCards(
			[
				MainRoleType.Defender,
				MainRoleType.Fox,
				MainRoleType.Witch
			]);
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			roster.Take(2).Select(player => player.Id),
			roster.Skip(2).Select(player => player.Id));
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			roster,
			lockIn,
			actorSetupCards,
			partition));
		var session = service.GetGameStateView(start.GameGuid)
			.Should().BeOfType<GameSession>().Subject;
		session.TryRecordPhysicalCharacterCardOwnership(
			lockIn.Version,
			roster[0].Id,
			cards[0].Id).Should().BeTrue();
		session.AssignRole(roster[0].Id, MainRoleType.Thief);
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { roster[0].Id },
			MainRoleType.Thief);
		var nightStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					start.CreateResponse()));
		var thiefWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					nightStart.CreateResponse()));
		var thiefChoice =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					thiefWake.CreateResponse()));
		thiefChoice.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseThiefOffer);
		var thiefSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					thiefChoice.CreateResponse(ThiefOfferOptionIds.Decline)));
		thiefSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);

		var prejudicedManipulatorIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					thiefSleep.CreateResponse()));

		prejudicedManipulatorIdentification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		prejudicedManipulatorIdentification.RoleIdentification.Should().Be(
			MainRoleType.PrejudicedManipulator);
		prejudicedManipulatorIdentification.CountConstraint.Should().Be(
			NumberRangeConstraint.Single);

		var actorIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					prejudicedManipulatorIdentification.CreateResponse(
						[roster[1].Id])));

		actorIdentification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		actorIdentification.RoleIdentification.Should().Be(MainRoleType.Actor);
		actorIdentification.CountConstraint.Should().Be(
			NumberRangeConstraint.Single);

		var actorChoice =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					actorIdentification.CreateResponse([roster[2].Id])));

		actorChoice.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseActorSetupCard);
	}

	[Fact]
	public void NightOne_KnownHolder_SkipsTheWholeIdentificationSlot()
	{
		var roster = Enumerable.Range(1, 5)
			.Select(index => new GameSessionPlayerConfig(
				Guid.NewGuid(),
				$"Player{index}"))
			.ToArray();
		var roles = new List<MainRoleType>
		{
			MainRoleType.PrejudicedManipulator,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			roster.Take(2).Select(player => player.Id),
			roster.Skip(2).Select(player => player.Id));
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			roster,
			roles,
			publicGroupPartition: partition));
		var session = service.GetGameStateView(start.GameGuid)
			.Should().BeOfType<GameSession>().Subject;
		var card = session.GetModeratorPhysicalCharacterCards()
			.Single(state =>
				state.Card.PrintedRole ==
					MainRoleType.PrejudicedManipulator)
			.Card;
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			roster[0].Id,
			card.Id).Should().BeTrue();
		session.AssignRole(roster[0].Id, MainRoleType.PrejudicedManipulator);
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { roster[0].Id },
			MainRoleType.PrejudicedManipulator);
		service.ProcessInstruction(start.GameGuid, start.CreateResponse())
			.IsSuccess.Should().BeTrue();
		var nightStart = InstructionAssert.ExpectType<ConfirmationInstruction>(
			service.GetCurrentInstruction(start.GameGuid));

		var next = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			service.ProcessInstruction(
				start.GameGuid,
				nightStart.CreateResponse()));

		next.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
	}

	[Fact]
	public void NightOne_UnknownHolder_IdentifiesExactlyOnceBeforeWerewolfObservation()
	{
		var roster = Enumerable.Range(1, 5)
			.Select(index => new GameSessionPlayerConfig(
				Guid.NewGuid(),
				$"Player{index}"))
			.ToArray();
		var roles = new List<MainRoleType>
		{
			MainRoleType.PrejudicedManipulator,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			roster.Take(2).Select(player => player.Id),
			roster.Skip(2).Select(player => player.Id));
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			roster,
			roles,
			publicGroupPartition: partition));
		service.ProcessInstruction(start.GameGuid, start.CreateResponse())
			.IsSuccess.Should().BeTrue();
		var nightStart = InstructionAssert.ExpectType<ConfirmationInstruction>(
			service.GetCurrentInstruction(start.GameGuid));

		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					nightStart.CreateResponse()));

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(
			MainRoleType.PrejudicedManipulator);
		identification.CountConstraint.Should().Be(
			NumberRangeConstraint.Single);

		var werewolfObservation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					identification.CreateResponse([roster[0].Id])));

		werewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		var identifiedState = service.GetGameStateView(start.GameGuid)!
			.GetPlayerState(roster[0].Id);
		identifiedState.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.PrejudicedManipulator);
		identifiedState.PhysicalCharacterCardId.Should().NotBeNull();
	}
}
