using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Serialization;

/// <summary>
/// Data Transfer Object for serializing a complete GameSession.
/// </summary>
internal class GameSessionDto
{
    public Guid Id { get; set; }
    public List<PlayerDto> Players { get; set; } = new();
    public List<Guid> SeatingOrder { get; set; } = new();
    public List<MainRoleType> RolesInPlay { get; set; } = new();
    public int TurnNumber { get; set; }
    
    // Transient state
    public GamePhaseStateCacheDto PhaseStateCache { get; set; } = new();
    public ModeratorInstruction? PendingInstruction { get; set; }
    
    // Event source
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
/// Data Transfer Object for serializing the GamePhaseStateCache.
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
