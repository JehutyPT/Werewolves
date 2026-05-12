using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
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
    /// DR-002: Werewolves must have at least one valid target (non-werewolf) available.
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
            "Werewolf identification");
        var identifyResponse = identifyInstruction.CreateResponse(werewolves);
        var afterIdentify = builder.Process(identifyResponse);

        // Act - Get victim selection instruction
        var victimInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterIdentify,
            "Werewolf victim selection");

        // Assert - Verify constraints
        victimInstruction.SelectablePlayerIds.Should().NotBeEmpty(
            "Werewolves must have at least one valid target");
        
        victimInstruction.SelectablePlayerIds.Should().NotContain(werewolves,
            "Werewolves cannot target other werewolves");

        victimInstruction.CountConstraint.Minimum.Should().BeGreaterOrEqualTo(1,
            "Must select at least one victim");

        MarkTestCompleted();
    }

    #endregion

    #region DR-010 to DR-012: Role Reveal Flow

    /// <summary>
    /// DR-010: When a player is eliminated at dawn, the system requests role assignment.
    /// </summary>
    [Fact]
    public void VictimEliminated_RoleRevealRequested()
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

        // Act - Get the next instruction after night (should be role assignment request)
        var instruction = builder.GetCurrentInstruction();

        // Assert - Should be AssignRolesInstruction containing the victim
        var assignInstruction = instruction.Should().BeOfType<AssignRolesInstruction>().Subject;
        assignInstruction.PlayersForAssignment.Should().Contain(victim.Id,
            "Victim should be included in role assignment request");

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
