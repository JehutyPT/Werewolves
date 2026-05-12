using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for event sourcing: log integrity, state reconstruction.
/// Test IDs: ES-001 through ES-012
/// </summary>
public class EventSourcingTests : DiagnosticTestBase
{
    public EventSourcingTests(ITestOutputHelper output) : base(output) { }

    #region ES-001 to ES-003: Log Entry Integrity

    /// <summary>
    /// ES-001: All game actions create log entries.
    /// </summary>
    [Fact]
    public void AllActions_CreateLogEntries()
    {
        // Arrange - Complete a full night cycle to generate various log entries
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToList();
        var werewolfPlayer = players[0];
        var seerPlayer = players[1];
        var victimPlayer = players[4];

        // Complete werewolf action
        builder.CompleteWerewolfNightAction([werewolfPlayer.Id], victimPlayer.Id);

        // Complete seer action
        builder.CompleteSeerNightAction(seerPlayer.Id, victimPlayer.Id);

        // Act
        var finalSession = builder.GetGameState()!;
        var logEntries = finalSession.GameHistoryLog.ToList();

        // Assert - Should have multiple entry types
        logEntries.Should().NotBeEmpty();
        
        // Should have role assignments
        logEntries.OfType<AssignRoleLogEntry>().Should().NotBeEmpty(
            "Role assignments should be logged");
        
        // Should have night actions
        logEntries.OfType<NightActionLogEntry>().Should().NotBeEmpty(
            "Night actions should be logged");

        MarkTestCompleted();
    }

    /// <summary>
    /// ES-002: Log entries contain correct TurnNumber.
    /// </summary>
    [Fact]
    public void LogEntries_ContainCorrectTurnNumber()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToList();

        builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);
        builder.CompleteSeerNightAction(players[1].Id, players[4].Id);

        // Act
        var finalSession = builder.GetGameState()!;
        var logEntries = finalSession.GameHistoryLog.ToList();

        // Assert - All entries from Night 1 should have TurnNumber = 1
        var nightActions = logEntries.OfType<NightActionLogEntry>().ToList();
        nightActions.Should().AllSatisfy(entry => 
            entry.TurnNumber.Should().Be(1, "Night 1 actions should have TurnNumber 1"));

        MarkTestCompleted();
    }

    /// <summary>
    /// ES-003: Log entries contain correct Phase.
    /// </summary>
    [Fact]
    public void LogEntries_ContainCorrectPhase()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToList();

        builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);

        // Act
        var finalSession = builder.GetGameState()!;
        var nightActions = finalSession.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .ToList();

        // Assert - Night actions should have Night phase
        nightActions.Should().AllSatisfy(entry =>
            entry.CurrentPhase.Should().Be(GamePhase.Night,
                "Night actions should be recorded with Night phase"));

        MarkTestCompleted();
    }

    #endregion

    #region ES-010 to ES-012: Log Replay and State Reconstruction

    /// <summary>
    /// ES-010: Replaying log entries reconstructs player health correctly.
    /// </summary>
    [Fact]
    public void ReplayLog_ReconstructsPlayerHealth()
    {
        // Arrange - Create a game and complete some actions
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToList();

        builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);
        builder.CompleteSeerNightAction(players[1].Id, players[4].Id);

        var finalSession = builder.GetGameState()!;
        var playerIds = finalSession.GetPlayers().Select(p => p.Id).ToList();

        // Act - Replay all log entries through test mutator
        var testMutator = new TestSessionMutator(playerIds);
        foreach (var entry in finalSession.GameHistoryLog)
        {
            entry.Apply(testMutator);
        }

        // Assert - Compare replayed health with cached health
        var derivedStates = testMutator.GetDerivedStates();
        foreach (var player in finalSession.GetPlayers())
        {
            derivedStates[player.Id].Health.Should().Be(player.State.Health,
                $"Player {player.Name} health from replay should match cached state");
        }

        MarkTestCompleted();
    }

    /// <summary>
    /// ES-011: Replaying log entries reconstructs player roles correctly.
    /// </summary>
    [Fact]
    public void ReplayLog_ReconstructsKnownRoles()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToList();

        // Complete werewolf action which involves role identification
        builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);

        var finalSession = builder.GetGameState()!;
        var playerIds = finalSession.GetPlayers().Select(p => p.Id).ToList();

        // Act - Replay all log entries
        var testMutator = new TestSessionMutator(playerIds);
        foreach (var entry in finalSession.GameHistoryLog)
        {
            entry.Apply(testMutator);
        }

        // Assert - Compare replayed roles with cached roles
        var derivedStates = testMutator.GetDerivedStates();
        foreach (var player in finalSession.GetPlayers())
        {
            // Only compare if role is assigned
            if (player.State.MainRole.HasValue)
            {
                derivedStates[player.Id].MainRole.Should().Be(player.State.MainRole,
                    $"Player {player.Name} role from replay should match cached state");
            }
        }

        MarkTestCompleted();
    }

    /// <summary>
    /// ES-012: Derived state from log replay matches cached state (comprehensive).
    /// </summary>
    [Fact]
    public void DerivedState_MatchesCachedState()
    {
        // Arrange - Run a more complete game cycle
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToList();
        var werewolfPlayer = players[0];
        var seerPlayer = players[1];
        var victimPlayer = players[4];

        // Complete night actions
        builder.CompleteWerewolfNightAction([werewolfPlayer.Id], victimPlayer.Id);
        builder.CompleteSeerNightAction(seerPlayer.Id, victimPlayer.Id);

        var finalSession = builder.GetGameState()!;
        var playerIds = finalSession.GetPlayers().Select(p => p.Id).ToList();

        // Act - Replay log entries
        var testMutator = new TestSessionMutator(playerIds);
        foreach (var entry in finalSession.GameHistoryLog)
        {
            entry.Apply(testMutator);
        }

        // Assert - Comprehensive state comparison
        var derivedStates = testMutator.GetDerivedStates();
        foreach (var player in finalSession.GetPlayers())
        {
            var derived = derivedStates[player.Id];
            
            // Role comparison
            derived.MainRole.Should().Be(player.State.MainRole,
                $"Player {player.Name} role mismatch");
            
            // Health comparison
            derived.Health.Should().Be(player.State.Health,
                $"Player {player.Name} health mismatch");
            
            // Status effects comparison
            var cachedEffects = player.State.GetActiveStatusEffects();
            var derivedEffects = derived.GetActiveStatusEffects();
            derivedEffects.Should().BeEquivalentTo(cachedEffects,
                $"Player {player.Name} status effects mismatch");
        }

        MarkTestCompleted();
    }

    #endregion
}
