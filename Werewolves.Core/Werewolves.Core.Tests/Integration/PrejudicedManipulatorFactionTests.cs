using System.Collections.Immutable;
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

public sealed class PrejudicedManipulatorFactionTests
{
	[Fact]
	public void ThiefAcquiresPrejudicedManipulator_ChangesBeneficiaryAndAllAgentFacts()
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
		session.CommitFactionFactBatch(context =>
		{
			var boundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			return new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ExplicitTransition,
					"test-thief-initial-faction"),
				Facts =
				[
					.. roster.Select(player =>
						FactionFact.Beneficiary(
							player.Id,
							player.Id == roster[1].Id
								? Faction.Werewolf
								: Faction.Villager,
							boundary)),
					.. roster.SelectMany(player =>
						FactionFactFactions.All.Select(faction =>
							FactionFact.Agent(
								player.Id,
								faction,
								player.Id == roster[1].Id &&
								faction == Faction.Werewolf
									? FactionAgentKnowledge.KnownAgent
									: FactionAgentKnowledge.KnownNonAgent,
								boundary)))
				]
			};
		});
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
					choice.CreateResponse(ThiefOfferOptionIds.Offer1)));

		session.GetPlayerState(roster[0].Id).CurrentRole.Should().Be(
			MainRoleType.PrejudicedManipulator);
		session.GetPlayerState(roster[0].Id).PhysicalCharacterCardRole.Should().Be(
			MainRoleType.PrejudicedManipulator);
		session.RequireKnownFactionBeneficiary(roster[0].Id).Should().Be(
			Faction.PrejudicedManipulator);
		FactionFactFactions.All.Should().OnlyContain(faction =>
			session.GetFactionAgentKnowledge(roster[0].Id, faction) ==
			FactionAgentKnowledge.KnownNonAgent);
		var next =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					sleep.CreateResponse()));
		next.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		next.AffectedPlayerIds.Should().Equal(roster[1].Id);
	}

	[Fact]
	public void AcceptedIdentification_WithKnownWerewolfGroup_CommitsExclusiveBeneficiaryClosureWithoutAgents()
	{
		var roster = Enumerable.Range(1, 5)
			.Select(index => new GameSessionPlayerConfig(
				Guid.NewGuid(),
				$"Player{index}"))
			.ToArray();
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			roster.Take(2).Select(player => player.Id),
			roster.Skip(2).Select(player => player.Id));
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			roster,
			[
				MainRoleType.PrejudicedManipulator,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			publicGroupPartition: partition));
		var session = service.GetGameStateView(start.GameGuid)
			.Should().BeOfType<GameSession>().Subject;
		session.CommitFactionFactBatch(context =>
		{
			var boundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			return new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ScheduledObservation,
					"test-complete-werewolf-agent-group"),
				Facts = roster.Select(player => FactionFact.Agent(
					player.Id,
					Faction.Werewolf,
					player.Id == roster[1].Id
						? FactionAgentKnowledge.KnownAgent
						: FactionAgentKnowledge.KnownNonAgent,
					boundary)).ToImmutableArray()
			};
		});
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

		service.ProcessInstruction(
			start.GameGuid,
			identification.CreateResponse([roster[0].Id]))
			.IsSuccess.Should().BeTrue();

		session.RequireKnownFactionBeneficiary(roster[0].Id)
			.Should().Be(Faction.PrejudicedManipulator);
		session.RequireKnownFactionBeneficiary(roster[1].Id)
			.Should().Be(Faction.Werewolf);
		foreach (var villager in roster.Skip(2))
		{
			session.RequireKnownFactionBeneficiary(villager.Id)
				.Should().Be(Faction.Villager);
		}
		roster.Should().OnlyContain(player =>
			session.GetFactionAgentKnowledge(
				player.Id,
				Faction.PrejudicedManipulator) ==
			FactionAgentKnowledge.Unknown);
		session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Source.Kind ==
					FactionFactSourceKind.InitialBeneficiaryClosure &&
				entry.Facts.Any(fact =>
					fact.PlayerId == roster[0].Id &&
					fact.Type == FactionFactType.Beneficiary &&
					fact.Faction == Faction.PrejudicedManipulator) &&
				entry.Facts.All(fact =>
					fact.Type == FactionFactType.Beneficiary));
	}

	[Fact]
	public void FactionVocabulary_IncludesPrejudicedManipulator()
	{
		Enum.GetNames<Faction>().Should().Contain("PrejudicedManipulator");
	}

	[Fact]
	public void FactionFactVocabulary_AcceptsPrejudicedManipulatorBeneficiaryFacts()
	{
		var faction = Enum.Parse<Faction>("PrejudicedManipulator");

		var fact = FactionFact.Beneficiary(
			Guid.NewGuid(),
			faction,
			new FactionFactEffectiveBoundary(1, GamePhase.Night, 0));

		fact.Faction.Should().Be(faction);
	}

	[Fact]
	public void FactionFactSchema_CurrentVersionIsTwo()
	{
		FactionFactSchema.CurrentVersion.Should().Be(2);
	}

	[Fact]
	public void FactionFactSchema_VersionOneIsRejected()
	{
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Player1", "Player2", "Player3", "Player4", "Player5"],
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]));
		var payload = JsonNode.Parse(
			service.GetGameStateView(start.GameGuid)!.Serialize())!.AsObject();
		payload[nameof(GameSessionDto.FactionFactSchemaVersion)] = 1;

		Action rehydrate = () =>
			new GameService().RehydrateSession(payload.ToJsonString());

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*Unsupported Faction fact schema version '1'*");
	}
}
