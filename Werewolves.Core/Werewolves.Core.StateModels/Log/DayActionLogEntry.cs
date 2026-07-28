using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Represents a generic power used by a player/role that's not constrained to be used at night.
/// </summary>
public record DayActionLogEntry : GameLogEntryBase, IGameFactLogEntry
{
	public List<Guid>? TargetIds { get; init; } // ID of the player targeted, if applicable

	public required DayPowerType ActionType { get; init; }

	internal override void EnforceValidity()
	{
		if (ActionType == DayPowerType.Unknown ||
		    CurrentPhase != GamePhase.Day ||
		    TargetIds is { } targets &&
		    (targets.Any(targetId => targetId == Guid.Empty) ||
		     targets.Count != targets.Distinct().Count()) ||
		    ActionType == DayPowerType.JudgeExtraVote &&
		    TargetIds != null)
		{
			throw new InvalidOperationException(
				"The Day Action log entry is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		//no state change, just logging
		return this;
	}

	public override string ToString() =>
		TargetIds is { Count: > 0 }
			? $"DayAction: {ActionType} targeting [{string.Join(", ", TargetIds)}]"
			: $"DayAction: {ActionType}";
}
