using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Logs the raw outcome of a vote as reported by the moderator.
/// </summary>
public record VoteOutcomeReportedLogEntry : GameLogEntryBase
{
    // Guid.Empty represents a reported tie.
    // A specific PlayerId represents player reported as eliminated.
    public required Guid ReportedOutcomePlayerId { get; init; }

    // Consider adding VoteType (Standard, Nightmare, etc.) if needed later

    /// <summary>
    /// Applies the vote outcome to the game state.
    /// </summary>
    protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
    {
		//logging only; no state change
		return this;
	}

    public override string ToString() =>
        ReportedOutcomePlayerId == Guid.Empty
            ? "VoteOutcome: Tie"
            : $"VoteOutcome: {ReportedOutcomePlayerId}";
}
