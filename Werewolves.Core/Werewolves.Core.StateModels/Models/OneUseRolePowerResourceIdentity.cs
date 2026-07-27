using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Complete owner-qualified identity of one spendable Role Power resource.
/// Action, target, turn, and phase describe a commit, not the resource itself.
/// </summary>
public readonly record struct OneUseRolePowerResourceIdentity(
	Guid ActingPlayerId,
	MainRoleType SourceRole,
	string SourcePowerIdentifier,
	Guid PowerInstanceId,
	RolePowerInstanceOrigin PowerInstanceOrigin,
	Guid OneUseResourceId)
{
	public bool IsValid =>
		ActingPlayerId != Guid.Empty &&
		Enum.IsDefined(SourceRole) &&
		!string.IsNullOrWhiteSpace(SourcePowerIdentifier) &&
		PowerInstanceId != Guid.Empty &&
		Enum.IsDefined(PowerInstanceOrigin) &&
		OneUseResourceId != Guid.Empty;

	public void EnforceValidity()
	{
		if (!IsValid)
		{
			throw new InvalidOperationException(
				"The One-Use Role Power Resource identity is structurally invalid.");
		}
	}
}
