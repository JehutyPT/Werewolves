namespace Werewolves.Core.StateModels.Enums;

/// <summary>
/// Represents the type of hook listener.
/// Used to distinguish between different categories of listeners that can respond to game hooks.
/// </summary>
public enum GameHookListenerType
{
    /// <summary>
    /// A main role-based listener (e.g., Werewolf, Seer, Hunter).
    /// </summary>
    MainRole,
    
    /// <summary>
    /// An event card-based listener (e.g., Retribution, Backfire).
    /// </summary>
    SpiritCard,

    /// <summary>
    /// A status effect-based listener. Can stack on top of main roles (e.g., Sheriff, Lovers, Charmed).
    /// </summary>
    StatusEffect
}
