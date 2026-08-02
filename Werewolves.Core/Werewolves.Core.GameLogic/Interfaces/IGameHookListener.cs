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

internal sealed record TargetPrivateRolePowerRecoveryBoundary(
	int TurnNumber,
	GamePhase CurrentPhase,
	NightActionType ActionType,
	RolePowerInstanceIdentity PowerIdentity,
	OneUseRolePowerResourceIdentity? SpentResourceIdentity)
{
	internal Guid ActingPlayerId => PowerIdentity.ActingPlayerId;
	internal MainRoleType SourceRole => PowerIdentity.SourceRole;
	internal string SourcePowerIdentifier =>
		PowerIdentity.SourcePowerIdentifier;

	internal static TargetPrivateRolePowerRecoveryBoundary FromCommittedEntry(
		TargetPrivateRolePowerCommittedLogEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		return new(
			entry.TurnNumber,
			entry.CurrentPhase,
			entry.ActionType,
			entry.PowerIdentity,
			entry.SpentResourceIdentity);
	}

	internal static TargetPrivateRolePowerRecoveryBoundary
		FromActorBorrowedSeerCheckCommit(
			ActorBorrowedSeerCheckCommit commit)
	{
		ArgumentNullException.ThrowIfNull(commit);
		return new(
			commit.TurnNumber,
			commit.CurrentPhase,
			NightActionType.SeerCheck,
				commit.PowerIdentity,
				SpentResourceIdentity: null);
	}

	internal static TargetPrivateRolePowerRecoveryBoundary
		FromActorBorrowedFoxCheckCommit(
			ActorBorrowedFoxCheckCommit commit)
	{
		ArgumentNullException.ThrowIfNull(commit);
		return new(
			commit.TurnNumber,
			commit.CurrentPhase,
			NightActionType.FoxCheck,
			commit.PowerIdentity,
			commit.SpentResourceIdentity);
	}
}

internal interface ITargetPrivateRolePowerRecoveryCapability
{
    bool TryValidateCommittedRecoveryBoundary(
        GameSession session,
        ModeratorInstruction? startingInstruction,
        ModeratorResponse input,
        TargetPrivateRolePowerRecoveryBoundary committedBoundary,
        ModeratorInstruction nextInstruction);

    void ValidateRecoveryCursorIdentity(
		GameSession session,
		DomainRecoveryCursor cursor);
}
