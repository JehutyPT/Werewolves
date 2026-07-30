using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Atomically records the Village Idiot's automatic pardon, its owned
/// One-Use Resource spend, and the permanent voting consequences.
/// </summary>
public sealed record VillageIdiotPardonCommittedLogEntry
	: GameLogEntryBase,
		IGameFactLogEntry,
		IOneUseRolePowerCommittedLogEntry
{
	public required Guid PlayerId { get; init; }
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
		if (PlayerId == Guid.Empty ||
		    PlayerId != ActingPlayerId ||
		    SourceRole != MainRoleType.VillageIdiot ||
		    PowerInstanceId != ActingPlayerId ||
		    PowerInstanceOrigin != RolePowerInstanceOrigin.Native)
		{
			throw new InvalidOperationException(
				"The Village Idiot pardon must belong to its native living Role holder.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		mutator.SetDurableVotingPower(PlayerId, durableVotingPower: 0);
		mutator.SetVotingRight(PlayerId, hasVotingRight: false);
		return this;
	}

	public override string ToString() =>
		$"VillageIdiotPardon: player {PlayerId}, resource {OneUseResourceId}";
}
