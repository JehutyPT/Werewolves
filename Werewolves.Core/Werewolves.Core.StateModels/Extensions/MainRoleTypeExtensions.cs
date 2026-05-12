using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Extensions;

/// <summary>
/// Defines the logical groupings for roles in the game.
/// </summary>
public enum RoleGroup
{
    Werewolves,
    Villagers,
    Ambiguous,
    Loners,
    NewMoon
}

/// <summary>
/// Extension methods for <see cref="MainRoleType"/>.
/// </summary>
public static class MainRoleTypeExtensions
{
    /// <summary>
    /// Gets the role group that the specified role belongs to.
    /// </summary>
    /// <param name="role">The role to categorize.</param>
    /// <returns>The <see cref="RoleGroup"/> the role belongs to.</returns>
    public static RoleGroup GetRoleGroup(this MainRoleType role) => role switch
    {
        // Werewolves
        MainRoleType.SimpleWerewolf => RoleGroup.Werewolves,
        MainRoleType.BigBadWolf => RoleGroup.Werewolves,
        MainRoleType.AccursedWolfFather => RoleGroup.Werewolves,
        MainRoleType.WhiteWerewolf => RoleGroup.Werewolves,

        // Villagers
        MainRoleType.SimpleVillager => RoleGroup.Villagers,
        MainRoleType.VillagerVillager => RoleGroup.Villagers,
        MainRoleType.Seer => RoleGroup.Villagers,
        MainRoleType.Cupid => RoleGroup.Villagers,
        MainRoleType.Witch => RoleGroup.Villagers,
        MainRoleType.Hunter => RoleGroup.Villagers,
        MainRoleType.LittleGirl => RoleGroup.Villagers,
        MainRoleType.Defender => RoleGroup.Villagers,
        MainRoleType.Elder => RoleGroup.Villagers,
        MainRoleType.Scapegoat => RoleGroup.Villagers,
        MainRoleType.VillageIdiot => RoleGroup.Villagers,
        MainRoleType.TwoSisters => RoleGroup.Villagers,
        MainRoleType.ThreeBrothers => RoleGroup.Villagers,
        MainRoleType.Fox => RoleGroup.Villagers,
        MainRoleType.BearTamer => RoleGroup.Villagers,
        MainRoleType.StutteringJudge => RoleGroup.Villagers,
        MainRoleType.KnightWithRustySword => RoleGroup.Villagers,

        // Ambiguous
        MainRoleType.Thief => RoleGroup.Ambiguous,
        MainRoleType.DevotedServant => RoleGroup.Ambiguous,
        MainRoleType.Actor => RoleGroup.Ambiguous,
        MainRoleType.WildChild => RoleGroup.Ambiguous,
        MainRoleType.WolfHound => RoleGroup.Ambiguous,

        // Loners
        MainRoleType.Angel => RoleGroup.Loners,
        MainRoleType.Piper => RoleGroup.Loners,
        MainRoleType.PrejudicedManipulator => RoleGroup.Loners,

        // NewMoon
        MainRoleType.Gypsy => RoleGroup.NewMoon,

        _ => throw new ArgumentOutOfRangeException(nameof(role), role, $"Unknown role type: {role}")
    };
}
