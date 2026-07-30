using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Atomically records a recurring Role Power's owner-qualified identity and
/// its Night Action intent without representing a One-Use Resource.
/// </summary>
public sealed record RecurringRolePowerCommittedLogEntry
	: NightActionLogEntry
{
	public required Guid ActingPlayerId { get; init; }
	public required MainRoleType SourceRole { get; init; }
	public required string SourcePowerIdentifier { get; init; }
	public required Guid PowerInstanceId { get; init; }
	public required RolePowerInstanceOrigin PowerInstanceOrigin { get; init; }

	[JsonIgnore]
	public RolePowerInstanceIdentity PowerIdentity => new(
		ActingPlayerId,
		SourceRole,
		SourcePowerIdentifier,
		PowerInstanceId,
		PowerInstanceOrigin);

	internal override void EnforceValidity()
	{
		PowerIdentity.EnforceValidity();
		if (ActionType == NightActionType.Unknown ||
		    TargetIds is not [var targetId] ||
		    targetId == Guid.Empty)
		{
			throw new InvalidOperationException(
				"The recurring Role Power commit is structurally invalid.");
		}
	}

	public override string ToString() =>
		$"RecurringRolePower: actor {ActingPlayerId}, " +
		$"{SourceRole}/{SourcePowerIdentifier} " +
		$"instance {PowerInstanceId} ({PowerInstanceOrigin}), " +
		$"action {ActionType}";
}
