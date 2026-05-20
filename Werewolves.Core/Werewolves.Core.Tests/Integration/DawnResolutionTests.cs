using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for dawn resolution: victim calculation, eliminations, and role reveals.
/// Test IDs: DR-001 through DR-012
/// </summary>
public class DawnResolutionTests : DiagnosticTestBase
{
    public DawnResolutionTests(ITestOutputHelper output) : base(output) { }

    #region DR-001 to DR-002: Victim Calculation

    /// <summary>
    /// DR-001: Werewolf victim (unprotected) is eliminated at dawn.
    /// </summary>
    [Fact]
    public void WerewolfVictim_Unprotected_IsEliminated()
    {
        // Arrange - 5 players: 1 WW, 1 Seer, 3 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolf = players[0]; // Index 0 is werewolf per WithSimpleGame
        var victim = players[2];   // Index 2 is first villager

        // Act - Complete night with werewolf targeting the villager
        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: victim.Id,
            seerId: players[1].Id,
            seerTargetId: werewolf.Id);

        // Complete dawn phase
        var roleAssignments = new Dictionary<Guid, MainRoleType>
        {
            { victim.Id, MainRoleType.SimpleVillager }
        };
        builder.CompleteDawnPhase(roleAssignments);

        // Assert - Verify elimination via log
        var eliminationLogs = gameState.GameHistoryLog
            .OfType<PlayerEliminatedLogEntry>()
            .Where(e => e.PlayerId == victim.Id)
            .ToList();

        eliminationLogs.Should().HaveCount(1);
        eliminationLogs[0].Reason.Should().Be(EliminationReason.WerewolfAttack);

        // Verify player state
        var victimState = gameState.GetPlayers().First(p => p.Id == victim.Id);
        victimState.State.Health.Should().Be(PlayerHealth.Dead);

