using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Logs when a player is eliminated from the game.
/// </summary>
public record PlayerEliminatedLogEntry : GameLogEntryBase
{
    public required Guid PlayerId { get; init; }
    public required EliminationReason Reason { get; init; }
	/// <summary>
	/// Applies the player elimination to the game state.
	/// </summary>
	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
    {
        mutator.SetPlayerHealth(PlayerId, PlayerHealth.Dead);
        return this;
    }

    public override string ToString() =>
        $"PlayerEliminated: {PlayerId} ({Reason})";
}
