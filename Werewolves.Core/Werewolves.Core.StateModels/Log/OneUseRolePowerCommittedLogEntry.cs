using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Atomically records a one-use Role Power resource spend and its Night Action intent.
/// The resource identity is qualified by its concrete owning power instance.
/// </summary>
public sealed record OneUseRolePowerCommittedLogEntry
	: NightActionLogEntry,
		IOneUseRolePowerCommittedLogEntry
{
	public required Guid ActingPlayerId { get; init; }
	public required MainRoleType SourceRole { get; init; }
	public required string SourcePowerIdentifier { get; init; }
	public required Guid PowerInstanceId { get; init; }
	public required RolePowerInstanceOrigin PowerInstanceOrigin { get; init; }
	public required Guid OneUseResourceId { get; init; }

	[JsonIgnore]
	public OneUseRolePowerResourceIdentity ResourceIdentity => new(
		ActingPlayerId,
		SourceRole,
		SourcePowerIdentifier,
		PowerInstanceId,
		PowerInstanceOrigin,
		OneUseResourceId);

	internal override void EnforceValidity()
	{
		ResourceIdentity.EnforceValidity();
		if (ActionType == NightActionType.Unknown ||
		    TargetIds is not { Count: 1 } ||
		    TargetIds[0] == Guid.Empty)
		{
			throw new InvalidOperationException(
				"The committed One-Use Resource log entry is structurally invalid.");
		}
	}

	public override string ToString() =>
		$"OneUseRolePower: actor {ActingPlayerId}, " +
		$"{SourceRole}/{SourcePowerIdentifier} " +
		$"instance {PowerInstanceId} ({PowerInstanceOrigin}), " +
		$"resource {OneUseResourceId}, " +
		$"action {ActionType}";
}
