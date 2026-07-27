using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;

namespace Werewolves.Core.Tests.Helpers;

/// <summary>
/// Test implementation of ISessionMutator for log replay comparison.
/// Allows replaying Apply() methods from log entries to derive state independently.
/// </summary>
internal class TestSessionMutator : ISessionMutator
{
    private readonly Dictionary<Guid, TestPlayerState> _states;
    private readonly List<GameLogEntryBase> _appliedEntries = [];

    public TestSessionMutator(IEnumerable<Guid> playerIds)
    {
        _states = playerIds.ToDictionary(id => id, id => new TestPlayerState());
    }

    public int CurrentTurnNumber { get; private set; } = 1;
    public GamePhase CurrentPhase { get; private set; } = GamePhase.Night;

    /// <summary>
    /// Gets the entries that have been applied to this mutator.
    /// </summary>
    public IReadOnlyList<GameLogEntryBase> AppliedEntries => _appliedEntries;

    public void SetModeratorKnownRole(Guid playerId, MainRoleType role)
    {
        if (_states.TryGetValue(playerId, out var state))
            state.ModeratorKnownRole = role;
    }

    public void SetPhysicalCharacterCardRole(Guid playerId, MainRoleType role)
    {
        if (_states.TryGetValue(playerId, out var state))
            state.PhysicalCharacterCardRole = role;
    }

    public void SetPlayerRole(Guid playerId, MainRoleType role)
    {
        if (_states.TryGetValue(playerId, out var state))
            state.MainRole = role;
    }

    public void SetPubliclyRevealedRole(Guid playerId, MainRoleType role)
    {
        if (_states.TryGetValue(playerId, out var state))
            state.PubliclyRevealedRole = role;
    }

    public void SetPlayerHealth(Guid playerId, PlayerHealth health)
    {
        if (_states.TryGetValue(playerId, out var state))
            state.Health = health;
    }

    public void SetVotingRight(Guid playerId, bool hasVotingRight)
    {
        if (_states.TryGetValue(playerId, out var state))
            state.HasVotingRight = hasVotingRight;
    }

    public void SetStatusEffect(Guid playerId, StatusEffectTypes effect, bool active)
    {
        if (!_states.TryGetValue(playerId, out var state))
            return;

        if (active)
            state.ActiveEffects |= effect;
        else
            state.ActiveEffects &= ~effect;
    }

    public void SetCurrentPhase(GamePhase phase)
    {
        CurrentPhase = phase;
        if (phase == GamePhase.Night)
            CurrentTurnNumber++;
    }

    public void AddLogEntry<T>(T entry) where T : GameLogEntryBase
    {
        _appliedEntries.Add(entry);
    }

    /// <summary>
    /// Gets the derived states after replay for comparison with cached state.
    /// </summary>
    public IReadOnlyDictionary<Guid, TestPlayerState> GetDerivedStates() => _states;
}

/// <summary>
/// Test implementation of player state for log replay comparison.
/// Implements the same interface contract as production PlayerState.
/// </summary>
internal class TestPlayerState : IPlayerState
{
    public MainRoleType? CurrentRole { get; set; }
    public MainRoleType? MainRole
    {
        get => CurrentRole;
        set => CurrentRole = value;
    }
    public MainRoleType? PhysicalCharacterCardRole { get; set; }
    public MainRoleType? ModeratorKnownRole { get; set; }
    public MainRoleType? PubliclyRevealedRole { get; set; }
    public PlayerHealth Health { get; set; } = PlayerHealth.Alive;
    public bool HasVotingRight { get; set; } = true;
    internal StatusEffectTypes ActiveEffects { get; set; } = StatusEffectTypes.None;

    public List<StatusEffectTypes> GetActiveStatusEffects()
    {
        var effects = new List<StatusEffectTypes>();
        foreach (StatusEffectTypes effect in Enum.GetValues<StatusEffectTypes>())
        {
            if (effect != StatusEffectTypes.None && HasStatusEffect(effect))
            {
                effects.Add(effect);
            }
        }
        return effects;
    }

    /// <summary>
    /// Checks if a specific status effect is active.
    /// For None: returns true only if the player has zero active effects.
    /// For other effects: performs standard bitwise flag check.
    /// </summary>
    public bool HasStatusEffect(StatusEffectTypes effect)
        => effect == StatusEffectTypes.None 
            ? ActiveEffects == StatusEffectTypes.None
            : (ActiveEffects & effect) == effect;

    public bool IsImmuneToLynching
        => MainRole == MainRoleType.VillageIdiot &&
           !HasStatusEffect(StatusEffectTypes.LynchingImmunityUsed);
}
