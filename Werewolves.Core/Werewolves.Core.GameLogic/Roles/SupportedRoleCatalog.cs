using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Roles;

public static class SupportedRoleCatalog
{
	private static readonly RoleAdmissionCatalog Catalog = new(
	[
		RoleAdmission.Active(MainRoleType.SimpleWerewolf, () => new SimpleWerewolfRole()),
		RoleAdmission.Active(MainRoleType.Seer, () => new SeerRole()),
		RoleAdmission.Active(MainRoleType.WildChild, () => new WildChildRole()),
		RoleAdmission.Passive(MainRoleType.SimpleVillager)
	]);

	private static readonly MainRoleType[] SupportedRoleTypes = Catalog.Roles.ToArray();

	private static readonly HashSet<MainRoleType> SupportedRoleSet = SupportedRoleTypes.ToHashSet();

	internal static readonly IReadOnlyDictionary<ListenerIdentifier, Func<IGameHookListener>> ListenerFactories =
		Catalog.ListenerFactories;

	internal static RoleAdmissionCatalog Admissions => Catalog;

	public static IReadOnlyList<MainRoleType> Roles => SupportedRoleTypes;

	public static LobbySetupMetadata CreateLobbySetupMetadata()
	{
		return new LobbySetupMetadata(
			GameSessionConfig.MinimumPlayerCount,
			SupportedRoleTypes.Select(CreateRoleMetadata).ToArray());
	}

	public static bool IsSupported(MainRoleType role) => SupportedRoleSet.Contains(role);

	public static IReadOnlyList<MainRoleType> GetUnsupportedRoles(IEnumerable<MainRoleType> roles)
	{
		return roles
			.Distinct()
			.Where(role => !IsSupported(role))
			.ToArray();
	}

	private static LobbySetupRoleMetadata CreateRoleMetadata(MainRoleType role)
	{
		var group = role.GetRoleGroup();
		return new LobbySetupRoleMetadata(
			role,
			role.GetPublicName(),
			group,
			group.GetDisplayName(),
			GameSessionConfig.RoleCountConstraints[role]);
	}

}
