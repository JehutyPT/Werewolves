using FluentAssertions;
using FluentAssertions.Execution;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class DayVoteContractTests
{
    [Fact]
    public void Instruction_RejectsDeadAndMultipleTargetsWithoutChangingTheGameSession()
    {
        var scenario = DayVoteScenario.Start();
        var before = PublicGameSessionSnapshot.Capture(scenario.Builder);
        var twoLivingTargets = scenario.Instruction.SelectablePlayerIds.Take(2).ToHashSet();

        var selectDeadPlayer = () =>
            scenario.Instruction.CreateResponse([scenario.EliminatedPlayerId]);
        var selectMultiplePlayers = () =>
            scenario.Instruction.CreateResponse(twoLivingTargets);

        using (new AssertionScope())
        {
            selectDeadPlayer.Should().Throw<ArgumentException>();
            selectMultiplePlayers.Should().Throw<InvalidOperationException>();
            PublicGameSessionSnapshot.Capture(scenario.Builder).Should()
                .BeEquivalentTo(before, options => options.WithStrictOrdering());
        }
    }

    [Fact]
    public void LegalLivingTarget_CommitsExactlyOnceBeforeTheNextInstruction()
    {
        var scenario = DayVoteScenario.Start();
        var response = scenario.Instruction.CreateResponse([scenario.LivingTargetId]);

        var result = scenario.Builder.Process(response);

        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            scenario.Builder.GetCurrentInstruction()!.InstructionId.Should()
                .NotBe(scenario.Instruction.InstructionId);
            scenario.Builder.GetGameState()!.GameHistoryLog
                .OfType<VoteOutcomeReportedLogEntry>()
                .Should().ContainSingle(entry =>
                    entry.ReportedOutcomePlayerId == scenario.LivingTargetId);
        }

        var beforeReplay = PublicGameSessionSnapshot.Capture(scenario.Builder);
        var replay = () => scenario.Builder.Process(response);

        replay.Should().Throw<InvalidOperationException>();
        PublicGameSessionSnapshot.Capture(scenario.Builder).Should()
            .BeEquivalentTo(beforeReplay, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Tie_CommitsOneEmptyVoteResultWithoutEliminatingAPlayer()
    {
        var scenario = DayVoteScenario.Start();
        var livingPlayersBefore = scenario.Builder.GetGameState()!.GetPlayers()
            .Where(player => player.State.Health == PlayerHealth.Alive)
            .Select(player => player.Id)
            .Order()
            .ToArray();

        var result = scenario.Builder.Process(
            scenario.Instruction.CreateResponse([]));

        var session = scenario.Builder.GetGameState()!;
        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            session.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
                .Should().ContainSingle(entry =>
                    entry.ReportedOutcomePlayerId == Guid.Empty);
            session.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
                .Should().NotContain(entry =>
                    entry.Reason == EliminationReason.DayVote);
			result.ModeratorInstruction.Should()
				.NotBeOfType<AssignRolesInstruction>();
			session.GameHistoryLog
				.OfType<EliminationCascadeCompletedLogEntry>()
				.Should().NotContain(entry =>
					entry.ScopeId.StartsWith(
						$"Day:{session.TurnNumber}:Vote:",
						StringComparison.Ordinal));
            session.GetPlayers()
                .Where(player => player.State.Health == PlayerHealth.Alive)
                .Select(player => player.Id)
                .Order()
                .Should().Equal(livingPlayersBefore);
        }
    }

	[Fact]
	public void ConsecutiveVotes_UseFreshScopesAndRehydrateAtTheExactSecondVote()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(
				"Werewolf",
				"Public Villager-Villager",
				"Night victim",
				"Second vote target",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.VillagerVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolfId = players[0].Id;
		var publicVoteTargetId = players[1].Id;
		var nightVictimId = players[2].Id;
		var secondVoteTargetId = players[3].Id;

		var publicObservation = builder.ConfirmGameStart()
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		builder.Process(
			publicObservation.CreateResponse([publicVoteTargetId]));
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction(
			[werewolfId],
			nightVictimId);
		builder.CompleteDawnPhase(new()
		{
			[nightVictimId] = MainRoleType.SimpleVillager
		});

		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var firstVote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		builder.ArrangeDayAction(DayPowerType.JudgeExtraVote);

		var firstAnnouncement = builder.Process(
				firstVote.CreateResponse([publicVoteTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		firstAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		var secondVote = builder.Process(firstAnnouncement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredSecondVote = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		recoveredSecondVote.InstructionId.Should().Be(
			secondVote.InstructionId);
		recoveredSecondVote.Semantic.Should().Be(
			secondVote.Semantic);

		var secondReveal = recoveredService.ProcessInstruction(
				recoveredGameId,
				recoveredSecondVote.CreateResponse(
					[secondVoteTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		secondReveal.PlayersForAssignment.Should().Equal(
			secondVoteTargetId);
		var secondAnnouncement = recoveredService.ProcessInstruction(
				recoveredGameId,
				secondReveal.CreateResponse(new()
				{
					[secondVoteTargetId] =
						MainRoleType.SimpleVillager
				}))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		recoveredService.ProcessInstruction(
			recoveredGameId,
			secondAnnouncement.CreateResponse());

		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		recovered.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Select(entry => entry.ReportedOutcomePlayerId)
			.Should().Equal(
				publicVoteTargetId,
				secondVoteTargetId);
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Where(entry => entry.Reason == EliminationReason.DayVote)
			.Select(entry => entry.PlayerId)
			.Should().Equal(
				publicVoteTargetId,
				secondVoteTargetId);
		recovered.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Where(entry =>
				entry.ScopeId.StartsWith(
					"Day:1:Vote:",
					StringComparison.Ordinal))
			.Select(entry => entry.ScopeId)
			.Should().Equal(
				"Day:1:Vote:1",
				"Day:1:Vote:2");
		recovered.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Should().BeEmpty();

	}

    [Fact]
    public void StaleSameShapedResponse_FromAnotherGameSession_IsSideEffectFree()
    {
        var source = DayVoteScenario.Start();
        var destination = DayVoteScenario.Start();

        using (new AssertionScope())
        {
            source.Instruction.InstructionId.Should().NotBe(destination.Instruction.InstructionId);
            source.Instruction.CountConstraint.Minimum.Should()
                .Be(destination.Instruction.CountConstraint.Minimum);
            source.Instruction.CountConstraint.Maximum.Should()
                .Be(destination.Instruction.CountConstraint.Maximum);
            source.Instruction.CountConstraint.IsOptional.Should()
                .Be(destination.Instruction.CountConstraint.IsOptional);
        }

        var staleTieResponse = source.Instruction.CreateResponse([]);
        var before = PublicGameSessionSnapshot.Capture(destination.Builder);

        var act = () => destination.Builder.Process(staleTieResponse);

        act.Should().Throw<InvalidOperationException>();
        PublicGameSessionSnapshot.Capture(destination.Builder).Should()
            .BeEquivalentTo(before, options => options.WithStrictOrdering());
    }
}

internal sealed record DayVoteScenario(
    GameTestBuilder Builder,
    SelectPlayersInstruction Instruction,
    ConfirmationInstruction StableDayBoundaryInstruction,
    Guid LivingTargetId,
    Guid EliminatedPlayerId)
{
    public static DayVoteScenario Start(
        IRolePowerAvailabilityPolicy? rolePowerAvailabilityPolicy = null,
        MainRoleType? livingTargetRole = null,
		bool arrangeKnownPhysicalRole = true)
    {
		var builder = GameTestBuilder.Create()
			.WithOptionalRolePowerAvailabilityPolicy(
				rolePowerAvailabilityPolicy);
		if (livingTargetRole is { } configuredRole)
		{
			builder
				.WithPlayers(5)
				.WithRoles(
					MainRoleType.SimpleWerewolf,
					MainRoleType.Seer,
					configuredRole,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager);
		}
		else
		{
			builder.WithSimpleGame(
				playerCount: 5,
				werewolfCount: 1,
				includeSeer: true);
		}
        builder.StartGame();

        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var nightVictimId = players[2].Id;
        var livingTargetId = players[3].Id;
		if (arrangeKnownPhysicalRole &&
			livingTargetRole is { } role)
        {
			builder.ArrangeKnownPhysicalRole(livingTargetId, role);
        }

        builder.ConfirmGameStart();

        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: nightVictimId,
            seerId: seerId,
            seerTargetId: livingTargetId);
        builder.CompleteDawnPhase(new()
        {
            [nightVictimId] = MainRoleType.SimpleVillager
        });

        var debateInstruction = builder.GetCurrentInstruction()
            .Should().BeOfType<ConfirmationInstruction>().Subject;
        var afterDebate = builder.Process(debateInstruction.CreateResponse());
        var voteInstruction = afterDebate.ModeratorInstruction
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        return new DayVoteScenario(
            builder,
            voteInstruction,
            debateInstruction,
            livingTargetId,
            nightVictimId);
    }
}

internal sealed record PublicGameSessionSnapshot(
    Guid GameSessionId,
    GamePhase Phase,
    int TurnNumber,
    string StableRecoveryPayload,
    PendingInstructionSnapshot PendingInstruction,
    IReadOnlyList<PlayerSnapshot> Players,
    IReadOnlyList<RoleCountSnapshot> RolesInPlay,
    IReadOnlyList<GameLogSnapshot> GameHistory)
{
    public static PublicGameSessionSnapshot Capture(GameTestBuilder builder)
        => Capture(builder.GameService, builder.GameId);

    public static PublicGameSessionSnapshot Capture(GameService gameService, Guid gameId)
    {
        var session = gameService.GetGameStateView(gameId)!;
        var instruction = gameService.GetCurrentInstruction(gameId)!;

        return new PublicGameSessionSnapshot(
            session.Id,
            session.GetCurrentPhase(),
            session.TurnNumber,
            session.Serialize(),
            PendingInstructionSnapshot.Capture(instruction),
            session.GetPlayers()
                .Select(PlayerSnapshot.Capture)
                .ToArray(),
            Enum.GetValues<MainRoleType>()
                .Select(role => new RoleCountSnapshot(
                    role,
                    session.RoleInPlayCount(role)))
                .Where(roleCount => roleCount.Count > 0)
                .ToArray(),
            session.GameHistoryLog
                .Select(GameLogSnapshot.Capture)
                .ToArray());
    }
}

internal sealed record PendingInstructionSnapshot(
    Guid InstructionId,
    Type InstructionType,
    string? PublicAnnouncement,
    string? PrivateInstruction,
    IReadOnlyList<SoundEffectsEnum> SoundEffects,
    IReadOnlyList<Guid> AffectedPlayerIds,
    IReadOnlyList<Guid> SelectablePlayerIds,
    int? MinimumSelectionCount,
    int? MaximumSelectionCount,
    bool? IsSelectionOptional,
    MainRoleType? RoleIdentification,
    string? EmptySelectionOptionLabel)
{
    public static PendingInstructionSnapshot Capture(ModeratorInstruction instruction)
        => new(
            instruction.InstructionId,
            instruction.GetType(),
            instruction.PublicAnnouncement,
            instruction.PrivateInstruction,
            instruction.SoundEffects.ToArray(),
            instruction.AffectedPlayerIds?.Order().ToArray() ?? [],
            instruction is SelectPlayersInstruction selectPlayers
                ? selectPlayers.SelectablePlayerIds.Order().ToArray()
                : [],
            instruction is SelectPlayersInstruction minimumSelection
                ? minimumSelection.CountConstraint.Minimum
                : null,
            instruction is SelectPlayersInstruction maximumSelection
                ? maximumSelection.CountConstraint.Maximum
                : null,
            instruction is SelectPlayersInstruction optionalSelection
                ? optionalSelection.CountConstraint.IsOptional
                : null,
            instruction is SelectPlayersInstruction roleSelection
                ? roleSelection.RoleIdentification
                : null,
            instruction is SelectPlayersInstruction emptySelection
                ? emptySelection.EmptySelectionOptionLabel
                : null);
}

internal sealed record PlayerSnapshot(
    Guid Id,
    string Name,
    MainRoleType? Role,
    PlayerHealth Health,
    bool HasVotingRight,
    int DurableVotingPower,
    IReadOnlyList<StatusEffectTypes> StatusEffects)
{
    public static PlayerSnapshot Capture(IPlayer player)
        => new(
            player.Id,
            player.Name,
            player.State.MainRole,
            player.State.Health,
            player.State.HasVotingRight,
            player.State.DurableVotingPower,
            player.State.GetActiveStatusEffects().Order().ToArray());
}

internal sealed record RoleCountSnapshot(MainRoleType Role, int Count);

internal sealed record GameLogSnapshot(
    Type EntryType,
    DateTimeOffset Timestamp,
    int TurnNumber,
    GamePhase Phase,
    string SemanticDescription)
{
    public static GameLogSnapshot Capture(GameLogEntryBase entry)
        => new(
            entry.GetType(),
            entry.Timestamp,
            entry.TurnNumber,
            entry.CurrentPhase,
            entry.ToString());
}
