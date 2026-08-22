using System.Collections.Immutable;
using FluentAssertions;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
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
		observation.CountConstraint.Should().Be(NumberRangeConstraint.AtLeast(1));
		observation.SelectablePlayerIds.Should().BeEquivalentTo(
			players.Select(player => player.Id));

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
	public void UnknownLivingAgentGroup_ObservationCandidatesRespectCommittedWerewolfAgency()
	{
		var builder = GameTestBuilder.Create()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		ArrangeWerewolfAgentKnowledge(
			builder,
			players[0].Id,
			FactionAgentKnowledge.KnownAgent);
		ArrangeWerewolfAgentKnowledge(
			builder,
			players[1].Id,
			FactionAgentKnowledge.KnownNonAgent);

		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				StartNight(builder));

		observation.SelectablePlayerIds.Should().BeEquivalentTo(
			players.Where(player => player.Id != players[1].Id)
				.Select(player => player.Id));
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
	public void UnknownLivingGroup_WithUnknownDeadSeat_LeavesClosureIncompleteAndContinues()
	{
		var builder = GameTestBuilder.Create()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeEliminatedPlayer(players[^1].Id);

		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				StartNight(builder));
		observation.SelectablePlayerIds.Should().NotContain(players[^1].Id);

		var afterObservation = builder.Process(
			observation.CreateResponse([players[0].Id]));

		InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				afterObservation)
			.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectWerewolfVictim);
		builder.GetGameState()!.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
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
