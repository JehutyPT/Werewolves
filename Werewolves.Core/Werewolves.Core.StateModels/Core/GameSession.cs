using System.Diagnostics.CodeAnalysis;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.StateModels.Core;

public interface IGameSession
{
    public IEnumerable<GameLogEntryBase> GameHistoryLog { get; }
    public Guid Id { get; }
    public GamePhase GetCurrentPhase();
    public int TurnNumber { get; }
    public IPlayer GetPlayer(Guid playerId);
    public IPlayerState GetPlayerState(Guid playerId);
    public IEnumerable<IPlayer> GetPlayers();
    public FactionBeneficiaryKnowledge GetFactionBeneficiaryKnowledge(
        Guid playerId);
    public FactionAgentKnowledge GetFactionAgentKnowledge(
        Guid playerId,
        Faction faction);
    public bool TryGetKnownFactionAgents(
        Faction faction,
        out IReadOnlyList<IPlayer> agents);
    public Faction RequireKnownFactionBeneficiary(Guid playerId);
    public IReadOnlyList<IPlayer> RequireKnownFactionAgents(Faction faction);
    public int RoleInPlayCount(MainRoleType type);

    /// <summary>
    /// Serializes the latest stable main-phase recovery snapshot for Rehydration.
    /// </summary>
	public string Serialize();
}

/// <summary>
/// Used to grant game flow manager access to updating the pending moderator instruction cache
/// </summary>
public interface IGameFlowManagerKey{}

/// <summary>
/// Used to grant phase manager access to updating main-phase sub-phase state cache
/// </summary>
public interface IPhaseManagerKey {}

/// <summary>
/// Used to grant phase manager access to updating sub-phase stage state cache
/// </summary>
public interface ISubPhaseManagerKey { }

/// <summary>
/// Used to grant IHookSubPhaseStage access to updating game hook listener and listener state
/// </summary>
public interface IHookSubPhaseKey{}

internal readonly record struct GameFactContext(
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase);

/// <summary>
/// Represents the tracked state of a single ongoing game.
/// This class encapsulates all game state and provides a controlled API for state mutations.
/// The GameHistoryLog is the single source of truth for all non-deterministic game events.
/// </summary>
internal class GameSession : IGameSession
{
	public Guid Id => _gameSessionKernel.Id;
	public IEnumerable<GameLogEntryBase> GameHistoryLog => _gameSessionKernel.GetAllLogEntries();

	internal GameSession(Guid id, ModeratorInstruction initialInstruction, GameSessionConfig config, IStateChangeObserver? stateChangeObserver = null)
	{
		_gameSessionKernel = new GameSessionKernel(id, initialInstruction, config, stateChangeObserver);
	}

	/// <summary>
	/// Restores a session from its stable recovery snapshot.
	/// </summary>
	/// <returns></returns>
	internal GameSession(string json)
	{
		_gameSessionKernel = GameSessionKernel.Deserialize(json);
	}

	#region Private Fields

	// Core immutable properties

	private readonly GameSessionKernel _gameSessionKernel;

	#endregion


	#region Public Game Cache read-access

	public GamePhase GetCurrentPhase() => _gameSessionKernel.CurrentPhase;
    public int TurnNumber => _gameSessionKernel.TurnNumber;
    
	#endregion

	#region Internal Game Cache read-access
    internal ModeratorInstruction? PendingModeratorInstruction => _gameSessionKernel.PendingModeratorInstruction;
    internal AcceptedObservationRecoveryCursor? GetAcceptedObservationRecoveryCursor(
        IGameFlowManagerKey key) =>
        _gameSessionKernel.AcceptedObservationRecoveryCursor;
    internal DomainRecoveryCursor? GetDomainRecoveryCursor(
        IGameFlowManagerKey key) =>
        _gameSessionKernel.DomainRecoveryCursor;
	internal T? GetSubPhase<T>() where T : struct, Enum => _gameSessionKernel.PhaseStateCache.GetSubPhase<T>();
    internal string? GetSubPhaseId() => _gameSessionKernel.PhaseStateCache.GetSubPhaseId();
    internal ListenerIdentifier? GetCurrentListener() => _gameSessionKernel.PhaseStateCache.GetCurrentListener();
    internal string? GetActiveSubPhaseStage() => _gameSessionKernel.PhaseStateCache.GetActiveSubPhaseStage();

    internal T? GetCurrentListenerState<T>(ListenerIdentifier listener) where T : struct, Enum =>
        _gameSessionKernel.PhaseStateCache.GetCurrentListenerState<T>(listener);
    internal bool TryGetActiveGameHook(out GameHook hook) =>
	    Enum.TryParse(_gameSessionKernel.PhaseStateCache.GetActiveSubPhaseStage(), out hook);
	#endregion

