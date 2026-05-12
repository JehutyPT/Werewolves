using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using static Werewolves.Core.StateModels.Enums.NightActionType;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Represents a generic night action taken by a player/role.
/// </summary>
public record NightActionLogEntry : GameLogEntryBase
{
    public List<Guid>? TargetIds { get; init; } // ID of the player targeted, if applicable

    /// Example: "WerewolfVictimSelection", "SeerCheck", "WitchSave", "WitchKill"
    public required NightActionType ActionType { get; init; } = Unknown; // Use enum

    /// <summary>
    /// Applies the night action to the game state.
    /// Note: This is primarily for logging purposes as individual night actions
    /// don't directly change game state - their effects are processed during dawn resolution.
    /// </summary>
    protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
    {
		//no state change, just logging of who used what power on whom
		return this;
	}

    public override string ToString() =>
        TargetIds is { Count: > 0 }
            ? $"NightAction: {ActionType} targeting [{string.Join(", ", TargetIds)}]"
            : $"NightAction: {ActionType}";
}