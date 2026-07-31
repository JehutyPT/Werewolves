using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

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

    public void SetDurableVotingPower(Guid playerId, int durableVotingPower)
    {
        if (_states.TryGetValue(playerId, out var state))
            state.DurableVotingPower = durableVotingPower;
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

	public void ApplyFactionFacts(IFactionFactBatchLogEntry entry)
    {
        var projection = FactionFactProjection.Create(
            _appliedEntries
				.OfType<IFactionFactBatchLogEntry>()
                .Append(entry),
            _states.Keys.ToArray());

        foreach (var playerId in _states.Keys)
        {
            _states[playerId].ReplaceFactionProjection(
                projection.Beneficiaries[playerId],
                projection.Agents[playerId]);
        }
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
    private readonly Dictionary<Faction, FactionAgentKnowledge>
        _factionAgentKnowledge = Enum
            .GetValues<Faction>()
            .ToDictionary(
                faction => faction,
                _ => FactionAgentKnowledge.Unknown);

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
    public int DurableVotingPower { get; set; } = 1;
    public FactionBeneficiaryKnowledge FactionBeneficiary { get; private set; } =
        FactionBeneficiaryKnowledge.Unknown;
    internal StatusEffectTypes ActiveEffects { get; set; } = StatusEffectTypes.None;

    public FactionAgentKnowledge GetFactionAgentKnowledge(Faction faction) =>
        _factionAgentKnowledge[faction];

    internal void ReplaceFactionProjection(
        FactionBeneficiaryKnowledge beneficiary,
        IReadOnlyDictionary<Faction, FactionAgentKnowledge> agents)
    {
        FactionBeneficiary = beneficiary;
        foreach (var faction in Enum.GetValues<Faction>())
        {
            _factionAgentKnowledge[faction] = agents[faction];
        }
    }

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

}
