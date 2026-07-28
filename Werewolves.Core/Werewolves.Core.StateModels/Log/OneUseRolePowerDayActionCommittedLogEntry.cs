using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Atomically records a one-use Role Power resource spend and its Day Action.
/// </summary>
public sealed record OneUseRolePowerDayActionCommittedLogEntry
	: DayActionLogEntry,
		IOneUseRolePowerCommittedLogEntry
{
	public required OneUseRolePowerResourceIdentity ResourceIdentity
		{ get; init; }

	internal override void EnforceValidity()
	{
		base.EnforceValidity();
		ResourceIdentity.EnforceValidity();
	}

	public override string ToString() =>
		$"OneUseRolePowerDayAction: actor {ResourceIdentity.ActingPlayerId}, " +
		$"instance {ResourceIdentity.PowerInstanceId} ({ResourceIdentity.PowerInstanceOrigin}), " +
		$"resource {ResourceIdentity.OneUseResourceId}, action {ActionType}";
}
