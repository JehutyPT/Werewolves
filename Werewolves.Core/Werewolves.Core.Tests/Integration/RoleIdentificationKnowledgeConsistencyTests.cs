using FluentAssertions;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class RoleIdentificationKnowledgeConsistencyTests
	: DiagnosticTestBase
{
	public RoleIdentificationKnowledgeConsistencyTests(ITestOutputHelper output)
		: base(output) { }

	[Fact]
	public void SeerIdentification_AfterInitialWerewolfObservation_NarrowsRejectsAndAllowsLegalRetry()
	{
		var builder = CreateBuilder()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var initialWerewolfAgent = players[0];
		var seer = players[1];
		var victim = players[2];
		builder.CompleteWerewolfNightAction(
			[initialWerewolfAgent.Id],
			victim.Id);

		var identification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.Seer);
		identification.SelectablePlayerIds.Should().NotContain(
			initialWerewolfAgent.Id);
		identification.SelectablePlayerIds.Should().Contain(seer.Id);

		var stateBeforeRejection = session.Serialize();
		var forgedContradiction = new ModeratorResponse
		{
			InstructionId = identification.InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = new HashSet<Guid>
			{
				initialWerewolfAgent.Id
			}
		};

		Action reject = () => builder.Process(forgedContradiction);

		reject.Should().Throw<InvalidOperationException>();
		session.Serialize().Should().Be(stateBeforeRejection);
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			identification.InstructionId);

		var accepted = builder.Process(
			identification.CreateResponse([seer.Id]));

		accepted.IsSuccess.Should().BeTrue();
		seer.State.CurrentRole.Should().Be(MainRoleType.Seer);
		MarkTestCompleted();
	}

	[Fact]
	public void ThiefIdentification_WithInitialAgentForgery_RejectsBeforeBindingCardAndAllowsLegalRetry()
	{
		var cards = new[]
		{
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Thief),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Seer),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Cupid)
		};
		var lockIn = new RoleLockIn(
			version: 1,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Take(5).Select(card => card.Id),
			offer1CardId: cards[5].Id,
			offer2CardId: cards[6].Id);
		var service = new GameService();
		var start = service.StartNewSimulationGame(
			new GameSessionConfig(
				["Player1", "Player2", "Player3", "Player4", "Player5"],
				lockIn),
			Enumerable.Range(1, 5)
				.Select(seatNumber => CreateSimulationFactionFacts(
					seatNumber,
					isInitialWerewolfAgent: seatNumber == 1))
				.ToArray());
		var session = service.GetGameStateView(start.GameGuid)!;
		var players = session.GetPlayers().ToArray();
		var initialWerewolfAgent = players[0];
		var legalThief = players[1];
		var nightStart = InstructionAssert.ExpectSuccessWithType<
			ConfirmationInstruction>(service.ProcessInstruction(
			start.GameGuid,
			start.CreateResponse()));
		var identification = InstructionAssert.ExpectSuccessWithType<
			SelectPlayersInstruction>(service.ProcessInstruction(
			start.GameGuid,
			nightStart.CreateResponse()));
		identification.RoleIdentification.Should().Be(MainRoleType.Thief);
		identification.SelectablePlayerIds.Should().NotContain(
			initialWerewolfAgent.Id);
		var stateBeforeRejection = session.Serialize();
		var forgedContradiction = new ModeratorResponse
		{
			InstructionId = identification.InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = new HashSet<Guid>
			{
				initialWerewolfAgent.Id
			}
		};

		Action reject = () => GameFlowManager.HandleInput(
			(GameSession)session,
			forgedContradiction,
			SupportedRoleCatalog.Admissions);

		reject.Should().Throw<InvalidOperationException>()
			.WithMessage("Role Identification contradicts committed Role knowledge.");
		session.Serialize().Should().Be(stateBeforeRejection);
		service.GetCurrentInstruction(start.GameGuid)!.InstructionId.Should().Be(
			identification.InstructionId);
		initialWerewolfAgent.State.PhysicalCharacterCardId.Should().BeNull();
		session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.Id == cards[0].Id)
			.Zone.Should().Be(PhysicalCharacterCardZone.DealPool);

		var accepted = service.ProcessInstruction(
			start.GameGuid,
			identification.CreateResponse([legalThief.Id]));

		accepted.IsSuccess.Should().BeTrue();
		legalThief.State.CurrentRole.Should().Be(MainRoleType.Thief);
		legalThief.State.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.Thief);
		MarkTestCompleted();
	}

	[Fact]
	public void AccursedWolfFatherIdentification_AfterInitialWerewolfObservation_OffersOnlyInitialAgents()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var initialWerewolfAgents = new HashSet<Guid>
		{
			players[0].Id,
			players[1].Id
		};
		builder.CompleteWerewolfNightAction(
			initialWerewolfAgents,
			players[2].Id);

		var identification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identification.RoleIdentification.Should().Be(
			MainRoleType.AccursedWolfFather);
		identification.SelectablePlayerIds.Should().BeEquivalentTo(
			initialWerewolfAgents);

		var accepted = builder.Process(
			identification.CreateResponse([players[1].Id]));

		accepted.IsSuccess.Should().BeTrue();
		players[1].State.CurrentRole.Should().Be(
			MainRoleType.AccursedWolfFather);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(MainRoleType.WhiteWerewolf)]
	[InlineData(MainRoleType.BigBadWolf)]
	public void WolfSideIdentification_AfterInitialWerewolfObservation_OffersAndAcceptsAnInitialAgent(
		MainRoleType identifiedRole)
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				identifiedRole,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var initialWerewolfAgents = new HashSet<Guid>
		{
			players[0].Id,
			players[1].Id
		};
		builder.CompleteWerewolfNightAction(
			initialWerewolfAgents,
			players[2].Id);

		var identification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identification.RoleIdentification.Should().Be(identifiedRole);
		identification.SelectablePlayerIds.Should().BeEquivalentTo(
			initialWerewolfAgents);

		var accepted = builder.Process(
			identification.CreateResponse([players[1].Id]));

		accepted.IsSuccess.Should().BeTrue();
		players[1].State.CurrentRole.Should().Be(identifiedRole);
		MarkTestCompleted();
	}

	[Fact]
	public void ScapegoatIdentification_AfterDawnInfection_OffersAndAcceptsTheInfectedPlayer()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var infectedPlayer = players[2];
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [players[0].Id, players[1].Id],
			WerewolfVictimId = infectedPlayer.Id,
			AccursedWolfFatherId = players[1].Id,
			AccursedWolfFatherInfectsVictim = true
		});

		infectedPlayer.State.HasStatusEffect(
			StatusEffectTypes.LycanthropyInfection).Should().BeTrue();
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var holderObservation = builder.Process(vote.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		holderObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveScapegoatHolderForTie);
		holderObservation.SelectablePlayerIds.Should().Contain(
			infectedPlayer.Id);

		var accepted = builder.Process(
			holderObservation.CreateResponse([infectedPlayer.Id]));

		accepted.IsSuccess.Should().BeTrue();
		infectedPlayer.State.CurrentRole.Should().Be(MainRoleType.Scapegoat);
		builder.GetGameState()!.GetFactionAgentKnowledge(
			infectedPlayer.Id,
			Faction.Werewolf).Should().Be(FactionAgentKnowledge.KnownAgent);
		MarkTestCompleted();
	}

	private static SimulationPlayerFactionFacts CreateSimulationFactionFacts(
		int seatNumber,
		bool isInitialWerewolfAgent)
	{
		var agents = Enum.GetValues<Faction>().ToDictionary(
			faction => faction,
			_ => FactionAgentKnowledge.KnownNonAgent);
		if (isInitialWerewolfAgent)
		{
			agents[Faction.Werewolf] = FactionAgentKnowledge.KnownAgent;
		}

		return new SimulationPlayerFactionFacts(
			seatNumber,
			FactionBeneficiaryKnowledge.Known(
				isInitialWerewolfAgent
					? Faction.Werewolf
					: Faction.Villager),
			agents);
	}
}
