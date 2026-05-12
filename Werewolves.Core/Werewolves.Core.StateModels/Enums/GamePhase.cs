namespace Werewolves.Core.StateModels.Enums;

/// <summary>
/// Represents the current stage of the game.
/// </summary>
public enum GamePhase
{
    Night,          // Consolidated night phase with internal sub-phases
    Dawn,           // Consolidated dawn phase for night resolution and role reveals
    Day,            // Day discussion + voting phase
}
