using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Models;

public sealed record LobbySetupMetadata(
	int MinimumPlayerCount,
	IReadOnlyList<LobbySetupRoleMetadata> AvailableRoles);

public sealed record LobbySetupRoleMetadata(
	MainRoleType Role,
	string DisplayName,
	RoleGroup Group,
	string GroupDisplayName,
	NumberRangeConstraint CountConstraint);
