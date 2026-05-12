using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Abstract base class for all game log entries.
/// Provides common properties like timestamp, turn number, and game phase.
/// Based on Roadmap Phase 0 and Architecture doc log structure.
/// </summary>
public abstract record GameLogEntryBase
{
    public required DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required int TurnNumber { get; init; }
    public required GamePhase CurrentPhase { get; init; }

    internal void Apply(ISessionMutator mutator)
    {
        var log = InnerApply(mutator);

        //returning log ensures that if TurnNumber changed during Apply, the log reflects the new value
        
		mutator.AddLogEntry(log);
    }

    /// <summary>
    /// Applies the state changes from this log entry to the game session.
    /// This method is internal to ensure state mutations only happen through the log-driven pattern.
    /// </summary>
    /// <param name="mutator">The state mutator interface for applying changes</param>
    protected abstract GameLogEntryBase InnerApply(ISessionMutator mutator);

    /// <summary>
    /// Returns a diagnostic summary of this log entry focusing on "what" and "who".
    /// </summary>
    public abstract override string ToString();
}
