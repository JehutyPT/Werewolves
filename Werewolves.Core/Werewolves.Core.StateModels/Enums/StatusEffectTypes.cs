namespace Werewolves.Core.StateModels.Enums;

/// <summary>
/// Specifies all persistent status effects that can be applied to a player.
/// These effects persist across turns and affect gameplay or UI display.
/// Some effects can register as hook listeners (e.g., Sheriff, Executioner).
/// </summary>
/// <remarks>
/// This is a [Flags] enum to allow multiple status effects to be active simultaneously 
/// (e.g., Sheriff + Infected + Charmed). The Flags pattern allows efficient storage and querying.
/// </remarks>
[Flags]
public enum StatusEffectTypes
{
    None = 0,
    
    // Persistent conditions (non-hookable)
    ElderProtectionLost = 1 << 0,      // Elder's extra life has been used
    LycanthropyInfection = 1 << 1,     // Player has been infected by the wolf father
    WildChildChanged = 1 << 2,         // Wild Child has changed their role
    LynchingImmunityUsed = 1 << 3,     // Village Idiot has used their immunity
    
    // Hookable status effects (formerly SecondaryRoleType)
    Sheriff = 1 << 4,                  // Player holds the Sheriff title
    Lovers = 1 << 5,                   // Player is one of the Lovers
    Charmed = 1 << 6,                  // Player has been charmed by the Piper
    TownCrier = 1 << 7,                // Player is the Town Crier (New Moon)
    Executioner = 1 << 8,              // Player is the Executioner (New Moon)
}
