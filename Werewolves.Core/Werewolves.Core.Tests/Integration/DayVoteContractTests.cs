using FluentAssertions;
using FluentAssertions.Execution;
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
            session.GetPlayers()
                .Where(player => player.State.Health == PlayerHealth.Alive)
                .Select(player => player.Id)
                .Order()
                .Should().Equal(livingPlayersBefore);
        }
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
    public static DayVoteScenario Start()
    {
        var builder = GameTestBuilder.Create()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var nightVictimId = players[2].Id;
        var livingTargetId = players[3].Id;

        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: nightVictimId,
            seerId: seerId,
            seerTargetId: livingTargetId);
        builder.CompleteDawnPhase();

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
    bool IsImmuneToLynching,
    IReadOnlyList<StatusEffectTypes> StatusEffects)
{
    public static PlayerSnapshot Capture(IPlayer player)
        => new(
            player.Id,
            player.Name,
            player.State.MainRole,
            player.State.Health,
            player.State.IsImmuneToLynching,
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
