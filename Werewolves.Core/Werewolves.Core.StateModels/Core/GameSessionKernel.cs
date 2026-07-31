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
		private readonly RoleLockIn _roleLockIn;
		private readonly Dictionary<Guid, PhysicalCharacterCardState> _physicalCardStates = new();
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
		private DomainRecoveryCursor? _domainRecoveryCursor;

		internal Guid Id { get; }

		internal IGamePhaseStateCache PhaseStateCache => _phaseStateCache;

		internal IEnumerable<TLogEntry> FindLogEntries<TLogEntry>
			(NumberRangeConstraint? turnIntervalConstraint = null, GamePhase? phase = null, Func<TLogEntry, bool>? filter = null) where TLogEntry : GameLogEntryBase 
			=> _gameHistoryLog.FindLogEntries(turnIntervalConstraint ?? NumberRangeConstraint.Any, phase, filter);
		internal IReadOnlyList<Guid> GetPlayerSeatingOrder() => _playerSeatingOrder.AsReadOnly();
		internal IReadOnlyList<MainRoleType> GetRolesInPlay() => _rolesInPlay.AsReadOnly();
		internal RoleLockIn GetRoleLockIn() => _roleLockIn;
		internal IReadOnlyList<PhysicalCharacterCardState> GetPhysicalCharacterCardStates() =>
			_roleLockIn.RoleComposition
				.Select(card => _physicalCardStates[card.Id])
				.ToArray();
		internal IReadOnlyList<GameLogEntryBase> GetAllLogEntries() => _gameHistoryLog.GetAllLogEntries();

		private int _turnNumber;
		internal int TurnNumber => _turnNumber;

		private ModeratorInstruction? _pendingModeratorInstruction = null;
		
		internal ModeratorInstruction? PendingModeratorInstruction => _pendingModeratorInstruction;
		internal AcceptedObservationRecoveryCursor? AcceptedObservationRecoveryCursor =>
			_acceptedObservationRecoveryCursor;
		internal DomainRecoveryCursor? DomainRecoveryCursor =>
			_domainRecoveryCursor;
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
			_roleLockIn = config.RoleLockIn;
			foreach (var card in _roleLockIn.DealPool)
			{
				_physicalCardStates.Add(
					card.Id,
					new PhysicalCharacterCardState(
						card,
						PhysicalCharacterCardZone.DealPool,
						OwnerPlayerId: null));
			}
			if (_roleLockIn.Offer1 is { } offer1)
			{
				_physicalCardStates.Add(
					offer1.Id,
					new PhysicalCharacterCardState(
						offer1,
						PhysicalCharacterCardZone.Offer1,
						OwnerPlayerId: null));
			}
			if (_roleLockIn.Offer2 is { } offer2)
			{
				_physicalCardStates.Add(
					offer2.Id,
					new PhysicalCharacterCardState(
						offer2,
						PhysicalCharacterCardZone.Offer2,
						OwnerPlayerId: null));
			}
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
				_gameHistoryLog.PreflightLogEntry(entry, _players.Keys);
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
				new GameResultConverter(),
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
				AcceptedObservationRecoveryCursor? acceptedObservationRecoveryCursor = null,
				DomainRecoveryCursor? domainRecoveryCursor = null)
		{
			var candidateBoundary = CreateDto();
			candidateBoundary.AcceptedObservationRecoveryCursor =
				acceptedObservationRecoveryCursor;
			candidateBoundary.DomainRecoveryCursor = domainRecoveryCursor;
			ValidateAcceptedObservationRecoveryCursor(
				candidateBoundary,
				_pendingModeratorInstruction);
			ValidateDomainRecoveryCursor(
				candidateBoundary,
				_pendingModeratorInstruction);

			_acceptedObservationRecoveryCursor = acceptedObservationRecoveryCursor;
			_domainRecoveryCursor = domainRecoveryCursor;
				_recoveryBoundary = candidateBoundary;
			}

			internal void NormalizeLegacyRecurringRolePowerCommit(
				NightActionType actionType,
				Guid targetId,
				RolePowerInstanceIdentity powerIdentity)
			{
				if (!Enum.IsDefined(actionType) ||
				    actionType == NightActionType.Unknown ||
				    targetId == Guid.Empty ||
				    !powerIdentity.IsValid)
				{
					throw new InvalidOperationException(
						"The legacy recurring Role Power normalization request is structurally invalid.");
				}

				_gameHistoryLog.NormalizeLegacyRecurringRolePowerCommit(
					actionType,
					targetId,
					powerIdentity,
					CurrentPhase,
					TurnNumber);
				_recoveryBoundary = CreateDto();
			}

			private GameSessionDto CreateDto()
		{
			return new GameSessionDto
			{
				Id = Id,
					TurnNumber = _turnNumber,
					RoleFactSchemaVersion = RoleFactSchema.CurrentVersion,
					FactionFactSchemaVersion = FactionFactSchema.CurrentVersion,
					IsStableRecoveryBoundary = true,
				SeatingOrder = _playerSeatingOrder.ToList(),
				RolesInPlay = _rolesInPlay.ToList(),
				RoleLockIn = RoleLockInDto.FromValue(_roleLockIn),
				PhysicalCharacterCards = GetPhysicalCharacterCardStates()
					.Select(state => new PhysicalCharacterCardStateDto
					{
						CardId = state.Card.Id,
						Zone = state.Zone,
						OwnerPlayerId = state.OwnerPlayerId
					})
					.ToList(),
				PendingInstruction = _pendingModeratorInstruction,
				PendingInstructionSemantic = _pendingModeratorInstruction?.Semantic,
				GameHistoryLog = _gameHistoryLog.GetAllLogEntries().ToList(),
					AcceptedObservationRecoveryCursor = _acceptedObservationRecoveryCursor,
					DomainRecoveryCursor = _domainRecoveryCursor,
				PhaseStateCache = _phaseStateCache.ToDto(),
				Players = GetIPlayers().Select(p => new PlayerDto
				{
					Id = p.Id,
					Name = p.Name,
					MainRole = p.State.MainRole,
					PhysicalCharacterCardId = p.State.PhysicalCharacterCardId,
					PhysicalCharacterCardRole = p.State.PhysicalCharacterCardRole,
					ModeratorKnownRole = p.State.ModeratorKnownRole,
						PubliclyRevealedRole = p.State.PubliclyRevealedRole,
							ActiveEffects = ((PlayerState)p.State).ActiveEffects,
								Health = p.State.Health,
								HasVotingRight = p.State.HasVotingRight,
								DurableVotingPower = p.State.DurableVotingPower,
							FactionBeneficiary = p.State.FactionBeneficiary,
							FactionAgentKnowledge = Enum.GetValues<Faction>()
								.ToDictionary(
									faction => faction,
									faction => p.State
										.GetFactionAgentKnowledge(faction))
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
			_roleLockIn = dto.RoleLockIn?.ToValue()
				?? throw new InvalidOperationException(
					"The stable recovery snapshot is missing Role Lock-In.");
			var cardsById = _roleLockIn.RoleComposition
				.ToDictionary(card => card.Id);
			if (dto.PhysicalCharacterCards.Count != cardsById.Count ||
				dto.PhysicalCharacterCards.Select(state => state.CardId)
					.Distinct().Count() != cardsById.Count)
			{
				throw new InvalidOperationException(
					"The stable recovery snapshot has an invalid Physical Character Card zone projection.");
			}
			foreach (var stateDto in dto.PhysicalCharacterCards)
			{
				if (!cardsById.TryGetValue(stateDto.CardId, out var card) ||
					!Enum.IsDefined(stateDto.Zone) ||
					(stateDto.Zone == PhysicalCharacterCardZone.PlayerOwned) !=
						stateDto.OwnerPlayerId.HasValue)
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has an invalid Physical Character Card zone projection.");
				}

				_physicalCardStates.Add(
					card.Id,
					new PhysicalCharacterCardState(
						card,
						stateDto.Zone,
						stateDto.OwnerPlayerId));
			}
				_pendingModeratorInstruction = RestorePendingInstructionSemantic(dto);
				ValidateRoleFactSchemaVersion(dto.RoleFactSchemaVersion);
				ValidateFactionFactSchemaVersion(dto.FactionFactSchemaVersion);
			_acceptedObservationRecoveryCursor =
				ValidateAcceptedObservationRecoveryCursor(
					dto,
					_pendingModeratorInstruction);
			_domainRecoveryCursor = ValidateDomainRecoveryCursor(
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
				mutableState.PhysicalCharacterCardId = playerDto.PhysicalCharacterCardId;
				mutableState.PhysicalCharacterCardRole = playerDto.PhysicalCharacterCardRole;
				mutableState.ModeratorKnownRole = dto.RoleFactSchemaVersion ==
					RoleFactSchema.LegacyVersion
						? playerDto.ModeratorKnownRole ?? playerDto.MainRole
						: playerDto.ModeratorKnownRole;
				mutableState.PubliclyRevealedRole = playerDto.PubliclyRevealedRole;
					mutableState.ActiveEffects = playerDto.ActiveEffects;
						mutableState.Health = playerDto.Health;
						mutableState.HasVotingRight = playerDto.HasVotingRight ?? true;
						if (playerDto.DurableVotingPower < 0)
						{
							throw new InvalidOperationException(
								"Durable Voting Power cannot be negative.");
						}
						mutableState.DurableVotingPower =
							playerDto.DurableVotingPower;
					var (beneficiary, agents) =
						ValidatePlayerFactionState(playerDto);
					mutableState.ReplaceFactionProjection(
						beneficiary,
						agents);
					_players.Add(player.Id, player);
			}

			// Restore log entries (already deserialized, just store them)
			foreach (var entry in dto.GameHistoryLog)
			{
					_gameHistoryLog.RestoreLogEntry(entry, _players.Keys);
				}

				ValidatePhysicalCharacterCardProjectionMatchesHistory();
				ValidateFactionProjectionMatchesHistory();
				ValidateLoversPairProjectionMatchesHistory();

				_recoveryBoundary = CreateDto();
			}

			private static (
				FactionBeneficiaryKnowledge Beneficiary,
				IReadOnlyDictionary<Faction, FactionAgentKnowledge> Agents)
				ValidatePlayerFactionState(PlayerDto playerDto)
			{
				var beneficiary = playerDto.FactionBeneficiary
					?? throw new InvalidOperationException(
						"Current Faction state is missing.");
				var agents = playerDto.FactionAgentKnowledge
					?? throw new InvalidOperationException(
						"Current Faction state is missing.");
				var factions = Enum.GetValues<Faction>();
				if (agents.Count != factions.Length
					|| agents.Keys.Any(faction => !Enum.IsDefined(faction))
					|| factions.Any(faction =>
						!agents.TryGetValue(faction, out var knowledge)
						|| !Enum.IsDefined(knowledge)))
				{
					throw new InvalidOperationException(
						"Current Faction state is structurally invalid.");
				}

				return (
					beneficiary,
					new Dictionary<Faction, FactionAgentKnowledge>(agents));
			}

			private void ValidatePhysicalCharacterCardProjectionMatchesHistory()
			{
				var playerIds = _playerSeatingOrder.ToHashSet();
				var projected = CreateInitialPhysicalCharacterCardProjection();
				foreach (var entry in _gameHistoryLog.GetAllLogEntries())
				{
					switch (entry)
					{
						case PhysicalCharacterCardOwnershipObservedLogEntry ownership:
							ApplyOwnershipObservation(projected, playerIds, ownership);
							break;
						case VillagerVillagerPublicFromDealLogEntry villagerVillager:
							ApplyOwnershipObservation(
								projected,
								playerIds,
								villagerVillager.RoleLockInVersion,
								villagerVillager.PlayerId,
								villagerVillager.CardId,
								MainRoleType.VillagerVillager);
							break;
						case PermanentRoleSwapCommittedLogEntry swap:
							ApplyPermanentRoleSwapProjection(projected, playerIds, swap);
							break;
					}
				}

				if (projected.Count != _physicalCardStates.Count ||
					projected.Any(pair =>
						!_physicalCardStates.TryGetValue(pair.Key, out var actual) ||
						actual != pair.Value))
				{
					throw new InvalidOperationException(
						"Physical Character Card ownership does not match committed history.");
				}

				var ownedCardsByPlayer = projected.Values
					.Where(state => state.Zone == PhysicalCharacterCardZone.PlayerOwned)
					.GroupBy(state => state.OwnerPlayerId!.Value)
					.ToDictionary(group => group.Key, group => group.ToArray());
				if (ownedCardsByPlayer.Keys.Any(ownerId => !playerIds.Contains(ownerId)) ||
					ownedCardsByPlayer.Values.Any(cards => cards.Length != 1))
				{
					throw new InvalidOperationException(
						"Physical Character Card ownership does not match the Player roster.");
				}
				foreach (var playerId in _playerSeatingOrder)
				{
					var playerState = GetPlayer(playerId).GetMutableState(
						new DeserializationKey());
					var ownedCard = ownedCardsByPlayer.GetValueOrDefault(playerId)?
						.Single();
					if (playerState.PhysicalCharacterCardId != ownedCard?.Card.Id ||
						playerState.PhysicalCharacterCardRole !=
						ownedCard?.Card.PrintedRole)
					{
						throw new InvalidOperationException(
							"Physical Character Card ownership does not match the Player projection.");
					}
				}
			}

			private Dictionary<Guid, PhysicalCharacterCardState>
				CreateInitialPhysicalCharacterCardProjection()
			{
				var projection = _roleLockIn.DealPool.ToDictionary(
					card => card.Id,
					card => new PhysicalCharacterCardState(
						card,
						PhysicalCharacterCardZone.DealPool,
						OwnerPlayerId: null));
				if (_roleLockIn.Offer1 is { } offer1)
				{
					projection.Add(
						offer1.Id,
						new PhysicalCharacterCardState(
							offer1,
							PhysicalCharacterCardZone.Offer1,
							OwnerPlayerId: null));
				}
				if (_roleLockIn.Offer2 is { } offer2)
				{
					projection.Add(
						offer2.Id,
						new PhysicalCharacterCardState(
							offer2,
							PhysicalCharacterCardZone.Offer2,
							OwnerPlayerId: null));
				}

				return projection;
			}

			private void ApplyOwnershipObservation(
				Dictionary<Guid, PhysicalCharacterCardState> projection,
				IReadOnlySet<Guid> playerIds,
				PhysicalCharacterCardOwnershipObservedLogEntry entry) =>
				ApplyOwnershipObservation(
					projection,
					playerIds,
					entry.RoleLockInVersion,
					entry.PlayerId,
					entry.CardId,
					entry.PrintedRole);

			private void ApplyOwnershipObservation(
				Dictionary<Guid, PhysicalCharacterCardState> projection,
				IReadOnlySet<Guid> playerIds,
				long roleLockInVersion,
				Guid playerId,
				Guid cardId,
				MainRoleType printedRole)
			{
				if (roleLockInVersion != _roleLockIn.Version ||
					!playerIds.Contains(playerId) ||
					!projection.TryGetValue(cardId, out var cardState) ||
					cardState.Zone != PhysicalCharacterCardZone.DealPool ||
					cardState.OwnerPlayerId is not null ||
					cardState.Card.PrintedRole != printedRole ||
					projection.Values.Any(state =>
						state.OwnerPlayerId == playerId))
				{
					throw new InvalidOperationException(
						"Physical Character Card ownership history is invalid.");
				}

				projection[cardId] = cardState with
				{
					Zone = PhysicalCharacterCardZone.PlayerOwned,
					OwnerPlayerId = playerId
				};
			}

			private void ApplyPermanentRoleSwapProjection(
				Dictionary<Guid, PhysicalCharacterCardState> projection,
				IReadOnlySet<Guid> playerIds,
				PermanentRoleSwapCommittedLogEntry entry)
			{
				var movement = entry.PhysicalCards;
				var expectedAcquiredOwnerId =
					movement.ExpectedAcquiredCardOwnerPlayerId;
				if (entry.RoleLockInVersion != _roleLockIn.Version ||
					!playerIds.Contains(entry.PlayerId) ||
					!projection.TryGetValue(movement.OutgoingOwnedCardId, out var outgoing) ||
					outgoing is not
					{
						Zone: PhysicalCharacterCardZone.PlayerOwned,
						OwnerPlayerId: var outgoingOwnerId
					} ||
					outgoingOwnerId != entry.PlayerId ||
					!projection.TryGetValue(movement.AcquiredCardId, out var acquired) ||
					!IsExpectedAcquiredCardState(acquired, expectedAcquiredOwnerId) ||
					acquired.Card.PrintedRole != entry.NewCurrentRole ||
					expectedAcquiredOwnerId == entry.PlayerId ||
					expectedAcquiredOwnerId is { } ownerId &&
						!playerIds.Contains(ownerId))
				{
					throw new InvalidOperationException(
						"Permanent Role Swap physical-card history is invalid.");
				}

				foreach (var cardId in movement.AdditionalSetAsideCardIds)
				{
					if (!projection.TryGetValue(cardId, out var cardState) ||
						cardState.OwnerPlayerId is not null ||
						cardState.Zone is PhysicalCharacterCardZone.PlayerOwned or
							PhysicalCharacterCardZone.SetAside)
					{
						throw new InvalidOperationException(
							"Permanent Role Swap physical-card history is invalid.");
					}
					projection[cardId] = cardState with
					{
						Zone = PhysicalCharacterCardZone.SetAside,
						OwnerPlayerId = null
					};
				}

				projection[movement.OutgoingOwnedCardId] = outgoing with
				{
					Zone = PhysicalCharacterCardZone.SetAside,
					OwnerPlayerId = null
				};
				projection[movement.AcquiredCardId] = acquired with
				{
					Zone = PhysicalCharacterCardZone.PlayerOwned,
					OwnerPlayerId = entry.PlayerId
				};

				static bool IsExpectedAcquiredCardState(
					PhysicalCharacterCardState acquired,
					Guid? expectedOwnerId) =>
					expectedOwnerId is { } ownerId
						? acquired is
						{
							Zone: PhysicalCharacterCardZone.PlayerOwned,
							OwnerPlayerId: var actualOwnerId
						} && actualOwnerId == ownerId
						: acquired.OwnerPlayerId is null &&
							acquired.Zone is PhysicalCharacterCardZone.DealPool or
								PhysicalCharacterCardZone.Offer1 or
								PhysicalCharacterCardZone.Offer2;
			}

			private void ValidateFactionProjectionMatchesHistory()
			{
				var projection = FactionFactProjection.Create(
					_gameHistoryLog
						.GetAllLogEntries()
						.OfType<IFactionFactBatchLogEntry>(),
					_playerSeatingOrder);
				foreach (var playerId in _playerSeatingOrder)
				{
					var state = GetPlayer(playerId).GetMutableState(
						new DeserializationKey());
					if (state.FactionBeneficiary !=
							projection.Beneficiaries[playerId]
						|| Enum.GetValues<Faction>().Any(faction =>
							state.GetFactionAgentKnowledge(faction) !=
							projection.Agents[playerId][faction]))
					{
						throw new InvalidOperationException(
							"Current Faction state does not match committed history.");
					}
				}
			}

			private void ValidateLoversPairProjectionMatchesHistory()
			{
				var history = _gameHistoryLog.GetAllLogEntries().ToList();
				var pair = history
					.OfType<LoversPairCommittedLogEntry>()
					.SingleOrDefault();
				var relationshipCleared = pair is not null && history
					.Skip(history.IndexOf(pair) + 1)
					.OfType<PermanentRoleSwapCommittedLogEntry>()
					.Any(entry =>
						pair.PlayerIds.Contains(entry.PlayerId) &&
						entry.StateChanges.RelationshipEffectsToClear.Contains(
							StatusEffectTypes.Lovers));
				var expectedLoverIds = relationshipCleared
					? []
					: pair?.PlayerIds.ToHashSet() ?? [];

				var actualLoverIds = _playerSeatingOrder
					.Where(playerId =>
						GetPlayer(playerId)
							.GetMutableState(new DeserializationKey())
							.HasStatusEffect(StatusEffectTypes.Lovers))
					.ToHashSet();
				if (!actualLoverIds.SetEquals(expectedLoverIds))
				{
					throw new InvalidOperationException(
						"The current Lovers statuses do not match committed history.");
				}
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

		private static void ValidateFactionFactSchemaVersion(int version)
		{
			if (version != FactionFactSchema.CurrentVersion)
			{
				throw new InvalidOperationException(
					$"Unsupported Faction fact schema version '{version}'.");
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
				(cursor.ContinuationRole.HasValue &&
				 !Enum.IsDefined(cursor.ContinuationRole.Value)) ||
				!Enum.IsDefined(cursor.NextInstructionSemantic) ||
				cursor.NextInstructionSemantic ==
					ModeratorInstructionSemantic.Unspecified ||
				cursor.NextInstructionId == Guid.Empty)
			{
				throw new InvalidOperationException(
					"The accepted observation recovery cursor is structurally invalid.");
			}

			if (pendingModeratorInstruction == null ||
				pendingModeratorInstruction.InstructionId != cursor.NextInstructionId)
			{
				throw new InvalidOperationException(
					"The accepted observation recovery cursor does not match its Pending Instruction.");
			}

			if (pendingModeratorInstruction.Semantic != cursor.NextInstructionSemantic)
			{
				throw new InvalidOperationException(
					"The Pending Instruction Semantic does not match the accepted observation recovery cursor.");
			}

			return cursor;
		}

		private static DomainRecoveryCursor? ValidateDomainRecoveryCursor(
			GameSessionDto dto,
			ModeratorInstruction? pendingModeratorInstruction)
		{
			var cursor = dto.DomainRecoveryCursor;
			if (cursor == null)
			{
				return null;
			}

			if (!dto.IsStableRecoveryBoundary)
			{
				throw new InvalidOperationException(
					"Domain recovery cursors require a stable recovery boundary.");
			}

			if (dto.AcceptedObservationRecoveryCursor != null)
			{
				throw new InvalidOperationException(
					"A domain recovery cursor must supersede an accepted observation cursor.");
			}

			if (cursor.Version != DomainRecoveryCursor.CurrentVersion ||
			    !Enum.IsDefined(cursor.Kind))
			{
				throw new InvalidOperationException(
					$"Unsupported domain recovery cursor '{cursor.Kind}' version '{cursor.Version}'.");
			}

			if (!Enum.IsDefined(cursor.CommittedActionType) ||
			    cursor.CommittedActionType == NightActionType.Unknown ||
                cursor.CommittedTargetIds == null ||
                cursor.Kind ==
                    DomainRecoveryCursorKind.TargetPrivateRolePowerCommit &&
                cursor.CommittedTargetIds.Count != 0 ||
                cursor.Kind !=
                    DomainRecoveryCursorKind.TargetPrivateRolePowerCommit &&
                cursor.CommittedTargetIds.Count == 0 ||
			    cursor.CommittedTargetIds.Any(targetId =>
				    targetId == Guid.Empty) ||
			    cursor.CommittedTargetIds.Distinct().Count() !=
				    cursor.CommittedTargetIds.Count ||
			    !Enum.IsDefined(cursor.NextInstructionSemantic) ||
			    cursor.NextInstructionSemantic ==
				    ModeratorInstructionSemantic.Unspecified ||
			    cursor.NextInstructionId == Guid.Empty)
			{
				throw new InvalidOperationException(
					"The domain recovery cursor is structurally invalid.");
			}

			if (pendingModeratorInstruction == null ||
			    pendingModeratorInstruction.InstructionId != cursor.NextInstructionId ||
			    pendingModeratorInstruction.Semantic !=
				    cursor.NextInstructionSemantic ||
			    pendingModeratorInstruction is SelectPlayersInstruction
			    {
				    RoleIdentification: not null
			    })
			{
				throw new InvalidOperationException(
					"The domain recovery cursor does not match its Pending Instruction.");
			}

			if (cursor.Kind ==
			    DomainRecoveryCursorKind.OneUseRolePowerCommit)
			{
				var cursorResourceIdentity = cursor.ResourceIdentity;
				if (!cursorResourceIdentity.HasValue ||
				    !cursorResourceIdentity.Value.IsValid ||
				    !IsCurrentRolePowerIdentity(
					    dto,
					    cursorResourceIdentity.Value))
				{
					throw new InvalidOperationException(
						"The domain recovery cursor is structurally invalid.");
				}

				var committedEntry = dto.GameHistoryLog
					.OfType<OneUseRolePowerCommittedLogEntry>()
					.LastOrDefault();
				if (committedEntry == null ||
				    committedEntry.ActionType !=
					    cursor.CommittedActionType ||
				    committedEntry.ResourceIdentity !=
					    cursorResourceIdentity.Value ||
				    committedEntry.TargetIds is not { Count: 1 } ||
				    committedEntry.TargetIds[0] !=
					    cursor.CommittedTargetIds.Single())
				{
					throw new InvalidOperationException(
						"The domain recovery cursor does not match the latest committed One-Use Resource action.");
				}

				return cursor;
			}

            if (cursor.Kind ==
                DomainRecoveryCursorKind.TargetPrivateRolePowerCommit)
            {
                if (cursor.PowerIdentity is not { } targetPrivatePowerIdentity ||
					!targetPrivatePowerIdentity.IsValid ||
					!IsCurrentRolePowerIdentity(
						dto,
						targetPrivatePowerIdentity))
                {
                    throw new InvalidOperationException(
                        "The domain recovery cursor is structurally invalid.");
                }

                OneUseRolePowerResourceIdentity? cursorSpentResource = null;
                if (cursor.OneUseResourceId != Guid.Empty)
                {
                    cursorSpentResource = cursor.ResourceIdentity;
                    if (cursorSpentResource is not { IsValid: true })
                    {
                        throw new InvalidOperationException(
                            "The domain recovery cursor is structurally invalid.");
                    }
                }

                var committedEntry = dto.GameHistoryLog
                    .OfType<TargetPrivateRolePowerCommittedLogEntry>()
                    .LastOrDefault();
                if (committedEntry == null ||
                    committedEntry.ActionType !=
                        cursor.CommittedActionType ||
                    committedEntry.CurrentPhase != GamePhase.Night ||
                    committedEntry.TurnNumber != dto.TurnNumber ||
                    committedEntry.TargetIds is { Count: > 0 } ||
                    committedEntry.PowerIdentity !=
                        targetPrivatePowerIdentity ||
                    committedEntry.SpentResourceIdentity !=
                        cursorSpentResource)
                {
                    throw new InvalidOperationException(
                        "The domain recovery cursor does not match the latest target-private Role Power action.");
                }

                return cursor;
            }

			if (cursor.Kind !=
				    DomainRecoveryCursorKind
					    .RecurringNativeRolePowerCommit ||
			    cursor.PowerIdentity is not { } cursorPowerIdentity ||
			    !cursorPowerIdentity.IsValid ||
			    cursor.OneUseResourceId != Guid.Empty)
			{
				throw new InvalidOperationException(
					"The domain recovery cursor is structurally invalid.");
			}

			if (cursor.SourceRole == MainRoleType.Cupid ||
			    cursor.CommittedActionType == NightActionType.CupidLink)
			{
				var latestLoversPair = dto.GameHistoryLog
					.OfType<LoversPairCommittedLogEntry>()
					.LastOrDefault();
				if (cursor.SourceRole != MainRoleType.Cupid ||
				    cursor.CommittedActionType != NightActionType.CupidLink ||
				    cursor.CommittedTargetIds.Count != 2 ||
				    latestLoversPair is null ||
				    latestLoversPair.TurnNumber != dto.TurnNumber ||
				    latestLoversPair.CurrentPhase != GamePhase.Night ||
				    latestLoversPair.PowerIdentity != cursorPowerIdentity ||
				    !latestLoversPair.PlayerIds.SequenceEqual(
					    cursor.CommittedTargetIds))
				{
					throw new InvalidOperationException(
						"The domain recovery cursor does not match the private Lovers pair commitment.");
				}

				return cursor;
			}

			var latestActionEntry = dto.GameHistoryLog
				.OfType<NightActionLogEntry>()
				.LastOrDefault(entry =>
					entry.ActionType == cursor.CommittedActionType);
			var matchesRecurringCommit =
				IsCurrentRolePowerIdentity(dto, cursorPowerIdentity) &&
				latestActionEntry is RecurringRolePowerCommittedLogEntry
				{
					CurrentPhase: GamePhase.Night,
					TargetIds: { Count: > 0 } targetIds
				} recurringEntry &&
				recurringEntry.TurnNumber == dto.TurnNumber &&
				recurringEntry.PowerIdentity == cursorPowerIdentity &&
				targetIds.SequenceEqual(cursor.CommittedTargetIds);
			var matchesLegacyAction =
				cursorPowerIdentity.PowerInstanceOrigin ==
					RolePowerInstanceOrigin.Native &&
				cursorPowerIdentity.PowerInstanceId ==
					cursorPowerIdentity.ActingPlayerId &&
				cursor.CommittedTargetIds.Count == 1 &&
				latestActionEntry?.GetType() ==
					typeof(NightActionLogEntry) &&
				latestActionEntry.CurrentPhase == GamePhase.Night &&
				latestActionEntry.TurnNumber == dto.TurnNumber &&
				latestActionEntry.TargetIds is
					{ Count: 1 } legacyTargetIds &&
				legacyTargetIds[0] ==
					cursor.CommittedTargetIds.Single();
			if (matchesLegacyAction &&
			    (pendingModeratorInstruction?.AffectedPlayerIds is not
				     { Count: 1 } ownerAffectedPlayerIds ||
			     ownerAffectedPlayerIds[0] !=
				     cursorPowerIdentity.ActingPlayerId))
			{
				throw new InvalidOperationException(
					"The legacy recurring Role Power commit does not match its Pending Instruction owner.");
			}

			var matchesLegacyCommit =
				matchesLegacyAction &&
				pendingModeratorInstruction?.AffectedPlayerIds is
					{ Count: 1 } legacyAffectedPlayerIds &&
				legacyAffectedPlayerIds[0] ==
					cursorPowerIdentity.ActingPlayerId;
			if (!matchesRecurringCommit && !matchesLegacyCommit)
			{
				throw new InvalidOperationException(
					"The domain recovery cursor does not match the latest recurring native Role Power action.");
			}

			return cursor;
			}

		private static bool IsCurrentRolePowerIdentity(
			GameSessionDto dto,
			OneUseRolePowerResourceIdentity resourceIdentity) =>
			IsCurrentRolePowerIdentity(
				dto,
				new RolePowerInstanceIdentity(
					resourceIdentity.ActingPlayerId,
					resourceIdentity.SourceRole,
					resourceIdentity.SourcePowerIdentifier,
					resourceIdentity.PowerInstanceId,
					resourceIdentity.PowerInstanceOrigin));

		private static bool IsCurrentRolePowerIdentity(
			GameSessionDto dto,
			RolePowerInstanceIdentity identity)
		{
			var player = dto.Players.SingleOrDefault(candidate =>
				candidate.Id == identity.ActingPlayerId);
			if (player?.MainRole != identity.SourceRole)
			{
				return false;
			}

			var latestSwap = dto.GameHistoryLog
				.OfType<PermanentRoleSwapCommittedLogEntry>()
				.LastOrDefault(entry =>
					entry.PlayerId == identity.ActingPlayerId);
			return latestSwap is null
				? identity.PowerInstanceOrigin == RolePowerInstanceOrigin.Native &&
				  identity.PowerInstanceId == identity.ActingPlayerId
				: latestSwap.NewCurrentRole == identity.SourceRole &&
				  identity.PowerInstanceOrigin == RolePowerInstanceOrigin.Swapped &&
				  identity.PowerInstanceId == latestSwap.NewPowerInstanceId;
		}

		/// <summary>
		/// Special key used only during deserialization to access mutable state
		/// </summary>
		private class DeserializationKey : SessionMutator.IStateMutatorKey { }

		#endregion
	}
}