	#region Internal Game Cache write-access
	// Only accessible by PhaseManager or GameFlowManager via key parameter

	internal void SetPendingModeratorInstruction(IGameFlowManagerKey key, ModeratorInstruction instruction) =>
		_gameSessionKernel.SetPendingModeratorInstruction(instruction);

    internal void CaptureRecoveryBoundary(
        IGameFlowManagerKey key,
        AcceptedObservationRecoveryCursor? acceptedObservationRecoveryCursor = null,
        DomainRecoveryCursor? domainRecoveryCursor = null) =>
		_gameSessionKernel.CaptureRecoveryBoundary(
            acceptedObservationRecoveryCursor,
            domainRecoveryCursor);

    internal void NormalizeLegacyRecurringRolePowerCommit(
        IGameFlowManagerKey key,
        NightActionType actionType,
        Guid targetId,
        RolePowerInstanceIdentity powerIdentity) =>
        _gameSessionKernel.NormalizeLegacyRecurringRolePowerCommit(
            actionType,
            targetId,
            powerIdentity);

    internal void RestoreTransientContinuation(
        IGameFlowManagerKey key,
        string activeSubPhaseStage,
        ListenerIdentifier listener,
        string listenerState) =>
        _gameSessionKernel.RestoreTransientContinuation(
            activeSubPhaseStage,
            listener,
            listenerState);

	internal void TransitionSubPhaseCache(IPhaseManagerKey key, Enum subPhase) =>
        _gameSessionKernel.TransitionSubPhase(subPhase);

    /// <summary>
    /// Checks if the specified sub-phase stage can be entered,
    /// and starts it if entering for the first time for the current sub-phase.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="subPhaseStageId"></param>
    /// <returns></returns>
    internal bool TryEnterSubPhaseStage(ISubPhaseManagerKey key, string subPhaseStageId)
    {
        var currentSubPhaseStage = _gameSessionKernel.PhaseStateCache.GetActiveSubPhaseStage();

		// If already in a different sub-phase stage, cannot enter
		if (currentSubPhaseStage != null && currentSubPhaseStage != subPhaseStageId)
        {
            return false;
        }
        else
        // If no sub-phase stage is active:
        if (currentSubPhaseStage == null)
        {
			// If this sub-phase stage has already been completed, cannot enter
			if (_gameSessionKernel.PhaseStateCache.HasSubPhaseStageCompleted(subPhaseStageId))
            {
                return false;
            }
			// Otherwise, enter the sub-phase stage
			else
			{
				_gameSessionKernel.StartSubPhaseStage(subPhaseStageId);
			}
        }

		// Either already in this sub-phase stage, or just entered it successfully
		return true;
    }

    internal void CompleteSubPhaseStageCache(IPhaseManagerKey key) =>
        _gameSessionKernel.CompleteSubPhaseStage();

	internal void TransitionListenerStateCache(IHookSubPhaseKey key, ListenerIdentifier listener, string state)  =>
        _gameSessionKernel.TransitionListenerAndState(listener, state);

	internal void ClearCurrentListenerCache(IHookSubPhaseKey key) =>
		_gameSessionKernel.ClearCurrentListener();

	/// <summary>
	/// Gets or creates a listener instance for this session. Listeners are cached per-session
	/// to ensure state machine isolation between games while maintaining consistency within a game.
	/// </summary>
	internal T GetOrCreateListener<T>(ListenerIdentifier id, Func<T> factory) where T : class
	{
		if (_gameSessionKernel.ListenerInstanceCache.TryGetValue(id, out var existing))
		{
			return (T)existing;
		}

		var instance = factory();
		_gameSessionKernel.ListenerInstanceCache[id] = instance;
		return instance;
	}


	internal bool TryGetExistingListener<T>(
		ListenerIdentifier id,
		[NotNullWhen(true)] out T? listener)
		where T : class
	{
		if (_gameSessionKernel.ListenerInstanceCache.TryGetValue(
				id,
				out var existing) &&
			existing is T typedListener)
		{
			listener = typedListener;
			return true;
		}

		listener = null;
		return false;
	}
    #endregion


    // Public API for state queries

    #region Public Query API

    public IPlayer GetPlayer(Guid playerId) => _gameSessionKernel.GetIPlayer(playerId);

    public IPlayerState GetPlayerState(Guid playerId) => GetPlayer(playerId).State;

    public IEnumerable<IPlayer> GetPlayers() => _gameSessionKernel.GetIPlayers();

