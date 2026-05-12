namespace Werewolves.Core.StateModels.Enums;

/// <summary>
/// Defines the specific types of actions that can occur during the night phase.
/// Used primarily for logging and potential internal logic.
/// </summary>
public enum NightActionType
{
    Unknown = 0,                    // Default/unspecified
    WerewolfVictimSelection,        // Standard werewolf choice
    BigBadWolfVictimSelection,      // BBW additional choice
    WhiteWerewolfVictimSelection,   // WWWWW choice
    AccursedWolfFatherInfection,    // AWF infection choice
    SeerCheck,                      // Seer looking at a player
    FoxCheck,                       // Fox checking neighbors
    WitchSave,                      // Witch using healing potion
    WitchKill,                      // Witch using poison potion
    DefenderProtect,                // Defender choosing target
    PiperCharm,                     // Piper choosing targets
    RustySword,                     // Knight's Rusty Sword delayed action
    ThiefSwap,                      // Thief swapping roles
    ActorEmulate,                   // Actor emulating another role
    WildChildModel,                 // Wild Child choosing new role
    CupidLink,                      // Cupid linking lovers
    WolfHoundChoice,                // Wolf Hound team choice
					 // Add other night actions as roles are implemented (e.g., CupidLink, WildChildModel, ActorEmulate, etc.)
}

/// <summary>
/// Defines the types of role powers available in the game that can be used during the day phase.
/// These are non-deterministic events that players can choose to activate.
/// </summary>
public enum DayPowerType
{
	Unknown = 0,            // Default/unspecified
    JudgeExtraVote,         // Judge's ability to force a re-vote
    DevotedServantSwap,     // Devoted Servant's ability to swap roles with another player
    TownCrierCardReveal,    // Town Crier's ability to reveal an event card
}