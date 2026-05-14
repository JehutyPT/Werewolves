using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Client.Tests.Helpers;

internal static class LobbySetupMetadataFixture
{
	public static LobbySetupMetadata Default() => SupportedRoleCatalog.CreateLobbySetupMetadata();

	public static LobbySetupMetadata ForRoles(params MainRoleType[] roles)
	{
		return new LobbySetupMetadata(
			GameSessionConfig.MinimumPlayerCount,
			roles.Select(RoleMetadata).ToArray());
	}

	public static LobbySetupState DefaultState() => new(Default());

	public static LobbySetupState StateWithRoles(params MainRoleType[] roles) => new(ForRoles(roles));

	public static LobbySetupRoleMetadata RoleMetadata(MainRoleType role)
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
