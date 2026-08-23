using System.Collections.Immutable;
using FluentAssertions;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class WerewolfCollectiveTests
{
	[Fact]
	public void UnknownLivingAgentGroup_ObservationCommitsCompleteAgentPartitionWithoutIdentifyingRoles()
	{
		var builder = GameTestBuilder.Create()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		var afterGameStart = builder.ConfirmGameStart();
		var nightStart = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			afterGameStart);
		var afterNightStart = builder.Process(nightStart.CreateResponse());
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var observedAgent = players[0];

		var observation = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			afterNightStart);
		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		observation.RoleIdentification.Should().BeNull();
		observation.CountConstraint.Should().Be(NumberRangeConstraint.Exact(1));
		observation.SelectablePlayerIds.Should().BeEquivalentTo(
			players.Select(player => player.Id));
		var historyCount = session.GameHistoryLog.Count();

		var oversizedResponse = new ModeratorResponse
		{
			InstructionId = observation.InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = new HashSet<Guid>
			{
				observedAgent.Id,
				players[1].Id
			}
		};
		var oversizedAct = () => builder.Process(oversizedResponse);

		oversizedAct.Should().Throw<InvalidOperationException>();
		session.GameHistoryLog.Should().HaveCount(historyCount);
		players.Should().AllSatisfy(player =>
			session.GetFactionAgentKnowledge(
					player.Id,
					Faction.Werewolf)
				.Should().Be(FactionAgentKnowledge.Unknown));
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			observation.InstructionId);

		var afterObservation = builder.Process(
			observation.CreateResponse([observedAgent.Id]));

		InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			afterObservation).Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		session.GetFactionAgentKnowledge(
				observedAgent.Id,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		foreach (var nonAgent in players.Skip(1))
		{
			session.GetFactionAgentKnowledge(
					nonAgent.Id,
					Faction.Werewolf)
				.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		}

		players.Should().AllSatisfy(player =>
		{
			player.State.CurrentRole.Should().BeNull();
			player.State.ModeratorKnownRole.Should().BeNull();
		});
		var observationEntry = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Single(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.ScheduledObservation);
		observationEntry.Facts.Should().OnlyContain(
			fact => fact.Type == FactionFactType.Agent);
		session.GetFactionBeneficiaryKnowledge(observedAgent.Id)
			.Should().Be(FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
		foreach (var nonAgent in players.Skip(1))
		{
			session.GetFactionBeneficiaryKnowledge(nonAgent.Id)
				.Should().Be(FactionBeneficiaryKnowledge.Known(Faction.Villager));
		}

		session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
	}

	[Fact]
	public void UnknownLivingAgentGroup_WhiteWerewolfContributesToExactCapacity()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();

		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				StartNight(builder));

		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		observation.CountConstraint.Should().Be(NumberRangeConstraint.Exact(2));
	}

	[Fact]
	public void KnownNonCardWolfHoundAgent_AddsToActiveAgencyCardCapacity()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.WolfHound,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var holder = builder.GetGameState()!.GetPlayers().First();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var alignment =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(identification.CreateResponse([holder.Id])));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(alignment.CreateResponse(
					WolfHoundAlignmentOptionIds.Werewolves)));

		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(sleep.CreateResponse()));

		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		observation.CountConstraint.Should().Be(NumberRangeConstraint.Exact(2));
		observation.SelectablePlayerIds.Should().Contain(holder.Id);
	}

	[Fact]
	public void ThiefAcquiredAgencyCard_CountsOnce_WhileSetAsideAgencyCardDoesNotCount()
	{
		var cards = new[]
		{
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Thief),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.WhiteWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.BigBadWolf)
		};
		var lockIn = new RoleLockIn(
			version: 1,
			playerCount: 5,
			cards,
			cards.Take(5).Select(card => card.Id),
			cards[5].Id,
			cards[6].Id);
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Player1", "Player2", "Player3", "Player4", "Player5"],
			lockIn));
		var session = service.GetGameStateView(start.GameGuid)!;
		var holder = session.GetPlayers().First();
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
		var choice =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					identification.CreateResponse([holder.Id])));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					choice.CreateResponse(ThiefOfferOptionIds.Offer1)));

		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					sleep.CreateResponse()));

		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		observation.CountConstraint.Should().Be(NumberRangeConstraint.Exact(2));
		session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.Id == cards[5].Id)
			.Should().Match<PhysicalCharacterCardState>(card =>
				card.Zone == PhysicalCharacterCardZone.PlayerOwned &&
				card.OwnerPlayerId == holder.Id);
		session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.Id == cards[6].Id)
			.Zone.Should().Be(PhysicalCharacterCardZone.SetAside);
	}

	[Fact]
	public void UnknownLivingAgentGroup_UnlinkedKnownAgent_FailsBeforeObservation()
	{
		var builder = GameTestBuilder.Create()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		ArrangeWerewolfAgentKnowledge(
			builder,
			players[0].Id,
			FactionAgentKnowledge.KnownAgent);
		var afterGameStart = builder.ConfirmGameStart();
		var nightStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterGameStart);
		var historyCount = session.GameHistoryLog.Count();

		var act = () => builder.Process(nightStart.CreateResponse());

		act.Should().Throw<InvalidOperationException>();
		session.GameHistoryLog.Should().HaveCount(historyCount);
		session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Source.Kind ==
					FactionFactSourceKind.ScheduledObservation ||
				entry.Source.Kind ==
					FactionFactSourceKind.InitialBeneficiaryClosure);
		session.GetFactionAgentKnowledge(
				players[0].Id,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			nightStart.InstructionId);
	}

	[Fact]
	public void KnownNonemptyLivingAgentGroup_WakesCollectiveAndTargetsOnlyKnownNonAgents()
	{
		var builder = GameTestBuilder.Create()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolves = players.Take(2).Select(player => player.Id).ToHashSet();
		ArrangeKnownWerewolfAgentGroup(builder, werewolves);

		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				StartNight(builder));

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().BeEquivalentTo(werewolves);

		var afterWake = builder.Process(wake.CreateResponse());
		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				afterWake);
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		victimSelection.AffectedPlayerIds.Should().BeEquivalentTo(werewolves);
		victimSelection.SelectablePlayerIds.Should().BeEquivalentTo(
			players.Skip(2).Select(player => player.Id));
		victimSelection.CountConstraint.Should().Be(NumberRangeConstraint.Single);

		var victimId = players[2].Id;
		var afterVictim = builder.Process(
			victimSelection.CreateResponse([victimId]));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterVictim);
		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().BeEquivalentTo(werewolves);
		builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.WerewolfVictimSelection &&
				entry.TargetIds!.Single() == victimId);
	}

	[Fact]
	public void KnownEmptyLivingAgentGroup_OmitsCollectiveOperation()
	{
		var builder = GameTestBuilder.Create()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		ArrangeKnownWerewolfAgentGroup(builder, new HashSet<Guid>());

		var firstRoleInstruction =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				StartNight(builder));

		firstRoleInstruction.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		firstRoleInstruction.RoleIdentification.Should().Be(MainRoleType.Seer);
		builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType == NightActionType.WerewolfVictimSelection);
	}

	[Fact]
	public void KnownNonemptyAgentGroup_WithoutLivingKnownNonAgent_SleepsWithoutAttackAndAdvances()
	{
		var builder = GameTestBuilder.Create()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var allLivingPlayerIds = players.Select(player => player.Id).ToHashSet();
		ArrangeKnownWerewolfAgentGroup(builder, allLivingPlayerIds);

		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				StartNight(builder));
		var session = builder.GetGameState()!;

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(wake.CreateResponse()));

		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().BeEquivalentTo(allLivingPlayerIds);
		session.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType == NightActionType.WerewolfVictimSelection);
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			sleep.InstructionId);

		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));

		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		session.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType == NightActionType.WerewolfVictimSelection);
	}

	[Fact]
	public void UnknownLivingGroup_WithUnresolvedDeadSeat_FailsBeforeObservation()
	{
		var builder = GameTestBuilder.Create()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		builder.ArrangeEliminatedPlayer(players[^1].Id);
		var afterGameStart = builder.ConfirmGameStart();
		var nightStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterGameStart);
		var historyCount = session.GameHistoryLog.Count();

		var act = () => builder.Process(nightStart.CreateResponse());

		act.Should().Throw<InvalidOperationException>();
		session.GameHistoryLog.Should().HaveCount(historyCount);
		session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Source.Kind ==
					FactionFactSourceKind.ScheduledObservation ||
				entry.Source.Kind ==
					FactionFactSourceKind.InitialBeneficiaryClosure);
		session.GetPlayers().Should().AllSatisfy(player =>
			session.GetFactionAgentKnowledge(
					player.Id,
					Faction.Werewolf)
				.Should().Be(FactionAgentKnowledge.Unknown));
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			nightStart.InstructionId);
	}

	[Fact]
	public void UnknownLivingGroup_InvalidEmptyObservation_IsSideEffectFree()
	{
		var builder = GameTestBuilder.Create()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				StartNight(builder));
		var session = builder.GetGameState()!;
		var historyCount = session.GameHistoryLog.Count();

		var invalidResponse = new ModeratorResponse
		{
			InstructionId = observation.InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = ImmutableHashSet<Guid>.Empty
		};
		var act = () => builder.Process(invalidResponse);

		act.Should().Throw<InvalidOperationException>();
		session.GameHistoryLog.Should().HaveCount(historyCount);
		session.GetPlayers().Should().AllSatisfy(player =>
			session.GetFactionAgentKnowledge(
					player.Id,
					Faction.Werewolf)
				.Should().Be(FactionAgentKnowledge.Unknown));
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			observation.InstructionId);
	}

	private static ProcessResult StartNight(GameTestBuilder builder)
	{
		var afterGameStart = builder.ConfirmGameStart();
		var nightStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterGameStart);
		return builder.Process(nightStart.CreateResponse());
	}

	private static void ArrangeKnownWerewolfAgentGroup(
		GameTestBuilder builder,
		IReadOnlySet<Guid> werewolfAgentIds)
	{
		var session = builder.GetGameState()!;
		var boundary = new FactionFactEffectiveBoundary(
			session.TurnNumber,
			session.GetCurrentPhase(),
			session.GameHistoryLog.Count());
		var facts = session.GetPlayers()
			.Select(player => FactionFact.Agent(
				player.Id,
				Faction.Werewolf,
				werewolfAgentIds.Contains(player.Id)
					? FactionAgentKnowledge.KnownAgent
					: FactionAgentKnowledge.KnownNonAgent,
				boundary))
			.ToArray();
		builder.ArrangeExplicitFactionTransition(
			"test-known-werewolf-agent-group",
			facts);
	}

	private static void ArrangeWerewolfAgentKnowledge(
		GameTestBuilder builder,
		Guid playerId,
		FactionAgentKnowledge knowledge)
	{
		var session = builder.GetGameState()!;
		var boundary = new FactionFactEffectiveBoundary(
			session.TurnNumber,
			session.GetCurrentPhase(),
			session.GameHistoryLog.Count());
		builder.ArrangeExplicitFactionTransition(
			"test-partial-werewolf-agent-knowledge",
			FactionFact.Agent(
				playerId,
				Faction.Werewolf,
				knowledge,
				boundary));
	}
}