    public FactionBeneficiaryKnowledge GetFactionBeneficiaryKnowledge(
        Guid playerId) =>
        GetPlayerState(playerId).FactionBeneficiary;

    public FactionAgentKnowledge GetFactionAgentKnowledge(
        Guid playerId,
        Faction faction) =>
        GetPlayerState(playerId).GetFactionAgentKnowledge(faction);

    public bool TryGetKnownFactionAgents(
        Faction faction,
        out IReadOnlyList<IPlayer> agents)
    {
        if (!Enum.IsDefined(faction))
        {
            throw new ArgumentOutOfRangeException(nameof(faction));
        }

        var players = GetPlayers().ToArray();
        if (players.Any(player =>
            player.State.GetFactionAgentKnowledge(faction) ==
            FactionAgentKnowledge.Unknown))
        {
            agents = Array.Empty<IPlayer>();
            return false;
        }

        agents = Array.AsReadOnly(players
            .Where(player =>
                player.State.GetFactionAgentKnowledge(faction) ==
                FactionAgentKnowledge.KnownAgent)
            .ToArray());
        return true;
    }

    public Faction RequireKnownFactionBeneficiary(Guid playerId)
    {
        var knowledge = GetFactionBeneficiaryKnowledge(playerId);
        if (!knowledge.IsKnown)
        {
            throw FactionFactsNotReady();
        }

        return knowledge.Faction!.Value;
    }

    public IReadOnlyList<IPlayer> RequireKnownFactionAgents(Faction faction)
    {
        if (!TryGetKnownFactionAgents(faction, out var agents))
        {
            throw FactionFactsNotReady();
        }

        return agents;
    }

    public int RoleInPlayCount(MainRoleType type) => _gameSessionKernel.GetRolesInPlay().Count(r => r == type);
    
    /// <summary>
    /// To support GameSession rehydration
    /// </summary>
    /// <returns></returns>
    public string Serialize()
    {
	    return _gameSessionKernel.Serialize();
    }

    #endregion

    private static InvalidOperationException FactionFactsNotReady() =>
        new("Required Faction facts are not ready.");

	#region Internal Command API

	internal void CommitGameFact<TEntry>(
		Func<GameFactContext, TEntry> entryFactory)
		where TEntry : GameLogEntryBase, IGameFactLogEntry
		=> CommitSessionEntry(entryFactory, "game fact");

	internal void CommitFactionFactBatch(
		Func<GameFactContext, FactionFactsCommittedLogEntry> entryFactory) =>
		CommitSessionEntry(entryFactory, "Faction fact batch");

	private void CommitSessionEntry<TEntry>(
		Func<GameFactContext, TEntry> entryFactory,
		string entryDescription)
		where TEntry : GameLogEntryBase
	{
		ArgumentNullException.ThrowIfNull(entryFactory);

		var context = new GameFactContext(
			DateTimeOffset.UtcNow,
			TurnNumber,
			_gameSessionKernel.PhaseStateCache.GetCurrentPhase());
		var entry = entryFactory(context)
			?? throw new InvalidOperationException(
				$"The {entryDescription} factory returned no log entry.");
		if (entry.Timestamp != context.Timestamp ||
		    entry.TurnNumber != context.TurnNumber ||
		    entry.CurrentPhase != context.CurrentPhase)
		{
			throw new InvalidOperationException(
				$"The {entryDescription} must use the session-provided timestamp, turn, and phase.");
		}

		_gameSessionKernel.AddEntryAndUpdateState(entry);
	}

	internal void PerformNightActionNoTarget(NightActionType type)
        => PerformNightActionCore(type, null);

    internal void PerformNightAction(NightActionType type, Guid targetId) 
        => PerformNightActionCore(type, [targetId]);

    internal void PerformNightAction(NightActionType type, List<Guid> targetIds)
        => PerformNightActionCore(type, targetIds);

	internal void CommitOneUseRolePowerNightAction(
		NightActionType actionType,
		Guid targetId,
		OneUseRolePowerResourceIdentity resourceIdentity)
	{
		if (actionType == NightActionType.Unknown)
		{
			throw new ArgumentOutOfRangeException(nameof(actionType));
		}

		if (targetId == Guid.Empty)
		{
			throw new ArgumentException(
				"One-use Role Power commits require a concrete target identity.");
		}

				resourceIdentity.EnforceValidity();
				var entry = new OneUseRolePowerCommittedLogEntry
		{
			Timestamp = DateTimeOffset.UtcNow,
			TurnNumber = TurnNumber,
			CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
			ActionType = actionType,
			TargetIds = [targetId],
			ActingPlayerId = resourceIdentity.ActingPlayerId,
			SourceRole = resourceIdentity.SourceRole,
			SourcePowerIdentifier = resourceIdentity.SourcePowerIdentifier,
			PowerInstanceId = resourceIdentity.PowerInstanceId,
			PowerInstanceOrigin = resourceIdentity.PowerInstanceOrigin,
			OneUseResourceId = resourceIdentity.OneUseResourceId
		};

		_gameSessionKernel.AddEntryAndUpdateState(entry);
	}

