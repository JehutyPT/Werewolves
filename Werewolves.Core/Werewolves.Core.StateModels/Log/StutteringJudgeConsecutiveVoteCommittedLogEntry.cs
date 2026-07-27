using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// One atomic fact: the Judge's signal occurred, its owner-qualified One-Use
/// Resource was spent, and a Consecutive Vote was committed.
/// </summary>
public sealed record StutteringJudgeConsecutiveVoteCommittedLogEntry
	: DayActionLogEntry,
		IOneUseRolePowerCommittedLogEntry
{
	public required OneUseRolePowerResourceIdentity ResourceIdentity
		{ get; init; }

	[JsonIgnore]
	public bool SignalOccurred => true;

	internal void EnforceValidity()
	{
		ResourceIdentity.EnforceValidity();
		if (ActionType != DayPowerType.JudgeExtraVote ||
		    TargetIds != null)
		{
			throw new InvalidOperationException(
				"The committed Stuttering Judge transaction is structurally invalid.");
		}
	}

	public override string ToString() =>
		$"StutteringJudgeConsecutiveVote: actor {ResourceIdentity.ActingPlayerId}, " +
		$"instance {ResourceIdentity.PowerInstanceId} ({ResourceIdentity.PowerInstanceOrigin}), " +
		$"resource {ResourceIdentity.OneUseResourceId}";
}
