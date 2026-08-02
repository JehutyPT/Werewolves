using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Resources;

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
/// Extension methods for <see cref="MainRoleType"/> and <see cref="RoleGroup"/>.
/// </summary>
public static class MainRoleTypeExtensions
{
    public static string GetPublicName(this MainRoleType role) =>
        GameStrings.ResourceManager.GetString($"{role}RoleName") ?? role.ToString();

    public static string GetDisplayName(this RoleGroup group) =>
        GameStrings.ResourceManager.GetString($"{group}GroupName") ?? group.ToString();

	public static bool IsHardAlignedWerewolf(this MainRoleType role) => role is
		MainRoleType.SimpleWerewolf
		or MainRoleType.BigBadWolf
		or MainRoleType.AccursedWolfFather;

	public static bool IsHardAlignedVillager(this MainRoleType role) => role is
		MainRoleType.SimpleVillager
		or MainRoleType.VillagerVillager
		or MainRoleType.Seer
		or MainRoleType.Cupid
		or MainRoleType.Witch
		or MainRoleType.Hunter
		or MainRoleType.LittleGirl
		or MainRoleType.Defender
		or MainRoleType.Elder
		or MainRoleType.Scapegoat
		or MainRoleType.VillageIdiot
		or MainRoleType.TwoSisters
		or MainRoleType.ThreeBrothers
		or MainRoleType.Fox
		or MainRoleType.BearTamer
		or MainRoleType.StutteringJudge
		or MainRoleType.KnightWithRustySword
		or MainRoleType.Actor;

	public static bool IsEligibleActorSetupCard(this MainRoleType role) =>
		role is MainRoleType.Seer
			or MainRoleType.Cupid
			or MainRoleType.Witch
			or MainRoleType.Hunter
			or MainRoleType.LittleGirl
			or MainRoleType.Defender
			or MainRoleType.Elder
			or MainRoleType.Scapegoat
			or MainRoleType.VillageIdiot
			or MainRoleType.Fox
			or MainRoleType.BearTamer
			or MainRoleType.StutteringJudge
			or MainRoleType.KnightWithRustySword;

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
		MainRoleType.Actor => RoleGroup.Villagers,

        // Ambiguous
        MainRoleType.Thief => RoleGroup.Ambiguous,
        MainRoleType.DevotedServant => RoleGroup.Ambiguous,
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