	internal void CommitRecurringRolePowerNightAction(
		NightActionType actionType,
		Guid targetId,
		RolePowerInstanceIdentity powerIdentity) =>
		CommitRecurringRolePowerNightAction(
			actionType,
			new[] { targetId },
			powerIdentity);

	internal void CommitRecurringRolePowerNightAction(
		NightActionType actionType,
		IReadOnlyCollection<Guid> targetIds,
		RolePowerInstanceIdentity powerIdentity)
	{
		if (actionType == NightActionType.Unknown)
		{
			throw new ArgumentOutOfRangeException(nameof(actionType));
		}

		ArgumentNullException.ThrowIfNull(targetIds);
		if (targetIds.Count == 0 ||
		    targetIds.Any(targetId => targetId == Guid.Empty) ||
		    targetIds.Distinct().Count() != targetIds.Count)
		{
			throw new ArgumentException(
				"Recurring Role Power commits require a nonempty distinct target set.",
				nameof(targetIds));
		}

		var targetSet = targetIds.ToHashSet();
		var deterministicTargetIds = GetPlayers()
			.Select(player => player.Id)
			.Where(targetSet.Contains)
			.ToList();
		if (deterministicTargetIds.Count != targetSet.Count)
		{
			throw new ArgumentException(
				"Recurring Role Power targets must identify Players in the session.",
				nameof(targetIds));
		}

		powerIdentity.EnforceValidity();
		var entry = new RecurringRolePowerCommittedLogEntry
		{
			Timestamp = DateTimeOffset.UtcNow,
			TurnNumber = TurnNumber,
			CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
			ActionType = actionType,
			TargetIds = deterministicTargetIds,
			ActingPlayerId = powerIdentity.ActingPlayerId,
			SourceRole = powerIdentity.SourceRole,
			SourcePowerIdentifier = powerIdentity.SourcePowerIdentifier,
			PowerInstanceId = powerIdentity.PowerInstanceId,
			PowerInstanceOrigin = powerIdentity.PowerInstanceOrigin
		};

		_gameSessionKernel.AddEntryAndUpdateState(entry);
	}

	    internal void EliminatePlayer(Guid playerId, EliminationReason reason)
	    {
        var entry = new PlayerEliminatedLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            PlayerId = playerId,
            Reason = reason,
        };

