using System.Text.Json;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.StateModels.Core
{
	internal sealed partial class GameSessionKernel
	{
		private readonly Dictionary<Guid, Player> _players = new();
		private readonly List<Guid> _playerSeatingOrder = new();
		private readonly List<MainRoleType> _rolesInPlay = new();
		private readonly IStateChangeObserver? _stateChangeObserver;

		/// <summary>
		/// Session-scoped cache of listener instances. Created on-demand via factories, lives for the session lifetime.
		/// This ensures each game session has fresh listener instances with clean state machines.
		/// </summary>
		private readonly Dictionary<ListenerIdentifier, object> _listenerInstanceCache = new();

		internal Dictionary<ListenerIdentifier, object> ListenerInstanceCache => _listenerInstanceCache;

		// Private canonical state - the single source of truth
		private readonly GameLogManager _gameHistoryLog = new();
		// Transient execution state
		private GamePhaseStateCache _phaseStateCache = new();

		internal Guid Id { get; }

		internal IGamePhaseStateCache PhaseStateCache => _phaseStateCache;

		internal IEnumerable<TLogEntry> FindLogEntries<TLogEntry>
			(NumberRangeConstraint? turnIntervalConstraint = null, GamePhase? phase = null, Func<TLogEntry, bool>? filter = null) where TLogEntry : GameLogEntryBase 
			=> _gameHistoryLog.FindLogEntries(turnIntervalConstraint ?? NumberRangeConstraint.Any, phase, filter);
		internal IReadOnlyList<Guid> GetPlayerSeatingOrder() => _playerSeatingOrder.AsReadOnly();
		internal IReadOnlyList<MainRoleType> GetRolesInPlay() => _rolesInPlay.AsReadOnly();
		internal IReadOnlyList<GameLogEntryBase> GetAllLogEntries() => _gameHistoryLog.GetAllLogEntries();

		private int _turnNumber;
		internal int TurnNumber => _turnNumber;

		private ModeratorInstruction? _pendingModeratorInstruction = null;
		
		internal ModeratorInstruction? PendingModeratorInstruction => _pendingModeratorInstruction;
		internal GamePhase CurrentPhase => _phaseStateCache.GetCurrentPhase();

		internal GameSessionKernel(Guid id, ModeratorInstruction initialInstruction, GameSessionConfig config, IStateChangeObserver? stateChangeObserver = null)
		{
			Id = id;

			_pendingModeratorInstruction = initialInstruction;
			config.EnforceValidity();

			foreach (var name in config.Players)
			{
				var player = new Player(name);
				_players.Add(player.Id, player);

				//TODO: add seating order input logic
				_playerSeatingOrder.Add(player.Id);
			}

			_rolesInPlay = new List<MainRoleType>(config.Roles);
			_phaseStateCache = new GamePhaseStateCache(GamePhase.Night);
			_turnNumber = 1;

			_stateChangeObserver = stateChangeObserver;
			_stateChangeObserver?.OnPendingInstructionChanged(initialInstruction);
			_stateChangeObserver?.OnMainPhaseChanged(GamePhase.Night);
			_stateChangeObserver?.OnTurnNumberChanged(1);
		}

		internal void AddEntryAndUpdateState(GameLogEntryBase entry)
		{
			entry.Apply(new SessionMutator(this));
		}

		internal void TransitionSubPhase(Enum subPhase)
		{
			_phaseStateCache.TransitionSubPhase(subPhase);
			_stateChangeObserver?.OnSubPhaseChanged(subPhase.ToString());
		}

		internal void StartSubPhaseStage(string subPhaseStage)
		{
			_phaseStateCache.StartSubPhaseStage(subPhaseStage);
			_stateChangeObserver?.OnSubPhaseStageChanged(subPhaseStage);
		}

		internal void CompleteSubPhaseStage()
		{
			_phaseStateCache.CompleteSubPhaseStage();
			_stateChangeObserver?.OnSubPhaseStageChanged(null);
		}

		internal void TransitionListenerAndState(ListenerIdentifier listener, string state)
		{
			_phaseStateCache.TransitionListenerAndState(listener, state);
			_stateChangeObserver?.OnListenerChanged(listener, state);
		}

		internal void ClearCurrentListener()
		{
			_phaseStateCache.ClearCurrentListener();
			_stateChangeObserver?.OnListenerChanged(null, null);
		}

		internal IPlayer GetIPlayer(Guid playerId) => GetPlayer(playerId);

		internal IEnumerable<IPlayer> GetIPlayers() => _players.Values;

		private Player GetPlayer(Guid playerId)
		{
			if (!_players.TryGetValue(playerId, out var player))
			{
				throw new KeyNotFoundException($"Player with ID {playerId} not found.");
			}

			return player;
		}

		private PlayerState GetMutablePlayerState(SessionMutator.IStateMutatorKey key, Guid playerId) => GetPlayer(playerId).GetMutableState(key);
		private void IncrementTurnNumber(SessionMutator.IStateMutatorKey key) => _turnNumber++;
		internal void SetPendingModeratorInstruction(ModeratorInstruction instruction)
		{
			_pendingModeratorInstruction = instruction;
			_stateChangeObserver?.OnPendingInstructionChanged(instruction);
		}

		#region Serialization

		private static readonly JsonSerializerOptions SerializationOptions = new()
		{
			Converters =
			{
				new GameLogEntryConverter(),
				new ModeratorInstructionConverter(),
				new JsonStringEnumConverter()
			},
			WriteIndented = false
		};

		internal string Serialize()
		{
			var dto = new GameSessionDto
			{
				Id = Id,
				TurnNumber = _turnNumber,
				SeatingOrder = _playerSeatingOrder.ToList(),
				RolesInPlay = _rolesInPlay.ToList(),
				PendingInstruction = _pendingModeratorInstruction,
				GameHistoryLog = _gameHistoryLog.GetAllLogEntries().ToList(),
				PhaseStateCache = _phaseStateCache.ToDto(),
				Players = GetIPlayers().Select(p => new PlayerDto
				{
					Id = p.Id,
					Name = p.Name,
					MainRole = p.State.MainRole,
					ActiveEffects = ((PlayerState)p.State).ActiveEffects,
					Health = p.State.Health
				}).ToList()
			};

			return JsonSerializer.Serialize(dto, SerializationOptions);
		}

		public static GameSessionKernel Deserialize(string json)
		{
			var dto = JsonSerializer.Deserialize<GameSessionDto>(json, SerializationOptions)
				?? throw new InvalidOperationException("Failed to deserialize game session");

			return new GameSessionKernel(dto);
		}

		/// <summary>
		/// Private constructor for deserialization
		/// </summary>
		private GameSessionKernel(GameSessionDto dto)
		{
			Id = dto.Id;
			_turnNumber = dto.TurnNumber;
			_playerSeatingOrder = dto.SeatingOrder;
			_rolesInPlay = dto.RolesInPlay;
			_pendingModeratorInstruction = dto.PendingInstruction;
			_phaseStateCache = GamePhaseStateCache.FromDto(dto.PhaseStateCache);

			foreach (var playerDto in dto.Players)
			{
				var player = new Player(playerDto.Name, playerDto.Id);
				var mutableState = player.GetMutableState(new DeserializationKey());
				mutableState.MainRole = playerDto.MainRole;
				mutableState.ActiveEffects = playerDto.ActiveEffects;
				mutableState.Health = playerDto.Health;
				_players.Add(player.Id, player);
			}

			// Restore log entries (already deserialized, just store them)
			foreach (var entry in dto.GameHistoryLog)
			{
				_gameHistoryLog.RestoreLogEntry(entry);
			}
		}

		/// <summary>
		/// Special key used only during deserialization to access mutable state
		/// </summary>
		private class DeserializationKey : SessionMutator.IStateMutatorKey { }

		#endregion
	}
}
