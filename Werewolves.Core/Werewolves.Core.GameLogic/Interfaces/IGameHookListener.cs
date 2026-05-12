using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Interfaces;

/// <summary>
/// Defines the contract for hook listeners that can respond to game events.
/// Both roles and event cards can implement this interface.
/// This replaces the IRole interface in the new hook-based architecture.
/// </summary>
internal interface IGameHookListener
{
    /// <summary>
    /// Advances the listener's state machine in response to a game hook.
    /// This is the single universal method that all listeners must implement.
    /// </summary>
    /// <param name="session">The current game session.</param>
    /// <param name="input">The moderator response to process.</param>
    /// <returns>A HookListenerActionResult indicating the outcome of the state machine advancement.</returns>
    HookListenerActionResult Execute(GameSession session, ModeratorResponse input);

    ListenerIdentifier Id { get; }
}
