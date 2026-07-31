using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records that a Role Power action occurred while deliberately retaining no
/// target or result in the public Game History surface.
/// </summary>
public sealed record TargetPrivateRolePowerCommittedLogEntry
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

	[JsonInclude]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	internal OneUseRolePowerResourceIdentity? SpentResourceIdentity { get; init; }

	internal override void EnforceValidity()
	{
		PowerIdentity.EnforceValidity();
		if (ActionType == NightActionType.Unknown ||
			TargetIds is { Count: > 0 })
		{
			throw new InvalidOperationException(
				"The target-private Role Power commit is structurally invalid.");
		}

		if (SpentResourceIdentity is not { } spentResource)
		{
			return;
		}

		spentResource.EnforceValidity();
		if (spentResource.ActingPlayerId != ActingPlayerId ||
			spentResource.SourceRole != SourceRole ||
			!StringComparer.Ordinal.Equals(
				spentResource.SourcePowerIdentifier,
				SourcePowerIdentifier) ||
			spentResource.PowerInstanceId != PowerInstanceId ||
			spentResource.PowerInstanceOrigin != PowerInstanceOrigin)
		{
			throw new InvalidOperationException(
				"The target-private spent Resource must belong to the committed concrete Role Power.");
		}
	}

	public override string ToString() =>
		$"TargetPrivateRolePower: actor {ActingPlayerId}, " +
		$"{SourceRole}/{SourcePowerIdentifier} " +
		$"instance {PowerInstanceId} ({PowerInstanceOrigin}), " +
		$"action {ActionType}";
}
