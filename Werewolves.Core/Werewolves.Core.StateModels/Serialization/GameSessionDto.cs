using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Serialization;

/// <summary>
/// Durable recovery snapshot captured at a stable main-phase boundary.
/// </summary>
internal class GameSessionDto
{
    // Session identity and setup data.
    public Guid Id { get; set; }
    public List<Guid> SeatingOrder { get; set; } = new();
    public List<MainRoleType> RolesInPlay { get; set; } = new();

    // Derived state restored directly during Rehydration.
    public List<PlayerDto> Players { get; set; } = new();
    public int TurnNumber { get; set; }

    // True for the ADR-0002 payload shape that preserves a committed boundary cursor.
    public bool IsStableRecoveryBoundary { get; set; }

    // Committed boundary instruction and minimal phase cursor. Active stage/listener fields in
    // GamePhaseStateCacheDto are serialized for DTO compatibility but ignored during Rehydration.
    public GamePhaseStateCacheDto PhaseStateCache { get; set; } = new();
    public ModeratorInstruction? PendingInstruction { get; set; }

    // Event source as of the same stable boundary as the derived state above.
    public List<GameLogEntryBase> GameHistoryLog { get; set; } = new();
}

/// <summary>
/// Data Transfer Object for serializing Player state.
/// </summary>
internal class PlayerDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MainRoleType? MainRole { get; set; }
    public StatusEffectTypes ActiveEffects { get; set; }
    public PlayerHealth Health { get; set; }
}

/// <summary>
/// Serialized phase cache shape. Stable-boundary Rehydration restores only CurrentPhase,
/// SubPhase, and CompletedSubPhaseStages.
/// </summary>
internal class GamePhaseStateCacheDto
{
    public GamePhase CurrentPhase { get; set; }
    public string? SubPhase { get; set; }
    public string? ActiveSubPhaseStage { get; set; }
    public List<string> CompletedSubPhaseStages { get; set; } = new();
    public string? CurrentListenerId { get; set; }
    public string? CurrentListenerType { get; set; }
    public string? CurrentListenerState { get; set; }
}
