using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

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
    public int RoleInPlayCount(MainRoleType type);

    /// <summary>
    /// To support GameSession rehydration
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
	/// To support GameSession rehydration
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

	internal T? GetSubPhase<T>() where T : struct, Enum => _gameSessionKernel.PhaseStateCache.GetSubPhase<T>();
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

    #endregion


    // Public API for state queries

    #region Public Query API

    public IPlayer GetPlayer(Guid playerId) => _gameSessionKernel.GetIPlayer(playerId);

    public IPlayerState GetPlayerState(Guid playerId) => GetPlayer(playerId).State;

    public IEnumerable<IPlayer> GetPlayers() => _gameSessionKernel.GetIPlayers();

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

	#region Internal Command API

	internal void PerformNightActionNoTarget(NightActionType type) 
        => PerformNightActionCore(type, null);

    internal void PerformNightAction(NightActionType type, Guid targetId) 
        => PerformNightActionCore(type, [targetId]);

    internal void PerformNightAction(NightActionType type, List<Guid> targetIds)
        => PerformNightActionCore(type, targetIds);

    #endregion

    #region Internal Query API

    internal IEnumerable<IPlayer> GetPlayersTargetedLastNight(NightActionType actionType,
        NumberRangeConstraint countConstraint, NumberRangeConstraint? turnsAgoConstraint = null)
    {
        var turnNumber = _gameSessionKernel.TurnNumber;
		var logEntries = _gameSessionKernel.FindLogEntries<NightActionLogEntry>(NumberRangeConstraint.Exact(turnNumber), GamePhase.Night, log => log.ActionType == actionType);

        var playerList = logEntries.SelectMany(log => log.TargetIds ?? new()).ToList();

        countConstraint.Enforce(playerList);

        return playerList.Select(GetPlayer);
	}

    internal bool WasDayAbilityTriggeredThisTurn(DayPowerType powerType)
    {
        var turnNumber = _gameSessionKernel.TurnNumber;

        return _gameSessionKernel.FindLogEntries<DayActionLogEntry>(
            NumberRangeConstraint.Exact(turnNumber), 
            filter: log => log.ActionType == powerType).Any();
    }

    internal bool HasPlayerBeenVotedForPreviously(Guid playerId)
    {
        var turnNumber = _gameSessionKernel.TurnNumber;
        return _gameSessionKernel.FindLogEntries<VoteOutcomeReportedLogEntry>(
            NumberRangeConstraint.Range(1, turnNumber - 1), 
            filter: log => log.ReportedOutcomePlayerId == playerId).Any();
    }

    internal bool ShouldVoteRepeat()
    {
        var hasJudgeVoted  = _gameSessionKernel.FindLogEntries<DayActionLogEntry>(
            NumberRangeConstraint.Exact(_gameSessionKernel.TurnNumber),
            filter: log => log.ActionType == DayPowerType.JudgeExtraVote).Any();

        var currentTurnVoteCount = _gameSessionKernel.FindLogEntries<VoteOutcomeReportedLogEntry>(
            NumberRangeConstraint.Exact(_gameSessionKernel.TurnNumber)).Count();

        return hasJudgeVoted && currentTurnVoteCount == 1;
    }
	

    internal IEnumerable<IPlayer> GetPlayersEliminatedThisDawn()
    {
	    var turnNumber = _gameSessionKernel.TurnNumber;
		var logEntries = _gameSessionKernel.FindLogEntries<PlayerEliminatedLogEntry>(NumberRangeConstraint.Exact(turnNumber), phase: GamePhase.Dawn);

		var playerList = logEntries.Select(log => log.PlayerId).ToList();

        return playerList.Select(GetPlayer);
    }

	/// <summary>
	/// This actually checks for all players eliminated during the Day phase of the current turn,
	/// including those eliminated during the voting process, but also the Scapegoat or death loop eliminations.
	/// </summary>
	/// <returns></returns>
	internal IEnumerable<Guid> GetPlayerEliminatedThisVote()
    {
	    var turnNumber = _gameSessionKernel.TurnNumber;
		var logEntries = _gameSessionKernel.FindLogEntries<PlayerEliminatedLogEntry>(NumberRangeConstraint.Exact(turnNumber), phase: GamePhase.Day);

        IEnumerable<Guid> playerIds = logEntries.Select(log => log.PlayerId);

        return playerIds;
    }

    internal List<MainRoleType> GetUnassignedRoles()
    {
        var assignedRoles = _gameSessionKernel.GetIPlayers()
            .Select(p => p.State.MainRole)
            .Where(r => r.HasValue)
            .Select(r => r!.Value)
            .ToList();
        var unassignedRoles = _gameSessionKernel.GetRolesInPlay().ToList();
        foreach (var role in assignedRoles)
        {
            unassignedRoles.Remove(role);
        }
        return unassignedRoles;
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

    internal void ApplyStatusEffect(StatusEffectTypes effectType, Guid playerId)
    {
        var entry = new StatusEffectLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            PlayerId = playerId,
            EffectType = effectType
        };
        _gameSessionKernel.AddEntryAndUpdateState(entry);
	}

    /// <summary>
    /// Gets the player ID that was voted for in the most recent vote of the current turn.
    /// Returns null if the vote was a tie (Guid.Empty in the log).
    /// </summary>
    internal Guid? GetCurrentVoteTarget()
    {
        var turnNumber = _gameSessionKernel.TurnNumber;
        var voteEntry = _gameSessionKernel.FindLogEntries<VoteOutcomeReportedLogEntry>(
            NumberRangeConstraint.Exact(turnNumber))
            .LastOrDefault();

        if (voteEntry == null || voteEntry.ReportedOutcomePlayerId == Guid.Empty)
            return null;

        return voteEntry.ReportedOutcomePlayerId;
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

    internal void VictoryConditionMet(Team winningTeam, string description)
    {
        var entry = new VictoryConditionMetLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            WinningTeam = winningTeam,
            ConditionDescription = description
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
