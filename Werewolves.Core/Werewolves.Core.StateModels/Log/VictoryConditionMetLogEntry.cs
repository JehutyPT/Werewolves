using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Logs when a victory condition is met.
/// </summary>
public record VictoryConditionMetLogEntry : GameLogEntryBase
{
    public required Team WinningTeam { get; init; }
    public string ConditionDescription { get; init; } = GameStrings.DefaultLogValue;

    /// <summary>
    /// Applies the victory condition to the game state.
    /// </summary>
    protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
    {
		//logging only, no state change
		return this;
    }

    public override string ToString() =>
        $"Victory: {WinningTeam} - {ConditionDescription}";
}
