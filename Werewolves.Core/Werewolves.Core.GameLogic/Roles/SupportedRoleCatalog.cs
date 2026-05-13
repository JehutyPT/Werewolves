using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Roles;

public static class SupportedRoleCatalog
{
	private static readonly SupportedRoleDescriptor[] SupportedRoles =
	[
		new(MainRoleType.SimpleWerewolf, () => new SimpleWerewolfRole()),
		new(MainRoleType.Seer, () => new SeerRole()),
		new(MainRoleType.WildChild, () => new WildChildRole()),
		new(MainRoleType.SimpleVillager, () => new SimpleVillagerRole())
	];

	private static readonly MainRoleType[] SupportedRoleTypes = SupportedRoles
		.Select(role => role.Role)
		.ToArray();

	private static readonly HashSet<MainRoleType> SupportedRoleSet = SupportedRoleTypes.ToHashSet();

	internal static readonly IReadOnlyDictionary<ListenerIdentifier, Func<IGameHookListener>> ListenerFactories =
		SupportedRoles.ToDictionary(
			role => ListenerIdentifier.Listener(role.Role),
			role => role.CreateListener);

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

	private sealed record SupportedRoleDescriptor(
		MainRoleType Role,
		Func<IGameHookListener> CreateListener);
}
