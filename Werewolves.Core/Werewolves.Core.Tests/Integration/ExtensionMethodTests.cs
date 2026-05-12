using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for extension methods: role grouping, player filtering.
/// Test IDs: EX-001 through EX-006
/// </summary>
public class ExtensionMethodTests
{
    #region EX-001: GetRoleGroup_Werewolves

    /// <summary>
    /// EX-001: GetRoleGroup returns Werewolves group for werewolf roles.
    /// </summary>
    [Theory]
    [InlineData(MainRoleType.SimpleWerewolf)]
    [InlineData(MainRoleType.BigBadWolf)]
    [InlineData(MainRoleType.AccursedWolfFather)]
    [InlineData(MainRoleType.WhiteWerewolf)]
    public void GetRoleGroup_Werewolves_ReturnsWerewolvesGroup(MainRoleType role)
    {
        // Act
        var group = role.GetRoleGroup();

        // Assert
        group.Should().Be(RoleGroup.Werewolves);
    }

    #endregion

    #region EX-002: GetRoleGroup_Villagers

    /// <summary>
    /// EX-002: GetRoleGroup returns Villagers group for villager roles.
    /// </summary>
    [Theory]
    [InlineData(MainRoleType.SimpleVillager)]
    [InlineData(MainRoleType.VillagerVillager)]
    [InlineData(MainRoleType.Seer)]
    [InlineData(MainRoleType.Cupid)]
    [InlineData(MainRoleType.Witch)]
    [InlineData(MainRoleType.Hunter)]
    [InlineData(MainRoleType.LittleGirl)]
    [InlineData(MainRoleType.Defender)]
    [InlineData(MainRoleType.Elder)]
    [InlineData(MainRoleType.Scapegoat)]
    [InlineData(MainRoleType.VillageIdiot)]
    [InlineData(MainRoleType.TwoSisters)]
    [InlineData(MainRoleType.ThreeBrothers)]
    [InlineData(MainRoleType.Fox)]
    [InlineData(MainRoleType.BearTamer)]
    [InlineData(MainRoleType.StutteringJudge)]
    [InlineData(MainRoleType.KnightWithRustySword)]
    public void GetRoleGroup_Villagers_ReturnsVillagersGroup(MainRoleType role)
    {
        // Act
        var group = role.GetRoleGroup();

        // Assert
        group.Should().Be(RoleGroup.Villagers);
    }

    #endregion

    #region EX-003: GetRoleGroup_Ambiguous

    /// <summary>
    /// EX-003: GetRoleGroup returns Ambiguous group for ambiguous roles.
    /// </summary>
    [Theory]
    [InlineData(MainRoleType.Thief)]
    [InlineData(MainRoleType.DevotedServant)]
    [InlineData(MainRoleType.Actor)]
    [InlineData(MainRoleType.WildChild)]
    [InlineData(MainRoleType.WolfHound)]
    public void GetRoleGroup_Ambiguous_ReturnsAmbiguousGroup(MainRoleType role)
    {
        // Act
        var group = role.GetRoleGroup();

        // Assert
        group.Should().Be(RoleGroup.Ambiguous);
    }

    #endregion

    #region EX-004: GetRoleGroup_Loners

    /// <summary>
    /// EX-004: GetRoleGroup returns Loners group for loner roles.
    /// </summary>
    [Theory]
    [InlineData(MainRoleType.Angel)]
    [InlineData(MainRoleType.Piper)]
    [InlineData(MainRoleType.PrejudicedManipulator)]
    public void GetRoleGroup_Loners_ReturnsLonersGroup(MainRoleType role)
    {
        // Act
        var group = role.GetRoleGroup();

        // Assert
        group.Should().Be(RoleGroup.Loners);
    }

    #endregion

    #region EX-005: GetRoleGroup_NewMoon

    /// <summary>
    /// EX-005: GetRoleGroup returns NewMoon group for New Moon roles.
    /// </summary>
    [Theory]
    [InlineData(MainRoleType.Gypsy)]
    public void GetRoleGroup_NewMoon_ReturnsNewMoonGroup(MainRoleType role)
    {
        // Act
        var group = role.GetRoleGroup();

        // Assert
        group.Should().Be(RoleGroup.NewMoon);
    }

    #endregion

    #region EX-006: GetRoleGroup_AllRoles

    /// <summary>
    /// EX-006: GetRoleGroup returns a valid group for all MainRoleType values without throwing.
    /// </summary>
    [Fact]
    public void GetRoleGroup_AllRoles_ReturnValidGroup()
    {
        // Arrange
        var allRoles = Enum.GetValues<MainRoleType>();

        // Act & Assert
        foreach (var role in allRoles)
        {
            var act = () => role.GetRoleGroup();
            act.Should().NotThrow($"GetRoleGroup should handle {role}");

            var group = role.GetRoleGroup();
            group.Should().BeOneOf(
                RoleGroup.Werewolves,
                RoleGroup.Villagers,
                RoleGroup.Ambiguous,
                RoleGroup.Loners,
                RoleGroup.NewMoon);
        }
    }

    #endregion
}
