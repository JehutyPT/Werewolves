namespace Werewolves.Core.StateModels.Enums;

/// <summary>
/// Defines the reason a player was eliminated from the game.
/// </summary>
public enum EliminationReason
{
    Unknown, // Default or error state
    WerewolfAttack, // Standard Werewolf kill
    WitchKill, // Killed by the Witch's poison
    HunterShot, // Killed by the Hunter's retaliatory shot
    LoversHeartbreak, // Died due to lover's death
    RustySword, // Died due to the Knight's curse
    ScapegoatSacrifice, // Sacrificed by the Scapegoat
    EventElimination, // Eliminated due to a game event
	DayVote, // Voted out during the day
    // Add other reasons later: WitchPoison, HunterShot, LoversHeartbreak, KnightCurse, Scapegoat, Events, etc.
} 