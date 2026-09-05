using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.Tests.Helpers;

internal static class RoleKnowledgeExpectedValues
{
	private static readonly IReadOnlySet<MainRoleType> AllRoles =
		new HashSet<MainRoleType>
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.BigBadWolf,
			MainRoleType.AccursedWolfFather,
			MainRoleType.WhiteWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.VillagerVillager,
			MainRoleType.Seer,
			MainRoleType.Cupid,
			MainRoleType.Witch,
			MainRoleType.Hunter,
			MainRoleType.LittleGirl,
			MainRoleType.Defender,
			MainRoleType.Elder,
			MainRoleType.Scapegoat,
			MainRoleType.VillageIdiot,
			MainRoleType.TwoSisters,
			MainRoleType.ThreeBrothers,
			MainRoleType.Fox,
			MainRoleType.BearTamer,
			MainRoleType.StutteringJudge,
			MainRoleType.KnightWithRustySword,
			MainRoleType.Thief,
			MainRoleType.DevotedServant,
			MainRoleType.Actor,
			MainRoleType.WildChild,
			MainRoleType.WolfHound,
			MainRoleType.Angel,
			MainRoleType.Piper,
			MainRoleType.PrejudicedManipulator,
			MainRoleType.Gypsy
		};

	internal static IReadOnlySet<MainRoleType>
		InitialWerewolfAgencyRoles { get; } = new HashSet<MainRoleType>
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.BigBadWolf,
			MainRoleType.AccursedWolfFather,
			MainRoleType.WhiteWerewolf
		};

	internal static bool EstablishesInitialWerewolfAgency(MainRoleType role)
	{
		if (!AllRoles.Contains(role))
		{
			throw new ArgumentOutOfRangeException(nameof(role), role, null);
		}

		return InitialWerewolfAgencyRoles.Contains(role);
	}
}
