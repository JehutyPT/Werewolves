using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Serialization;

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

    /// <summary>
    /// Lets a listener reclaim one unanswered durable instruction after
    /// transient hook state has been discarded by serialization.
    /// </summary>
    bool TryResolvePendingInstructionContinuation(
        GameHook hook,
        GameSession session,
        ModeratorInstruction pendingInstruction,
        out string listenerState)
    {
        listenerState = string.Empty;
        return false;
    }

    ListenerIdentifier Id { get; }
}

internal interface ITargetPrivateRolePowerRecoveryCapability
{
    bool TryValidateCommittedRecoveryBoundary(
        GameSession session,
        ModeratorInstruction? startingInstruction,
        ModeratorResponse input,
        TargetPrivateRolePowerCommittedLogEntry committedEntry,
        ModeratorInstruction nextInstruction);

    void ValidateRecoveryCursorIdentity(
		GameSession session,
		DomainRecoveryCursor cursor);
}