        MarkTestCompleted();
    }

    /// <summary>
    /// DR-002: Victim selection exposes at least one non-werewolf target.
    /// </summary>
    [Fact]
    public void Werewolves_VictimSelection_HasValidTargets()
    {
        // Arrange - 5 players: 2 WW, 1 Seer, 2 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 2, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        builder.ConfirmNightStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolves = new HashSet<Guid> { players[0].Id, players[1].Id }; // First two are werewolves

        // Get werewolf identification instruction and identify them
        var identifyInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.WerewolfIdentification);
        var identifyResponse = identifyInstruction.CreateResponse(werewolves);
        var afterIdentify = builder.Process(identifyResponse);

        // Act - Get victim selection instruction
        var victimInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterIdentify,
            CoreTestReferences.InstructionContexts.WerewolfVictimSelection);

        // Assert - Verify constraints
        victimInstruction.SelectablePlayerIds.Should().NotBeEmpty(
            CoreTestReferences.AssertionReasons.WerewolvesNeedValidTarget);
        
        victimInstruction.SelectablePlayerIds.Should().NotContain(werewolves,
            CoreTestReferences.AssertionReasons.WerewolvesCannotTargetWerewolves);

        victimInstruction.CountConstraint.Minimum.Should().BeGreaterOrEqualTo(1,
            CoreTestReferences.AssertionReasons.VictimSelectionRequiresVictim);

        MarkTestCompleted();
    }

    #endregion

    #region DR-010 to DR-012: Role Reveal Flow

    /// <summary>
    /// DR-010: When a dawn victim has a single possible role type, the system records it automatically.
    /// </summary>
    [Fact]
    public void VictimEliminated_SinglePossibleRole_AutoAssignsRoleAndAnnouncesVictim()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolf = players[0];
        var victim = players[2]; // Villager

        // Complete night phase
        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: victim.Id,
            seerId: players[1].Id,
            seerTargetId: werewolf.Id);

        // Act - Get the next instruction after night.
        var instruction = builder.GetCurrentInstruction();

        // Assert - Should be a confirmation-only announcement containing the victim.
        var confirmation = instruction.Should().BeOfType<ConfirmationInstruction>().Subject;
        confirmation.PublicAnnouncement.Should().Contain(victim.Name);
        var victimAfterReveal = gameState.GetPlayer(victim.Id);
        victimAfterReveal.State.MainRole.Should().Be(MainRoleType.SimpleVillager);

        var roleLog = gameState.GameHistoryLog
            .OfType<AssignRoleLogEntry>()
            .Single(entry => entry.PlayerIds.Contains(victim.Id));
        roleLog.AssignedMainRole.Should().Be(MainRoleType.SimpleVillager);
        roleLog.CurrentPhase.Should().Be(GamePhase.Dawn);

        MarkTestCompleted();
    }

    /// <summary>
    /// DR-010b: When a dawn victim has multiple possible role types, Moderator assignment is still requested.
    /// </summary>
    [Fact]
    public void VictimEliminated_MultiplePossibleRoles_RequestsRoleAssignment()
    {
        var session = new GameSession(
            Guid.NewGuid(),
            new ConfirmationInstruction(privateInstruction: nameof(VictimEliminated_MultiplePossibleRoles_RequestsRoleAssignment)),
            new GameSessionConfig(
                ["Werewolf", "Seer", "Wild Child", "Villager A", "Villager B"],
                [
                    MainRoleType.SimpleWerewolf,
                    MainRoleType.Seer,
                    MainRoleType.WildChild,
                    MainRoleType.SimpleVillager,
                    MainRoleType.SimpleVillager
                ]));

        var players = session.GetPlayers().ToList();
        var knownWerewolf = players[0];
        var victim = players[1];

        session.AssignRole(knownWerewolf.Id, MainRoleType.SimpleWerewolf);
        session.TransitionMainPhase(GamePhase.Dawn);
        session.EliminatePlayer(victim.Id, EliminationReason.WerewolfAttack);

        var instruction = DawnPhaseHandlers.AnnounceVictimsAndRequestRoles(session, new ModeratorResponse());

        var assignment = instruction.Should().BeOfType<AssignRolesInstruction>().Subject;
        assignment.PlayersForAssignment.Should().Contain(victim.Id);
        assignment.RolesForAssignment.Distinct().Should().HaveCountGreaterThan(1);
        session.GetPlayer(victim.Id).State.MainRole.Should().BeNull();

        DawnPhaseHandlers.AssignVictimRoles(session, assignment.CreateResponse(new Dictionary<Guid, MainRoleType>
        {
            [victim.Id] = MainRoleType.Seer
        }));

        session.GetPlayer(victim.Id).State.MainRole.Should().Be(MainRoleType.Seer);
        session.GameHistoryLog
            .OfType<AssignRoleLogEntry>()
            .Should()
            .ContainSingle(entry =>
                entry.PlayerIds.Contains(victim.Id) &&
                entry.AssignedMainRole == MainRoleType.Seer);

        MarkTestCompleted();
    }

    /// <summary>
    /// DR-010c: Multiple dawn victims still require Moderator assignment even when their remaining role type is duplicated.
    /// </summary>
    [Fact]
    public void VictimsEliminated_MultipleVictimsWithSamePossibleRole_RequestRoleAssignment()
    {
        var session = new GameSession(
            Guid.NewGuid(),
            new ConfirmationInstruction(privateInstruction: nameof(VictimsEliminated_MultipleVictimsWithSamePossibleRole_RequestRoleAssignment)),
            new GameSessionConfig(
                ["Werewolf", "Villager A", "Villager B", "Villager C", "Villager D"],
                [
                    MainRoleType.SimpleWerewolf,
                    MainRoleType.SimpleVillager,
                    MainRoleType.SimpleVillager,
                    MainRoleType.SimpleVillager,
                    MainRoleType.SimpleVillager
                ]));

        var players = session.GetPlayers().ToList();
        var werewolf = players[0];
        var victimIds = players.Skip(1).Select(player => player.Id).ToHashSet();

        session.AssignRole(werewolf.Id, MainRoleType.SimpleWerewolf);
        session.TransitionMainPhase(GamePhase.Dawn);
        foreach (var victimId in victimIds)
        {
            session.EliminatePlayer(victimId, EliminationReason.WerewolfAttack);
        }

        var instruction = DawnPhaseHandlers.AnnounceVictimsAndRequestRoles(session, new ModeratorResponse());

        var assignment = instruction.Should().BeOfType<AssignRolesInstruction>().Subject;
        assignment.PlayersForAssignment.Should().BeEquivalentTo(victimIds);
        assignment.RolesForAssignment.Should().BeEquivalentTo(
            [
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager
            ]);

        foreach (var victimId in victimIds)
        {
            session.GetPlayer(victimId).State.MainRole.Should().BeNull();
        }

        MarkTestCompleted();
    }

    /// <summary>
    /// DR-011: Role assignment for eliminated victim creates AssignRoleLogEntry.
    /// </summary>
    [Fact]
    public void VictimRole_Revealed_CreatesAssignRoleLogEntry()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolf = players[0];
        var victim = players[2]; // Villager

        // Complete night phase
        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: victim.Id,
            seerId: players[1].Id,
            seerTargetId: werewolf.Id);

        // Act - Complete dawn with specific role assignment
        var roleAssignments = new Dictionary<Guid, MainRoleType>
        {
            { victim.Id, MainRoleType.SimpleVillager }
        };
        builder.CompleteDawnPhase(roleAssignments);

        // Assert - Verify AssignRoleLogEntry was created
        var roleLogs = gameState.GameHistoryLog
            .OfType<AssignRoleLogEntry>()
            .Where(e => e.PlayerIds.Contains(victim.Id))
            .ToList();

        roleLogs.Should().HaveCount(1);
        roleLogs[0].AssignedMainRole.Should().Be(MainRoleType.SimpleVillager);

        MarkTestCompleted();
    }

    /// <summary>
    /// DR-012: Eliminated victim's health status is set to Dead.
    /// </summary>
    [Fact]
    public void VictimHealthStatus_SetToDead()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolf = players[0];
        var victim = players[2]; // Villager

        // Verify victim starts alive
        var victimBefore = gameState.GetPlayers().First(p => p.Id == victim.Id);
        victimBefore.State.Health.Should().Be(PlayerHealth.Alive);

        // Complete night phase
        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: victim.Id,
            seerId: players[1].Id,
            seerTargetId: werewolf.Id);

        // Act - Complete dawn phase
        builder.CompleteDawnPhase();

        // Assert - Victim should now be dead
        var victimAfter = gameState.GetPlayers().First(p => p.Id == victim.Id);
        victimAfter.State.Health.Should().Be(PlayerHealth.Dead);

        MarkTestCompleted();
    }

    #endregion
}
