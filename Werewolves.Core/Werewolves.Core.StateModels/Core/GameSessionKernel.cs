using System.Text.Json;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
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
		private readonly RoleLockIn _roleLockIn;
		private readonly PublicGroupPartition? _publicGroupPartition;
		private readonly Dictionary<Guid, PhysicalCharacterCardState> _physicalCardStates = new();
		private readonly ActorSetupCards _actorSetupCards = ActorSetupCards.None;
		private readonly Dictionary<Guid, Guid>
			_actorSetupCardSpendActivationIds = [];
		private readonly byte[] _actorBorrowedRolePowerCommitmentKey;
		private ActorBorrowedRolePowerActivation?
			_activeActorBorrowedRolePowerActivation;
		private readonly List<ActorBorrowedSeerCheckCommit>
			_actorBorrowedSeerCheckCommits = [];
		private readonly List<ActorBorrowedDefenderProtectionCommit>
			_actorBorrowedDefenderProtectionCommits = [];
		private readonly List<ActorBorrowedFoxCheckCommit>
			_actorBorrowedFoxCheckCommits = [];
		private readonly List<ActorBorrowedBearTamerGrowlCommit>
			_actorBorrowedBearTamerGrowlCommits = [];
		private readonly List<ActorBorrowedKnightRustySwordScheduleCommit>
			_actorBorrowedKnightRustySwordScheduleCommits = [];
		private readonly List<ActorBorrowedHunterFinalShotCommit>
			_actorBorrowedHunterFinalShotCommits = [];
		private readonly List<ActorBorrowedElderResistanceCommit>
			_actorBorrowedElderResistanceCommits = [];
		private readonly List<ActorBorrowedElderSuppressionCommit>
			_actorBorrowedElderSuppressionCommits = [];
		private readonly List<ActorBorrowedScapegoatTieReplacementCommit>
			_actorBorrowedScapegoatTieReplacementCommits = [];
		private readonly List<ActorBorrowedScapegoatVoterRestrictionCommit>
			_actorBorrowedScapegoatVoterRestrictionCommits = [];
		private readonly List<ActorBorrowedVillageIdiotPardonCommit>
			_actorBorrowedVillageIdiotPardonCommits = [];
		private readonly List<ActorBorrowedWitchPotionUseCommit>
			_actorBorrowedWitchPotionUseCommits = [];
		private readonly List<ActorBorrowedWitchPotionDeclineCommit>
			_actorBorrowedWitchPotionDeclineCommits = [];
		private readonly List<ActorBorrowedCupidLoversCommit>
			_actorBorrowedCupidLoversCommits = [];
		private readonly List<ActorBorrowedStutteringJudgeSignalSetupCommit>
			_actorBorrowedStutteringJudgeSignalSetupCommits = [];
		private readonly List<ActorBorrowedStutteringJudgeSignalObservationCommit>
			_actorBorrowedStutteringJudgeSignalObservationCommits = [];
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
		internal RoleLockIn GetRoleLockIn() => _roleLockIn;
		internal PublicGroupPartition? GetPublicGroupPartition() => _publicGroupPartition;
		internal ActorSetupCards GetActorSetupCards() => _actorSetupCards;
		internal IReadOnlyList<PhysicalCharacterCard> GetRemainingActorSetupCards() =>
			OrderActorSetupCards(_actorSetupCards.Cards
				.Where(card =>
					!_actorSetupCardSpendActivationIds.ContainsKey(card.Id)));
		internal IReadOnlyList<PhysicalCharacterCard> GetSpentActorSetupCards() =>
			OrderActorSetupCards(_actorSetupCards.Cards
				.Where(card =>
					_actorSetupCardSpendActivationIds.ContainsKey(card.Id)));
		internal ActorBorrowedRolePowerActivation?
			GetActiveActorBorrowedRolePowerActivation() =>
			_activeActorBorrowedRolePowerActivation;
		internal IReadOnlyList<ActorBorrowedSeerCheckCommit>
			GetActorBorrowedSeerCheckCommits() =>
			_actorBorrowedSeerCheckCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedDefenderProtectionCommit>
			GetActorBorrowedDefenderProtectionCommits() =>
			_actorBorrowedDefenderProtectionCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedFoxCheckCommit>
			GetActorBorrowedFoxCheckCommits() =>
			_actorBorrowedFoxCheckCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedBearTamerGrowlCommit>
			GetActorBorrowedBearTamerGrowlCommits() =>
			_actorBorrowedBearTamerGrowlCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedKnightRustySwordScheduleCommit>
			GetActorBorrowedKnightRustySwordScheduleCommits() =>
			_actorBorrowedKnightRustySwordScheduleCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedHunterFinalShotCommit>
			GetActorBorrowedHunterFinalShotCommits() =>
			_actorBorrowedHunterFinalShotCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedElderResistanceCommit>
			GetActorBorrowedElderResistanceCommits() =>
			_actorBorrowedElderResistanceCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedElderSuppressionCommit>
			GetActorBorrowedElderSuppressionCommits() =>
			_actorBorrowedElderSuppressionCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedScapegoatTieReplacementCommit>
			GetActorBorrowedScapegoatTieReplacementCommits() =>
			_actorBorrowedScapegoatTieReplacementCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedScapegoatVoterRestrictionCommit>
			GetActorBorrowedScapegoatVoterRestrictionCommits() =>
			_actorBorrowedScapegoatVoterRestrictionCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedVillageIdiotPardonCommit>
			GetActorBorrowedVillageIdiotPardonCommits() =>
			_actorBorrowedVillageIdiotPardonCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedWitchPotionUseCommit>
			GetActorBorrowedWitchPotionUseCommits() =>
			_actorBorrowedWitchPotionUseCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedWitchPotionDeclineCommit>
			GetActorBorrowedWitchPotionDeclineCommits() =>
			_actorBorrowedWitchPotionDeclineCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedCupidLoversCommit>
			GetActorBorrowedCupidLoversCommits() =>
			_actorBorrowedCupidLoversCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedStutteringJudgeSignalSetupCommit>
			GetActorBorrowedStutteringJudgeSignalSetupCommits() =>
			_actorBorrowedStutteringJudgeSignalSetupCommits.AsReadOnly();
		internal IReadOnlyList<ActorBorrowedStutteringJudgeSignalObservationCommit>
			GetActorBorrowedStutteringJudgeSignalObservationCommits() =>
			_actorBorrowedStutteringJudgeSignalObservationCommits.AsReadOnly();
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
		internal ExecutionView Execution => _phaseStateCache.CreateExecutionView(
			_pendingModeratorInstruction,
			_acceptedObservationRecoveryCursor,
			_domainRecoveryCursor);

		internal GameSessionKernel(Guid id, ModeratorInstruction initialInstruction, GameSessionConfig config, IStateChangeObserver? stateChangeObserver = null)
		{
			Id = id;

			_pendingModeratorInstruction = initialInstruction;
			config.EnforceValidity();
			_actorBorrowedRolePowerCommitmentKey =
				ActorBorrowedRolePowerCommitment.CreateKey();

				foreach (var playerConfig in config.PlayerRoster)
				{
					var player = new Player(playerConfig.Name, playerConfig.Id);
					_players.Add(player.Id, player);

				//TODO: add seating order input logic
				_playerSeatingOrder.Add(player.Id);
			}

			_roleLockIn = config.RoleLockIn;
			_publicGroupPartition = config.PublicGroupPartition;
			_actorSetupCards = config.ActorSetupCards;
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

		private static IReadOnlyList<PhysicalCharacterCard> OrderActorSetupCards(
			IEnumerable<PhysicalCharacterCard> cards) =>
			cards
				.OrderBy(card => card.PrintedRole)
				.ThenBy(card => card.Id)
				.ToArray();

		internal bool TrySpendActorSetupCard(
			Guid actingPlayerId,
			Guid selectedCardId,
			out ActorBorrowedRolePowerActivation? activation)
		{
			activation = null;
			if (actingPlayerId == Guid.Empty ||
				selectedCardId == Guid.Empty ||
				CurrentPhase != GamePhase.Night ||
				_activeActorBorrowedRolePowerActivation is not null ||
				!_players.TryGetValue(actingPlayerId, out var actor))
			{
				return false;
			}
			var actorState = ((IPlayer)actor).State;
			if (actorState.Health != PlayerHealth.Alive ||
				actorState.CurrentRole != MainRoleType.Actor)
			{
				return false;
			}

			var selectedCard = _actorSetupCards.Cards
				.SingleOrDefault(card => card.Id == selectedCardId);
			if (selectedCard is null ||
				_actorSetupCardSpendActivationIds.ContainsKey(selectedCardId) ||
				!selectedCard.PrintedRole.IsEligibleActorSetupCard())
			{
				return false;
			}

			Guid activationId;
			do
			{
				activationId = Guid.NewGuid();
			}
			while (IsReservedActorBorrowedActivationId(activationId));

			var committedActivation = new ActorBorrowedRolePowerActivation(
				activationId,
				actingPlayerId,
				MainRoleType.Actor,
				selectedCard.Id,
				selectedCard.PrintedRole);
			AddEntryAndUpdateState(new ActorSetupCardSpendCommandLogEntry
			{
				Timestamp = DateTimeOffset.UtcNow,
				TurnNumber = TurnNumber,
				CurrentPhase = CurrentPhase,
				Activation = committedActivation
			});
			activation = committedActivation;
			return true;
		}

		private bool IsReservedActorBorrowedActivationId(Guid candidate) =>
			candidate == Guid.Empty ||
			candidate == Id ||
			_players.ContainsKey(candidate) ||
			_actorSetupCards.Cards.Any(card => card.Id == candidate) ||
			_actorSetupCardSpendActivationIds.ContainsValue(candidate);

		internal bool TryExpireActorBorrowedRolePowerActivation()
		{
			if (_activeActorBorrowedRolePowerActivation is not { } activation ||
				CurrentPhase != GamePhase.Night)
			{
				return false;
			}

			AddEntryAndUpdateState(
				new ActorBorrowedRolePowerActivationExpiryCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					ExpectedActivation = activation
				});
			return true;
		}

		internal void CommitActorBorrowedSeerCheck(
			RolePowerInstanceIdentity powerIdentity,
			Guid targetPlayerId,
			FactionAgentKnowledge targetAgentKnowledge)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(new ActorBorrowedSeerCheckCommandLogEntry
			{
				Timestamp = DateTimeOffset.UtcNow,
				TurnNumber = TurnNumber,
				CurrentPhase = CurrentPhase,
				PowerIdentity = powerIdentity,
				ActorSetupCardId = activation.SelectedCardId,
				TargetPlayerId = targetPlayerId,
				TargetAgentKnowledge = targetAgentKnowledge
			});
		}

		internal void CommitActorBorrowedDefenderProtection(
			RolePowerInstanceIdentity powerIdentity,
			Guid targetPlayerId)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedDefenderProtectionCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = activation.SelectedCardId,
					TargetPlayerId = targetPlayerId
				});
		}

		internal void CommitActorBorrowedFoxCheck(
			RolePowerInstanceIdentity powerIdentity,
			Guid centerPlayerId,
			FactionAgentKnowledge neighborhoodAgentKnowledge,
			OneUseRolePowerResourceIdentity? spentResourceIdentity)
		{
				var activation = _activeActorBorrowedRolePowerActivation
					?? throw new InvalidOperationException(
						"The borrowed Role Power activation is unavailable.");

				AddEntryAndUpdateState(new ActorBorrowedFoxCheckCommandLogEntry
			{
				Timestamp = DateTimeOffset.UtcNow,
				TurnNumber = TurnNumber,
				CurrentPhase = CurrentPhase,
				PowerIdentity = powerIdentity,
				ActorSetupCardId = activation.SelectedCardId,
				CenterPlayerId = centerPlayerId,
				NeighborhoodAgentKnowledge = neighborhoodAgentKnowledge,
				SpentResourceIdentity = spentResourceIdentity
			});
		}

		internal void CommitActorBorrowedBearTamerGrowl(
			RolePowerInstanceIdentity powerIdentity)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedBearTamerGrowlCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = activation.SelectedCardId
				});
		}

		internal void CommitActorBorrowedKnightRustySwordSchedule(
			RolePowerInstanceIdentity powerIdentity,
			Guid targetPlayerId,
			int werewolfAttackEliminationLogIndex,
			string cascadeScopeId)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedKnightRustySwordScheduleCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = activation.SelectedCardId,
					TargetPlayerId = targetPlayerId,
					WerewolfAttackEliminationLogIndex =
						werewolfAttackEliminationLogIndex,
					CascadeScopeId = cascadeScopeId
				});
		}

		internal void CommitActorBorrowedVillageIdiotPardon(
			RolePowerInstanceIdentity powerIdentity,
			OneUseRolePowerResourceIdentity spentResourceIdentity)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedVillageIdiotPardonCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = activation.SelectedCardId,
					SpentResourceIdentity = spentResourceIdentity
				});
		}

		internal void CommitActorBorrowedHunterFinalShot(
			RolePowerInstanceIdentity powerIdentity,
			string cascadeScopeId,
			IReadOnlyList<Guid> triggeringPlayerIds,
			Guid targetPlayerId)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedHunterFinalShotCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = activation.SelectedCardId,
					CascadeScopeId = cascadeScopeId,
					TriggeringPlayerIds = triggeringPlayerIds.ToArray(),
					TargetPlayerId = targetPlayerId
				});
		}

		internal void CommitActorBorrowedElderResistance(
			RolePowerInstanceIdentity powerIdentity,
			Guid targetPlayerId,
			int triggeringNightActionLogIndex,
			int? restoringWitchSaveLogIndex)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedElderResistanceCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = activation.SelectedCardId,
					TargetPlayerId = targetPlayerId,
					TriggeringNightActionLogIndex =
						triggeringNightActionLogIndex,
					RestoringWitchSaveLogIndex =
						restoringWitchSaveLogIndex
				});
		}

		internal void CommitActorBorrowedElderSuppression(
			RolePowerInstanceIdentity powerIdentity,
			int triggeringVoteOutcomeLogIndex,
			string cascadeScopeId,
			Guid announcementInstructionId)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedElderSuppressionCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = activation.SelectedCardId,
					TriggeringVoteOutcomeLogIndex =
						triggeringVoteOutcomeLogIndex,
					CascadeScopeId = cascadeScopeId,
					AnnouncementInstructionId = announcementInstructionId
				});
		}

		internal void CommitActorBorrowedScapegoatTieReplacement(
			RolePowerInstanceIdentity powerIdentity,
			int triggeringVoteOutcomeLogIndex,
			int voteOrdinal,
			string cascadeScopeId)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedScapegoatTieReplacementCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = activation.SelectedCardId,
					TriggeringVoteOutcomeLogIndex =
						triggeringVoteOutcomeLogIndex,
					VoteOrdinal = voteOrdinal,
					CascadeScopeId = cascadeScopeId
				});
		}

		internal void CommitActorBorrowedScapegoatVoterRestriction(
			RolePowerInstanceIdentity powerIdentity,
			int tieReplacementPublicMarkerLogIndex,
			string cascadeScopeId,
			IReadOnlyCollection<Guid> candidatePlayerIds,
			IReadOnlyCollection<Guid> permittedVoterIds,
			int appliesOnTurnNumber,
			Guid announcementInstructionId)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedScapegoatVoterRestrictionCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = activation.SelectedCardId,
					TieReplacementPublicMarkerLogIndex =
						tieReplacementPublicMarkerLogIndex,
					CascadeScopeId = cascadeScopeId,
					CandidatePlayerIds = candidatePlayerIds
						.OrderBy(playerId => playerId)
						.ToArray(),
					PermittedVoterIds = permittedVoterIds
						.OrderBy(playerId => playerId)
						.ToArray(),
					AppliesOnTurnNumber = appliesOnTurnNumber,
					AnnouncementInstructionId = announcementInstructionId
				});
		}

		internal void CommitActorBorrowedWitchPotionUse(
			RolePowerInstanceIdentity powerIdentity,
			OneUseRolePowerResourceIdentity spentResourceIdentity,
			Guid targetPlayerId)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedWitchPotionUseCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = activation.SelectedCardId,
					SpentResourceIdentity = spentResourceIdentity,
					TargetPlayerId = targetPlayerId
				});
		}

		internal void CommitActorBorrowedWitchPotionDecline(
			RolePowerInstanceIdentity powerIdentity,
			OneUseRolePowerResourceIdentity offeredResourceIdentity)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedWitchPotionDeclineCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = activation.SelectedCardId,
					OfferedResourceIdentity = offeredResourceIdentity
				});
		}

		internal void CommitActorBorrowedStutteringJudgeSignalSetup(
			RolePowerInstanceIdentity powerIdentity)
		{
			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedStutteringJudgeSignalSetupCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = activation.SelectedCardId
				});
		}

		internal void CommitActorBorrowedStutteringJudgeSignalObservation(
			RolePowerInstanceIdentity powerIdentity,
			bool signalOccurred,
			OneUseRolePowerResourceIdentity? spentResourceIdentity)
		{
			powerIdentity.EnforceValidity();
			var setup = _actorBorrowedStutteringJudgeSignalSetupCommits
				.SingleOrDefault(commit => commit.PowerIdentity == powerIdentity)
				?? throw new InvalidOperationException(
					"The Actor borrowed Stuttering Judge signal setup is unavailable.");
			AddEntryAndUpdateState(
				new ActorBorrowedStutteringJudgeSignalObservationCommandLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = TurnNumber,
					CurrentPhase = CurrentPhase,
					PowerIdentity = powerIdentity,
					ActorSetupCardId = setup.ActorSetupCardId,
					SignalOccurred = signalOccurred,
					SpentResourceIdentity = spentResourceIdentity
				});
		}

		internal void CommitActorBorrowedCupidLovers(
			RolePowerInstanceIdentity powerIdentity,
			IReadOnlyCollection<Guid> playerIds,
			ActorBorrowedCupidLoversDisposition disposition)
		{
			ArgumentNullException.ThrowIfNull(playerIds);
			powerIdentity.EnforceValidity();
			if (playerIds.Count != 2 ||
				playerIds.Any(playerId => playerId == Guid.Empty) ||
				playerIds.Distinct().Count() != 2)
			{
				throw new ArgumentException(
					"The Lovers pair requires exactly two distinct Players.",
					nameof(playerIds));
			}

			var activation = _activeActorBorrowedRolePowerActivation
				?? throw new InvalidOperationException(
					"The borrowed Role Power activation is unavailable.");
			var selectedCard = _actorSetupCards.Cards.SingleOrDefault(card =>
				card.Id == activation.SelectedCardId);
			var canonicalPlayerIds = playerIds.Order().ToArray();
			if (CurrentPhase != GamePhase.Night ||
				powerIdentity.ActingPlayerId != activation.ActingPlayerId ||
				powerIdentity.SourceRole != MainRoleType.Cupid ||
				powerIdentity.SourceRole != activation.SourceRole ||
				!StringComparer.Ordinal.Equals(
					powerIdentity.SourcePowerIdentifier,
					ActorBorrowedCupidLoversCommit
						.ExpectedSourcePowerIdentifier) ||
				powerIdentity.PowerInstanceId != activation.ActivationId ||
				powerIdentity.PowerInstanceOrigin !=
					RolePowerInstanceOrigin.Borrowed ||
				selectedCard?.PrintedRole != MainRoleType.Cupid ||
				!_actorSetupCardSpendActivationIds.TryGetValue(
					activation.SelectedCardId,
					out var spentActivationId) ||
				spentActivationId != activation.ActivationId ||
				!_players.TryGetValue(
					powerIdentity.ActingPlayerId,
					out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Alive,
					CurrentRole: MainRoleType.Actor
				} ||
				canonicalPlayerIds.Any(playerId =>
					!_players.TryGetValue(playerId, out var player) ||
					((IPlayer)player).State.Health != PlayerHealth.Alive) ||
				_actorBorrowedCupidLoversCommits.Count > 0 ||
				_gameHistoryLog.GetAllLogEntries()
					.OfType<LoversPairCommittedLogEntry>()
					.Any())
			{
				throw new InvalidOperationException(
					"The Actor borrowed Cupid Lovers commit is stale or invalid.");
			}

			AddEntryAndUpdateState(new ActorBorrowedCupidLoversCommandLogEntry
			{
				Timestamp = DateTimeOffset.UtcNow,
				TurnNumber = TurnNumber,
				CurrentPhase = CurrentPhase,
				PowerIdentity = powerIdentity,
				ActorSetupCardId = activation.SelectedCardId,
				FirstPlayerId = canonicalPlayerIds[0],
				SecondPlayerId = canonicalPlayerIds[1],
				Disposition = disposition
			});
		}

			internal void AddEntryAndUpdateState(GameLogEntryBase entry)
			{
				if (entry is PhaseTransitionLogEntry phaseTransition &&
					phaseTransition.PreviousPhase != Execution.CurrentPhase)
				{
					throw new InvalidOperationException(
						"The Main Phase transition is stale.");
				}

				_gameHistoryLog.PreflightLogEntry(entry, _players.Keys);
				entry.Apply(new SessionMutator(this));
			}

			internal void TransitionSubPhase(Enum subPhase)
			{
				var expected = Execution;
				ApplyExecutionTransition(
					ExecutionTransition.ChangeSubPhase(expected, subPhase));
			}

			internal bool TryEnterSubPhaseStage(string subPhaseStage)
			{
				ArgumentException.ThrowIfNullOrWhiteSpace(subPhaseStage);
				var expected = Execution;
				if (expected.ActiveSubPhaseStage != null)
				{
					return StringComparer.Ordinal.Equals(
						expected.ActiveSubPhaseStage,
						subPhaseStage);
				}
				if (expected.HasSubPhaseStageCompleted(subPhaseStage))
				{
					return false;
				}

				ApplyExecutionTransition(
					ExecutionTransition.EnterStage(expected, subPhaseStage));
				return true;
			}

			internal void CompleteSubPhaseStage()
			{
				var expected = Execution;
				ApplyExecutionTransition(
					ExecutionTransition.CompleteStage(expected));
			}

			internal void TransitionListenerAndState(
				ListenerIdentifier listener,
				string state)
			{
				ArgumentNullException.ThrowIfNull(listener);
				var expected = Execution;
				ApplyExecutionTransition(
					ExecutionTransition.PauseOrResumeListener(
						expected,
						listener,
						state));
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
				var expected = Execution;
				ApplyExecutionTransition(
					ExecutionTransition.ClearListener(expected));
			}

			private void ApplyExecutionTransition(ExecutionTransition transition)
			{
				ArgumentNullException.ThrowIfNull(transition);
				var current = Execution;
				transition.EnforceValidAgainst(current);
				_phaseStateCache = _phaseStateCache.WithExecutionCursor(
					transition.Candidate);
				transition.NotifyObserver(_stateChangeObserver);
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
				RolesInPlay = _roleLockIn.DealPool
					.Select(card => card.PrintedRole)
					.ToList(),
					RoleLockIn = RoleLockInDto.FromValue(_roleLockIn),
					PublicGroupPartition = _publicGroupPartition is null
						? null
						: PublicGroupPartitionDto.FromValue(_publicGroupPartition),
					ActorSetupCards = ActorSetupCardsDto.FromValue(
						_actorSetupCards),
					ActorSetupCardSpends = OrderActorSetupCards(
							_actorSetupCards.Cards)
						.Where(card =>
							_actorSetupCardSpendActivationIds.ContainsKey(card.Id))
						.Select(card => new ActorSetupCardSpendDto
						{
							CardId = card.Id,
							ActivationId =
								_actorSetupCardSpendActivationIds[card.Id]
						})
						.ToList(),
					ActorBorrowedRolePowerCommitmentKey =
						ActorBorrowedRolePowerCommitment.EncodeKey(
							_actorBorrowedRolePowerCommitmentKey),
					ActiveActorBorrowedRolePowerActivation =
						_activeActorBorrowedRolePowerActivation is null
							? null
							: ActorBorrowedRolePowerActivationDto.FromValue(
								_activeActorBorrowedRolePowerActivation),
					ActorBorrowedSeerCheckCommits =
						_actorBorrowedSeerCheckCommits
							.Select(ActorBorrowedSeerCheckCommitDto.FromValue)
							.ToList(),
					ActorBorrowedDefenderProtectionCommits =
						_actorBorrowedDefenderProtectionCommits
							.Select(
								ActorBorrowedDefenderProtectionCommitDto.FromValue)
							.ToList(),
					ActorBorrowedFoxCheckCommits =
						_actorBorrowedFoxCheckCommits
							.Select(ActorBorrowedFoxCheckCommitDto.FromValue)
							.ToList(),
					ActorBorrowedBearTamerGrowlCommits =
						_actorBorrowedBearTamerGrowlCommits
							.Select(
								ActorBorrowedBearTamerGrowlCommitDto.FromValue)
							.ToList(),
					ActorBorrowedKnightRustySwordScheduleCommits =
						_actorBorrowedKnightRustySwordScheduleCommits
							.Select(
								ActorBorrowedKnightRustySwordScheduleCommitDto
									.FromValue)
							.ToList(),
					ActorBorrowedHunterFinalShotCommits =
						_actorBorrowedHunterFinalShotCommits
							.Select(
								ActorBorrowedHunterFinalShotCommitDto.FromValue)
							.ToList(),
					ActorBorrowedElderResistanceCommits =
						_actorBorrowedElderResistanceCommits
							.Select(
								ActorBorrowedElderResistanceCommitDto.FromValue)
							.ToList(),
					ActorBorrowedElderSuppressionCommits =
						_actorBorrowedElderSuppressionCommits
							.Select(
								ActorBorrowedElderSuppressionCommitDto.FromValue)
							.ToList(),
					ActorBorrowedScapegoatTieReplacementCommits =
						_actorBorrowedScapegoatTieReplacementCommits
							.Select(
								ActorBorrowedScapegoatTieReplacementCommitDto
									.FromValue)
							.ToList(),
					ActorBorrowedScapegoatVoterRestrictionCommits =
						_actorBorrowedScapegoatVoterRestrictionCommits
							.Select(
								ActorBorrowedScapegoatVoterRestrictionCommitDto
									.FromValue)
							.ToList(),
					ActorBorrowedVillageIdiotPardonCommits =
						_actorBorrowedVillageIdiotPardonCommits
							.Select(
								ActorBorrowedVillageIdiotPardonCommitDto
									.FromValue)
							.ToList(),
					ActorBorrowedWitchPotionUseCommits =
						_actorBorrowedWitchPotionUseCommits
							.Select(
								ActorBorrowedWitchPotionUseCommitDto.FromValue)
							.ToList(),
					ActorBorrowedWitchPotionDeclineCommits =
						_actorBorrowedWitchPotionDeclineCommits
							.Select(
								ActorBorrowedWitchPotionDeclineCommitDto.FromValue)
							.ToList(),
					ActorBorrowedCupidLoversCommits =
						_actorBorrowedCupidLoversCommits
							.Select(ActorBorrowedCupidLoversCommitDto.FromValue)
							.ToList(),
					ActorBorrowedStutteringJudgeSignalSetupCommits =
						_actorBorrowedStutteringJudgeSignalSetupCommits
							.Select(
								ActorBorrowedStutteringJudgeSignalSetupCommitDto.FromValue)
							.ToList(),
					ActorBorrowedStutteringJudgeSignalObservationCommits =
						_actorBorrowedStutteringJudgeSignalObservationCommits
							.Select(
								ActorBorrowedStutteringJudgeSignalObservationCommitDto.FromValue)
							.ToList(),
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
				_roleLockIn = dto.RoleLockIn?.ToValue()
					?? throw new InvalidOperationException(
						"The stable recovery snapshot is missing Role Lock-In.");
				ValidateRecoveryRoster(dto, _roleLockIn);
				_playerSeatingOrder = dto.SeatingOrder.ToList();
				_publicGroupPartition = RestorePublicGroupPartition(dto, _roleLockIn);
				_actorSetupCards = RestoreActorSetupCards(
					dto,
					_roleLockIn);
				_actorBorrowedRolePowerCommitmentKey =
					RestoreActorBorrowedRolePowerCommitmentKey(dto);
				RestoreActorRuntimeState(dto);
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

				ValidatePermanentRoleSwapPlayerProjectionMatchesHistory();
				ValidatePhysicalCharacterCardProjectionMatchesHistory();
				ValidateFactionProjectionMatchesHistory();
				ValidateLoversPairProjectionMatchesHistory();
				ValidateActorRuntimeState();

				_recoveryBoundary = CreateDto();
			}

			private static void ValidateRecoveryRoster(
				GameSessionDto dto,
				RoleLockIn roleLockIn)
			{
				if (dto.Players is null || dto.SeatingOrder is null)
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot is missing its Player roster or Seating Order.");
				}

				var playerIds = dto.Players.Select(player => player.Id).ToArray();
				if (dto.Players.Count != roleLockIn.PlayerCount ||
					dto.Players.Any(player =>
						player.Id == Guid.Empty ||
						string.IsNullOrWhiteSpace(player.Name)) ||
					playerIds.Distinct().Count() != playerIds.Length ||
					dto.SeatingOrder.Count != playerIds.Length ||
					dto.SeatingOrder.Any(playerId => playerId == Guid.Empty) ||
					dto.SeatingOrder.Distinct().Count() != dto.SeatingOrder.Count ||
					!playerIds.ToHashSet().SetEquals(dto.SeatingOrder))
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has an invalid Player roster or Seating Order.");
				}
			}

			private static PublicGroupPartition? RestorePublicGroupPartition(
				GameSessionDto dto,
				RoleLockIn roleLockIn)
			{
				var prejudicedManipulatorReachable = roleLockIn.RoleComposition.Any(
					card => card.PrintedRole == MainRoleType.PrejudicedManipulator);
				if (prejudicedManipulatorReachable !=
					(dto.PublicGroupPartition is not null))
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has an invalid Public Group Partition coordinate.");
				}
				if (dto.PublicGroupPartition is null)
				{
					return null;
				}

				try
				{
					return dto.PublicGroupPartition.ToValue(
						dto.Players.Select(player => player.Id));
				}
				catch (ArgumentException exception)
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has an invalid Public Group Partition coordinate.",
						exception);
				}
			}

			private static ActorSetupCards RestoreActorSetupCards(
				GameSessionDto dto,
				RoleLockIn roleLockIn)
			{
				try
				{
					var setup = dto.ActorSetupCards?.ToValue()
						?? throw new InvalidOperationException();
					_ = GameSessionConfig.TryGetRoleLockInPhysicalSetupIssues(
						roleLockIn.PlayerCount,
						roleLockIn.DealPool
							.Select(card => card.PrintedRole)
							.ToArray(),
						roleLockIn.Offer1?.PrintedRole,
						roleLockIn.Offer2?.PrintedRole,
						setup,
						out var issues);
					if (issues.Any(issue => issue.Type is
						GameConfigValidationErrorType.UnexpectedActorSetupCards or
						GameConfigValidationErrorType.ActorSetupCardCountMismatch or
						GameConfigValidationErrorType.DuplicateActorSetupCardSource or
						GameConfigValidationErrorType.ActorSetupCardInRoleComposition or
						GameConfigValidationErrorType.IneligibleActorSetupCard))
					{
						throw new InvalidOperationException();
					}
					return setup;
				}
				catch (Exception exception) when (
					exception is ArgumentException or InvalidOperationException)
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has invalid Actor Setup Cards.");
				}
			}

			private static byte[] RestoreActorBorrowedRolePowerCommitmentKey(
				GameSessionDto dto)
			{
				if (ActorBorrowedRolePowerCommitment.TryDecodeKey(
						dto.ActorBorrowedRolePowerCommitmentKey,
						out var key))
				{
					return key;
				}

				var privateCommitCount =
					(dto.ActorBorrowedSeerCheckCommits?.Count ?? 0) +
					(dto.ActorBorrowedDefenderProtectionCommits?.Count ?? 0) +
					(dto.ActorBorrowedFoxCheckCommits?.Count ?? 0) +
					(dto.ActorBorrowedBearTamerGrowlCommits?.Count ?? 0) +
					(dto.ActorBorrowedKnightRustySwordScheduleCommits?.Count ?? 0) +
					(dto.ActorBorrowedHunterFinalShotCommits?.Count ?? 0) +
					(dto.ActorBorrowedElderResistanceCommits?.Count ?? 0) +
					(dto.ActorBorrowedElderSuppressionCommits?.Count ?? 0) +
					(dto.ActorBorrowedScapegoatTieReplacementCommits?.Count ?? 0) +
					(dto.ActorBorrowedScapegoatVoterRestrictionCommits?.Count ?? 0) +
					(dto.ActorBorrowedVillageIdiotPardonCommits?.Count ?? 0) +
					(dto.ActorBorrowedWitchPotionUseCommits?.Count ?? 0) +
					(dto.ActorBorrowedWitchPotionDeclineCommits?.Count ?? 0) +
					(dto.ActorBorrowedCupidLoversCommits?.Count ?? 0) +
					(dto.ActorBorrowedStutteringJudgeSignalSetupCommits?.Count ?? 0) +
					(dto.ActorBorrowedStutteringJudgeSignalObservationCommits?.Count ?? 0);
				var publicMarkerCount = dto.GameHistoryLog?
					.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
					.Count() ?? 0;
				if (string.IsNullOrWhiteSpace(
						dto.ActorBorrowedRolePowerCommitmentKey) &&
					privateCommitCount == 0 &&
					publicMarkerCount == 0)
				{
					return ActorBorrowedRolePowerCommitment.CreateKey();
				}

				throw new InvalidOperationException(
					"The stable recovery snapshot has invalid Actor borrowed Role Power integrity state.");
			}

			private void RestoreActorRuntimeState(GameSessionDto dto)
			{
				var spends = dto.ActorSetupCardSpends
					?? throw new InvalidOperationException(
						"The stable recovery snapshot has invalid Actor runtime state.");
				var setupCardIds = _actorSetupCards.Cards
					.Select(card => card.Id)
					.ToHashSet();
				if (spends.Any(spend =>
						spend.CardId == Guid.Empty ||
						spend.ActivationId == Guid.Empty ||
						!setupCardIds.Contains(spend.CardId)) ||
					spends.Select(spend => spend.CardId).Distinct().Count() !=
						spends.Count ||
					spends.Select(spend => spend.ActivationId).Distinct().Count() !=
						spends.Count)
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has invalid Actor runtime state.");
				}

				foreach (var spend in spends)
				{
					_actorSetupCardSpendActivationIds.Add(
						spend.CardId,
						spend.ActivationId);
				}

				try
				{
					_activeActorBorrowedRolePowerActivation =
						dto.ActiveActorBorrowedRolePowerActivation?.ToValue();
				}
				catch (ArgumentException)
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has invalid Actor runtime state.");
				}

				if (dto.ActorBorrowedSeerCheckCommits is null ||
					dto.ActorBorrowedDefenderProtectionCommits is null ||
					dto.ActorBorrowedFoxCheckCommits is null ||
					dto.ActorBorrowedBearTamerGrowlCommits is null ||
					dto.ActorBorrowedKnightRustySwordScheduleCommits is null ||
					dto.ActorBorrowedHunterFinalShotCommits is null ||
					dto.ActorBorrowedElderResistanceCommits is null ||
					dto.ActorBorrowedElderSuppressionCommits is null ||
					dto.ActorBorrowedScapegoatTieReplacementCommits is null ||
					dto.ActorBorrowedScapegoatVoterRestrictionCommits is null ||
					dto.ActorBorrowedVillageIdiotPardonCommits is null ||
					dto.ActorBorrowedWitchPotionUseCommits is null ||
					dto.ActorBorrowedWitchPotionDeclineCommits is null ||
					dto.ActorBorrowedCupidLoversCommits is null ||
					dto.ActorBorrowedStutteringJudgeSignalSetupCommits is null ||
					dto.ActorBorrowedStutteringJudgeSignalObservationCommits is null)
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has invalid Actor runtime state.");
				}
				try
				{
					foreach (var commitDto in dto.ActorBorrowedSeerCheckCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedSeerCheckCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedDefenderProtectionCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedDefenderProtectionCommits.Add(commit);
					}
					foreach (var commitDto in dto.ActorBorrowedFoxCheckCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedFoxCheckCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedBearTamerGrowlCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedBearTamerGrowlCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedKnightRustySwordScheduleCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedKnightRustySwordScheduleCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedHunterFinalShotCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedHunterFinalShotCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedElderResistanceCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedElderResistanceCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedElderSuppressionCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedElderSuppressionCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedScapegoatTieReplacementCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedScapegoatTieReplacementCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedScapegoatVoterRestrictionCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedScapegoatVoterRestrictionCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedVillageIdiotPardonCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedVillageIdiotPardonCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedWitchPotionUseCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedWitchPotionUseCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedWitchPotionDeclineCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedWitchPotionDeclineCommits.Add(commit);
					}
					foreach (var commitDto in dto.ActorBorrowedCupidLoversCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedCupidLoversCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedStutteringJudgeSignalSetupCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedStutteringJudgeSignalSetupCommits.Add(commit);
					}
					foreach (var commitDto in
						dto.ActorBorrowedStutteringJudgeSignalObservationCommits)
					{
						var commit = commitDto.ToValue();
						commit.EnforceValidity();
						_actorBorrowedStutteringJudgeSignalObservationCommits.Add(commit);
					}
				}
				catch (Exception exception) when (
					exception is ArgumentException or InvalidOperationException)
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has invalid Actor runtime state.");
				}
			}

			private void ValidateActorRuntimeState()
			{
				var history = _gameHistoryLog.GetAllLogEntries();
				var spendCount = history
					.OfType<ActorSetupCardSpendCommittedLogEntry>()
					.Count();
				var expiryCount = history
					.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
					.Count();
				var expectedExpiryCount =
					_actorSetupCardSpendActivationIds.Count -
					(_activeActorBorrowedRolePowerActivation is null ? 0 : 1);
				if (spendCount != _actorSetupCardSpendActivationIds.Count ||
					expiryCount != expectedExpiryCount)
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has invalid Actor runtime state.");
				}
				ValidateActorBorrowedRolePowerCommitProjection(history);

				if (_activeActorBorrowedRolePowerActivation is not { } active)
				{
					return;
				}

				var selectedCard = _actorSetupCards.Cards.SingleOrDefault(
					card => card.Id == active.SelectedCardId);
				if (!_players.ContainsKey(active.ActingPlayerId) ||
					selectedCard is null ||
					selectedCard.PrintedRole != active.SourceRole ||
					!_actorSetupCardSpendActivationIds.TryGetValue(
						active.SelectedCardId,
						out var spendActivationId) ||
					spendActivationId != active.ActivationId)
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has invalid Actor runtime state.");
				}
			}

			private void ValidateActorBorrowedRolePowerCommitProjection(
				IReadOnlyList<GameLogEntryBase> history)
			{
				var commits = _actorBorrowedSeerCheckCommits
					.Cast<IActorBorrowedRolePowerCommit>()
					.Concat(_actorBorrowedDefenderProtectionCommits)
					.Concat(_actorBorrowedFoxCheckCommits)
					.Concat(_actorBorrowedBearTamerGrowlCommits)
					.Concat(_actorBorrowedKnightRustySwordScheduleCommits)
					.Concat(_actorBorrowedHunterFinalShotCommits)
					.Concat(_actorBorrowedElderResistanceCommits)
					.Concat(_actorBorrowedElderSuppressionCommits)
					.Concat(_actorBorrowedScapegoatTieReplacementCommits)
					.Concat(_actorBorrowedScapegoatVoterRestrictionCommits)
					.Concat(_actorBorrowedVillageIdiotPardonCommits)
					.Concat(_actorBorrowedWitchPotionUseCommits)
					.Concat(_actorBorrowedWitchPotionDeclineCommits)
					.Concat(_actorBorrowedCupidLoversCommits)
					.Concat(_actorBorrowedStutteringJudgeSignalSetupCommits)
					.Concat(_actorBorrowedStutteringJudgeSignalObservationCommits)
					.ToArray();
				var coordinates = commits
					.Select(commit => commit.Coordinate)
					.ToArray();
				if (history.Any(entry =>
						TryGetCommittedRolePowerIdentity(entry, out var identity) &&
						identity.PowerInstanceOrigin ==
							RolePowerInstanceOrigin.Borrowed &&
						_players.TryGetValue(
							identity.ActingPlayerId,
							out var actingPlayer) &&
						((IPlayer)actingPlayer).State.CurrentRole ==
							MainRoleType.Actor) ||
					history.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
						.Count() != coordinates.Length ||
					_actorBorrowedSeerCheckCommits
						.Select(commit => commit.PowerIdentity)
						.Distinct().Count() !=
						_actorBorrowedSeerCheckCommits.Count ||
					_actorBorrowedDefenderProtectionCommits
						.Select(commit => commit.PowerIdentity)
						.Distinct().Count() !=
						_actorBorrowedDefenderProtectionCommits.Count ||
					_actorBorrowedFoxCheckCommits
						.Select(commit => commit.PowerIdentity)
						.Distinct().Count() !=
						_actorBorrowedFoxCheckCommits.Count ||
					_actorBorrowedBearTamerGrowlCommits
						.Select(commit => commit.PowerIdentity)
						.Distinct().Count() !=
						_actorBorrowedBearTamerGrowlCommits.Count ||
					!HasValidActorBorrowedBearTamerGrowlCommitSequence(history) ||
					_actorBorrowedKnightRustySwordScheduleCommits
						.Select(commit => commit.PowerIdentity)
						.Distinct().Count() !=
						_actorBorrowedKnightRustySwordScheduleCommits.Count ||
					!HasValidActorBorrowedKnightRustySwordScheduleCommitSequence(
						history) ||
					_actorBorrowedHunterFinalShotCommits
						.Select(commit => commit.PowerIdentity)
						.Distinct().Count() !=
						_actorBorrowedHunterFinalShotCommits.Count ||
					!HasValidActorBorrowedElderResistanceCommitSequence() ||
					_actorBorrowedElderSuppressionCommits
						.Select(commit => commit.PowerIdentity)
						.Distinct().Count() !=
						_actorBorrowedElderSuppressionCommits.Count ||
					_actorBorrowedScapegoatTieReplacementCommits
						.Select(commit => commit.PowerIdentity)
						.Distinct().Count() !=
						_actorBorrowedScapegoatTieReplacementCommits.Count ||
					_actorBorrowedScapegoatVoterRestrictionCommits
						.Select(commit => commit.PowerIdentity)
						.Distinct().Count() !=
						_actorBorrowedScapegoatVoterRestrictionCommits.Count ||
					!HasValidActorBorrowedScapegoatCommitSequence(history) ||
					_actorBorrowedVillageIdiotPardonCommits
						.Select(commit => commit.SpentResourceIdentity)
						.Distinct().Count() !=
						_actorBorrowedVillageIdiotPardonCommits.Count ||
					_actorBorrowedWitchPotionUseCommits
						.Select(commit => commit.SpentResourceIdentity)
						.Distinct().Count() !=
						_actorBorrowedWitchPotionUseCommits.Count ||
					_actorBorrowedWitchPotionDeclineCommits
						.Select(commit => commit.OfferedResourceIdentity)
						.Distinct().Count() !=
						_actorBorrowedWitchPotionDeclineCommits.Count ||
					_actorBorrowedWitchPotionUseCommits
						.Select(commit => commit.SpentResourceIdentity)
						.Intersect(
							_actorBorrowedWitchPotionDeclineCommits.Select(commit =>
								commit.OfferedResourceIdentity))
						.Any() ||
					_actorBorrowedCupidLoversCommits
						.Select(commit => commit.PowerIdentity)
						.Distinct().Count() !=
						_actorBorrowedCupidLoversCommits.Count ||
					_actorBorrowedStutteringJudgeSignalSetupCommits
						.Select(commit => commit.PowerIdentity)
						.Distinct().Count() !=
						_actorBorrowedStutteringJudgeSignalSetupCommits.Count ||
					_actorBorrowedStutteringJudgeSignalObservationCommits
						.Select(commit => commit.PowerIdentity)
						.Distinct().Count() !=
						_actorBorrowedStutteringJudgeSignalObservationCommits.Count ||
					coordinates
						.Select(coordinate => coordinate.PublicMarkerLogIndex)
						.Distinct().Count() != coordinates.Length)
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has invalid Actor borrowed Role Power state.");
				}

				foreach (var commit in commits)
				{
					var coordinate = commit.Coordinate;
					coordinate.EnforceValidity();
					var selectedCard = _actorSetupCards.Cards.SingleOrDefault(card =>
						card.Id == coordinate.ActorSetupCardId);
					if (coordinate.PublicMarkerLogIndex >= history.Count ||
						history[coordinate.PublicMarkerLogIndex] is not
							ActorBorrowedRolePowerCommittedLogEntry marker ||
						marker.Timestamp != coordinate.Timestamp ||
						marker.TurnNumber != coordinate.TurnNumber ||
						marker.CurrentPhase != coordinate.CurrentPhase ||
					!ActorBorrowedRolePowerCommitment.Matches(
							_actorBorrowedRolePowerCommitmentKey,
							commit,
							marker.IntegrityCommitment) ||
						selectedCard?.PrintedRole !=
							coordinate.PowerIdentity.SourceRole ||
						!_actorSetupCardSpendActivationIds.TryGetValue(
							coordinate.ActorSetupCardId,
							out var activationId) ||
						activationId != coordinate.PowerIdentity.PowerInstanceId ||
						!_players.ContainsKey(
							coordinate.PowerIdentity.ActingPlayerId))
					{
						throw new InvalidOperationException(
							"The stable recovery snapshot has invalid Actor borrowed Role Power state.");
					}
				}

				foreach (var observation in
					_actorBorrowedStutteringJudgeSignalObservationCommits)
				{
					var setup = _actorBorrowedStutteringJudgeSignalSetupCommits
						.SingleOrDefault(commit =>
							commit.PowerIdentity == observation.PowerIdentity);
					if (setup is null ||
						setup.ActorSetupCardId != observation.ActorSetupCardId ||
						setup.TurnNumber != observation.TurnNumber ||
						setup.CurrentPhase != GamePhase.Night ||
						observation.CurrentPhase != GamePhase.Day)
					{
						throw new InvalidOperationException(
							"The stable recovery snapshot has invalid Actor borrowed Stuttering Judge signal state.");
					}
				}

				if (_actorBorrowedSeerCheckCommits.Any(commit =>
						!_players.ContainsKey(commit.TargetPlayerId)) ||
					_actorBorrowedDefenderProtectionCommits.Any(commit =>
						!_players.ContainsKey(commit.TargetPlayerId)) ||
					_actorBorrowedFoxCheckCommits.Any(commit =>
						!_players.ContainsKey(commit.CenterPlayerId)) ||
					_actorBorrowedHunterFinalShotCommits.Any(commit =>
						!_players.ContainsKey(commit.TargetPlayerId) ||
						!_players.TryGetValue(
							commit.PowerIdentity.ActingPlayerId,
							out var hunterActor) ||
						((IPlayer)hunterActor).State is not
						{
							CurrentRole: MainRoleType.Actor,
							Health: PlayerHealth.Dead
						} ||
						commit.TriggeringPlayerIds.Any(playerId =>
							!_players.TryGetValue(
								playerId,
								out var triggeringPlayer) ||
							((IPlayer)triggeringPlayer).State.Health !=
								PlayerHealth.Dead) ||
						!history
							.OfType<EliminationCascadeBatchResolvedLogEntry>()
							.Any(batch =>
								StringComparer.Ordinal.Equals(
									batch.ScopeId,
									commit.CascadeScopeId) &&
								batch.CommittedEliminations
									.Select(elimination => elimination.PlayerId)
									.SequenceEqual(commit.TriggeringPlayerIds))) ||
					_actorBorrowedElderResistanceCommits.Any(commit =>
						!_players.TryGetValue(
							commit.PowerIdentity.ActingPlayerId,
							out var elderActor) ||
						((IPlayer)elderActor).State is not
						{
							CurrentRole: MainRoleType.Actor
						} ||
						commit.TargetPlayerId !=
							commit.PowerIdentity.ActingPlayerId ||
						commit.TriggeringNightActionLogIndex < 0 ||
						commit.TriggeringNightActionLogIndex >= history.Count ||
						commit.TriggeringNightActionLogIndex >=
							commit.PublicMarkerLogIndex ||
						commit.RestoringWitchSaveLogIndex is { } restorationLogIndex &&
							(restorationLogIndex <=
								commit.TriggeringNightActionLogIndex ||
							 restorationLogIndex >= commit.PublicMarkerLogIndex ||
							 restorationLogIndex >= history.Count)) ||
					_actorBorrowedElderSuppressionCommits.Any(commit =>
						!_players.TryGetValue(
							commit.PowerIdentity.ActingPlayerId,
							out var suppressionActor) ||
						((IPlayer)suppressionActor).State is not
						{
							CurrentRole: MainRoleType.Actor,
							Health: PlayerHealth.Dead,
							PhysicalCharacterCardRole: MainRoleType.Actor,
							PubliclyRevealedRole: MainRoleType.Actor
						} ||
						!HasQualifyingActorBorrowedElderSuppressionHistory(
							history,
							commit.PowerIdentity.ActingPlayerId,
							commit.TurnNumber,
							commit.TriggeringVoteOutcomeLogIndex,
							commit.CascadeScopeId,
							commit.PublicMarkerLogIndex) ||
						history
							.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
							.Count() != 1 ||
						commit.PublicMarkerLogIndex + 1 >= history.Count ||
						history[commit.PublicMarkerLogIndex + 1] is not
							VillagerRolePowerSuppressionCommittedLogEntry
							{
								CurrentPhase: GamePhase.Day,
								AnnouncementInstructionId:
									var announcementInstructionId
							} suppressionFact ||
						suppressionFact.TurnNumber != commit.TurnNumber ||
						announcementInstructionId !=
							commit.AnnouncementInstructionId) ||
					_actorBorrowedVillageIdiotPardonCommits.Any(commit =>
						!_players.TryGetValue(
							commit.PowerIdentity.ActingPlayerId,
							out var actor) ||
						((IPlayer)actor).State is not
						{
							CurrentRole: MainRoleType.Actor,
							DurableVotingPower: 0,
							HasVotingRight: false
						} ||
						commit.PublicMarkerLogIndex + 1 >= history.Count ||
						history[commit.PublicMarkerLogIndex + 1] is not
							VotingRightChangedLogEntry
							{
								CurrentPhase: GamePhase.Day,
								PlayerId: var affectedPlayerId,
								HasVotingRight: false,
								DurableVotingPower: 0
							} votingConsequence ||
						votingConsequence.TurnNumber != commit.TurnNumber ||
						affectedPlayerId !=
							commit.PowerIdentity.ActingPlayerId) ||
					_actorBorrowedWitchPotionUseCommits.Any(commit =>
						!_players.ContainsKey(commit.TargetPlayerId)) ||
					_actorBorrowedCupidLoversCommits.Any(commit =>
						!_players.ContainsKey(commit.FirstPlayerId) ||
						!_players.ContainsKey(commit.SecondPlayerId)))
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has invalid Actor borrowed Role Power state.");
				}

				foreach (var commit in _actorBorrowedCupidLoversCommits
					.Where(commit => commit.TurnNumber == 1))
				{
					var closureEntries = history
						.Select((entry, index) => (entry, index))
						.Where(candidate =>
							candidate.entry is FactionFactsCommittedLogEntry
							{
								Source.Kind: FactionFactSourceKind
									.InitialBeneficiaryClosure
							})
						.ToArray();
					if (closureEntries.Length == 0)
					{
						if (commit.Disposition !=
							ActorBorrowedCupidLoversDisposition
								.DeferredToInitialBeneficiaryClosure)
						{
							throw new InvalidOperationException(
								"The stable recovery snapshot has an Actor borrowed Cupid classification without its Initial Beneficiary Closure.");
						}

						continue;
					}

					if (closureEntries is not [var closure] ||
						closure.index <= commit.PublicMarkerLogIndex ||
						commit.Disposition ==
						ActorBorrowedCupidLoversDisposition
							.DeferredToInitialBeneficiaryClosure)
					{
						throw new InvalidOperationException(
							"The stable recovery snapshot has invalid Actor borrowed Cupid Initial Beneficiary Closure state.");
					}
				}
			}

			private bool
				HasValidActorBorrowedKnightRustySwordScheduleCommitSequence(
					IReadOnlyList<GameLogEntryBase> history)
			{
				if (_actorBorrowedKnightRustySwordScheduleCommits.Count > 1)
				{
					return false;
				}

				foreach (var commit in
					_actorBorrowedKnightRustySwordScheduleCommits)
				{
					var selectedCard = _actorSetupCards.Cards.SingleOrDefault(card =>
						card.Id == commit.ActorSetupCardId);
					if (!_players.ContainsKey(commit.TargetPlayerId) ||
						selectedCard?.PrintedRole !=
							MainRoleType.KnightWithRustySword ||
						commit.PowerIdentity.SourceRole !=
							MainRoleType.KnightWithRustySword ||
						!StringComparer.Ordinal.Equals(
							commit.PowerIdentity.SourcePowerIdentifier,
							ActorBorrowedKnightRustySwordScheduleCommit
								.ExpectedSourcePowerIdentifier) ||
						commit.PowerIdentity.PowerInstanceOrigin !=
							RolePowerInstanceOrigin.Borrowed ||
						!HasQualifyingActorBorrowedKnightScheduleHistory(
							history,
							commit.PowerIdentity.ActingPlayerId,
							commit.TurnNumber,
							commit.WerewolfAttackEliminationLogIndex,
							commit.CascadeScopeId,
							commit.PublicMarkerLogIndex) ||
						history.OfType<StatusEffectLogEntry>().Any(entry =>
							entry.TurnNumber == commit.TurnNumber &&
							entry.CurrentPhase == GamePhase.Dawn &&
							entry.PlayerId == commit.TargetPlayerId &&
							entry.EffectType ==
								StatusEffectTypes.RustySwordDisease))
					{
						return false;
					}
				}

				return true;
			}

			private static bool HasQualifyingActorBorrowedKnightScheduleHistory(
				IReadOnlyList<GameLogEntryBase> history,
				Guid actorId,
				int turnNumber,
				int werewolfAttackEliminationLogIndex,
				string cascadeScopeId,
				int markerLogIndex)
			{
				if (actorId == Guid.Empty ||
					turnNumber < 1 ||
					!StringComparer.Ordinal.Equals(
						cascadeScopeId,
						$"Dawn:{turnNumber}") ||
					werewolfAttackEliminationLogIndex < 0 ||
					werewolfAttackEliminationLogIndex >= markerLogIndex ||
					markerLogIndex > history.Count ||
					history[werewolfAttackEliminationLogIndex] is not
						PlayerEliminatedLogEntry
						{
							CurrentPhase: GamePhase.Dawn,
							PlayerId: var eliminatedPlayerId,
							Reason: EliminationReason.WerewolfAttack
						} eliminated ||
					eliminated.TurnNumber != turnNumber ||
					eliminatedPlayerId != actorId)
				{
					return false;
				}

				var determinationIndex = -1;
				for (var index = 0;
					index < werewolfAttackEliminationLogIndex;
					index++)
				{
					if (history[index] is DawnVictimDeterminedLogEntry
						{
							CurrentPhase: GamePhase.Dawn,
							PlayerId: var determinedPlayerId,
							Reason: EliminationReason.WerewolfAttack
						} determination &&
						determination.TurnNumber == turnNumber &&
						determinedPlayerId == actorId)
					{
						determinationIndex = index;
					}
				}

				var expectedElimination = new EliminationCascadeElimination(
					actorId,
					EliminationReason.WerewolfAttack);
				var batchIndex = -1;
				var completionIndex = -1;
				for (var index = werewolfAttackEliminationLogIndex + 1;
					index < markerLogIndex;
					index++)
				{
					if (batchIndex < 0 &&
						history[index] is
							EliminationCascadeBatchResolvedLogEntry batch &&
						batch.CurrentPhase == GamePhase.Dawn &&
						batch.TurnNumber == turnNumber &&
						StringComparer.Ordinal.Equals(
							batch.ScopeId,
							cascadeScopeId) &&
						batch.RequestedEliminations.Contains(
							expectedElimination) &&
						batch.CommittedEliminations.Contains(
							expectedElimination))
					{
						batchIndex = index;
						continue;
					}

					if (batchIndex >= 0 &&
						history[index] is EliminationCascadeCompletedLogEntry
						{
							CurrentPhase: GamePhase.Dawn,
							ScopeId: var completedScopeId
						} completion &&
						completion.TurnNumber == turnNumber &&
						StringComparer.Ordinal.Equals(
							completedScopeId,
							cascadeScopeId))
					{
						completionIndex = index;
						break;
					}
				}

				return determinationIndex >= 0 &&
					batchIndex > werewolfAttackEliminationLogIndex &&
					completionIndex > batchIndex;
			}

			private bool HasValidActorBorrowedBearTamerGrowlCommitSequence(
				IReadOnlyList<GameLogEntryBase> history)
			{
				if (_actorBorrowedBearTamerGrowlCommits.Count > 1)
				{
					return false;
				}

				foreach (var commit in _actorBorrowedBearTamerGrowlCommits)
				{
					var selectedCard = _actorSetupCards.Cards.SingleOrDefault(card =>
						card.Id == commit.ActorSetupCardId);
					var sameDawnGrowls = history
						.Select((entry, index) => (entry, index))
						.Where(candidate =>
							candidate.entry is BearTamerGrowlOccurredLogEntry
							{
								CurrentPhase: GamePhase.Dawn
							} growl &&
							growl.TurnNumber == commit.TurnNumber)
						.ToArray();
					if (commit.PublicMarkerLogIndex + 1 >= history.Count ||
						history[commit.PublicMarkerLogIndex] is not
							ActorBorrowedRolePowerCommittedLogEntry marker ||
						marker.Timestamp != commit.Timestamp ||
						marker.TurnNumber != commit.TurnNumber ||
						marker.CurrentPhase != GamePhase.Dawn ||
						history[commit.PublicMarkerLogIndex + 1] is not
							BearTamerGrowlOccurredLogEntry
							{
								CurrentPhase: GamePhase.Dawn
							} growl ||
						growl.TurnNumber != commit.TurnNumber ||
						sameDawnGrowls is not [var soleGrowl] ||
						soleGrowl.index != commit.PublicMarkerLogIndex + 1 ||
						selectedCard?.PrintedRole != MainRoleType.BearTamer ||
						commit.PowerIdentity.SourceRole != MainRoleType.BearTamer ||
						!StringComparer.Ordinal.Equals(
							commit.PowerIdentity.SourcePowerIdentifier,
							ActorBorrowedBearTamerGrowlCommit
								.ExpectedSourcePowerIdentifier) ||
						commit.PowerIdentity.PowerInstanceOrigin !=
							RolePowerInstanceOrigin.Borrowed ||
						!_actorSetupCardSpendActivationIds.TryGetValue(
							commit.ActorSetupCardId,
							out var spentActivationId) ||
						spentActivationId != commit.PowerIdentity.PowerInstanceId)
					{
						return false;
					}
				}

				return true;
			}

			private bool HasValidActorBorrowedElderResistanceCommitSequence()
			{
				if (_actorBorrowedElderResistanceCommits
						.Select(commit => (
							commit.PowerIdentity,
							commit.TriggeringNightActionLogIndex))
						.Distinct().Count() !=
					_actorBorrowedElderResistanceCommits.Count ||
					_actorBorrowedElderResistanceCommits
						.Where(commit =>
							commit.RestoringWitchSaveLogIndex.HasValue)
						.Select(commit => commit.RestoringWitchSaveLogIndex!.Value)
						.Distinct().Count() !=
					_actorBorrowedElderResistanceCommits.Count(commit =>
						commit.RestoringWitchSaveLogIndex.HasValue))
				{
					return false;
				}

				foreach (var activationCommits in
					_actorBorrowedElderResistanceCommits.GroupBy(commit =>
						commit.PowerIdentity))
				{
					var orderedCommits = activationCommits
						.OrderBy(commit => commit.PublicMarkerLogIndex)
						.ToArray();
					for (var index = 1; index < orderedCommits.Length; index++)
					{
						var previous = orderedCommits[index - 1];
						var current = orderedCommits[index];
						if (previous.RestoringWitchSaveLogIndex is null ||
							previous.PublicMarkerLogIndex >=
								current.TriggeringNightActionLogIndex)
						{
							return false;
						}
					}
				}

				return true;
			}

			private bool HasValidActorBorrowedScapegoatCommitSequence(
				IReadOnlyList<GameLogEntryBase> history)
			{
				if (_actorBorrowedScapegoatTieReplacementCommits.Count > 1 ||
					_actorBorrowedScapegoatVoterRestrictionCommits.Count > 1)
				{
					return false;
				}

				var tieReplacement =
					_actorBorrowedScapegoatTieReplacementCommits.SingleOrDefault();
				var restriction =
					_actorBorrowedScapegoatVoterRestrictionCommits.SingleOrDefault();
				if (tieReplacement is null)
				{
					return restriction is null;
				}

				if (tieReplacement.TriggeringVoteOutcomeLogIndex < 0 ||
					tieReplacement.TriggeringVoteOutcomeLogIndex >=
						tieReplacement.PublicMarkerLogIndex ||
					tieReplacement.PublicMarkerLogIndex >= history.Count ||
					history[tieReplacement.TriggeringVoteOutcomeLogIndex] is not
						VoteOutcomeReportedLogEntry
						{
							CurrentPhase: GamePhase.Day,
							ReportedOutcomePlayerId: var reportedOutcomePlayerId
						} vote ||
					vote.TurnNumber != tieReplacement.TurnNumber ||
					reportedOutcomePlayerId != Guid.Empty)
				{
					return false;
				}

				var voteOrdinal = history
					.Take(tieReplacement.TriggeringVoteOutcomeLogIndex + 1)
					.OfType<VoteOutcomeReportedLogEntry>()
					.Count(entry =>
						entry.CurrentPhase == GamePhase.Day &&
						entry.TurnNumber == tieReplacement.TurnNumber);
				if (voteOrdinal != tieReplacement.VoteOrdinal ||
					!StringComparer.Ordinal.Equals(
						tieReplacement.CascadeScopeId,
						$"Day:{tieReplacement.TurnNumber}:Vote:{voteOrdinal}") ||
					history
						.Skip(tieReplacement.TriggeringVoteOutcomeLogIndex + 1)
						.Take(
							tieReplacement.PublicMarkerLogIndex -
							tieReplacement.TriggeringVoteOutcomeLogIndex - 1)
						.OfType<VoteOutcomeReportedLogEntry>()
						.Any(laterVote =>
							laterVote.CurrentPhase == GamePhase.Day &&
							laterVote.TurnNumber == tieReplacement.TurnNumber) ||
					history.OfType<ScapegoatTieReplacementLogEntry>()
						.Any(native => StringComparer.Ordinal.Equals(
							native.ScopeId,
							tieReplacement.CascadeScopeId)))
				{
					return false;
				}

				var actorPlayerId = tieReplacement.PowerIdentity.ActingPlayerId;
				var revealIndex = FindLogIndex(
					history,
					tieReplacement.TriggeringVoteOutcomeLogIndex + 1,
					tieReplacement.PublicMarkerLogIndex,
					entry => entry is RoleRevealLogEntry reveal &&
						reveal.CurrentPhase == GamePhase.Day &&
						reveal.TurnNumber == tieReplacement.TurnNumber &&
						reveal.RevealedRoles.TryGetValue(
							actorPlayerId,
							out var role) &&
						role == MainRoleType.Actor);
				var eliminationIndex = FindLogIndex(
					history,
					tieReplacement.PublicMarkerLogIndex + 1,
					history.Count,
					entry => entry is PlayerEliminatedLogEntry
					{
						CurrentPhase: GamePhase.Day,
						PlayerId: var playerId,
						Reason: EliminationReason.EventElimination
					} elimination &&
						elimination.TurnNumber == tieReplacement.TurnNumber &&
						playerId == actorPlayerId);
				var expectedElimination = new EliminationCascadeElimination(
					actorPlayerId,
					EliminationReason.EventElimination);
				var batchIndex = FindLogIndex(
					history,
					eliminationIndex + 1,
					history.Count,
					entry => entry is EliminationCascadeBatchResolvedLogEntry batch &&
						batch.CurrentPhase == GamePhase.Day &&
						batch.TurnNumber == tieReplacement.TurnNumber &&
						StringComparer.Ordinal.Equals(
							batch.ScopeId,
							tieReplacement.CascadeScopeId) &&
						batch.RequestedEliminations is [var requested] &&
						requested == expectedElimination &&
						batch.CommittedEliminations is [var committed] &&
						committed == expectedElimination);
				if (revealIndex < 0 ||
					eliminationIndex <= tieReplacement.PublicMarkerLogIndex ||
					batchIndex <= eliminationIndex ||
					!_players.TryGetValue(actorPlayerId, out var actor) ||
					((IPlayer)actor).State is not
					{
						CurrentRole: MainRoleType.Actor,
						PhysicalCharacterCardRole: MainRoleType.Actor,
						PubliclyRevealedRole: MainRoleType.Actor,
						Health: PlayerHealth.Dead
					})
				{
					return false;
				}

				if (restriction is null)
				{
					return true;
				}

				var candidates = restriction.CandidatePlayerIds.ToHashSet();
				var permitted = restriction.PermittedVoterIds.ToHashSet();
				var eliminatedBeforeRestriction = history
					.Take(restriction.PublicMarkerLogIndex)
					.OfType<PlayerEliminatedLogEntry>()
					.Select(elimination => elimination.PlayerId)
					.ToHashSet();
				var expectedCandidates = _players.Keys
					.Where(playerId =>
						!eliminatedBeforeRestriction.Contains(playerId))
					.ToHashSet();
				return restriction.PowerIdentity == tieReplacement.PowerIdentity &&
					restriction.ActorSetupCardId ==
						tieReplacement.ActorSetupCardId &&
					restriction.TieReplacementPublicMarkerLogIndex ==
						tieReplacement.PublicMarkerLogIndex &&
					restriction.TurnNumber == tieReplacement.TurnNumber &&
					restriction.CurrentPhase == tieReplacement.CurrentPhase &&
					StringComparer.Ordinal.Equals(
						restriction.CascadeScopeId,
						tieReplacement.CascadeScopeId) &&
					restriction.PublicMarkerLogIndex > batchIndex &&
					restriction.PublicMarkerLogIndex < history.Count &&
					restriction.CandidatePlayerIds.Count == candidates.Count &&
					candidates.SetEquals(expectedCandidates) &&
					restriction.PermittedVoterIds.Count == permitted.Count &&
					permitted.Count > 0 &&
					permitted.IsSubsetOf(candidates) &&
					!history.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
						.Any(native => StringComparer.Ordinal.Equals(
							native.ScopeId,
							restriction.CascadeScopeId));
			}

			private static int FindLogIndex(
				IReadOnlyList<GameLogEntryBase> history,
				int startIndex,
				int exclusiveUpperIndex,
				Func<GameLogEntryBase, bool> predicate)
			{
				if (startIndex < 0 ||
					exclusiveUpperIndex > history.Count ||
					startIndex >= exclusiveUpperIndex)
				{
					return -1;
				}

				for (var index = startIndex; index < exclusiveUpperIndex; index++)
				{
					if (predicate(history[index]))
					{
						return index;
					}
				}

				return -1;
			}

			private static bool
				HasQualifyingActorBorrowedElderSuppressionHistory(
					IReadOnlyList<GameLogEntryBase> history,
					Guid actorPlayerId,
					int turnNumber,
					int voteOutcomeLogIndex,
					string cascadeScopeId,
					int exclusiveUpperLogIndex)
			{
				if (voteOutcomeLogIndex < 0 ||
					voteOutcomeLogIndex >= exclusiveUpperLogIndex ||
					exclusiveUpperLogIndex > history.Count ||
					history[voteOutcomeLogIndex] is not
						VoteOutcomeReportedLogEntry
						{
							CurrentPhase: GamePhase.Day,
							ReportedOutcomePlayerId: var votedPlayerId
						} vote ||
					vote.TurnNumber != turnNumber ||
					votedPlayerId != actorPlayerId)
				{
					return false;
				}

				var voteOrdinal = history
					.Take(voteOutcomeLogIndex + 1)
					.OfType<VoteOutcomeReportedLogEntry>()
					.Count(entry =>
						entry.CurrentPhase == GamePhase.Day &&
						entry.TurnNumber == turnNumber);
				if (!StringComparer.Ordinal.Equals(
						cascadeScopeId,
						$"Day:{turnNumber}:Vote:{voteOrdinal}"))
				{
					return false;
				}

				var correlatedHistory = history
					.Skip(voteOutcomeLogIndex + 1)
					.Take(exclusiveUpperLogIndex - voteOutcomeLogIndex - 1)
					.ToArray();
				if (correlatedHistory.Any(entry =>
						entry is VoteOutcomeReportedLogEntry laterVote &&
						laterVote.CurrentPhase == GamePhase.Day &&
						laterVote.TurnNumber == turnNumber))
				{
					return false;
				}

				var revealIndex = Array.FindIndex(
					correlatedHistory,
					entry => entry is RoleRevealLogEntry reveal &&
						reveal.CurrentPhase == GamePhase.Day &&
						reveal.TurnNumber == turnNumber &&
						reveal.RevealedRoles.TryGetValue(
							actorPlayerId,
							out var revealedRole) &&
						revealedRole == MainRoleType.Actor);
				var eliminationIndex = Array.FindIndex(
					correlatedHistory,
					entry => entry is PlayerEliminatedLogEntry
					{
						CurrentPhase: GamePhase.Day,
						PlayerId: var eliminatedPlayerId,
						Reason: EliminationReason.DayVote
					} eliminated &&
					eliminated.TurnNumber == turnNumber &&
					eliminatedPlayerId == actorPlayerId);
				var expectedElimination = new EliminationCascadeElimination(
					actorPlayerId,
					EliminationReason.DayVote);
				var batchIndex = Array.FindIndex(
					correlatedHistory,
					entry => entry is EliminationCascadeBatchResolvedLogEntry batch &&
						batch.CurrentPhase == GamePhase.Day &&
						batch.TurnNumber == turnNumber &&
						StringComparer.Ordinal.Equals(
							batch.ScopeId,
							cascadeScopeId) &&
						batch.RequestedEliminations is [var requested] &&
						requested == expectedElimination &&
						batch.CommittedEliminations is [var committed] &&
						committed == expectedElimination);
				var completionIndex = Array.FindIndex(
					correlatedHistory,
					entry => entry is EliminationCascadeCompletedLogEntry
					{
						CurrentPhase: GamePhase.Day,
						ScopeId: var completedScopeId
					} completion &&
					completion.TurnNumber == turnNumber &&
					StringComparer.Ordinal.Equals(
						completedScopeId,
						cascadeScopeId));
				return revealIndex >= 0 &&
					eliminationIndex > revealIndex &&
					batchIndex > eliminationIndex &&
					completionIndex > batchIndex;
			}

			private static bool TryGetCommittedRolePowerIdentity(
				GameLogEntryBase entry,
				out RolePowerInstanceIdentity identity)
			{
				switch (entry)
				{
					case RecurringRolePowerCommittedLogEntry recurring:
						identity = recurring.PowerIdentity;
						return true;
					case TargetPrivateRolePowerCommittedLogEntry targetPrivate:
						identity = targetPrivate.PowerIdentity;
						return true;
					case LoversPairCommittedLogEntry loversPair:
						identity = loversPair.PowerIdentity;
						return true;
					case IOneUseRolePowerCommittedLogEntry oneUse:
						var resource = oneUse.ResourceIdentity;
						identity = new RolePowerInstanceIdentity(
							resource.ActingPlayerId,
							resource.SourceRole,
							resource.SourcePowerIdentifier,
							resource.PowerInstanceId,
							resource.PowerInstanceOrigin);
						return true;
					default:
						identity = default;
						return false;
				}
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
						case DevotedServantPublicSelfRevealCommittedLogEntry
							{ BindsCardOwnership: true } selfReveal:
							ApplyOwnershipObservation(
								projected,
								playerIds,
								selfReveal.RoleLockInVersion,
								selfReveal.ActingPlayerId,
								selfReveal.DevotedServantCardId,
								MainRoleType.DevotedServant);
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
						case DevotedServantRoleTakenCommittedLogEntry roleTake:
							ApplyDevotedServantRoleTakeProjection(
								projected,
								playerIds,
								roleTake);
							break;
						case ThiefOfferDeclinedLogEntry decline:
							ApplyThiefOfferDeclineProjection(projected, playerIds, decline);
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

			private void ApplyDevotedServantRoleTakeProjection(
				Dictionary<Guid, PhysicalCharacterCardState> projection,
				IReadOnlySet<Guid> playerIds,
				DevotedServantRoleTakenCommittedLogEntry entry)
			{
				var movement = entry.PhysicalCards;
				var expectedOwnerId = movement.ExpectedAcquiredCardOwnerPlayerId;
				if (entry.RoleLockInVersion != _roleLockIn.Version ||
					!playerIds.Contains(entry.ActingPlayerId) ||
					!playerIds.Contains(entry.VoteTargetId) ||
					!projection.TryGetValue(movement.OutgoingOwnedCardId, out var outgoing) ||
					outgoing.Zone != PhysicalCharacterCardZone.PlayerOwned ||
					outgoing.OwnerPlayerId != entry.ActingPlayerId ||
					outgoing.Card.PrintedRole != MainRoleType.DevotedServant ||
					!projection.TryGetValue(movement.AcquiredCardId, out var acquired) ||
					acquired.Card.PrintedRole != entry.ObservedPrintedRole ||
					!IsExpectedTargetCardState(
						projection,
						acquired,
						entry.VoteTargetId,
						expectedOwnerId))
				{
					throw new InvalidOperationException(
						"Devoted Servant physical-card transfer history is invalid.");
				}

				projection[movement.OutgoingOwnedCardId] = outgoing with
				{
					Zone = PhysicalCharacterCardZone.Discarded,
					OwnerPlayerId = null
				};
				projection[movement.AcquiredCardId] = acquired with
				{
					Zone = PhysicalCharacterCardZone.PlayerOwned,
					OwnerPlayerId = entry.ActingPlayerId
				};

				static bool IsExpectedTargetCardState(
					IReadOnlyDictionary<Guid, PhysicalCharacterCardState> projection,
					PhysicalCharacterCardState acquired,
					Guid targetId,
					Guid? expectedOwnerId) =>
					expectedOwnerId is { } ownerId
						? ownerId == targetId &&
						  acquired.Zone == PhysicalCharacterCardZone.PlayerOwned &&
						  acquired.OwnerPlayerId == targetId
						: acquired.Zone == PhysicalCharacterCardZone.DealPool &&
						  acquired.OwnerPlayerId is null &&
						  projection.Values.All(state =>
							  state.OwnerPlayerId != targetId);
			}

			private void ApplyThiefOfferDeclineProjection(
				Dictionary<Guid, PhysicalCharacterCardState> projection,
				IReadOnlySet<Guid> playerIds,
				ThiefOfferDeclinedLogEntry entry)
			{
				if (entry.RoleLockInVersion != _roleLockIn.Version ||
				    !playerIds.Contains(entry.PlayerId) ||
				    _roleLockIn.Offer1?.Id != entry.Offer1CardId ||
				    _roleLockIn.Offer2?.Id != entry.Offer2CardId ||
				    !projection.TryGetValue(entry.ThiefCardId, out var thief) ||
				    thief is not
				    {
					    Zone: PhysicalCharacterCardZone.PlayerOwned,
					    OwnerPlayerId: var ownerId
				    } || ownerId != entry.PlayerId ||
				    !projection.TryGetValue(entry.Offer1CardId, out var offer1) ||
				    offer1.Zone != PhysicalCharacterCardZone.Offer1 ||
				    !projection.TryGetValue(entry.Offer2CardId, out var offer2) ||
				    offer2.Zone != PhysicalCharacterCardZone.Offer2)
				{
					throw new InvalidOperationException(
						"Thief offer decline physical-card history is invalid.");
				}

				projection[entry.Offer1CardId] = offer1 with
				{
					Zone = PhysicalCharacterCardZone.SetAside
				};
				projection[entry.Offer2CardId] = offer2 with
				{
					Zone = PhysicalCharacterCardZone.SetAside
				};
			}

			private void ValidateFactionProjectionMatchesHistory()
			{
				var projection = FactionFactProjection.Create(
					_gameHistoryLog
						.GetAllLogEntries()
						.OfType<IFactionFactBatchLogEntry>()
						.Concat(_actorBorrowedCupidLoversCommits),
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
				var nativePairs = history
					.OfType<LoversPairCommittedLogEntry>()
					.ToArray();
				if (nativePairs.Length + _actorBorrowedCupidLoversCommits.Count > 1)
				{
					throw new InvalidOperationException(
						"The stable recovery snapshot has multiple Lovers pair commitments.");
				}

				var nativePair = nativePairs.SingleOrDefault();
				var actorPair = _actorBorrowedCupidLoversCommits.SingleOrDefault();
				var pairPlayerIds = nativePair?.PlayerIds ?? actorPair?.PlayerIds;
				var pairLogIndex = nativePair is not null
					? history.IndexOf(nativePair)
					: actorPair?.PublicMarkerLogIndex ?? -1;
				var relationshipCleared = pairPlayerIds is not null && history
					.Skip(pairLogIndex + 1)
					.OfType<PermanentRoleSwapCommittedLogEntry>()
					.Any(entry =>
						pairPlayerIds.Contains(entry.PlayerId) &&
						entry.StateChanges.RelationshipEffectsToClear.Contains(
							StatusEffectTypes.Lovers));
				var expectedLoverIds = relationshipCleared
					? []
					: pairPlayerIds?.ToHashSet() ?? [];

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

			if (!Enum.IsDefined(cursor.NextInstructionSemantic) ||
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

			if (cursor.Kind == DomainRecoveryCursorKind
				.ActorBorrowedStutteringJudgeSignalObservationCommit)
			{
				return ValidateActorBorrowedStutteringJudgeSignalObservationRecoveryCursor(
					dto,
					cursor,
					pendingModeratorInstruction);
			}

			if (cursor.CommittedDayActionType is not null)
			{
				throw new InvalidOperationException(
					"The domain recovery cursor is structurally invalid.");
			}

			if (cursor.Kind == DomainRecoveryCursorKind.ActorSetupCardSpendCommit)
			{
				return ValidateActorSetupCardSpendRecoveryCursor(
					dto,
					cursor,
					pendingModeratorInstruction);
			}

			if (cursor.Kind == DomainRecoveryCursorKind
				.ActorBorrowedWitchPotionUseCommit)
			{
				return ValidateActorBorrowedWitchPotionUseRecoveryCursor(
					dto,
					cursor,
					pendingModeratorInstruction);
			}

			if (cursor.Kind == DomainRecoveryCursorKind
				.ActorBorrowedWitchPotionDeclineCommit)
			{
				return ValidateActorBorrowedWitchPotionDeclineRecoveryCursor(
					dto,
					cursor,
					pendingModeratorInstruction);
			}

			var isTargetPrivate = cursor.Kind ==
				DomainRecoveryCursorKind.TargetPrivateRolePowerCommit;
			var isBorrowedTargetPrivate = isTargetPrivate &&
				cursor.PowerInstanceOrigin == RolePowerInstanceOrigin.Borrowed;
			if (!Enum.IsDefined(cursor.CommittedActionType) ||
			    cursor.CommittedActionType == NightActionType.Unknown ||
				cursor.CommittedTargetIds == null ||
				isBorrowedTargetPrivate &&
				cursor.CommittedTargetIds.Count != 1 ||
				isTargetPrivate && !isBorrowedTargetPrivate &&
				cursor.CommittedTargetIds.Count != 0 ||
				!isTargetPrivate &&
				cursor.CommittedTargetIds.Count == 0 ||
			    cursor.CommittedTargetIds.Any(targetId =>
				    targetId == Guid.Empty) ||
			    cursor.CommittedTargetIds.Distinct().Count() !=
				    cursor.CommittedTargetIds.Count)
			{
				throw new InvalidOperationException(
					"The domain recovery cursor is structurally invalid.");
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
				if (cursor.PowerInstanceOrigin ==
					RolePowerInstanceOrigin.Borrowed)
				{
					return ValidateActorBorrowedTargetPrivateRecoveryCursor(
						dto,
						cursor,
						pendingModeratorInstruction);
				}

                if (cursor.PowerIdentity is not { } targetPrivatePowerIdentity ||
					!targetPrivatePowerIdentity.IsValid ||
					cursor.ActorSetupCardId != Guid.Empty ||
					cursor.ActorBorrowedActivationId != Guid.Empty ||
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

			if (cursorPowerIdentity.PowerInstanceOrigin ==
				RolePowerInstanceOrigin.Borrowed)
			{
				return ValidateActorBorrowedRecurringRecoveryCursor(
					dto,
					cursor,
					pendingModeratorInstruction);
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

		private static DomainRecoveryCursor
			ValidateActorBorrowedWitchPotionUseRecoveryCursor(
				GameSessionDto dto,
				DomainRecoveryCursor cursor,
				ModeratorInstruction pendingModeratorInstruction)
		{
			ActorBorrowedRolePowerActivation active;
			ActorBorrowedWitchPotionUseCommit commit;
			try
			{
				active = dto.ActiveActorBorrowedRolePowerActivation?.ToValue()
					?? throw new InvalidOperationException();
				var matchingCommitDtos =
					dto.ActorBorrowedWitchPotionUseCommits?
						.Where(candidate =>
							candidate.PowerIdentity == cursor.PowerIdentity &&
							candidate.SpentResourceIdentity ==
								cursor.ResourceIdentity)
						.ToArray();
				if (matchingCommitDtos is not [var commitDto])
				{
					throw new InvalidOperationException();
				}

				commit = commitDto.ToValue();
				commit.EnforceValidity();
			}
			catch (Exception exception) when (
				exception is ArgumentException or InvalidOperationException)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Witch recovery cursor is structurally invalid.");
			}

			var powerIdentity = cursor.PowerIdentity;
			var resourceIdentity = cursor.ResourceIdentity;
			var setupCard = dto.ActorSetupCards?.Cards?.SingleOrDefault(card =>
				card.Id == cursor.ActorSetupCardId);
			var actingPlayer = dto.Players.SingleOrDefault(player =>
				player.Id == cursor.ActingPlayerId);
			var targetId = cursor.CommittedTargetIds is [var committedTargetId]
				? committedTargetId
				: Guid.Empty;
			var targetPlayer = dto.Players.SingleOrDefault(player =>
				player.Id == targetId);
			var marker = commit.PublicMarkerLogIndex >= 0 &&
				commit.PublicMarkerLogIndex < dto.GameHistoryLog.Count
				? dto.GameHistoryLog[commit.PublicMarkerLogIndex] as
					ActorBorrowedRolePowerCommittedLogEntry
				: null;
			var latestMarkerIndex = dto.GameHistoryLog.FindLastIndex(entry =>
				entry is ActorBorrowedRolePowerCommittedLogEntry);
			var committedActionType =
				ActorBorrowedWitchPotionUseCommit.GetActionType(
					commit.SpentResourceIdentity);
			var resourceId = resourceIdentity?.OneUseResourceId ?? Guid.Empty;
			var hasExpectedContinuation = committedActionType switch
			{
				NightActionType.WitchSave =>
					(pendingModeratorInstruction is SelectPlayersInstruction
					{
						Semantic:
							ModeratorInstructionSemantic.SelectWitchPoisonTarget,
						CountConstraint: var countConstraint,
						RoleIdentification: null,
						AffectedPlayerIds: [var poisonAffectedPlayerId]
					} &&
					countConstraint == NumberRangeConstraint.SingleOptional &&
					poisonAffectedPlayerId == cursor.ActingPlayerId) ||
					pendingModeratorInstruction is ConfirmationInstruction
					{
						Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
						AffectedPlayerIds: [var healingSleepAffectedPlayerId]
					} &&
					healingSleepAffectedPlayerId == cursor.ActingPlayerId,
				NightActionType.WitchKill =>
					pendingModeratorInstruction is ConfirmationInstruction
					{
						Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
						AffectedPlayerIds: [var poisonSleepAffectedPlayerId]
					} &&
					poisonSleepAffectedPlayerId == cursor.ActingPlayerId,
				_ => false
			};
			if (powerIdentity is not { IsValid: true } ||
				powerIdentity.Value.PowerInstanceOrigin !=
					RolePowerInstanceOrigin.Borrowed ||
				cursor.SourceRole != MainRoleType.Witch ||
				!StringComparer.Ordinal.Equals(
					cursor.SourcePowerIdentifier,
					ActorBorrowedWitchPotionUseCommit
						.ExpectedSourcePowerIdentifier) ||
				resourceIdentity is not { IsValid: true } ||
				resourceId != ActorBorrowedWitchPotionUseCommit.HealingResourceId &&
				resourceId != ActorBorrowedWitchPotionUseCommit.PoisonResourceId ||
				cursor.CommittedActionType != committedActionType ||
				cursor.ActorSetupCardId == Guid.Empty ||
				cursor.ActorBorrowedActivationId == Guid.Empty ||
				cursor.PowerInstanceId != cursor.ActorBorrowedActivationId ||
				active.ActivationId != cursor.ActorBorrowedActivationId ||
				active.ActingPlayerId != cursor.ActingPlayerId ||
				active.ActingRole != MainRoleType.Actor ||
				active.SelectedCardId != cursor.ActorSetupCardId ||
				active.SourceRole != MainRoleType.Witch ||
				setupCard?.PrintedRole != MainRoleType.Witch ||
				dto.ActorSetupCardSpends is not { } spends ||
				spends.Count(spend =>
					spend.CardId == cursor.ActorSetupCardId &&
					spend.ActivationId == cursor.ActorBorrowedActivationId) != 1 ||
				actingPlayer is not
				{
					MainRole: MainRoleType.Actor,
					Health: PlayerHealth.Alive
				} ||
				targetPlayer is not { Health: PlayerHealth.Alive } ||
				commit.PowerIdentity != powerIdentity.Value ||
				commit.ActorSetupCardId != cursor.ActorSetupCardId ||
				commit.SpentResourceIdentity != resourceIdentity.Value ||
				commit.TargetPlayerId != targetId ||
				commit.TurnNumber != dto.TurnNumber ||
				commit.CurrentPhase != GamePhase.Night ||
				commit.PublicMarkerLogIndex != latestMarkerIndex ||
				marker is null ||
				marker.Timestamp != commit.Timestamp ||
				marker.TurnNumber != commit.TurnNumber ||
				marker.CurrentPhase != commit.CurrentPhase ||
				dto.GameHistoryLog.Any(entry =>
					TryGetCommittedRolePowerIdentity(entry, out var publicIdentity) &&
					publicIdentity.PowerInstanceOrigin ==
						RolePowerInstanceOrigin.Borrowed) ||
				dto.GameHistoryLog
					.OfType<NightActionLogEntry>()
					.Any(entry =>
						entry.TurnNumber == dto.TurnNumber &&
						entry.CurrentPhase == GamePhase.Night &&
						entry.ActionType is NightActionType.WitchSave or
							NightActionType.WitchKill) ||
				!hasExpectedContinuation)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Witch recovery cursor does not match committed state.");
			}

			return cursor;
		}

		private static DomainRecoveryCursor
			ValidateActorBorrowedWitchPotionDeclineRecoveryCursor(
				GameSessionDto dto,
				DomainRecoveryCursor cursor,
				ModeratorInstruction pendingModeratorInstruction)
		{
			ActorBorrowedRolePowerActivation active;
			ActorBorrowedWitchPotionDeclineCommit commit;
			try
			{
				active = dto.ActiveActorBorrowedRolePowerActivation?.ToValue()
					?? throw new InvalidOperationException();
				var matchingCommitDtos =
					dto.ActorBorrowedWitchPotionDeclineCommits?
						.Where(candidate =>
							candidate.PowerIdentity == cursor.PowerIdentity &&
							candidate.OfferedResourceIdentity ==
								cursor.ResourceIdentity)
						.ToArray();
				if (matchingCommitDtos is not [var commitDto])
				{
					throw new InvalidOperationException();
				}

				commit = commitDto.ToValue();
				commit.EnforceValidity();
			}
			catch (Exception exception) when (
				exception is ArgumentException or InvalidOperationException)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Witch decline recovery cursor is structurally invalid.");
			}

			var powerIdentity = cursor.PowerIdentity;
			var resourceIdentity = cursor.ResourceIdentity;
			var setupCard = dto.ActorSetupCards?.Cards?.SingleOrDefault(card =>
				card.Id == cursor.ActorSetupCardId);
			var actingPlayer = dto.Players.SingleOrDefault(player =>
				player.Id == cursor.ActingPlayerId);
			var marker = commit.PublicMarkerLogIndex >= 0 &&
				commit.PublicMarkerLogIndex < dto.GameHistoryLog.Count
					? dto.GameHistoryLog[commit.PublicMarkerLogIndex] as
						ActorBorrowedRolePowerCommittedLogEntry
					: null;
			var latestMarkerIndex = dto.GameHistoryLog.FindLastIndex(entry =>
				entry is ActorBorrowedRolePowerCommittedLogEntry);
			var committedActionType =
				ActorBorrowedWitchPotionDeclineCommit.GetOfferedActionType(
					commit.OfferedResourceIdentity);
			var resourceId = resourceIdentity?.OneUseResourceId ?? Guid.Empty;
			var hasExpectedContinuation = committedActionType switch
			{
				NightActionType.WitchSave =>
					(pendingModeratorInstruction is SelectPlayersInstruction
					{
						Semantic:
							ModeratorInstructionSemantic.SelectWitchPoisonTarget,
						CountConstraint: var countConstraint,
						RoleIdentification: null,
						AffectedPlayerIds: [var poisonAffectedPlayerId]
					} &&
					countConstraint == NumberRangeConstraint.SingleOptional &&
					poisonAffectedPlayerId == cursor.ActingPlayerId) ||
					pendingModeratorInstruction is ConfirmationInstruction
					{
						Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
						AffectedPlayerIds: [var healingSleepAffectedPlayerId]
					} &&
					healingSleepAffectedPlayerId == cursor.ActingPlayerId,
				NightActionType.WitchKill =>
					pendingModeratorInstruction is ConfirmationInstruction
					{
						Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
						AffectedPlayerIds: [var poisonSleepAffectedPlayerId]
					} &&
					poisonSleepAffectedPlayerId == cursor.ActingPlayerId,
				_ => false
			};
			if (powerIdentity is not { IsValid: true } ||
				powerIdentity.Value.PowerInstanceOrigin !=
					RolePowerInstanceOrigin.Borrowed ||
				cursor.SourceRole != MainRoleType.Witch ||
				!StringComparer.Ordinal.Equals(
					cursor.SourcePowerIdentifier,
					ActorBorrowedWitchPotionUseCommit
						.ExpectedSourcePowerIdentifier) ||
				resourceIdentity is not { IsValid: true } ||
				resourceId != ActorBorrowedWitchPotionUseCommit.HealingResourceId &&
				resourceId != ActorBorrowedWitchPotionUseCommit.PoisonResourceId ||
				cursor.CommittedActionType != committedActionType ||
				cursor.CommittedTargetIds is not { Count: 0 } ||
				cursor.ActorSetupCardId == Guid.Empty ||
				cursor.ActorBorrowedActivationId == Guid.Empty ||
				cursor.PowerInstanceId != cursor.ActorBorrowedActivationId ||
				active.ActivationId != cursor.ActorBorrowedActivationId ||
				active.ActingPlayerId != cursor.ActingPlayerId ||
				active.ActingRole != MainRoleType.Actor ||
				active.SelectedCardId != cursor.ActorSetupCardId ||
				active.SourceRole != MainRoleType.Witch ||
				setupCard?.PrintedRole != MainRoleType.Witch ||
				dto.ActorSetupCardSpends is not { } spends ||
				spends.Count(spend =>
					spend.CardId == cursor.ActorSetupCardId &&
					spend.ActivationId == cursor.ActorBorrowedActivationId) != 1 ||
				actingPlayer is not
				{
					MainRole: MainRoleType.Actor,
					Health: PlayerHealth.Alive
				} ||
				commit.PowerIdentity != powerIdentity.Value ||
				commit.ActorSetupCardId != cursor.ActorSetupCardId ||
				commit.OfferedResourceIdentity != resourceIdentity.Value ||
				commit.TurnNumber != dto.TurnNumber ||
				commit.CurrentPhase != GamePhase.Night ||
				commit.PublicMarkerLogIndex != latestMarkerIndex ||
				marker is null ||
				marker.Timestamp != commit.Timestamp ||
				marker.TurnNumber != commit.TurnNumber ||
				marker.CurrentPhase != commit.CurrentPhase ||
				dto.ActorBorrowedWitchPotionUseCommits?.Any(candidate =>
					candidate.SpentResourceIdentity == resourceIdentity.Value) == true ||
				dto.GameHistoryLog.Any(entry =>
					TryGetCommittedRolePowerIdentity(entry, out var publicIdentity) &&
					publicIdentity.PowerInstanceOrigin ==
						RolePowerInstanceOrigin.Borrowed) ||
				dto.GameHistoryLog
					.OfType<NightActionLogEntry>()
					.Any(entry =>
						entry.TurnNumber == dto.TurnNumber &&
						entry.CurrentPhase == GamePhase.Night &&
						entry.ActionType is NightActionType.WitchSave or
							NightActionType.WitchKill) ||
				!hasExpectedContinuation)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Witch decline recovery cursor does not match committed state.");
			}

			return cursor;
		}

		private static DomainRecoveryCursor
			ValidateActorBorrowedStutteringJudgeSignalObservationRecoveryCursor(
				GameSessionDto dto,
				DomainRecoveryCursor cursor,
				ModeratorInstruction pendingModeratorInstruction)
		{
			ActorBorrowedRolePowerActivation active;
			ActorBorrowedStutteringJudgeSignalSetupCommit setup;
			ActorBorrowedStutteringJudgeSignalObservationCommit observation;
			try
			{
				active = dto.ActiveActorBorrowedRolePowerActivation?.ToValue()
					?? throw new InvalidOperationException();
				var matchingSetupDtos =
					dto.ActorBorrowedStutteringJudgeSignalSetupCommits?
						.Where(candidate =>
							candidate.PowerIdentity == cursor.PowerIdentity)
						.ToArray();
				var matchingObservationDtos =
					dto.ActorBorrowedStutteringJudgeSignalObservationCommits?
						.Where(candidate =>
							candidate.PowerIdentity == cursor.PowerIdentity)
						.ToArray();
				if (matchingSetupDtos is not [var setupDto] ||
					matchingObservationDtos is not [var observationDto])
				{
					throw new InvalidOperationException();
				}

				setup = setupDto.ToValue();
				observation = observationDto.ToValue();
				setup.EnforceValidity();
				observation.EnforceValidity();
			}
			catch (Exception exception) when (
				exception is ArgumentException or InvalidOperationException)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Stuttering Judge recovery cursor is structurally invalid.");
			}

			var powerIdentity = cursor.PowerIdentity;
			var resourceIdentity = cursor.ResourceIdentity;
			var setupCard = dto.ActorSetupCards?.Cards?.SingleOrDefault(card =>
				card.Id == cursor.ActorSetupCardId);
			var actingPlayer = dto.Players.SingleOrDefault(player =>
				player.Id == cursor.ActingPlayerId);
			var marker = observation.PublicMarkerLogIndex <
				dto.GameHistoryLog.Count
				? dto.GameHistoryLog[observation.PublicMarkerLogIndex] as
					ActorBorrowedRolePowerCommittedLogEntry
				: null;
			var latestMarkerIndex = dto.GameHistoryLog.FindLastIndex(entry =>
				entry is ActorBorrowedRolePowerCommittedLogEntry);
			var currentDayVoteOutcomeCount = dto.GameHistoryLog
				.OfType<VoteOutcomeReportedLogEntry>()
				.Count(entry =>
					entry.CurrentPhase == GamePhase.Day &&
					entry.TurnNumber == dto.TurnNumber);
			var livingPlayerIds = dto.Players
				.Where(player => player.Health == PlayerHealth.Alive)
				.Select(player => player.Id)
				.ToHashSet();
			if (dto.PhaseStateCache.CurrentPhase != GamePhase.Day ||
				dto.PhaseStateCache.SubPhase != "NormalVoting" ||
				powerIdentity is not { IsValid: true } ||
				powerIdentity.Value.PowerInstanceOrigin !=
					RolePowerInstanceOrigin.Borrowed ||
				cursor.SourceRole != MainRoleType.StutteringJudge ||
				cursor.CommittedActionType != NightActionType.Unknown ||
				cursor.CommittedDayActionType != DayPowerType.JudgeExtraVote ||
				!StringComparer.Ordinal.Equals(
					cursor.SourcePowerIdentifier,
					ActorBorrowedStutteringJudgeSignalSetupCommit
						.ExpectedSourcePowerIdentifier) ||
				resourceIdentity is not { IsValid: true } ||
				resourceIdentity.Value.OneUseResourceId !=
					ActorBorrowedStutteringJudgeSignalObservationCommit
						.ExpectedOneUseResourceId ||
				cursor.ActorSetupCardId == Guid.Empty ||
				cursor.ActorBorrowedActivationId == Guid.Empty ||
				cursor.PowerInstanceId != cursor.ActorBorrowedActivationId ||
				powerIdentity.Value.ActingPlayerId != cursor.ActingPlayerId ||
				powerIdentity.Value.SourceRole != cursor.SourceRole ||
				!StringComparer.Ordinal.Equals(
					powerIdentity.Value.SourcePowerIdentifier,
					cursor.SourcePowerIdentifier) ||
				powerIdentity.Value.PowerInstanceId !=
					cursor.ActorBorrowedActivationId ||
				cursor.CommittedTargetIds is not { Count: 0 } ||
				active.ActivationId != cursor.ActorBorrowedActivationId ||
				active.ActingPlayerId != cursor.ActingPlayerId ||
				active.ActingRole != MainRoleType.Actor ||
				active.SelectedCardId != cursor.ActorSetupCardId ||
				active.SourceRole != MainRoleType.StutteringJudge ||
				setupCard?.PrintedRole != MainRoleType.StutteringJudge ||
				dto.ActorSetupCardSpends is not { } spends ||
				spends.Count(spend =>
					spend.ActivationId == cursor.ActorBorrowedActivationId) != 1 ||
				!spends.Any(spend =>
					spend.ActivationId == cursor.ActorBorrowedActivationId &&
					spend.CardId == cursor.ActorSetupCardId) ||
				actingPlayer is not
				{
					MainRole: MainRoleType.Actor,
					Health: PlayerHealth.Alive
				} ||
				setup.PowerIdentity != powerIdentity.Value ||
				setup.ActorSetupCardId != cursor.ActorSetupCardId ||
				setup.TurnNumber != dto.TurnNumber ||
				setup.CurrentPhase != GamePhase.Night ||
				observation.PowerIdentity != powerIdentity.Value ||
				observation.ActorSetupCardId != cursor.ActorSetupCardId ||
				!observation.SignalOccurred ||
				observation.SpentResourceIdentity != resourceIdentity.Value ||
				observation.TurnNumber != dto.TurnNumber ||
				observation.CurrentPhase != GamePhase.Day ||
				observation.PublicMarkerLogIndex != latestMarkerIndex ||
				marker is null ||
				marker.Timestamp != observation.Timestamp ||
				marker.TurnNumber != observation.TurnNumber ||
				marker.CurrentPhase != observation.CurrentPhase ||
				currentDayVoteOutcomeCount != 0 ||
				dto.GameHistoryLog.Any(entry =>
					TryGetCommittedRolePowerIdentity(entry, out var publicIdentity) &&
					publicIdentity.PowerInstanceOrigin ==
						RolePowerInstanceOrigin.Borrowed) ||
				dto.GameHistoryLog
					.OfType<DayActionLogEntry>()
					.Any(entry =>
						entry.CurrentPhase == GamePhase.Day &&
						entry.TurnNumber == dto.TurnNumber &&
						entry.ActionType == DayPowerType.JudgeExtraVote) ||
				pendingModeratorInstruction is not SelectPlayersInstruction
				{
					Semantic: ModeratorInstructionSemantic.RecordDayVote,
					CountConstraint: var countConstraint,
					RoleIdentification: null,
					AffectedPlayerIds: null
				} voteInstruction ||
				countConstraint != NumberRangeConstraint.SingleOptional ||
				!voteInstruction.SelectablePlayerIds.SetEquals(livingPlayerIds))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Stuttering Judge recovery cursor does not match committed state.");
			}

			return cursor;
		}

		private static DomainRecoveryCursor
			ValidateActorBorrowedTargetPrivateRecoveryCursor(
				GameSessionDto dto,
				DomainRecoveryCursor cursor,
				ModeratorInstruction pendingModeratorInstruction)
		{
			if (cursor.SourceRole == MainRoleType.Fox)
			{
				return ValidateActorBorrowedFoxTargetPrivateRecoveryCursor(
					dto,
					cursor,
					pendingModeratorInstruction);
			}

			ActorBorrowedRolePowerActivation active;
			ActorBorrowedSeerCheckCommit commit;
			try
			{
				active = dto.ActiveActorBorrowedRolePowerActivation?.ToValue()
					?? throw new InvalidOperationException();
				var matchingCommitDtos = dto.ActorBorrowedSeerCheckCommits?
					.Where(candidate =>
						candidate.PowerIdentity.PowerInstanceId ==
						cursor.PowerInstanceId)
					.ToArray();
				if (matchingCommitDtos is not [var commitDto])
				{
					throw new InvalidOperationException();
				}

				commit = commitDto.ToValue();
				commit.EnforceValidity();
			}
			catch (Exception exception) when (
				exception is ArgumentException or InvalidOperationException)
			{
				throw new InvalidOperationException(
					"The Actor borrowed target-private recovery cursor is structurally invalid.");
			}

			var powerIdentity = cursor.PowerIdentity;
			var setupCard = dto.ActorSetupCards?.Cards?.SingleOrDefault(card =>
				card.Id == cursor.ActorSetupCardId);
			var actingPlayer = dto.Players.SingleOrDefault(player =>
				player.Id == cursor.ActingPlayerId);
			var targetId = cursor.CommittedTargetIds.Single();
			var targetPlayer = dto.Players.SingleOrDefault(player =>
				player.Id == targetId);
			var marker = commit.PublicMarkerLogIndex < dto.GameHistoryLog.Count
				? dto.GameHistoryLog[commit.PublicMarkerLogIndex] as
					ActorBorrowedRolePowerCommittedLogEntry
				: null;
			var latestMarkerIndex = dto.GameHistoryLog.FindLastIndex(entry =>
				entry is ActorBorrowedRolePowerCommittedLogEntry);
			if (powerIdentity is not { IsValid: true } ||
				powerIdentity.Value.PowerInstanceOrigin !=
					RolePowerInstanceOrigin.Borrowed ||
				cursor.SourceRole != MainRoleType.Seer ||
				cursor.CommittedActionType != NightActionType.SeerCheck ||
				string.IsNullOrWhiteSpace(cursor.SourcePowerIdentifier) ||
				cursor.OneUseResourceId != Guid.Empty ||
				cursor.ActorSetupCardId == Guid.Empty ||
				cursor.ActorBorrowedActivationId == Guid.Empty ||
				cursor.PowerInstanceId != cursor.ActorBorrowedActivationId ||
				active.ActivationId != cursor.ActorBorrowedActivationId ||
				active.ActingPlayerId != cursor.ActingPlayerId ||
				active.ActingRole != MainRoleType.Actor ||
				active.SelectedCardId != cursor.ActorSetupCardId ||
				active.SourceRole != MainRoleType.Seer ||
				setupCard?.PrintedRole != MainRoleType.Seer ||
				dto.ActorSetupCardSpends is not { } spends ||
				spends.Count(spend =>
					spend.CardId == cursor.ActorSetupCardId &&
					spend.ActivationId == cursor.ActorBorrowedActivationId) != 1 ||
				actingPlayer is not
				{
					MainRole: MainRoleType.Actor,
					Health: PlayerHealth.Alive
				} ||
				targetPlayer is not { Health: PlayerHealth.Alive } ||
				targetPlayer.FactionAgentKnowledge is not { } targetAgentFacts ||
				!targetAgentFacts.TryGetValue(
					Faction.Werewolf,
					out var currentTargetKnowledge) ||
				currentTargetKnowledge != commit.TargetAgentKnowledge ||
				targetId == cursor.ActingPlayerId ||
				commit.PowerIdentity != powerIdentity.Value ||
				commit.ActorSetupCardId != cursor.ActorSetupCardId ||
				commit.TargetPlayerId != targetId ||
				commit.TurnNumber != dto.TurnNumber ||
				commit.CurrentPhase != GamePhase.Night ||
				commit.PublicMarkerLogIndex != latestMarkerIndex ||
				marker is null ||
				marker.Timestamp != commit.Timestamp ||
				marker.TurnNumber != commit.TurnNumber ||
				marker.CurrentPhase != commit.CurrentPhase ||
				dto.GameHistoryLog.Any(entry =>
					TryGetCommittedRolePowerIdentity(entry, out var publicIdentity) &&
					publicIdentity.PowerInstanceOrigin ==
						RolePowerInstanceOrigin.Borrowed) ||
				dto.GameHistoryLog
					.OfType<NightActionLogEntry>()
					.Any(entry =>
						entry.TurnNumber == dto.TurnNumber &&
						entry.CurrentPhase == GamePhase.Night &&
						entry.ActionType == NightActionType.SeerCheck) ||
				pendingModeratorInstruction is not ConfirmationInstruction
				{
					Semantic: ModeratorInstructionSemantic.RevealSeerResult,
					PublicAnnouncement: null,
					PrivateInstruction: not null,
					AffectedPlayerIds: { Count: 1 } affectedPlayerIds
				} ||
				affectedPlayerIds[0] != cursor.ActingPlayerId)
			{
				throw new InvalidOperationException(
					"The Actor borrowed target-private recovery cursor does not match committed state.");
			}

			return cursor;
		}

		private static DomainRecoveryCursor
			ValidateActorBorrowedFoxTargetPrivateRecoveryCursor(
				GameSessionDto dto,
				DomainRecoveryCursor cursor,
				ModeratorInstruction pendingModeratorInstruction)
		{
			ActorBorrowedRolePowerActivation active;
			ActorBorrowedFoxCheckCommit commit;
			try
			{
				active = dto.ActiveActorBorrowedRolePowerActivation?.ToValue()
					?? throw new InvalidOperationException();
				var matchingCommitDtos = dto.ActorBorrowedFoxCheckCommits?
					.Where(candidate =>
						candidate.PowerIdentity.PowerInstanceId ==
						cursor.PowerInstanceId)
					.ToArray();
				if (matchingCommitDtos is not [var commitDto])
				{
					throw new InvalidOperationException();
				}

				commit = commitDto.ToValue();
				commit.EnforceValidity();
			}
			catch (Exception exception) when (
				exception is ArgumentException or InvalidOperationException)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Fox recovery cursor is structurally invalid.");
			}

			var powerIdentity = cursor.PowerIdentity;
			var setupCard = dto.ActorSetupCards?.Cards?.SingleOrDefault(card =>
				card.Id == cursor.ActorSetupCardId);
			var actingPlayer = dto.Players.SingleOrDefault(player =>
				player.Id == cursor.ActingPlayerId);
			var centerId = cursor.CommittedTargetIds.Single();
			var centerPlayer = dto.Players.SingleOrDefault(player =>
				player.Id == centerId);
			var marker = commit.PublicMarkerLogIndex < dto.GameHistoryLog.Count
				? dto.GameHistoryLog[commit.PublicMarkerLogIndex] as
					ActorBorrowedRolePowerCommittedLogEntry
				: null;
			var latestMarkerIndex = dto.GameHistoryLog.FindLastIndex(entry =>
				entry is ActorBorrowedRolePowerCommittedLogEntry);
			var spentResourceId =
				commit.SpentResourceIdentity?.OneUseResourceId ?? Guid.Empty;
			if (powerIdentity is not { IsValid: true } ||
				powerIdentity.Value.PowerInstanceOrigin !=
					RolePowerInstanceOrigin.Borrowed ||
				cursor.SourceRole != MainRoleType.Fox ||
				cursor.CommittedActionType != NightActionType.FoxCheck ||
				string.IsNullOrWhiteSpace(cursor.SourcePowerIdentifier) ||
				cursor.OneUseResourceId != spentResourceId ||
				commit.SpentResourceIdentity is { } spentResourceIdentity &&
					cursor.ResourceIdentity != spentResourceIdentity ||
				cursor.ActorSetupCardId == Guid.Empty ||
				cursor.ActorBorrowedActivationId == Guid.Empty ||
				cursor.PowerInstanceId != cursor.ActorBorrowedActivationId ||
				active.ActivationId != cursor.ActorBorrowedActivationId ||
				active.ActingPlayerId != cursor.ActingPlayerId ||
				active.ActingRole != MainRoleType.Actor ||
				active.SelectedCardId != cursor.ActorSetupCardId ||
				active.SourceRole != MainRoleType.Fox ||
				setupCard?.PrintedRole != MainRoleType.Fox ||
				dto.ActorSetupCardSpends is not { } spends ||
				spends.Count(spend =>
					spend.CardId == cursor.ActorSetupCardId &&
					spend.ActivationId == cursor.ActorBorrowedActivationId) != 1 ||
				actingPlayer is not
				{
					MainRole: MainRoleType.Actor,
					Health: PlayerHealth.Alive
				} ||
				centerPlayer is not { Health: PlayerHealth.Alive } ||
				commit.PowerIdentity != powerIdentity.Value ||
				commit.ActorSetupCardId != cursor.ActorSetupCardId ||
				commit.CenterPlayerId != centerId ||
				commit.TurnNumber != dto.TurnNumber ||
				commit.CurrentPhase != GamePhase.Night ||
				commit.PublicMarkerLogIndex != latestMarkerIndex ||
				marker is null ||
				marker.Timestamp != commit.Timestamp ||
				marker.TurnNumber != commit.TurnNumber ||
				marker.CurrentPhase != commit.CurrentPhase ||
				dto.GameHistoryLog.Any(entry =>
					TryGetCommittedRolePowerIdentity(entry, out var publicIdentity) &&
					publicIdentity.PowerInstanceOrigin ==
						RolePowerInstanceOrigin.Borrowed) ||
				dto.GameHistoryLog
					.OfType<NightActionLogEntry>()
					.Any(entry =>
						entry.TurnNumber == dto.TurnNumber &&
						entry.CurrentPhase == GamePhase.Night &&
						entry.ActionType == NightActionType.FoxCheck) ||
				pendingModeratorInstruction is not ConfirmationInstruction
				{
					Semantic: ModeratorInstructionSemantic.RevealFoxResult,
					PublicAnnouncement: null,
					PrivateInstruction: not null,
					AffectedPlayerIds: { Count: 1 } affectedPlayerIds
				} ||
				affectedPlayerIds[0] != cursor.ActingPlayerId)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Fox recovery cursor does not match committed state.");
			}

			return cursor;
		}

		private static DomainRecoveryCursor
			ValidateActorBorrowedRecurringRecoveryCursor(
				GameSessionDto dto,
				DomainRecoveryCursor cursor,
				ModeratorInstruction pendingModeratorInstruction)
		{
			if (cursor.SourceRole == MainRoleType.Cupid)
			{
				return ValidateActorBorrowedCupidRecurringRecoveryCursor(
					dto,
					cursor,
					pendingModeratorInstruction);
			}

			ActorBorrowedRolePowerActivation active;
			ActorBorrowedDefenderProtectionCommit commit;
			try
			{
				active = dto.ActiveActorBorrowedRolePowerActivation?.ToValue()
					?? throw new InvalidOperationException();
				var matchingCommitDtos =
					dto.ActorBorrowedDefenderProtectionCommits?
						.Where(candidate =>
							candidate.PowerIdentity.PowerInstanceId ==
							cursor.PowerInstanceId)
						.ToArray();
				if (matchingCommitDtos is not [var commitDto])
				{
					throw new InvalidOperationException();
				}

				commit = commitDto.ToValue();
				commit.EnforceValidity();
			}
			catch (Exception exception) when (
				exception is ArgumentException or InvalidOperationException)
			{
				throw new InvalidOperationException(
					"The Actor borrowed recurring recovery cursor is structurally invalid.");
			}

			var powerIdentity = cursor.PowerIdentity;
			var setupCard = dto.ActorSetupCards?.Cards?.SingleOrDefault(card =>
				card.Id == cursor.ActorSetupCardId);
			var actingPlayer = dto.Players.SingleOrDefault(player =>
				player.Id == cursor.ActingPlayerId);
			var targetId = cursor.CommittedTargetIds.Single();
			var targetPlayer = dto.Players.SingleOrDefault(player =>
				player.Id == targetId);
			var marker = commit.PublicMarkerLogIndex < dto.GameHistoryLog.Count
				? dto.GameHistoryLog[commit.PublicMarkerLogIndex] as
					ActorBorrowedRolePowerCommittedLogEntry
				: null;
			var latestMarkerIndex = dto.GameHistoryLog.FindLastIndex(entry =>
				entry is ActorBorrowedRolePowerCommittedLogEntry);
			if (powerIdentity is not { IsValid: true } ||
				powerIdentity.Value.PowerInstanceOrigin !=
					RolePowerInstanceOrigin.Borrowed ||
				cursor.SourceRole != MainRoleType.Defender ||
				cursor.CommittedActionType != NightActionType.DefenderProtect ||
				string.IsNullOrWhiteSpace(cursor.SourcePowerIdentifier) ||
				cursor.OneUseResourceId != Guid.Empty ||
				cursor.ActorSetupCardId == Guid.Empty ||
				cursor.ActorBorrowedActivationId == Guid.Empty ||
				cursor.PowerInstanceId != cursor.ActorBorrowedActivationId ||
				active.ActivationId != cursor.ActorBorrowedActivationId ||
				active.ActingPlayerId != cursor.ActingPlayerId ||
				active.ActingRole != MainRoleType.Actor ||
				active.SelectedCardId != cursor.ActorSetupCardId ||
				active.SourceRole != MainRoleType.Defender ||
				setupCard?.PrintedRole != MainRoleType.Defender ||
				dto.ActorSetupCardSpends is not { } spends ||
				spends.Count(spend =>
					spend.CardId == cursor.ActorSetupCardId &&
					spend.ActivationId == cursor.ActorBorrowedActivationId) != 1 ||
				actingPlayer is not
				{
					MainRole: MainRoleType.Actor,
					Health: PlayerHealth.Alive
				} ||
				targetPlayer is not { Health: PlayerHealth.Alive } ||
				commit.PowerIdentity != powerIdentity.Value ||
				commit.ActorSetupCardId != cursor.ActorSetupCardId ||
				commit.TargetPlayerId != targetId ||
				commit.TurnNumber != dto.TurnNumber ||
				commit.CurrentPhase != GamePhase.Night ||
				commit.PublicMarkerLogIndex != latestMarkerIndex ||
				marker is null ||
				marker.Timestamp != commit.Timestamp ||
				marker.TurnNumber != commit.TurnNumber ||
				marker.CurrentPhase != commit.CurrentPhase ||
				dto.GameHistoryLog.Any(entry =>
					TryGetCommittedRolePowerIdentity(entry, out var publicIdentity) &&
					publicIdentity.PowerInstanceOrigin ==
						RolePowerInstanceOrigin.Borrowed) ||
				dto.GameHistoryLog
					.OfType<NightActionLogEntry>()
					.Any(entry =>
						entry.TurnNumber == dto.TurnNumber &&
						entry.CurrentPhase == GamePhase.Night &&
						entry.ActionType == NightActionType.DefenderProtect) ||
				pendingModeratorInstruction is not ConfirmationInstruction
				{
					Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
					AffectedPlayerIds: { Count: 1 } affectedPlayerIds
				} ||
				affectedPlayerIds[0] != cursor.ActingPlayerId)
			{
				throw new InvalidOperationException(
					"The Actor borrowed recurring recovery cursor does not match committed state.");
			}

			return cursor;
		}

		private static DomainRecoveryCursor
			ValidateActorBorrowedCupidRecurringRecoveryCursor(
				GameSessionDto dto,
				DomainRecoveryCursor cursor,
				ModeratorInstruction pendingModeratorInstruction)
		{
			ActorBorrowedRolePowerActivation active;
			ActorBorrowedCupidLoversCommit commit;
			try
			{
				active = dto.ActiveActorBorrowedRolePowerActivation?.ToValue()
					?? throw new InvalidOperationException();
				var matchingCommitDtos = dto.ActorBorrowedCupidLoversCommits?
					.Where(candidate =>
						candidate.PowerIdentity.PowerInstanceId ==
						cursor.PowerInstanceId)
					.ToArray();
				if (matchingCommitDtos is not [var commitDto])
				{
					throw new InvalidOperationException();
				}

				commit = commitDto.ToValue();
				commit.EnforceValidity();
			}
			catch (Exception exception) when (
				exception is ArgumentException or InvalidOperationException)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Cupid recovery cursor is structurally invalid.");
			}

			var powerIdentity = cursor.PowerIdentity;
			var setupCard = dto.ActorSetupCards?.Cards?.SingleOrDefault(card =>
				card.Id == cursor.ActorSetupCardId);
			var actingPlayer = dto.Players.SingleOrDefault(player =>
				player.Id == cursor.ActingPlayerId);
			var committedTargetIds = cursor.CommittedTargetIds.ToArray();
			var targetPlayers = committedTargetIds
				.Select(targetId => dto.Players.SingleOrDefault(player =>
					player.Id == targetId))
				.ToArray();
			var marker = commit.PublicMarkerLogIndex < dto.GameHistoryLog.Count
				? dto.GameHistoryLog[commit.PublicMarkerLogIndex] as
					ActorBorrowedRolePowerCommittedLogEntry
				: null;
			var latestMarkerIndex = dto.GameHistoryLog.FindLastIndex(entry =>
				entry is ActorBorrowedRolePowerCommittedLogEntry);
			if (powerIdentity is not { IsValid: true } ||
				powerIdentity.Value.PowerInstanceOrigin !=
					RolePowerInstanceOrigin.Borrowed ||
				cursor.SourceRole != MainRoleType.Cupid ||
				cursor.CommittedActionType != NightActionType.CupidLink ||
				!StringComparer.Ordinal.Equals(
					cursor.SourcePowerIdentifier,
					ActorBorrowedCupidLoversCommit
						.ExpectedSourcePowerIdentifier) ||
				cursor.OneUseResourceId != Guid.Empty ||
				cursor.ActorSetupCardId == Guid.Empty ||
				cursor.ActorBorrowedActivationId == Guid.Empty ||
				cursor.PowerInstanceId != cursor.ActorBorrowedActivationId ||
				active.ActivationId != cursor.ActorBorrowedActivationId ||
				active.ActingPlayerId != cursor.ActingPlayerId ||
				active.ActingRole != MainRoleType.Actor ||
				active.SelectedCardId != cursor.ActorSetupCardId ||
				active.SourceRole != MainRoleType.Cupid ||
				setupCard?.PrintedRole != MainRoleType.Cupid ||
				dto.ActorSetupCardSpends is not { } spends ||
				spends.Count(spend =>
					spend.CardId == cursor.ActorSetupCardId &&
					spend.ActivationId == cursor.ActorBorrowedActivationId) != 1 ||
				actingPlayer is not
				{
					MainRole: MainRoleType.Actor,
					Health: PlayerHealth.Alive
				} ||
				committedTargetIds.Length != 2 ||
				targetPlayers.Any(player => player is not
					{ Health: PlayerHealth.Alive }) ||
				commit.PowerIdentity != powerIdentity.Value ||
				commit.ActorSetupCardId != cursor.ActorSetupCardId ||
				!commit.PlayerIds.SequenceEqual(committedTargetIds) ||
				commit.TurnNumber != dto.TurnNumber ||
				commit.CurrentPhase != GamePhase.Night ||
				commit.PublicMarkerLogIndex != latestMarkerIndex ||
				marker is null ||
				marker.Timestamp != commit.Timestamp ||
				marker.TurnNumber != commit.TurnNumber ||
				marker.CurrentPhase != commit.CurrentPhase ||
				dto.GameHistoryLog.Any(entry =>
					TryGetCommittedRolePowerIdentity(entry, out var publicIdentity) &&
					publicIdentity.PowerInstanceOrigin ==
						RolePowerInstanceOrigin.Borrowed) ||
				dto.GameHistoryLog.OfType<LoversPairCommittedLogEntry>().Any() ||
				pendingModeratorInstruction is not ConfirmationInstruction
				{
					Semantic: ModeratorInstructionSemantic.RecognizeLovers,
					PublicAnnouncement: null,
					PrivateInstruction: not null,
					AffectedPlayerIds: { Count: 2 } affectedPlayerIds
				} ||
				!affectedPlayerIds.ToHashSet().SetEquals(committedTargetIds))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Cupid recovery cursor does not match committed state.");
			}

			return cursor;
		}

		private static DomainRecoveryCursor
			ValidateActorSetupCardSpendRecoveryCursor(
				GameSessionDto dto,
				DomainRecoveryCursor cursor,
				ModeratorInstruction pendingModeratorInstruction)
		{
			ActorBorrowedRolePowerActivation active;
			try
			{
				active = dto.ActiveActorBorrowedRolePowerActivation?.ToValue()
					?? throw new InvalidOperationException();
			}
			catch (Exception exception) when (
				exception is ArgumentException or InvalidOperationException)
			{
				throw new InvalidOperationException(
					"The Actor spend recovery cursor is structurally invalid.");
			}

			var setupCard = dto.ActorSetupCards?.Cards?.SingleOrDefault(
				card => card.Id == cursor.ActorSetupCardId);
			var spends = dto.ActorSetupCardSpends;
			var actingPlayer = dto.Players.SingleOrDefault(
				player => player.Id == cursor.ActingPlayerId);
			var latestSpendMarker = dto.GameHistoryLog
				.OfType<ActorSetupCardSpendCommittedLogEntry>()
				.LastOrDefault();
			if (cursor.CommittedActionType != NightActionType.Unknown ||
				cursor.CommittedTargetIds is not { Count: 0 } ||
				cursor.ActingPlayerId == Guid.Empty ||
				cursor.SourceRole is not { } sourceRole ||
				!sourceRole.IsEligibleActorSetupCard() ||
				!string.IsNullOrEmpty(cursor.SourcePowerIdentifier) ||
				cursor.PowerInstanceId != Guid.Empty ||
				cursor.PowerInstanceOrigin is not null ||
				cursor.OneUseResourceId != Guid.Empty ||
				cursor.ActorSetupCardId == Guid.Empty ||
				cursor.ActorBorrowedActivationId == Guid.Empty ||
				setupCard is null ||
				setupCard.PrintedRole != sourceRole ||
				spends is null ||
				spends.Count(spend =>
					spend.CardId == cursor.ActorSetupCardId &&
					spend.ActivationId == cursor.ActorBorrowedActivationId) != 1 ||
				active.ActivationId != cursor.ActorBorrowedActivationId ||
				active.ActingPlayerId != cursor.ActingPlayerId ||
				active.ActingRole != MainRoleType.Actor ||
				active.SelectedCardId != cursor.ActorSetupCardId ||
				active.SourceRole != sourceRole ||
				actingPlayer is not
				{
					MainRole: MainRoleType.Actor,
					Health: PlayerHealth.Alive
				} ||
				pendingModeratorInstruction.Semantic !=
					ModeratorInstructionSemantic.PutRoleToSleep ||
				pendingModeratorInstruction.AffectedPlayerIds is not { Count: 1 } ||
				pendingModeratorInstruction.AffectedPlayerIds[0] !=
					cursor.ActingPlayerId ||
				latestSpendMarker is null ||
				latestSpendMarker.CurrentPhase != GamePhase.Night ||
				latestSpendMarker.TurnNumber != dto.TurnNumber)
			{
				throw new InvalidOperationException(
					"The Actor spend recovery cursor does not match committed state.");
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
				.OfType<IPermanentRoleSwapCommittedLogEntry>()
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
