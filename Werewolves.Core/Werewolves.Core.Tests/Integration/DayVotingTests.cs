using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for day phase voting: vote outcomes, tie handling, elimination flow.
/// Test IDs: DV-001 through DV-011
/// </summary>
public class DayVotingTests : DiagnosticTestBase
{
    public DayVotingTests(ITestOutputHelper output) : base(output) { }

    #region DV-001: Debate Phase Transitions to Voting

    /// <summary>
    /// DV-001: Debate sub-phase transitions to voting.
    /// After dawn phase completes, game enters Day.Debate. Confirming debate leads to voting.
    /// </summary>
    [Fact]
    public void DebatePhase_TransitionsToVoting()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1 = players[2].Id;
        var villager2 = players[3].Id;

        // Complete night phase (werewolf kills a villager)
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1,
            seerId: seerId,
            seerTargetId: villager2);

        // Complete dawn phase
        builder.CompleteDawnPhase();

        // Assert: We're in Day phase, and should have a confirmation instruction for debate
        var gameState = builder.GetGameState()!;
        gameState.GetCurrentPhase().Should().Be(GamePhase.Day);

        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmationInstruction);

        // Act: Confirm debate is complete
        var afterDebate = builder.Process(debateInstruction.CreateResponse(true));
        afterDebate.IsSuccess.Should().BeTrue();

        // After DetermineVoteType (silent transition), we should get a voting instruction
        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingSelectionInstruction);

        // Verify it's a voting instruction with appropriate constraints
        // Uses SingleOptional which has Minimum=1, Maximum=1, IsOptional=true
        // The IsOptional flag allows 0 selections for tie votes
        votingInstruction.CountConstraint.Should().NotBeNull();
        votingInstruction.CountConstraint!.IsOptional.Should().BeTrue(CoreTestReferences.AssertionReasons.TieVotesAllowNoSelection);
        votingInstruction.CountConstraint!.Maximum.Should().Be(1, CoreTestReferences.AssertionReasons.SinglePlayerCanBeLynched);

        MarkTestCompleted();
    }

    #endregion

    #region DV-002 to DV-005: Normal Vote Flow

    /// <summary>
    /// DV-002: Vote outcome with a deterministic role reveal announces the elimination.
    /// When the lynched player's only possible role type is known, the game should assign it automatically.
    /// </summary>
    [Fact]
    public void VoteOutcome_SinglePlayer_WithSinglePossibleRole_AnnouncesElimination()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2 = players[3];

        // Complete night and dawn phases
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2.Id);
        builder.CompleteDawnPhase();

        // Confirm debate
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse(true));

        // Get voting instruction
        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        // Act: Vote to lynch villager2 (who is still alive)
        var voteResponse = votingInstruction.CreateResponse([villager2.Id]);
        var afterVote = builder.Process(voteResponse);

        // Assert: Should get the death announcement without a role assignment instruction.
        var deathAnnouncement = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterVote,
            CoreTestReferences.InstructionContexts.DeathAnnouncementConfirmation);

        deathAnnouncement.PublicAnnouncement.Should().Contain(villager2.Name);
        builder.GetGameState()!.GetPlayer(villager2.Id).State.MainRole
            .Should().Be(MainRoleType.SimpleVillager);

        MarkTestCompleted();
    }

    /// <summary>
    /// DV-005: Vote outcome with a single possible role type assigns it automatically.
    /// </summary>
    [Fact]
    public void VoteOutcome_SinglePossibleRole_AutoAssignsRoleAndAnnouncesElimination()
    {
        // Arrange: 1 Werewolf and 4 Villagers leaves only Villager roles unknown after night.
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolf = players[0];
        var dawnVictim = players[1];
        var lynchedPlayer = players[2];

        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: dawnVictim.Id);
        builder.CompleteDawnPhase();

        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse(true));

        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        // Act: Vote to lynch a player whose only possible role type is SimpleVillager.
        var afterVote = builder.Process(votingInstruction.CreateResponse([lynchedPlayer.Id]));

        // Assert: The engine skips Moderator role assignment and announces the elimination.
        var announcement = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterVote,
            CoreTestReferences.InstructionContexts.DeathAnnouncementConfirmation);
        announcement.PublicAnnouncement.Should().Contain(lynchedPlayer.Name);

        var gameState = builder.GetGameState()!;
        gameState.GetPlayer(lynchedPlayer.Id).State.MainRole.Should().Be(MainRoleType.SimpleVillager);
        gameState.GetPlayer(lynchedPlayer.Id).State.Health.Should().Be(PlayerHealth.Dead);

        var roleLog = gameState.GameHistoryLog
            .OfType<AssignRoleLogEntry>()
            .Single(entry => entry.PlayerIds.Contains(lynchedPlayer.Id));
        roleLog.AssignedMainRole.Should().Be(MainRoleType.SimpleVillager);
        roleLog.CurrentPhase.Should().Be(GamePhase.Day);

        gameState.GameHistoryLog
            .OfType<PlayerEliminatedLogEntry>()
            .Should()
            .ContainSingle(entry =>
                entry.PlayerId == lynchedPlayer.Id &&
                entry.Reason == EliminationReason.DayVote);

        MarkTestCompleted();
    }

    /// <summary>
    /// DV-003: Vote elimination creates VoteOutcomeReportedLogEntry.
    /// </summary>
    [Fact]
    public void VoteElimination_CreatesVoteOutcomeLogEntry()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2Id = players[3].Id;

        // Complete night and dawn phases
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2Id);
        builder.CompleteDawnPhase();

        // Confirm debate
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse(true));

        // Get voting instruction and vote
        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        // Act: Vote to lynch villager2
        var voteResponse = votingInstruction.CreateResponse([villager2Id]);
        builder.Process(voteResponse);

        // Assert: VoteOutcomeReportedLogEntry should exist with correct player
        var gameState = builder.GetGameState()!;
        var voteLogs = gameState.GameHistoryLog
            .OfType<VoteOutcomeReportedLogEntry>()
            .ToList();

        voteLogs.Should().HaveCount(1);
        voteLogs[0].ReportedOutcomePlayerId.Should().Be(villager2Id);

        MarkTestCompleted();
    }

    /// <summary>
    /// DV-004: Vote elimination sets player health to Dead.
    /// </summary>
    [Fact]
    public void VoteElimination_PlayerHealthSetToDead()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2Id = players[3].Id;

        // Complete night and dawn phases
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2Id);
        builder.CompleteDawnPhase();

        // Act: Complete day phase with lynch
        builder.CompleteDayPhaseWithLynch(villager2Id);

        // Assert: Lynched player should be dead
        var gameState = builder.GetGameState()!;
        var lynchedPlayer = gameState.GetPlayers().First(p => p.Id == villager2Id);
        lynchedPlayer.State.Health.Should().Be(PlayerHealth.Dead);

        // Also verify PlayerEliminatedLogEntry was created
        var eliminationLogs = gameState.GameHistoryLog
            .OfType<PlayerEliminatedLogEntry>()
            .Where(e => e.PlayerId == villager2Id && e.Reason == EliminationReason.DayVote)
            .ToList();

        eliminationLogs.Should().HaveCount(1);

        MarkTestCompleted();
    }

    #endregion

    #region DV-010 to DV-011: Tie Votes

    /// <summary>
    /// DV-010: Tie vote (no player selected) results in no elimination.
    /// </summary>
    [Fact]
    public void TieVote_NoPlayerSelected_NoElimination()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2Id = players[3].Id;

        // Complete night and dawn phases
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2Id);
        builder.CompleteDawnPhase();

        // Get the count of living players before voting
        var livingPlayersBefore = builder.GetGameState()!.GetPlayers()
            .Count(p => p.State.Health == PlayerHealth.Alive);

        // Confirm debate
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse(true));

        // Get voting instruction
        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        // Act: Vote with no player selected (tie)
        var tieResponse = votingInstruction.CreateResponse([]);
        builder.Process(tieResponse);

        // Complete remaining day phase
        builder.CompleteDayPhaseWithTie();

        // Assert: No player eliminated during day voting (villager1 was killed at dawn)
        var gameState = builder.GetGameState()!;
        var dayEliminationLogs = gameState.GameHistoryLog
            .OfType<PlayerEliminatedLogEntry>()
            .Where(e => e.Reason == EliminationReason.DayVote)
            .ToList();

        dayEliminationLogs.Should().BeEmpty(CoreTestReferences.AssertionReasons.TieVoteDoesNotEliminatePlayer);

        // Living player count should be same as before voting
        var livingPlayersAfter = gameState.GetPlayers()
            .Count(p => p.State.Health == PlayerHealth.Alive);
        livingPlayersAfter.Should().Be(livingPlayersBefore);

        MarkTestCompleted();
    }

    /// <summary>
    /// DV-011: Tie vote creates correct log entry with Empty playerId.
    /// </summary>
    [Fact]
    public void TieVote_LogsCorrectOutcome()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2Id = players[3].Id;

        // Complete night and dawn phases
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2Id);
        builder.CompleteDawnPhase();

        // Confirm debate
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse(true));

        // Get voting instruction
        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        // Act: Vote with no player selected (tie)
        var tieResponse = votingInstruction.CreateResponse([]);
        builder.Process(tieResponse);

        // Assert: VoteOutcomeReportedLogEntry should exist with Guid.Empty
        var gameState = builder.GetGameState()!;
        var voteLogs = gameState.GameHistoryLog
            .OfType<VoteOutcomeReportedLogEntry>()
            .ToList();

        voteLogs.Should().HaveCount(1);
        voteLogs[0].ReportedOutcomePlayerId.Should().Be(Guid.Empty,
            CoreTestReferences.AssertionReasons.TieVoteLoggedWithEmptyPlayerId);

        MarkTestCompleted();
    }

    #endregion

    #region DV-020: Vote Target Validation

    /// <summary>
    /// DV-020: Vote cannot select a dead player.
    /// After dawn elimination, the dead player's ID should be excluded from selectable targets.
    /// </summary>
    [Fact]
    public void Vote_CannotSelectDeadPlayer()
    {
        // Arrange: Game with 5 players (1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2Id = players[3].Id;
        var villager3Id = players[4].Id;

        // Complete Night 1: werewolf kills villager1
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2Id);

        // Complete Dawn 1: victim eliminated
        builder.CompleteDawnPhase();

        // Verify villager1 is now dead
        var deadPlayer = builder.GetGameState()!.GetPlayers().First(p => p.Id == villager1Id);
        deadPlayer.State.Health.Should().Be(PlayerHealth.Dead);

        // Confirm debate to get to voting
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse(true));

        // Act: Get the vote selection instruction
        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        // Assert: Dead player's ID should NOT be in selectable targets
        votingInstruction.SelectablePlayerIds.Should().NotContain(villager1Id,
            CoreTestReferences.AssertionReasons.DeadPlayerInvalidVoteTarget);

        // Living players should be selectable
        votingInstruction.SelectablePlayerIds.Should().Contain(werewolfId);
        votingInstruction.SelectablePlayerIds.Should().Contain(seerId);
        votingInstruction.SelectablePlayerIds.Should().Contain(villager2Id);
        votingInstruction.SelectablePlayerIds.Should().Contain(villager3Id);

        MarkTestCompleted();
    }

    #endregion
}
