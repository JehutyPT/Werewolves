using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for status effects: querying, mutations, and extension methods.
/// Test IDs: SE-001 through SE-021
/// </summary>
public class StatusEffectsTests : DiagnosticTestBase
{
    public StatusEffectsTests(ITestOutputHelper output) : base(output) { }

    #region SE-001 to SE-005: Status Effect Querying

    /// <summary>
    /// SE-001: GetActiveStatusEffects returns empty list for new player.
    /// </summary>
    [Fact]
    public void GetActiveStatusEffects_NewPlayer_ReturnsEmptyList()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var player = gameState.GetPlayers().First();

        // Act
        var effects = player.State.GetActiveStatusEffects();

        // Assert
        effects.Should().BeEmpty();

        MarkTestCompleted();
    }

    /// <summary>
    /// SE-002: HasStatusEffect returns false for new player with no effects.
    /// </summary>
    [Fact]
    public void HasStatusEffect_NewPlayer_ReturnsFalse()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var player = gameState.GetPlayers().First();

        // Act & Assert
        player.State.HasStatusEffect(StatusEffectTypes.Sheriff).Should().BeFalse();
        player.State.HasStatusEffect(StatusEffectTypes.Charmed).Should().BeFalse();
        player.State.HasStatusEffect(StatusEffectTypes.Lovers).Should().BeFalse();
        player.State.HasStatusEffect(StatusEffectTypes.LycanthropyInfection).Should().BeFalse();

        MarkTestCompleted();
    }

    /// <summary>
    /// SE-003: HasStatusEffect(None) returns true when player has no active effects.
    /// The semantic is: "Does this player have none (zero) status effects?" → Yes/No
    /// </summary>
    [Fact]
    public void HasStatusEffect_None_ReturnsTrueWhenNoEffects()
    {
        // Arrange - new player starts with no effects
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var player = gameState.GetPlayers().First();

        // Act & Assert - None means "has zero effects", which is true for new players
        player.State.HasStatusEffect(StatusEffectTypes.None).Should().BeTrue();

        MarkTestCompleted();
    }

    /// <summary>
    /// SE-003b: HasStatusEffect(None) returns false when player has any effects.
    /// </summary>
    [Fact]
    public void HasStatusEffect_None_ReturnsFalseWhenHasEffects()
    {
        // Arrange - create player with a status effect
        var testState = new TestPlayerState
        {
            ActiveEffects = StatusEffectTypes.Sheriff
        };

        // Act & Assert - Player has effects, so HasStatusEffect(None) should be false
        testState.HasStatusEffect(StatusEffectTypes.None).Should().BeFalse();

        MarkTestCompleted();
    }

    /// <summary>
    /// SE-004: GetActiveStatusEffects excludes None from the list.
    /// </summary>
    [Fact]
    public void GetActiveStatusEffects_ExcludesNone()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var player = gameState.GetPlayers().First();

        // Act
        var effects = player.State.GetActiveStatusEffects();

        // Assert
        effects.Should().NotContain(StatusEffectTypes.None);

        MarkTestCompleted();
    }

    /// <summary>
    /// SE-005: Multiple status effects can be queried independently.
    /// </summary>
    [Fact]
    public void StatusEffects_MultipleEffects_CanBeQueriedIndependently()
    {
        // Arrange - Create test player state directly
        var testState = new TestPlayerState
        {
            ActiveEffects = StatusEffectTypes.Sheriff | StatusEffectTypes.Charmed
        };

        // Act & Assert
        testState.HasStatusEffect(StatusEffectTypes.Sheriff).Should().BeTrue();
        testState.HasStatusEffect(StatusEffectTypes.Charmed).Should().BeTrue();
        testState.HasStatusEffect(StatusEffectTypes.Lovers).Should().BeFalse();

        var effects = testState.GetActiveStatusEffects();
        effects.Should().HaveCount(2);
        effects.Should().Contain(StatusEffectTypes.Sheriff);
        effects.Should().Contain(StatusEffectTypes.Charmed);

        MarkTestCompleted();
    }

    #endregion

    #region SE-010 to SE-013: Status Effect Mutations

    /// <summary>
    /// SE-010: StatusEffectLogEntry applies the effect to player state.
    /// </summary>
    [Fact]
    public void StatusEffectLogEntry_AppliesEffect_ToPlayerState()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var mutator = new TestSessionMutator([playerId]);

        var logEntry = new StatusEffectLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = 1,
            CurrentPhase = GamePhase.Day,
            EffectType = StatusEffectTypes.Sheriff,
            PlayerId = playerId
        };

        // Act
        logEntry.Apply(mutator);

        // Assert
        var state = mutator.GetDerivedStates()[playerId];
        state.HasStatusEffect(StatusEffectTypes.Sheriff).Should().BeTrue();

        MarkTestCompleted();
    }

    /// <summary>
    /// SE-011: StatusEffectLogEntry with LycanthropyInfection applies infection.
    /// </summary>
    [Fact]
    public void StatusEffectLogEntry_LycanthropyInfection_AppliesEffect()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var mutator = new TestSessionMutator([playerId]);

        var logEntry = new StatusEffectLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = 1,
            CurrentPhase = GamePhase.Night,
            EffectType = StatusEffectTypes.LycanthropyInfection,
            PlayerId = playerId
        };

        // Act
        logEntry.Apply(mutator);

        // Assert
        var state = mutator.GetDerivedStates()[playerId];
        state.HasStatusEffect(StatusEffectTypes.LycanthropyInfection).Should().BeTrue();

        MarkTestCompleted();
    }

    /// <summary>
    /// SE-012: StatusEffectLogEntry with WildChildChanged only applies the status effect.
    /// </summary>
    [Fact]
    public void StatusEffectLogEntry_WildChildChanged_OnlyAppliesEffect()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var mutator = new TestSessionMutator([playerId]);
        mutator.SetPlayerRole(playerId, MainRoleType.WildChild);

        var logEntry = new StatusEffectLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = 2,
            CurrentPhase = GamePhase.Dawn,
            EffectType = StatusEffectTypes.WildChildChanged,
            PlayerId = playerId
        };

        // Act
        logEntry.Apply(mutator);

        // Assert
        var state = mutator.GetDerivedStates()[playerId];
        state.HasStatusEffect(StatusEffectTypes.WildChildChanged).Should().BeTrue();
        state.MainRole.Should().Be(MainRoleType.WildChild);

        MarkTestCompleted();
    }

    /// <summary>
    /// SE-012b: Wild Child transformation is composed by the listener as separate log entries.
    /// </summary>
    [Fact]
    public void WildChildModelEliminated_AppliesStatusEffectAndAssignsWerewolfRole()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithPlayers("Wild Child", "Role Model", "Werewolf", "Villager A", "Villager B", "Villager C")
            .WithRoles(
                MainRoleType.WildChild,
                MainRoleType.SimpleWerewolf,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var wildChild = players[0];
        var roleModel = players[1];
        var werewolf = players[2];

        var wildChildIdentifyInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.WildChildIdentification);
        var afterWildChildIdentify = builder.Process(wildChildIdentifyInstruction.CreateResponse([wildChild.Id]));

        var modelSelectionInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterWildChildIdentify,
            CoreTestReferences.InstructionContexts.WildChildModelSelection);
        modelSelectionInstruction.SelectablePlayerIds.Should().NotContain(wildChild.Id);
        var afterModelSelection = builder.Process(modelSelectionInstruction.CreateResponse([roleModel.Id]));

        var wildChildSleepInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterModelSelection,
            CoreTestReferences.InstructionContexts.WildChildSleepConfirmation);
        var afterWildChildSleep = builder.Process(wildChildSleepInstruction.CreateResponse());

        var werewolfIdentifyInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterWildChildSleep,
            CoreTestReferences.InstructionContexts.WerewolfIdentification);
        var afterWerewolfIdentify = builder.Process(werewolfIdentifyInstruction.CreateResponse([werewolf.Id]));

        var victimSelectionInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterWerewolfIdentify,
            CoreTestReferences.InstructionContexts.WerewolfVictimSelection);
        var afterVictimSelection = builder.Process(victimSelectionInstruction.CreateResponse([roleModel.Id]));

        var werewolfSleepInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterVictimSelection,
            CoreTestReferences.InstructionContexts.WerewolfSleepConfirmation);
        var afterWerewolfSleep = builder.Process(werewolfSleepInstruction.CreateResponse());

        var nightEndInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterWerewolfSleep,
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        var afterNightEnd = builder.Process(nightEndInstruction.CreateResponse());

        var roleRevealInstruction = InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
            afterNightEnd,
            CoreTestReferences.InstructionContexts.RoleRevealForEliminatedModel);
        roleRevealInstruction.PlayersForAssignment.Should().Equal(roleModel.Id);

        // Act
        builder.Process(roleRevealInstruction.CreateResponse(new Dictionary<Guid, MainRoleType>
        {
            { roleModel.Id, MainRoleType.SimpleVillager }
        }));

        // Assert
        var gameState = builder.GetGameState()!;
        var wildChildState = gameState.GetPlayerState(wildChild.Id);
        wildChildState.HasStatusEffect(StatusEffectTypes.WildChildChanged).Should().BeTrue();
        wildChildState.MainRole.Should().Be(MainRoleType.SimpleWerewolf);

        gameState.GameHistoryLog
            .OfType<StatusEffectLogEntry>()
            .Should()
            .ContainSingle(entry =>
                entry.PlayerId == wildChild.Id &&
                entry.EffectType == StatusEffectTypes.WildChildChanged);

        gameState.GameHistoryLog
            .OfType<AssignRoleLogEntry>()
            .Should()
            .ContainSingle(entry =>
                entry.PlayerIds.SetEquals(new[] { wildChild.Id }) &&
                entry.AssignedMainRole == MainRoleType.SimpleWerewolf);

        MarkTestCompleted();
    }

    /// <summary>
    /// SE-013: Multiple status effects can be applied to the same player.
    /// </summary>
    [Fact]
    public void StatusEffectLogEntry_MultipleEffects_CanBeApplied()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var mutator = new TestSessionMutator([playerId]);

        var entries = new[]
        {
            new StatusEffectLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                TurnNumber = 1,
                CurrentPhase = GamePhase.Night,
                EffectType = StatusEffectTypes.Lovers,
                PlayerId = playerId
            },
            new StatusEffectLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                TurnNumber = 1,
                CurrentPhase = GamePhase.Day,
                EffectType = StatusEffectTypes.Sheriff,
                PlayerId = playerId
            }
        };

        // Act
        foreach (var entry in entries)
        {
            entry.Apply(mutator);
        }

        // Assert
        var state = mutator.GetDerivedStates()[playerId];
        state.HasStatusEffect(StatusEffectTypes.Lovers).Should().BeTrue();
        state.HasStatusEffect(StatusEffectTypes.Sheriff).Should().BeTrue();
        state.GetActiveStatusEffects().Should().HaveCount(2);

        MarkTestCompleted();
    }

    #endregion

    #region SE-020 to SE-021: Village Idiot Immunity

    /// <summary>
    /// SE-020: VillageIdiot is immune to lynching before immunity is used.
    /// </summary>
    [Fact]
    public void VillageIdiot_IsImmuneToLynching_BeforeImmunityUsed()
    {
        // Arrange
        var testState = new TestPlayerState
        {
            MainRole = MainRoleType.VillageIdiot,
            ActiveEffects = StatusEffectTypes.None
        };

        // Act & Assert
        testState.IsImmuneToLynching.Should().BeTrue();

        MarkTestCompleted();
    }

    /// <summary>
    /// SE-021: VillageIdiot loses lynching immunity after LynchingImmunityUsed effect.
    /// </summary>
    [Fact]
    public void VillageIdiot_LosesImmunity_AfterLynchingImmunityUsed()
    {
        // Arrange
        var testState = new TestPlayerState
        {
            MainRole = MainRoleType.VillageIdiot,
            ActiveEffects = StatusEffectTypes.LynchingImmunityUsed
        };

        // Act & Assert
        testState.IsImmuneToLynching.Should().BeFalse();

        MarkTestCompleted();
    }

    #endregion
}
