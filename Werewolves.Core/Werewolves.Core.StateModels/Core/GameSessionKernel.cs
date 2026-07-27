using System.Text.Json;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
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
		private GameSessionDto? _recoveryBoundary;
		private AcceptedObservationRecoveryCursor? _acceptedObservationRecoveryCursor;

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
		internal AcceptedObservationRecoveryCursor? AcceptedObservationRecoveryCursor =>
			_acceptedObservationRecoveryCursor;
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
			CaptureRecoveryBoundary();
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

		internal void RestoreTransientContinuation(
			string activeSubPhaseStage,
			ListenerIdentifier listener,
			string listenerState) =>
			_phaseStateCache.RestoreTransientContinuation(
				activeSubPhaseStage,
				listener,
				listenerState);

		internal void ClearCurrentListener()
		{
			_phaseStateCache.ClearCurrentListener();
			_stateChangeObserver?.OnListenerChanged(null, null);
		}

		internal IPlayer GetIPlayer(Guid playerId) => GetPlayer(playerId);

		internal IEnumerable<IPlayer> GetIPlayers() =>
			_playerSeatingOrder.Select(playerId => (IPlayer)GetPlayer(playerId));

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
			return JsonSerializer.Serialize(_recoveryBoundary ?? CreateDto(), SerializationOptions);
		}

		internal void CaptureRecoveryBoundary(
			AcceptedObservationRecoveryCursor? acceptedObservationRecoveryCursor = null)
		{
			_acceptedObservationRecoveryCursor = acceptedObservationRecoveryCursor;
			_recoveryBoundary = CreateDto();
			ValidateAcceptedObservationRecoveryCursor(
				_recoveryBoundary,
				_pendingModeratorInstruction);
		}

		private GameSessionDto CreateDto()
		{
			return new GameSessionDto
			{
				Id = Id,
				TurnNumber = _turnNumber,
				RoleFactSchemaVersion = RoleFactSchema.CurrentVersion,
				IsStableRecoveryBoundary = true,
				SeatingOrder = _playerSeatingOrder.ToList(),
				RolesInPlay = _rolesInPlay.ToList(),
				PendingInstruction = _pendingModeratorInstruction,
				PendingInstructionSemantic = _pendingModeratorInstruction?.Semantic,
				GameHistoryLog = _gameHistoryLog.GetAllLogEntries().ToList(),
				AcceptedObservationRecoveryCursor = _acceptedObservationRecoveryCursor,
				PhaseStateCache = _phaseStateCache.ToDto(),
				Players = GetIPlayers().Select(p => new PlayerDto
				{
					Id = p.Id,
					Name = p.Name,
					MainRole = p.State.MainRole,
					PhysicalCharacterCardRole = p.State.PhysicalCharacterCardRole,
					ModeratorKnownRole = p.State.ModeratorKnownRole,
					PubliclyRevealedRole = p.State.PubliclyRevealedRole,
					ActiveEffects = ((PlayerState)p.State).ActiveEffects,
					Health = p.State.Health
				}).ToList()
			};
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
			_pendingModeratorInstruction = RestorePendingInstructionSemantic(dto);
			ValidateRoleFactSchemaVersion(dto.RoleFactSchemaVersion);
			_acceptedObservationRecoveryCursor =
				ValidateAcceptedObservationRecoveryCursor(
					dto,
					_pendingModeratorInstruction);
			_phaseStateCache = dto.IsStableRecoveryBoundary
				? GamePhaseStateCache.FromStableRecoveryBoundaryDto(dto.PhaseStateCache)
				: GamePhaseStateCache.FromDto(dto.PhaseStateCache);

			foreach (var playerDto in dto.Players)
			{
				var player = new Player(playerDto.Name, playerDto.Id);
				var mutableState = player.GetMutableState(new DeserializationKey());
				mutableState.MainRole = playerDto.MainRole;
				mutableState.PhysicalCharacterCardRole = playerDto.PhysicalCharacterCardRole;
				mutableState.ModeratorKnownRole = dto.RoleFactSchemaVersion ==
					RoleFactSchema.LegacyVersion
						? playerDto.ModeratorKnownRole ?? playerDto.MainRole
						: playerDto.ModeratorKnownRole;
				mutableState.PubliclyRevealedRole = playerDto.PubliclyRevealedRole;
				mutableState.ActiveEffects = playerDto.ActiveEffects;
				mutableState.Health = playerDto.Health;
				_players.Add(player.Id, player);
			}

			// Restore log entries (already deserialized, just store them)
			foreach (var entry in dto.GameHistoryLog)
			{
				_gameHistoryLog.RestoreLogEntry(entry);
			}

			_recoveryBoundary = CreateDto();
		}

		private static void ValidateRoleFactSchemaVersion(int version)
		{
			if (version is not
				RoleFactSchema.LegacyVersion and not
				RoleFactSchema.CurrentVersion)
			{
				throw new InvalidOperationException(
					$"Unsupported Role fact schema version '{version}'.");
			}
		}

		private static ModeratorInstruction? RestorePendingInstructionSemantic(
			GameSessionDto dto)
		{
			var semantic = dto.PendingInstructionSemantic;
			if (semantic == null)
			{
				return dto.PendingInstruction;
			}

			if (!Enum.IsDefined(semantic.Value))
			{
				throw new InvalidOperationException(
					$"Unsupported Pending Instruction Semantic '{semantic.Value}'.");
			}

			if (dto.PendingInstruction == null)
			{
				throw new InvalidOperationException(
					"A Pending Instruction Semantic requires a Pending Instruction.");
			}

			return dto.PendingInstruction.WithSemantic(semantic.Value);
		}

		private static AcceptedObservationRecoveryCursor?
			ValidateAcceptedObservationRecoveryCursor(
				GameSessionDto dto,
				ModeratorInstruction? pendingModeratorInstruction)
		{
			var cursor = dto.AcceptedObservationRecoveryCursor;
			if (cursor == null)
			{
				return null;
			}

			if (!dto.IsStableRecoveryBoundary)
			{
				throw new InvalidOperationException(
					"Accepted observation recovery cursors require a stable recovery boundary.");
			}

			if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion)
			{
				throw new InvalidOperationException(
					$"Unsupported accepted observation recovery cursor version '{cursor.Version}'.");
			}

			if (!Enum.IsDefined(cursor.AcceptedObservationSemantic) ||
				!Enum.IsDefined(cursor.ObservedRole) ||
				!Enum.IsDefined(cursor.NextInstructionSemantic) ||
				cursor.NextInstructionSemantic ==
					ModeratorInstructionSemantic.Unspecified ||
				cursor.NextInstructionId == Guid.Empty)
			{
				throw new InvalidOperationException(
					"The accepted observation recovery cursor is structurally invalid.");
			}

			if (cursor.AcceptedObservationSemantic !=
				ModeratorInstructionSemantic.IdentifyRoleHolders)
			{
				throw new InvalidOperationException(
					$"Unsupported accepted observation semantic '{cursor.AcceptedObservationSemantic}'.");
			}

			if (pendingModeratorInstruction is not
					SelectPlayersInstruction pendingInstruction ||
				pendingInstruction.InstructionId != cursor.NextInstructionId ||
				pendingInstruction.RoleIdentification != null)
			{
				throw new InvalidOperationException(
					"The accepted observation recovery cursor does not match its Pending Instruction.");
			}

			if (pendingInstruction.Semantic != cursor.NextInstructionSemantic)
			{
				throw new InvalidOperationException(
					"The Pending Instruction Semantic does not match the accepted observation recovery cursor.");
			}

			var observedPlayerIds = pendingInstruction.AffectedPlayerIds?.ToHashSet();
			if (observedPlayerIds == null ||
				!dto.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
					.Any(entry =>
						entry.Role == cursor.ObservedRole &&
						entry.PlayerIds.SetEquals(observedPlayerIds)))
			{
				throw new InvalidOperationException(
					"The accepted observation recovery cursor does not match a committed Role Identification.");
			}

			return cursor;
		}

		/// <summary>
		/// Special key used only during deserialization to access mutable state
		/// </summary>
		private class DeserializationKey : SessionMutator.IStateMutatorKey { }

		#endregion
	}
}