	        _gameSessionKernel.AddEntryAndUpdateState(entry);
	    }

		internal void SetPlayerVotingRight(Guid playerId, bool hasVotingRight)
		{
			var entry = new VotingRightChangedLogEntry
			{
				Timestamp = DateTimeOffset.UtcNow,
				TurnNumber = TurnNumber,
				CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
				PlayerId = playerId,
				HasVotingRight = hasVotingRight
			};

			_gameSessionKernel.AddEntryAndUpdateState(entry);
		}

	internal void RecordEliminationCascadeReactionCompletion(
		string scopeId,
		string reactionId,
		IReadOnlyCollection<EliminationCascadeElimination>
			triggeringEliminations,
		IReadOnlyCollection<EliminationCascadeElimination>
			admittedEliminations)
	{
		var entry = new EliminationCascadeReactionCompletedLogEntry
		{
			Timestamp = DateTimeOffset.UtcNow,
			TurnNumber = TurnNumber,
			CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
			ScopeId = scopeId,
			ReactionId = reactionId,
			TriggeringEliminations = triggeringEliminations.ToList(),
			AdmittedEliminations = admittedEliminations.ToList()
		};

		_gameSessionKernel.AddEntryAndUpdateState(entry);
	}

	internal void RecordEliminationCascadeBatchResolution(
		string scopeId,
		IReadOnlyCollection<EliminationCascadeElimination>
			requestedEliminations,
		IReadOnlyCollection<EliminationCascadeElimination>
			committedEliminations)
	{
		var entry = new EliminationCascadeBatchResolvedLogEntry
		{
			Timestamp = DateTimeOffset.UtcNow,
			TurnNumber = TurnNumber,
			CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
			ScopeId = scopeId,
			RequestedEliminations = requestedEliminations.ToList(),
			CommittedEliminations = committedEliminations.ToList()
		};

		_gameSessionKernel.AddEntryAndUpdateState(entry);
	}

	internal void RecordEliminationCascadeCompletion(string scopeId)
	{
		var entry = new EliminationCascadeCompletedLogEntry
		{
			Timestamp = DateTimeOffset.UtcNow,
			TurnNumber = TurnNumber,
			CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
			ScopeId = scopeId
		};

		_gameSessionKernel.AddEntryAndUpdateState(entry);
	}

    internal void DetermineDawnVictim(Guid playerId, EliminationReason reason)
    {
        var entry = new DawnVictimDeterminedLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            PlayerId = playerId,
            Reason = reason,
        };

        _gameSessionKernel.AddEntryAndUpdateState(entry);
    }

    internal void AssignRole(Guid playerId, MainRoleType mainRoleType) =>
        AssignRole([playerId], mainRoleType);


    internal void AssignRole(HashSet<Guid> playerIds, MainRoleType mainRoleType)
    {
        var entry = new AssignRoleLogEntry()
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            PlayerIds = playerIds,
            AssignedMainRole = mainRoleType
        };

        _gameSessionKernel.AddEntryAndUpdateState(entry);
    }

    internal void IdentifyRole(HashSet<Guid> playerIds, MainRoleType role)
    {
        var entry = new RoleIdentificationLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            PlayerIds = playerIds,
            Role = role
        };

        _gameSessionKernel.AddEntryAndUpdateState(entry);
    }

    internal void ObserveVillagerVillagerFromDeal(Guid playerId)
    {
        var entry = new VillagerVillagerPublicFromDealLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            PlayerId = playerId
        };

        _gameSessionKernel.AddEntryAndUpdateState(entry);
    }

    internal void RevealRoles(IReadOnlyDictionary<Guid, MainRoleType> revealedRoles)
    {
        ArgumentNullException.ThrowIfNull(revealedRoles);
        if (revealedRoles.Count == 0)
        {
            throw new ArgumentException("A Role Reveal must include at least one Player.", nameof(revealedRoles));
        }

        var entry = new RoleRevealLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            RevealedRoles = revealedRoles.ToDictionary()
        };

        _gameSessionKernel.AddEntryAndUpdateState(entry);
    }

    internal void ApplyStatusEffect(StatusEffectTypes effectType, Guid playerId)
        => SetStatusEffect(effectType, playerId, isActive: true);

    internal void RemoveStatusEffect(StatusEffectTypes effectType, Guid playerId)
        => SetStatusEffect(effectType, playerId, isActive: false);

    private void SetStatusEffect(
	    StatusEffectTypes effectType,
	    Guid playerId,
	    bool isActive)
    {
        var entry = new StatusEffectLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            PlayerId = playerId,
            EffectType = effectType,
            IsActive = isActive
        };
        _gameSessionKernel.AddEntryAndUpdateState(entry);
	}

	internal void PerformDayVote(Guid? reportedOutcomePlayerId)
    {
        var entry = new VoteOutcomeReportedLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            ReportedOutcomePlayerId = reportedOutcomePlayerId ?? Guid.Empty
        };

        _gameSessionKernel.AddEntryAndUpdateState(entry);
    }

    internal void TransitionMainPhase(GamePhase newPhase)
    {
        var oldPhase = GetCurrentPhase();

        var entry = new PhaseTransitionLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            PreviousPhase = oldPhase,
            CurrentPhase = newPhase,
        };

        _gameSessionKernel.AddEntryAndUpdateState(entry);

    }

    internal void VictoryConditionMet(
        GameResult gameResult,
        VictoryCheckWindow victoryCheckWindow)
    {
        var entry = new VictoryConditionMetLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            GameResult = gameResult,
            VictoryCheckWindow = victoryCheckWindow
        };

		_gameSessionKernel.AddEntryAndUpdateState(entry);
	}

	internal void PerformDayActionNoTarget(DayPowerType type)
	{
		if (type == DayPowerType.Unknown)
		{
			throw new ArgumentOutOfRangeException(nameof(type));
		}

		var entry = new DayActionLogEntry
		{
			Timestamp = DateTimeOffset.UtcNow,
			TurnNumber = TurnNumber,
			CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
			ActionType = type,
			TargetIds = null
		};

		_gameSessionKernel.AddEntryAndUpdateState(entry);
	}

		#endregion

	#region Private helpers

    private void PerformNightActionCore(NightActionType type, List<Guid>? targetIds)
    {
        var entry = new NightActionLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            ActionType = type,
            TargetIds = targetIds,
        };

		_gameSessionKernel.AddEntryAndUpdateState(entry);
	}

    

    #endregion

}
