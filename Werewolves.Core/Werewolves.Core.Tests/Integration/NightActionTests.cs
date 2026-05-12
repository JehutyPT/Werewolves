using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for night phase actions: werewolf attacks, seer investigations, action logging.
/// Test IDs: NA-001 through NA-022
/// </summary>
public class NightActionTests : DiagnosticTestBase
{
    public NightActionTests(ITestOutputHelper output) : base(output) { }
    #region NA-001 to NA-003: Werewolf Actions

    /// <summary>
    /// NA-001: Werewolves wake and select victim, action is logged.
    /// On Night 1, werewolves need to be identified first, then select a victim.
    /// </summary>
    [Fact]
    public void Werewolves_WakeAndSelectVictim_LogsNightAction()
    {
        // Arrange - 5 players: 1 WW, 1 Seer, 3 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        // Night 1: First role to wake is Werewolf (based on hook order after any first-night-only roles)
        // The instruction should be to identify the werewolf player
        var wwInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            "Werewolf wake/identify instruction");

        // Identify player 0 as the werewolf
        var werewolfPlayer = players[0];
        var identifyResponse = wwInstruction.CreateResponse([werewolfPlayer.Id]);
        var afterIdentify = builder.Process(identifyResponse);

        // Now we should get victim selection instruction
        var victimInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterIdentify,
            "Werewolf victim selection");

        // Select a villager as victim (player 4 should be a villager)
        var victimPlayer = players[4];
        var victimResponse = victimInstruction.CreateResponse([victimPlayer.Id]);
        builder.Process(victimResponse);

        // Assert - Check that a NightActionLogEntry was created
        var updatedState = builder.GetGameState()!;
        var nightActions = updatedState.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Where(e => e.ActionType == NightActionType.WerewolfVictimSelection)
            .ToList();

        nightActions.Should().HaveCount(1);
        nightActions[0].TargetIds.Should().BeEquivalentTo([victimPlayer.Id]);

        MarkTestCompleted();
    }

    /// <summary>
    /// NA-002: Werewolves cannot select another werewolf as victim.
    /// </summary>
    [Fact]
    public void Werewolves_CannotSelectWerewolf_AsVictim()
    {
        // Arrange - 5 players: 2 WW, 1 Seer, 2 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 2, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        // Get werewolf identification instruction
        var wwInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            "Werewolf wake/identify instruction");

        // Identify players 0 and 1 as werewolves
        var werewolf1 = players[0];
        var werewolf2 = players[1];
        var identifyResponse = wwInstruction.CreateResponse([werewolf1.Id, werewolf2.Id]);
        var afterIdentify = builder.Process(identifyResponse);

        // Get victim selection instruction
        var victimInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterIdentify,
            "Werewolf victim selection");

        // Assert - Werewolves should not be in selectable targets
        victimInstruction.SelectablePlayerIds.Should().NotContain(werewolf1.Id);
        victimInstruction.SelectablePlayerIds.Should().NotContain(werewolf2.Id);

        MarkTestCompleted();
    }

    /// <summary>
    /// NA-003: Werewolves complete the full wake-select-sleep cycle.
    /// </summary>
    [Fact]
    public void Werewolves_CompleteFlow_WakeSleepCycle()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        // Step 1: Identify werewolf
        var wwInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction());
        var identifyResponse = wwInstruction.CreateResponse([players[0].Id]);
        var result1 = builder.Process(identifyResponse);

        // Step 2: Select victim
        var victimInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(result1);
        var victimResponse = victimInstruction.CreateResponse([players[4].Id]);
        var result2 = builder.Process(victimResponse);

        // Step 3: Confirm sleep
        var sleepInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            result2,
            "Werewolf sleep confirmation");
        var sleepResponse = sleepInstruction.CreateResponse(true);
        var result3 = builder.Process(sleepResponse);

        // Assert - Should have moved to next role (Seer) or completed night
        result3.IsSuccess.Should().BeTrue();

        MarkTestCompleted();
    }

    #endregion

    #region NA-010 to NA-012: Seer Actions

    /// <summary>
    /// NA-010: Seer checks a werewolf and receives feedback indicating they wake with werewolves.
    /// </summary>
    [Fact]
    public void Seer_ChecksWerewolf_ReceivesFeedbackAndLogsAction()
    {
        // Arrange - Complete werewolf actions first to get to Seer
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolfPlayer = players[0];
        var seerPlayer = players[1]; // Seer is second in role order
        var villagerPlayer = players[4];

        // Complete werewolf actions
        CompleteWerewolfNightAction(builder, [werewolfPlayer.Id], villagerPlayer.Id);

        // Now Seer should wake up - first instruction is to identify
        var seerIdentifyInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            "Seer identification");
        var seerIdentifyResponse = seerIdentifyInstruction.CreateResponse([seerPlayer.Id]);
        var afterSeerIdentify = builder.Process(seerIdentifyResponse);

        // Get Seer's target selection instruction
        var seerTargetInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterSeerIdentify,
            "Seer target selection");

        // Seer checks the werewolf
        var seerTargetResponse = seerTargetInstruction.CreateResponse([werewolfPlayer.Id]);
        var afterSeerCheck = builder.Process(seerTargetResponse);

        // Assert - Verify feedback instruction is returned
        var feedbackInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterSeerCheck,
            "Seer feedback instruction");

        // Verify feedback indicates target wakes with werewolves
        feedbackInstruction.PrivateInstruction.Should().Contain(GameStrings.SeerResultWerewolfTeam);

        // Assert - Check that a SeerCheck action was logged
        var updatedState = builder.GetGameState()!;
        var seerActions = updatedState.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Where(e => e.ActionType == NightActionType.SeerCheck)
            .ToList();

        seerActions.Should().HaveCount(1);
        seerActions[0].TargetIds.Should().BeEquivalentTo([werewolfPlayer.Id]);

        MarkTestCompleted();
    }

    /// <summary>
    /// NA-011: Seer checks a villager and receives feedback indicating they do NOT wake with werewolves.
    /// </summary>
    [Fact]
    public void Seer_ChecksVillager_ReceivesFeedbackAndLogsAction()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolfPlayer = players[0];
        var seerPlayer = players[1];
        var villagerToCheck = players[2]; // Check a villager
        var victimPlayer = players[4];

        // Complete werewolf actions
        CompleteWerewolfNightAction(builder, [werewolfPlayer.Id], victimPlayer.Id);

        // Identify seer
        var seerIdentifyInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction());
        var seerIdentifyResponse = seerIdentifyInstruction.CreateResponse([seerPlayer.Id]);
        var afterSeerIdentify = builder.Process(seerIdentifyResponse);

        // Seer checks a villager
        var seerTargetInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterSeerIdentify);
        var seerTargetResponse = seerTargetInstruction.CreateResponse([villagerToCheck.Id]);
        var afterSeerCheck = builder.Process(seerTargetResponse);

        // Assert - Verify feedback instruction is returned
        var feedbackInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterSeerCheck,
            "Seer feedback instruction");

        // Verify feedback indicates target does NOT wake with werewolves
        feedbackInstruction.PrivateInstruction.Should().Contain(GameStrings.SeerResultNotWerewolfTeam);

        // Assert - Check that a SeerCheck action was logged
        var updatedState = builder.GetGameState()!;
        var seerActions = updatedState.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Where(e => e.ActionType == NightActionType.SeerCheck)
            .ToList();

        seerActions.Should().HaveCount(1);
        seerActions[0].TargetIds.Should().BeEquivalentTo([villagerToCheck.Id]);

        MarkTestCompleted();
    }

    /// <summary>
    /// NA-012: Seer action is logged with correct details.
    /// </summary>
    [Fact]
    public void Seer_ActionLogged_WithCorrectDetails()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        // Complete werewolf and seer actions
        CompleteWerewolfNightAction(builder, [players[0].Id], players[4].Id);
        CompleteSeerNightAction(builder, players[1].Id, players[2].Id);

        // Assert
        var updatedState = builder.GetGameState()!;
        var seerAction = updatedState.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .First(e => e.ActionType == NightActionType.SeerCheck);

        seerAction.TurnNumber.Should().Be(1);
        seerAction.CurrentPhase.Should().Be(GamePhase.Night);
        seerAction.TargetIds.Should().BeEquivalentTo([players[2].Id]);

        MarkTestCompleted();
    }

    /// <summary>
    /// NA-013: Seer cannot check themselves - their own ID is excluded from selectable targets.
    /// </summary>
    [Fact]
    public void Seer_CannotCheckSelf()
    {
        // Arrange - 5 players: 1 WW, 1 Seer, 3 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolfPlayer = players[0];
        var seerPlayer = players[1];
        var villagerPlayer = players[4];

        // Complete werewolf actions to get to Seer's turn
        CompleteWerewolfNightAction(builder, [werewolfPlayer.Id], villagerPlayer.Id);

        // Identify the Seer
        var seerIdentifyInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            "Seer identification");
        var seerIdentifyResponse = seerIdentifyInstruction.CreateResponse([seerPlayer.Id]);
        var afterSeerIdentify = builder.Process(seerIdentifyResponse);

        // Get Seer's target selection instruction
        var seerTargetInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterSeerIdentify,
            "Seer target selection");

        // Assert - Seer's own ID should NOT be in selectable targets
        seerTargetInstruction.SelectablePlayerIds.Should().NotContain(seerPlayer.Id,
            "Seer should not be able to check themselves");

        MarkTestCompleted();
    }

    /// <summary>
    /// NA-014: Seer cannot check dead players - dead player IDs are excluded from selectable targets.
    /// </summary>
    [Fact]
    public void Seer_CannotCheckDeadPlayers()
    {
        // Arrange: 5 players (1 WW, 1 Seer, 3 Villagers) to ensure game continues after deaths
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolfPlayer = players[0];   // WW
        var seerPlayer = players[1];       // Seer
        var villager1 = players[2];        // Villager (will be killed Night 1)
        var villager2 = players[3];        // Villager (will be lynched Day 1)
        var villager3 = players[4];        // Villager

        // === Night 1: Werewolf kills villager1, Seer checks villager3 ===
        builder.CompleteNightPhase(
            werewolfIds: [werewolfPlayer.Id],
            victimId: villager1.Id,
            seerId: seerPlayer.Id,
            seerTargetId: villager3.Id
        );

        // Verify we're in Dawn
        gameState.GetCurrentPhase().Should().Be(GamePhase.Dawn);

        // === Dawn 1: villager1 is eliminated ===
        builder.CompleteDawnPhase();

        // Verify villager1 is now dead
        var deadVillager = gameState.GetPlayer(villager1.Id);
        deadVillager.State.Health.Should().Be(PlayerHealth.Dead);

        // === Day 1: Lynch villager2 (to avoid WW victory and continue game) ===
        builder.CompleteDayPhaseWithLynch(villager2.Id);

        // Verify we're back to Night phase (Night 2)
        gameState.GetCurrentPhase().Should().Be(GamePhase.Night);
        gameState.TurnNumber.Should().Be(2);

        // === Night 2: Get to Seer's target selection and verify dead players are excluded ===
        // Confirm night starts
        ConfirmNightStart(builder);

        // On Night 2, werewolves are already identified - process until we get to target selection
        var currentInstruction = builder.GetCurrentInstruction();

        // Process confirmations until we get to werewolf victim selection
        while (currentInstruction is ConfirmationInstruction confirmInstr)
        {
            builder.Process(confirmInstr.CreateResponse(true));
            currentInstruction = builder.GetCurrentInstruction();
        }

        // Werewolf victim selection
        var victimInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            currentInstruction,
            "Werewolf victim selection (Night 2)");
        builder.Process(victimInstruction.CreateResponse([villager3.Id]));

        // Confirm werewolf sleep
        var sleepInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            "Werewolf sleep confirmation");
        builder.Process(sleepInstruction.CreateResponse(true));

        // Now Seer should wake - on Night 2 they're already identified
        // Process any confirmation instructions to get to target selection
        currentInstruction = builder.GetCurrentInstruction();
        while (currentInstruction is ConfirmationInstruction seerConfirmInstr)
        {
            builder.Process(seerConfirmInstr.CreateResponse(true));
            currentInstruction = builder.GetCurrentInstruction();
        }

        // Get Seer's target selection instruction
        var seerTargetInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            currentInstruction,
            "Seer target selection (Night 2)");

        // Assert - Dead players should NOT be in selectable targets
        seerTargetInstruction.SelectablePlayerIds.Should().NotContain(villager1.Id,
            "Villager killed Night 1 should not be selectable");
        seerTargetInstruction.SelectablePlayerIds.Should().NotContain(villager2.Id,
            "Villager lynched Day 1 should not be selectable");

        // Also verify that living players ARE selectable (sanity check)
        seerTargetInstruction.SelectablePlayerIds.Should().Contain(werewolfPlayer.Id,
            "Living werewolf should be selectable");
        seerTargetInstruction.SelectablePlayerIds.Should().Contain(villager3.Id,
            "Living villager should be selectable");

        MarkTestCompleted();
    }

    #endregion

    #region NA-020 to NA-022: Edge Cases

    /// <summary>
    /// NA-020: Seer targeted on Night 1 still acts before Dawn.
    /// Death only resolves at Dawn, so Seer performs their action.
    /// </summary>
    [Fact]
    public void Seer_TargetedNight1_StillActsBeforeDawn()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolfPlayer = players[0];
        var seerPlayer = players[1];

        // Werewolf targets the Seer
        CompleteWerewolfNightAction(builder, [werewolfPlayer.Id], seerPlayer.Id);

        // Seer should still wake up and act (death resolves at Dawn)
        var seerInstruction = builder.GetCurrentInstruction();
        seerInstruction.Should().BeOfType<SelectPlayersInstruction>(
            "Seer should still wake up even if targeted");

        // Complete Seer's action
        var seerIdentify = InstructionAssert.ExpectType<SelectPlayersInstruction>(seerInstruction);
        var identifyResponse = seerIdentify.CreateResponse([seerPlayer.Id]);
        var afterIdentify = builder.Process(identifyResponse);

        var seerTarget = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(afterIdentify);
        var targetResponse = seerTarget.CreateResponse([werewolfPlayer.Id]);
        builder.Process(targetResponse);

        // Assert - Seer's action should be logged
        var updatedState = builder.GetGameState()!;
        var seerActions = updatedState.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Where(e => e.ActionType == NightActionType.SeerCheck)
            .ToList();

        seerActions.Should().HaveCount(1, "Seer should have acted despite being targeted");

        MarkTestCompleted();
    }

    /// <summary>
    /// NA-021: Seer killed Night 1 cannot act on Night 2.
    /// </summary>
    [Fact]
    public void Seer_KilledNight1_CannotActNight2()
    {
        // Arrange: 5 players (1 WW, 1 Seer, 3 Villagers) to ensure game continues after deaths
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolfPlayer = players[0];   // WW
        var seerPlayer = players[1];       // Seer
        var villager1 = players[2];        // Villager (will be lynched Day 1)
        var villager2 = players[3];        // Villager
        var villager3 = players[4];        // Villager

        // === Night 1: Werewolf kills Seer, Seer acts normally ===
        builder.CompleteNightPhase(
            werewolfIds: [werewolfPlayer.Id],
            victimId: seerPlayer.Id,        // WW targets Seer
            seerId: seerPlayer.Id,
            seerTargetId: werewolfPlayer.Id // Seer checks WW before dying
        );

        // Verify we're in Dawn
        gameState.GetCurrentPhase().Should().Be(GamePhase.Dawn);

        // === Dawn 1: Seer is eliminated ===
        builder.CompleteDawnPhase();

        // Verify Seer is now dead
        var updatedSeer = gameState.GetPlayer(seerPlayer.Id);
        updatedSeer.State.Health.Should().Be(PlayerHealth.Dead);

        // === Day 1: Lynch a villager (to continue game) ===
        builder.CompleteDayPhaseWithLynch(villager1.Id);

        // Verify we're back to Night phase (Night 2)
        gameState.GetCurrentPhase().Should().Be(GamePhase.Night);
        gameState.TurnNumber.Should().Be(2);

        // === Night 2: Verify Seer is skipped ===
        // Confirm night starts
        ConfirmNightStart(builder);

        // On Night 2, werewolves are already identified
        // First comes the wake confirmation, then target selection
        // The flow might have intermediate confirmations, so let's handle them
        var currentInstruction = builder.GetCurrentInstruction();
        
        // Process confirmations until we get to target selection or something else
        while (currentInstruction is ConfirmationInstruction confirmInstr)
        {
            builder.Process(confirmInstr.CreateResponse(true));
            currentInstruction = builder.GetCurrentInstruction();
            
            // Safety check: we should hit target selection within a few iterations
            if (gameState.GetCurrentPhase() != GamePhase.Night)
            {
                break; // Transitioned out of night
            }
        }

        // Now we should have the victim selection
        var victimInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            currentInstruction,
            "Werewolf victim selection (Night 2)");
        builder.Process(victimInstruction.CreateResponse([villager2.Id]));

        // Confirm werewolf sleep
        var sleepInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            "Werewolf sleep confirmation");
        builder.Process(sleepInstruction.CreateResponse(true));

        // After werewolf completes, the next instruction should NOT be Seer
        // It should be night end confirmation or transition to Dawn
        var nextInstruction = builder.GetCurrentInstruction();
        
        // The Seer should be skipped - the next instruction should be a confirmation
        // for night ending, not a SelectPlayersInstruction for Seer
        nextInstruction.Should().BeOfType<ConfirmationInstruction>(
            "Seer is dead and should be skipped - should go to night end");

        // Count Seer actions in the log - should only be 1 from Night 1
        var seerActions = gameState.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Where(e => e.ActionType == NightActionType.SeerCheck)
            .ToList();

        seerActions.Should().HaveCount(1, "Seer should only have acted on Night 1 before dying");

        MarkTestCompleted();
    }

    /// <summary>
    /// NA-022: First night role identification records role correctly.
    /// </summary>
    [Fact]
    public void FirstNight_RoleIdentification_RecordsRoleCorrectly()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

		var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolfPlayer = players[0];

        // Initially, player should have no role assigned
        werewolfPlayer.State.MainRole.Should().BeNull("Role not yet identified");

        // Identify werewolf
        var wwInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction());
        var identifyResponse = wwInstruction.CreateResponse([werewolfPlayer.Id]);
        builder.Process(identifyResponse);

        // Assert - Role should now be assigned
        var updatedState = builder.GetGameState()!;
        var updatedPlayer = updatedState.GetPlayer(werewolfPlayer.Id);
        updatedPlayer.State.MainRole.Should().Be(MainRoleType.SimpleWerewolf);

        // Also verify via log entry
        var roleAssignments = updatedState.GameHistoryLog
            .OfType<AssignRoleLogEntry>()
            .Where(e => e.PlayerIds.Contains(werewolfPlayer.Id))
            .ToList();

        roleAssignments.Should().HaveCount(1);
        roleAssignments[0].AssignedMainRole.Should().Be(MainRoleType.SimpleWerewolf);

        MarkTestCompleted();
    }

    /// <summary>
    /// NA-023: Werewolves cannot target dead players - dead player IDs are excluded from selectable targets.
    /// </summary>
    [Fact]
    public void Werewolves_CannotTargetDeadPlayers()
    {
        // Arrange: 5 players (1 WW, 1 Seer, 3 Villagers) to ensure game continues after deaths
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolfPlayer = players[0];   // WW
        var seerPlayer = players[1];       // Seer
        var villager1 = players[2];        // Villager (will be killed Night 1)
        var villager2 = players[3];        // Villager (will be lynched Day 1)
        var villager3 = players[4];        // Villager

        // === Night 1: Werewolf kills villager1, Seer checks villager3 ===
        builder.CompleteNightPhase(
            werewolfIds: [werewolfPlayer.Id],
            victimId: villager1.Id,
            seerId: seerPlayer.Id,
            seerTargetId: villager3.Id
        );

        // Verify we're in Dawn
        gameState.GetCurrentPhase().Should().Be(GamePhase.Dawn);

        // === Dawn 1: villager1 is eliminated ===
        builder.CompleteDawnPhase();

        // Verify villager1 is now dead
        var deadVillager = gameState.GetPlayer(villager1.Id);
        deadVillager.State.Health.Should().Be(PlayerHealth.Dead);

        // === Day 1: Lynch villager2 (to avoid WW victory and continue game) ===
        builder.CompleteDayPhaseWithLynch(villager2.Id);

        // Verify we're back to Night phase (Night 2)
        gameState.GetCurrentPhase().Should().Be(GamePhase.Night);
        gameState.TurnNumber.Should().Be(2);

        // === Night 2: Get to werewolf victim selection and verify dead players are excluded ===
        // Confirm night starts
        ConfirmNightStart(builder);

        // On Night 2, werewolves are already identified - process confirmations until we get to target selection
        var currentInstruction = builder.GetCurrentInstruction();

        while (currentInstruction is ConfirmationInstruction confirmInstr)
        {
            builder.Process(confirmInstr.CreateResponse(true));
            currentInstruction = builder.GetCurrentInstruction();
        }

        // Get werewolf victim selection instruction
        var victimInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            currentInstruction,
            "Werewolf victim selection (Night 2)");

        // Assert - Dead players should NOT be in selectable targets
        victimInstruction.SelectablePlayerIds.Should().NotContain(villager1.Id,
            "Villager killed Night 1 should not be selectable as werewolf target");
        victimInstruction.SelectablePlayerIds.Should().NotContain(villager2.Id,
            "Villager lynched Day 1 should not be selectable as werewolf target");

        // Also verify that living players ARE selectable (sanity check)
        // Note: Werewolf cannot target themselves, so only villager3 and seer should be selectable
        victimInstruction.SelectablePlayerIds.Should().Contain(seerPlayer.Id,
            "Living Seer should be selectable");
        victimInstruction.SelectablePlayerIds.Should().Contain(villager3.Id,
            "Living villager should be selectable");

        // Verify werewolf cannot target themselves
        victimInstruction.SelectablePlayerIds.Should().NotContain(werewolfPlayer.Id,
            "Werewolf should not be able to target themselves");

        MarkTestCompleted();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Confirms the "night starts" instruction that precedes the hook loop.
    /// </summary>
    private static void ConfirmNightStart(GameTestBuilder builder)
    {
        var nightStartInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            "Night start confirmation");
        var response = nightStartInstruction.CreateResponse(true);
        builder.Process(response);
    }

    /// <summary>
    /// Completes the werewolf night action sequence: identify (if Night 1) → select victim → confirm sleep.
    /// </summary>
    private static void CompleteWerewolfNightAction(GameTestBuilder builder, HashSet<Guid> werewolfIds, Guid victimId)
    {
        // Identify werewolves
        var identifyInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            "Werewolf identification");
        var identifyResponse = identifyInstruction.CreateResponse(werewolfIds);
        var afterIdentify = builder.Process(identifyResponse);

        // Select victim
        var victimInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterIdentify,
            "Werewolf victim selection");
        var victimResponse = victimInstruction.CreateResponse([victimId]);
        var afterVictim = builder.Process(victimResponse);

        // Confirm sleep
        var sleepInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterVictim,
            "Werewolf sleep confirmation");
        var sleepResponse = sleepInstruction.CreateResponse(true);
        builder.Process(sleepResponse);
    }

    /// <summary>
    /// Completes the Seer night action sequence: identify (if Night 1) → select target → confirm sleep.
    /// </summary>
    private static void CompleteSeerNightAction(GameTestBuilder builder, Guid seerId, Guid targetId)
    {
        // Identify seer
        var identifyInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            "Seer identification");
        var identifyResponse = identifyInstruction.CreateResponse([seerId]);
        var afterIdentify = builder.Process(identifyResponse);

        // Select target
        var targetInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterIdentify,
            "Seer target selection");
        var targetResponse = targetInstruction.CreateResponse([targetId]);
        var afterTarget = builder.Process(targetResponse);

        // Confirm sleep
        var sleepInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterTarget,
            "Seer sleep confirmation");
        var sleepResponse = sleepInstruction.CreateResponse(true);
        builder.Process(sleepResponse);
    }

    #endregion
}
