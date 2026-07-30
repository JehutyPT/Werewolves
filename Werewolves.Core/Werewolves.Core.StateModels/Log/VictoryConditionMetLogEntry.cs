using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Logs when a victory condition is met.
/// </summary>
public record VictoryConditionMetLogEntry : GameLogEntryBase
{
    public required GameResult GameResult { get; init; }
    public required VictoryCheckWindow VictoryCheckWindow { get; init; }

    /// <summary>
    /// Applies the victory condition to the game state.
    /// </summary>
    protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
    {
		//logging only, no state change
		return this;
    }

    public override string ToString() =>
        $"Victory: {GameResult.GetType().Name} at {VictoryCheckWindow}";
}
