using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Complete owner-qualified identity of one concrete Role Power instance.
/// Action, target, turn, and phase describe a commit, not the power instance.
/// </summary>
public readonly record struct RolePowerInstanceIdentity(
	Guid ActingPlayerId,
	MainRoleType SourceRole,
	string SourcePowerIdentifier,
	Guid PowerInstanceId,
	RolePowerInstanceOrigin PowerInstanceOrigin)
{
	public bool IsValid =>
		ActingPlayerId != Guid.Empty &&
		Enum.IsDefined(SourceRole) &&
		!string.IsNullOrWhiteSpace(SourcePowerIdentifier) &&
		PowerInstanceId != Guid.Empty &&
		Enum.IsDefined(PowerInstanceOrigin);

	public void EnforceValidity()
	{
		if (!IsValid)
		{
			throw new InvalidOperationException(
				"The Role Power instance identity is structurally invalid.");
		}
	}
}
