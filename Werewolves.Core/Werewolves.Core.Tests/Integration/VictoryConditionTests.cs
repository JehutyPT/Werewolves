using FluentAssertions;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for victory conditions: villager victory, werewolf victory, and timing.
/// Test IDs: VC-001 through VC-022
/// </summary>
public class VictoryConditionTests : DiagnosticTestBase
{
    public VictoryConditionTests(ITestOutputHelper output) : base(output) { }

    #region VC-001 to VC-002: Villager Victory

    /// <summary>
    /// VC-001: Last werewolf eliminated by the Witch at Dawn triggers Villager victory.
    /// </summary>
    [Fact]
    public void WerewolfEliminated_AtDawn_VillagerVictory()
    {
        var builder = CreateBuilder()
            .WithPlayers(
                "Werewolf",
                "Witch",
                "Attack victim",
                "Villager A",
                "Villager B")
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Witch,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var witch = players[1];
        var attackVictim = players[2];

        builder.CompleteWerewolfNightAction(
            [werewolf.Id],
            attackVictim.Id);
        var witchIdentification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        var healing = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            builder.Process(witchIdentification.CreateResponse([witch.Id])));
        var poison = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            builder.Process(healing.CreateResponse([])));
        var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            builder.Process(poison.CreateResponse([werewolf.Id])));
        var finishNight = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            builder.Process(sleep.CreateResponse()));
        var dawnReveal = InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
            builder.Process(finishNight.CreateResponse()));

        dawnReveal.SelectableRolesForPlayers.Keys.Should().BeEquivalentTo(
            [werewolf.Id, attackVictim.Id]);
        var finished =
            InstructionAssert.ExpectSuccessWithType<FinishedGameConfirmationInstruction>(
                builder.Process(dawnReveal.CreateObservedRoleResponse(new()
                {
                    [werewolf.Id] = MainRoleType.SimpleWerewolf,
                    [attackVictim.Id] = MainRoleType.SimpleVillager
                })));
        finished.GameResult.Should().Be(
            new SingleFactionGameResult(Faction.Villager));
        finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
        var completed = builder.GetGameState()!;
        completed.GetPlayerState(werewolf.Id).Health.Should()
            .Be(PlayerHealth.Dead);
        completed.GameHistoryLog
            .OfType<PlayerEliminatedLogEntry>()
            .Should().ContainSingle(entry =>
                entry.PlayerId == werewolf.Id &&
                entry.Reason == EliminationReason.WitchKill &&
                entry.TurnNumber == 1 &&
                entry.CurrentPhase == GamePhase.Dawn);
        var victory = completed.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .Should().ContainSingle(entry =>
                entry.TurnNumber == 1 &&
                entry.CurrentPhase == GamePhase.Day).Which;
        victory.GameResult.Should().Be(
            new SingleFactionGameResult(Faction.Villager));
        victory.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);

        MarkTestCompleted();
    }

    /// <summary>
    /// VC-002: Last werewolf voted out during day triggers villager victory.
    /// </summary>
    [Fact]
    public void WerewolfEliminated_AtDay_VillagerVictory()
    {
        // Arrange - 5 players: 1 WW, 4 Villagers
        // Need to kill 2 villagers first so voting out WW ends with villager victory (3 villagers remain)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        // Player 0 is the werewolf (first role assigned)
        var werewolf = players[0];
        var villager1 = players[1];
        var villager2 = players[2];

        // Night 1: Werewolf kills a villager
        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightAction([werewolf.Id], villager1.Id);

        // Confirm night end
        var nightEndInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEndInstruction.CreateResponse());

        // Dawn: Process victim elimination (villager1 dies)
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            { villager1.Id, MainRoleType.SimpleVillager }
        });

        // Day: Vote out the werewolf
        // Confirm debate
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        builder.Process(debateInstruction.CreateResponse());

        // Vote for werewolf
        var voteInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.VoteSelection);
        var afterVote = builder.Process(voteInstruction.CreateResponse([werewolf.Id]));

        // Assign the exact role before publicly revealing it.
        var roleRevealInstruction = InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
            afterVote,
            CoreTestReferences.InstructionContexts.RoleAssignmentAfterLynch);
        roleRevealInstruction.SelectableRolesForPlayers.Keys.Should().Equal(werewolf.Id);
        var afterRoleReveal = builder.Process(roleRevealInstruction.CreateObservedRoleResponse(new()
        {
            [werewolf.Id] = MainRoleType.SimpleWerewolf
        }));

        // Confirm the death announcement.
        var deathAnnouncementInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            afterRoleReveal.ModeratorInstruction,
            CoreTestReferences.InstructionContexts.DeathAnnouncementConfirmation);
        var result = builder.Process(deathAnnouncementInstruction.CreateResponse());

        // Assert - Should get FinishedGameConfirmationInstruction
        var finalInstruction = result.ModeratorInstruction;
        finalInstruction.Should().BeOfType<FinishedGameConfirmationInstruction>();

        // Verify victory log entry
        var updatedState = builder.GetGameState()!;
        var victoryLog = updatedState.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .SingleOrDefault();

        victoryLog.Should().NotBeNull();
        victoryLog!.GameResult.Should().Be(new SingleFactionGameResult(Faction.Villager));
        victoryLog.VictoryCheckWindow.Should().Be(VictoryCheckWindow.PreNight);

        MarkTestCompleted();
    }

    #endregion

    #region VC-010 to VC-013: Werewolf Victory

    /// <summary>
    /// VC-010: When werewolves equal or outnumber villagers, werewolves win.
    /// </summary>
    [Fact]
    public void WerewolvesEqualVillagers_WerewolvesWin()
    {
        // Arrange - 5 players: 2 WW, 3 Villagers
        // After werewolves kill 1 villager, we have 2 WW vs 2 Villagers = WW wins (equal)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 2, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        var werewolf1 = players[0];
        var werewolf2 = players[1];
        var villager1 = players[2];

        // Night 1: Werewolves kill villager1
        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightAction([werewolf1.Id, werewolf2.Id], villager1.Id);

        // Confirm night end
        var nightEndInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEndInstruction.CreateResponse());

        // Dawn: Process victim (now 2 WW vs 2 Villagers = WW victory!)
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            { villager1.Id, MainRoleType.SimpleVillager }
        });

        // Assert - Victory is detected at dawn when werewolves equal villagers
        var finalInstruction = builder.GetCurrentInstruction();
        finalInstruction.Should().BeOfType<FinishedGameConfirmationInstruction>();

        // Verify victory log entry
        var updatedState = builder.GetGameState()!;
        var victoryLog = updatedState.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .SingleOrDefault();

        victoryLog.Should().NotBeNull();
        victoryLog!.GameResult.Should().Be(new SingleFactionGameResult(Faction.Werewolf));
        victoryLog.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);

        MarkTestCompleted();
    }

    /// <summary>
    /// VC-011: When werewolves outnumber villagers, werewolves win.
    /// </summary>
    [Fact]
    public void WerewolvesOutnumberVillagers_WerewolvesWin()
    {
        // Arrange - 5 players: 3 WW, 2 Villagers
        // After werewolf kills 1 villager, we have 3 WW vs 1 Villager = WW wins
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 3, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        // First 3 players are werewolves
        var werewolf1 = players[0];
        var werewolf2 = players[1];
        var werewolf3 = players[2];
        var villager1 = players[3];

        // Night 1: Werewolves kill villager1
        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightAction([werewolf1.Id, werewolf2.Id, werewolf3.Id], villager1.Id);

        // Confirm night end
        var nightEndInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEndInstruction.CreateResponse());

        // Dawn: Process victim - after this, 3 WW vs 1 Villager
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            { villager1.Id, MainRoleType.SimpleVillager }
        });

        // Assert - Should get FinishedGameConfirmationInstruction (victory detected at dawn)
        var finalInstruction = builder.GetCurrentInstruction();
        finalInstruction.Should().BeOfType<FinishedGameConfirmationInstruction>();

        // Verify victory log entry
        var updatedState = builder.GetGameState()!;
        var victoryLog = updatedState.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .SingleOrDefault();

        victoryLog.Should().NotBeNull();
        victoryLog!.GameResult.Should().Be(new SingleFactionGameResult(Faction.Werewolf));
        victoryLog.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);

        MarkTestCompleted();
    }

    /// <summary>
    /// VC-012: Werewolves equal villagers at dawn → werewolf victory.
    /// This tests victory detection at dawn after werewolf night attack brings the count to equality.
    /// </summary>
    [Fact]
    public void VillagerKilled_AtDawn_WerewolfVictory()
    {
        // Arrange - 5 players: 1 WW, 4 Villagers
        // Night 1: WW kill 1 villager -> 1 WW vs 3 Villagers (no victory)
        // Day 1: Village votes out 1 villager -> 1 WW vs 2 Villagers (no victory)
        // Night 2: WW kills 1 villager -> 1 WW vs 1 Villager = WW victory at dawn
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        var werewolf = players[0];
        var villager1 = players[1];
        var villager2 = players[2];
        var villager3 = players[3];

        // Night 1: Werewolf kills villager1 (now 1 WW vs 3 Villagers)
        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightAction([werewolf.Id], villager1.Id);

        // Confirm night end
        var nightEndInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEndInstruction.CreateResponse());

        // Dawn: Process victim
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            { villager1.Id, MainRoleType.SimpleVillager }
        });

        // Game continues (1 WW vs 3 Villagers - no victory yet)
        builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Day);

        // Day: Vote out villager2 (now 1 WW vs 2 Villagers)
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        builder.Process(debateInstruction.CreateResponse());

        var voteInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.VoteSelection);
        var afterVote = builder.Process(voteInstruction.CreateResponse([villager2.Id]));

        var roleRevealInstruction = InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
            afterVote,
            CoreTestReferences.InstructionContexts.RoleAssignmentAfterLynch);
        roleRevealInstruction.SelectableRolesForPlayers.Keys.Should().Equal(villager2.Id);
        var afterRoleReveal = builder.Process(roleRevealInstruction.CreateObservedRoleResponse(new()
        {
            [villager2.Id] = MainRoleType.SimpleVillager
        }));

        // Confirm death after the moderator has publicly revealed the role.
        var deathConfirmation = InstructionAssert.ExpectType<ConfirmationInstruction>(
            afterRoleReveal.ModeratorInstruction,
            CoreTestReferences.InstructionContexts.DeathConfirmation);
        builder.Process(deathConfirmation.CreateResponse());

        // Game continues to Night 2 (1 WW vs 2 Villagers)
        builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Night);

        // Night 2: Werewolf kills villager3 (now 1 WW vs 1 Villager = WW victory)
        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightActionSubsequentNight(villager3.Id);

        // Confirm night end
        var nightEndInstruction2 = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEndInstruction2.CreateResponse());

        // Dawn: Process victim - victory should be detected after elimination
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            { villager3.Id, MainRoleType.SimpleVillager }
        });

        // Assert - Victory detected at dawn, game ends
        var finalInstruction = builder.GetCurrentInstruction();
        finalInstruction.Should().BeOfType<FinishedGameConfirmationInstruction>();

        var updatedState = builder.GetGameState()!;
        var victoryLog = updatedState.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .SingleOrDefault();

        victoryLog.Should().NotBeNull();
        victoryLog!.GameResult.Should().Be(new SingleFactionGameResult(Faction.Werewolf));
        victoryLog.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);

        MarkTestCompleted();
    }

    /// <summary>
    /// VC-013: 1 werewolf, 4 villagers; werewolf kills 1 at night, 2 are voted out at day → werewolf victory.
    /// </summary>
    [Fact]
    public void VillagerKilled_AtDay_WerewolfVictory()
    {
        // Arrange - 5 players: 1 WW, 4 Villagers
        // Night 1: WW kills villager1 (1 WW vs 3 Villagers)
        // Day 1: Vote out villager2 (1 WW vs 2 Villagers)
        // Night 2: WW kills villager3 (1 WW vs 1 Villager = WW wins at dawn)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        var werewolf = players[0];
        var villager1 = players[1];
        var villager2 = players[2];
        var villager3 = players[3];
        var villager4 = players[4];

        // Night 1: Werewolf kills villager1 (now 1 WW vs 3 Villagers)
        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightAction([werewolf.Id], villager1.Id);

        // Confirm night end
        var nightEndInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEndInstruction.CreateResponse());

        // Dawn: Process victim
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            { villager1.Id, MainRoleType.SimpleVillager }
        });

        // Game continues (1 WW vs 3 Villagers)
        var gamePhase = builder.GetGameState()!.GetCurrentPhase();
        gamePhase.Should().Be(GamePhase.Day);

        // Day: Vote out villager2 (now 1 WW vs 2 Villagers)
        // Confirm debate
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        builder.Process(debateInstruction.CreateResponse());

        // Vote for villager2
        var voteInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.VoteSelection);
        var afterVote = builder.Process(voteInstruction.CreateResponse([villager2.Id]));

        var roleRevealInstruction = InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
            afterVote,
            CoreTestReferences.InstructionContexts.RoleAssignmentAfterLynch);
        roleRevealInstruction.SelectableRolesForPlayers.Keys.Should().Equal(villager2.Id);
        var afterRoleReveal = builder.Process(roleRevealInstruction.CreateObservedRoleResponse(new()
        {
            [villager2.Id] = MainRoleType.SimpleVillager
        }));

        // Confirm death announcement after the moderator has publicly revealed the role.
        var deathAnnouncementInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            afterRoleReveal.ModeratorInstruction,
            CoreTestReferences.InstructionContexts.DeathAnnouncementConfirmation);
        builder.Process(deathAnnouncementInstruction.CreateResponse());

        // Game should continue to Night 2 (1 WW vs 2 Villagers)
        builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Night);

        // Night 2: Werewolf kills villager3 (now 1 WW vs 1 Villager = WW wins)
        // Use CompleteWerewolfNightActionSubsequentNight for Night 2+ (no identification needed)
        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightActionSubsequentNight(villager3.Id);

        // Confirm night end
        var nightEndInstruction2 = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEndInstruction2.CreateResponse());

        // Dawn 2: Process victim
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            { villager3.Id, MainRoleType.SimpleVillager }
        });

        // Assert - Victory detected at dawn after night 2
        var finalInstruction = builder.GetCurrentInstruction();
        finalInstruction.Should().BeOfType<FinishedGameConfirmationInstruction>();

        var updatedState = builder.GetGameState()!;
        var victoryLog = updatedState.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .SingleOrDefault();

        victoryLog.Should().NotBeNull();
        victoryLog!.GameResult.Should().Be(new SingleFactionGameResult(Faction.Werewolf));
        victoryLog.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);

        MarkTestCompleted();
    }

    #endregion

    [Fact]
    public void SoleWhiteWerewolf_AtDawn_CommitsAndRehydratesTypedTerminalBoundary()
    {
        var builder = CreateBuilder()
            .WithPlayers(5)
            .WithRoles(
                MainRoleType.WhiteWerewolf,
                MainRoleType.SimpleWerewolf,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToArray();
        var whiteWerewolf = players[0];
        var simpleWerewolf = players[1];
        var victim = players[2];
        var alreadyEliminated = players.Skip(3).ToArray();

        builder.ArrangeKnownRole(
                whiteWerewolf.Id,
                MainRoleType.WhiteWerewolf)
            .ArrangeKnownRole(
                simpleWerewolf.Id,
                MainRoleType.SimpleWerewolf)
            .ArrangeKnownWerewolfFactionAgentGroup(
                whiteWerewolf.Id,
                simpleWerewolf.Id);
        var boundary = new FactionFactEffectiveBoundary(
            session.TurnNumber,
            session.GetCurrentPhase(),
            session.GameHistoryLog.Count());
        builder.ArrangeExplicitFactionTransition(
            "test-white-werewolf-terminal-beneficiaries",
            [
                FactionFact.Beneficiary(
                    whiteWerewolf.Id,
                    Faction.WhiteWerewolf,
                    boundary),
                FactionFact.Beneficiary(
                    simpleWerewolf.Id,
                    Faction.Werewolf,
                    boundary),
                .. players.Skip(2).Select(player =>
                    FactionFact.Beneficiary(
                        player.Id,
                        Faction.Villager,
                        boundary))
            ]);
        builder.ArrangeEliminatedPlayer(simpleWerewolf.Id);
        foreach (var player in alreadyEliminated)
        {
            builder.ArrangeEliminatedPlayer(player.Id);
        }
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightAction(
            [whiteWerewolf.Id],
            victim.Id);

        var nightEnd = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEnd.CreateResponse());
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            [victim.Id] = MainRoleType.SimpleVillager
        });

        var finished = builder.GetCurrentInstruction().Should()
            .BeOfType<FinishedGameConfirmationInstruction>().Subject;
        finished.GameResult.Should().Be(
            new SingleFactionGameResult(Faction.WhiteWerewolf));
        finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
        var terminalState = builder.GetGameState()!;
        terminalState.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .Should().ContainSingle(entry =>
                entry.GameResult.Equals(finished.GameResult) &&
                entry.VictoryCheckWindow == VictoryCheckWindow.Dawn);

        var recoveryService = new GameService();
        var recoveredId = recoveryService.RehydrateSession(
            terminalState.Serialize());
        var recoveredFinished = recoveryService.GetCurrentInstruction(
                recoveredId)
            .Should().BeOfType<FinishedGameConfirmationInstruction>()
            .Subject;

        recoveredFinished.GameResult.Should().Be(finished.GameResult);
        recoveredFinished.VictoryCheckWindow.Should().Be(
            VictoryCheckWindow.Dawn);
        recoveryService.GetGameStateView(recoveredId)!.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .Should().ContainSingle();

        MarkTestCompleted();
    }

    [Fact]
    public void SoleWhiteWerewolf_AtPreNight_CommitsTypedTerminalBoundary()
    {
        var builder = CreateBuilder()
            .WithPlayers(5)
            .WithRoles(
                MainRoleType.WhiteWerewolf,
                MainRoleType.SimpleWerewolf,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToArray();
        var whiteWerewolf = players[0];
        var simpleWerewolf = players[1];
        var nightVictim = players[2];
        var voteVictim = players[3];
        var alreadyEliminated = players[4];

        builder.ArrangeKnownRole(
                whiteWerewolf.Id,
                MainRoleType.WhiteWerewolf)
            .ArrangeKnownRole(
                simpleWerewolf.Id,
                MainRoleType.SimpleWerewolf)
            .ArrangeKnownWerewolfFactionAgentGroup(
                whiteWerewolf.Id,
                simpleWerewolf.Id);
        var boundary = new FactionFactEffectiveBoundary(
            session.TurnNumber,
            session.GetCurrentPhase(),
            session.GameHistoryLog.Count());
        builder.ArrangeExplicitFactionTransition(
            "test-white-werewolf-pre-night-beneficiaries",
            [
                FactionFact.Beneficiary(
                    whiteWerewolf.Id,
                    Faction.WhiteWerewolf,
                    boundary),
                FactionFact.Beneficiary(
                    simpleWerewolf.Id,
                    Faction.Werewolf,
                    boundary),
                .. players.Skip(2).Select(player =>
                    FactionFact.Beneficiary(
                        player.Id,
                        Faction.Villager,
                        boundary))
            ]);
        builder.ArrangeEliminatedPlayer(simpleWerewolf.Id);
        builder.ArrangeEliminatedPlayer(alreadyEliminated.Id);
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightAction(
            [whiteWerewolf.Id],
            nightVictim.Id);
        var nightEnd = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEnd.CreateResponse());
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            [nightVictim.Id] = MainRoleType.SimpleVillager
        });

        builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Day);
        var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        builder.Process(debate.CreateResponse());
        var vote = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.VoteSelection);
        var afterVote = builder.Process(
            vote.CreateResponse([voteVictim.Id]));
        var roleReveal = InstructionAssert
            .ExpectSuccessWithType<AssignRolesInstruction>(
                afterVote,
                CoreTestReferences.InstructionContexts.RoleAssignmentAfterLynch);
        var afterRoleReveal = builder.Process(
            roleReveal.CreateObservedRoleResponse(new()
            {
                [voteVictim.Id] = MainRoleType.SimpleVillager
            }));
        var announcement = InstructionAssert.ExpectType<
            ConfirmationInstruction>(
            afterRoleReveal.ModeratorInstruction,
            CoreTestReferences.InstructionContexts
                .DeathAnnouncementConfirmation);
        var result = builder.Process(announcement.CreateResponse());

        var finished = result.ModeratorInstruction.Should()
            .BeOfType<FinishedGameConfirmationInstruction>().Subject;
        finished.GameResult.Should().Be(
            new SingleFactionGameResult(Faction.WhiteWerewolf));
        finished.VictoryCheckWindow.Should().Be(
            VictoryCheckWindow.PreNight);
        builder.GetGameState()!.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .Should().ContainSingle(entry =>
                entry.GameResult.Equals(finished.GameResult) &&
                entry.VictoryCheckWindow ==
                VictoryCheckWindow.PreNight);

        MarkTestCompleted();
    }

    #region VC-020 to VC-022: Victory Timing

    [Fact]
    public void IncompleteBeneficiaryClosure_AtVictoryCheckWindow_ThrowsInvariantFailure()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToArray();
        builder.ArrangeEliminatedPlayer(players[0].Id);
        var boundary = new FactionFactEffectiveBoundary(
            session.TurnNumber,
            session.GetCurrentPhase(),
            session.GameHistoryLog.Count());
        builder.ArrangeExplicitFactionTransition(
            "test-incomplete-closure-before-victory",
            players
                .Skip(1)
                .Select(player => FactionFact.Agent(
                    player.Id,
                    Faction.Werewolf,
                    FactionAgentKnowledge.KnownNonAgent,
                    boundary))
                .ToArray());
        builder.ConfirmGameStart();

        var finishNight = builder.ConfirmNightStart()
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        finishNight.Semantic.Should().Be(
            ModeratorInstructionSemantic.FinishNightActions);
        session.GetFactionBeneficiaryKnowledge(players[1].Id)
            .IsKnown.Should().BeFalse();
        var response = finishNight.CreateResponse();
        var stableBefore = session.Serialize();
        var phaseBefore = session.GetCurrentPhase();
        var historyCountBefore = session.GameHistoryLog.Count();
        var transitionCountBefore = session.GameHistoryLog
            .OfType<PhaseTransitionLogEntry>()
            .Count();
        builder.ClearObserverLog();
        var reachVictoryCheckWindow = () => builder.Process(response);

        reachVictoryCheckWindow.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Required Faction facts are not ready.");

        AssertCoherentGuardFailure(
            builder.GameService,
            builder.GameId,
            session,
            finishNight.InstructionId,
            stableBefore,
            phaseBefore,
            historyCountBefore,
            transitionCountBefore);
        builder.ObserverLog.Should().NotContain(
            $"[SubPhaseStage] → {GameHook.DawnMainActionLoop}");
        reachVictoryCheckWindow.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Required Faction facts are not ready.");
        AssertCoherentGuardFailure(
            builder.GameService,
            builder.GameId,
            session,
            finishNight.InstructionId,
            stableBefore,
            phaseBefore,
            historyCountBefore,
            transitionCountBefore);

        var replayService = new GameService();
        var replayGameId = replayService.RehydrateSession(stableBefore);
        var replayState = replayService.GetGameStateView(replayGameId)!;
        var replay = () => replayService.ProcessInstruction(
            replayGameId,
            response);
        replay.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Required Faction facts are not ready.");
        AssertCoherentGuardFailure(
            replayService,
            replayGameId,
            replayState,
            finishNight.InstructionId,
            stableBefore,
            phaseBefore,
            historyCountBefore,
            transitionCountBefore);

        MarkTestCompleted();
    }

    [Fact]
    public void IncompleteBeneficiaryClosure_AtPreNightWindow_DoesNotExpireVotingRestriction()
    {
        var builder = CreateBuilder()
            .WithPlayers(6)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Scapegoat,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var permittedWithoutVotingRight = players[2];
        var unknownBeneficiary = players[3];
        var secondNightVictim = players[4];
        var firstDawnVictim = players[5];
        builder.CompleteNightPhase([werewolf.Id], firstDawnVictim.Id);
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            [firstDawnVictim.Id] = MainRoleType.SimpleVillager
        });
        var dayOneSession = (GameSession)builder.GetGameState()!;
        var livingIds = dayOneSession.GetPlayers()
            .Where(player => player.State.Health == PlayerHealth.Alive)
            .Select(player => player.Id)
            .ToArray();
        foreach (var playerId in livingIds)
        {
            builder.ArrangeVotingRight(playerId, hasVotingRight: false);
        }

        var restrictionScope = "test-pre-night-readiness-restriction";
        var announcementInstructionId = Guid.NewGuid();
        DayVoteRules.CommitVoterEligibilityRestriction(
            dayOneSession,
            restrictionScope,
            MainRoleType.Scapegoat,
            livingIds,
            [permittedWithoutVotingRight.Id],
            dayOneSession.TurnNumber + 1,
            announcementInstructionId);
        DayVoteRules.AcknowledgeVoterEligibilityRestrictionAnnouncement(
            dayOneSession,
            restrictionScope,
            announcementInstructionId);
        var dayOneDebate =
            InstructionAssert.ExpectType<ConfirmationInstruction>(
                builder.GetCurrentInstruction());
        var afterDayOneDebate = builder.Process(
            dayOneDebate.CreateResponse());
        afterDayOneDebate.ModeratorInstruction!.Semantic.Should().Be(
            ModeratorInstructionSemantic.StartNight);
        builder.ConfirmNightStart();
        var afterWerewolves =
            builder.CompleteWerewolfNightActionSubsequentNight(
                secondNightVictim.Id);
        var nightEnd =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                afterWerewolves,
                CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEnd.CreateResponse());
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            [secondNightVictim.Id] = MainRoleType.SimpleVillager
        });
        var followingDayDebate =
            InstructionAssert.ExpectType<ConfirmationInstruction>(
                builder.GetCurrentInstruction());
        var recoveryPayload = RecoveryPayloadTestDriver
            .Parse(builder.GetGameState()!.Serialize())
            .RemoveInitialBeneficiaryClosureFact(unknownBeneficiary.Id)
            .Serialize();
        var service = new GameService();
        var gameId = service.RehydrateSession(recoveryPayload);
        var session = service.GetGameStateView(gameId)!;
        session.GetCurrentPhase().Should().Be(GamePhase.Day);
        DayVoteRules.GetActiveVoterEligibilityRestriction(session)
            .Should().NotBeNull();
        var response = followingDayDebate.CreateResponse();
        var stableBefore = session.Serialize();
        var phaseBefore = session.GetCurrentPhase();
        var historyCountBefore = session.GameHistoryLog.Count();
        var transitionCountBefore = session.GameHistoryLog
            .OfType<PhaseTransitionLogEntry>()
            .Count();
        var reachVictoryCheckWindow = () =>
            service.ProcessInstruction(gameId, response);

        reachVictoryCheckWindow.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Required Faction facts are not ready.");
        AssertRestrictionAndSessionRemainCoherent();
        reachVictoryCheckWindow.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Required Faction facts are not ready.");
        AssertRestrictionAndSessionRemainCoherent();

        MarkTestCompleted();

        void AssertRestrictionAndSessionRemainCoherent()
        {
            AssertCoherentGuardFailure(
                service,
                gameId,
                session,
                followingDayDebate.InstructionId,
                stableBefore,
                phaseBefore,
                historyCountBefore,
                transitionCountBefore);
            session.GameHistoryLog
                .OfType<VoterEligibilityRestrictionExpiredLogEntry>()
                .Should().BeEmpty();
            DayVoteRules.GetActiveVoterEligibilityRestriction(session)
                .Should().NotBeNull();
        }
    }

    [Fact]
    public void CommittedClosureWithUnknownBeneficiary_AtVictoryCheckWindow_ThrowsWithoutMutation()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        var session = builder.GetGameState()!;
        var mutableSession = (GameSession)session;
        var players = session.GetPlayers().ToArray();
        builder.ArrangeEliminatedPlayer(players[0].Id);
        var boundary = new FactionFactEffectiveBoundary(
            session.TurnNumber,
            session.GetCurrentPhase(),
            session.GameHistoryLog.Count());
        builder.ArrangeExplicitFactionTransition(
            "test-committed-closure-with-unknown-beneficiary",
            players
                .Skip(1)
                .Select(player => FactionFact.Agent(
                    player.Id,
                    Faction.Werewolf,
                    FactionAgentKnowledge.KnownNonAgent,
                    boundary))
                .ToArray());
        mutableSession.CommitFactionFactBatch(context =>
            new FactionFactsCommittedLogEntry
            {
                Timestamp = context.Timestamp,
                TurnNumber = context.TurnNumber,
                CurrentPhase = context.CurrentPhase,
                Source = new FactionFactSource(
                    FactionFactSourceKind.InitialBeneficiaryClosure,
                    "test-committed-closure-marker"),
                Facts =
                [
                    .. players
                        .Skip(2)
                        .Select(player => FactionFact.Beneficiary(
                            player.Id,
                            Faction.Villager,
                            boundary))
                ]
            });
        builder.ConfirmGameStart();

        var finishNight = builder.ConfirmNightStart()
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        finishNight.Semantic.Should().Be(
            ModeratorInstructionSemantic.FinishNightActions);
        session.GetFactionBeneficiaryKnowledge(players[1].Id)
            .IsKnown.Should().BeFalse();
        players
            .Skip(1)
            .Count(player => !session
                .GetFactionBeneficiaryKnowledge(player.Id)
                .IsKnown)
            .Should().Be(1);
        var response = finishNight.CreateResponse();
        var stableBefore = session.Serialize();
        var phaseBefore = session.GetCurrentPhase();
        var historyCountBefore = session.GameHistoryLog.Count();
        var transitionCountBefore = session.GameHistoryLog
            .OfType<PhaseTransitionLogEntry>()
            .Count();
        var reachVictoryCheckWindow = () => builder.Process(response);

        reachVictoryCheckWindow.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Required Faction facts are not ready.");
        AssertCoherentGuardFailure(
            builder.GameService,
            builder.GameId,
            session,
            finishNight.InstructionId,
            stableBefore,
            phaseBefore,
            historyCountBefore,
            transitionCountBefore);
        reachVictoryCheckWindow.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Required Faction facts are not ready.");
        AssertCoherentGuardFailure(
            builder.GameService,
            builder.GameId,
            session,
            finishNight.InstructionId,
            stableBefore,
            phaseBefore,
            historyCountBefore,
            transitionCountBefore);

        MarkTestCompleted();
    }

    private static void AssertCoherentGuardFailure(
        GameService service,
        Guid gameId,
        IGameSession expectedState,
        Guid expectedInstructionId,
        string expectedStableState,
        GamePhase expectedPhase,
        int expectedHistoryCount,
        int expectedTransitionCount)
    {
        var state = service.GetGameStateView(gameId);
        state.Should().NotBeNull();
        state.Should().BeSameAs(expectedState);
        state!.GetCurrentPhase().Should().Be(expectedPhase);
        state.Serialize().Should().Be(expectedStableState);
        state.GameHistoryLog.Should().HaveCount(expectedHistoryCount);
        state.GameHistoryLog.OfType<PhaseTransitionLogEntry>()
            .Should().HaveCount(expectedTransitionCount);
        state.GameHistoryLog.OfType<VictoryConditionMetLogEntry>()
            .Should().BeEmpty();
        service.GetCurrentInstruction(gameId)!.InstructionId
            .Should().Be(expectedInstructionId);
    }

    /// <summary>
    /// VC-020: Victory condition is checked and detected at dawn (before Day phase starts).
    /// </summary>
    [Fact]
    public void VictoryCondition_CheckedAtDawn()
    {
        // Arrange - 5 players: 3 WW, 2 Villagers
        // After night kill, 3 WW vs 1 Villager = victory at dawn, never reaches Day
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 3, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        var werewolf1 = players[0];
        var werewolf2 = players[1];
        var werewolf3 = players[2];
        var villager1 = players[3];

        // Night 1: Werewolves kill villager1
        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightAction([werewolf1.Id, werewolf2.Id, werewolf3.Id], villager1.Id);

        // Confirm night end
        var nightEndInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEndInstruction.CreateResponse());

        // Dawn: Process victim
        builder.ClearObserverLog();
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            { villager1.Id, MainRoleType.SimpleVillager }
        });

        // Assert - Game ends at dawn, never reaches Day phase
        var finalInstruction = builder.GetCurrentInstruction();
        var finished = finalInstruction.Should()
            .BeOfType<FinishedGameConfirmationInstruction>().Subject;
        finished.GameResult.Should().Be(new SingleFactionGameResult(Faction.Werewolf));
        finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);

        // Verify victory detected at transition to Day phase (after dawn processing)
        var updatedState = builder.GetGameState()!;
        var victoryLog = updatedState.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .Single();

        victoryLog.CurrentPhase.Should().Be(GamePhase.Day);
        var dawnBoundaryEntries = updatedState.GameHistoryLog
            .Where(entry => entry is PhaseTransitionLogEntry or VictoryConditionMetLogEntry)
            .TakeLast(2)
            .ToList();
        dawnBoundaryEntries[0].Should().BeOfType<PhaseTransitionLogEntry>()
            .Which.CurrentPhase.Should().Be(GamePhase.Day);
        dawnBoundaryEntries[1].Should().BeSameAs(victoryLog);
        var boundaryTimeline = builder.ObserverLog.ToList();
        var dawnFollowUpIndex = boundaryTimeline.IndexOf(
            $"[SubPhaseStage] → {GameHook.DawnMainActionLoop}");
        var dayTransitionIndex = boundaryTimeline.IndexOf("[Phase] → Day");
        dawnFollowUpIndex.Should().BeGreaterThanOrEqualTo(0);
        dayTransitionIndex.Should().BeGreaterThan(dawnFollowUpIndex);
        builder.ObserverLog.Should().NotContain(
            $"[SubPhaseStage] → {DaySubPhaseStage.Debate}");

        MarkTestCompleted();
    }

    /// <summary>
    /// VC-021: Victory condition is checked and detected after day vote.
    /// </summary>
    [Fact]
    public void VictoryCondition_CheckedAfterVote()
    {
        // Arrange - 5 players: 1 WW, 4 Villagers
        // Night: WW kills 1 (now 1 WW vs 3 Villagers)
        // Day: Vote out WW → Villager victory at Day.Finalize
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        var werewolf = players[0];
        var villager1 = players[1];

        // Night 1: Werewolf kills villager1
        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightAction([werewolf.Id], villager1.Id);

        // Confirm night end
        var nightEndInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEndInstruction.CreateResponse());

        // Dawn: Process victim
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            { villager1.Id, MainRoleType.SimpleVillager }
        });

        // Game continues to Day (1 WW vs 3 Villagers)
        builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Day);

        // Day: Vote out the werewolf
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        builder.Process(debateInstruction.CreateResponse());

        var voteInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.VoteSelection);
        var afterVote = builder.Process(voteInstruction.CreateResponse([werewolf.Id]));

        var roleRevealInstruction = InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
            afterVote,
            CoreTestReferences.InstructionContexts.RoleAssignmentAfterLynch);
        roleRevealInstruction.SelectableRolesForPlayers.Keys.Should().Equal(werewolf.Id);
        var afterRoleReveal = builder.Process(roleRevealInstruction.CreateObservedRoleResponse(new()
        {
            [werewolf.Id] = MainRoleType.SimpleWerewolf
        }));
        var deathAnnouncementInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            afterRoleReveal.ModeratorInstruction,
            CoreTestReferences.InstructionContexts.DeathAnnouncementConfirmation);
        builder.ClearObserverLog();
        var result = builder.Process(deathAnnouncementInstruction.CreateResponse());

        // Assert - Victory detected at Day phase
        var finalInstruction = result.ModeratorInstruction;
        var finished = finalInstruction.Should()
            .BeOfType<FinishedGameConfirmationInstruction>().Subject;
        finished.GameResult.Should().Be(new SingleFactionGameResult(Faction.Villager));
        finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.PreNight);

        var updatedState = builder.GetGameState()!;
        var victoryLog = updatedState.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .Single();

        victoryLog.CurrentPhase.Should().Be(GamePhase.Night);
        var duskBoundaryEntries = updatedState.GameHistoryLog
            .Where(entry => entry is PhaseTransitionLogEntry or VictoryConditionMetLogEntry)
            .TakeLast(2)
            .ToList();
        duskBoundaryEntries[0].Should().BeOfType<PhaseTransitionLogEntry>()
            .Which.CurrentPhase.Should().Be(GamePhase.Night);
        duskBoundaryEntries[1].Should().BeSameAs(victoryLog);
        builder.ObserverLog.Should().NotContain(
            $"[SubPhaseStage] → {NightSubPhaseStage.NightStart}");

        MarkTestCompleted();
    }

    /// <summary>
    /// VC-022: When no victory condition is met, game continues to next phase.
    /// </summary>
    [Fact]
    public void NoVictoryCondition_GameContinues()
    {
        // Arrange - 5 players: 1 WW, 4 Villagers (plenty of cushion)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        var werewolf = players[0];
        var villager1 = players[1];

        // Night 1: Werewolf kills villager1 (now 1 WW vs 3 Villagers)
        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightAction([werewolf.Id], villager1.Id);

        // Confirm night end
        var nightEndInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEndInstruction.CreateResponse());

        // Dawn: Process victim
        builder.ClearObserverLog();
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            { villager1.Id, MainRoleType.SimpleVillager }
        });

        // Assert - Game continues to Day (no victory)
        var currentPhase = builder.GetGameState()!.GetCurrentPhase();
        currentPhase.Should().Be(GamePhase.Day);

        // No victory log should exist yet
        var victoryLogs = builder.GetGameState()!.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .ToList();
        victoryLogs.Should().BeEmpty();

        // Current instruction should NOT be FinishedGameConfirmationInstruction
        builder.GetCurrentInstruction().Should().NotBeOfType<FinishedGameConfirmationInstruction>();
        var boundaryTimeline = builder.ObserverLog.ToList();
        var dayTransitionIndex = boundaryTimeline.IndexOf("[Phase] → Day");
        var dawnFollowUpIndex = boundaryTimeline.IndexOf(
            $"[SubPhaseStage] → {GameHook.DawnMainActionLoop}");
        var debateIndex = boundaryTimeline.IndexOf(
            $"[SubPhaseStage] → {DaySubPhaseStage.Debate}");

        dawnFollowUpIndex.Should().BeGreaterThanOrEqualTo(0);
        dayTransitionIndex.Should().BeGreaterThan(dawnFollowUpIndex);
        debateIndex.Should().BeGreaterThan(dayTransitionIndex);

        MarkTestCompleted();
    }

    #endregion
}
