using System.Collections.Immutable;
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
	public RoleLockIn RoleLockIn => throw new NotSupportedException(
		"This session projection does not expose a Role Lock-In.");
	public PublicGroupPartition? PublicGroupPartition => null;
	public IReadOnlyList<PhysicalCharacterCardState>
		GetModeratorPhysicalCharacterCards() => [];
	public ActorSetupCards GetModeratorActorSetupCards() => ActorSetupCards.None;
	public IReadOnlyList<PhysicalCharacterCard>
		GetModeratorRemainingActorSetupCards() => [];
	public IReadOnlyList<PhysicalCharacterCard>
		GetModeratorSpentActorSetupCards() => [];
	public ActorBorrowedRolePowerActivation?
		GetModeratorActiveActorBorrowedRolePowerActivation() => null;

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
	public RoleLockIn RoleLockIn => _gameSessionKernel.GetRoleLockIn();
	public PublicGroupPartition? PublicGroupPartition =>
		_gameSessionKernel.GetPublicGroupPartition();
	public ActorSetupCards GetModeratorActorSetupCards() =>
		_gameSessionKernel.GetActorSetupCards();

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
	internal IReadOnlyList<ActorBorrowedSeerCheckCommit>
		GetActorBorrowedSeerCheckCommits() =>
		_gameSessionKernel.GetActorBorrowedSeerCheckCommits();
	internal IReadOnlyList<ActorBorrowedDefenderProtectionCommit>
		GetActorBorrowedDefenderProtectionCommits() =>
		_gameSessionKernel.GetActorBorrowedDefenderProtectionCommits();
	internal IReadOnlyList<ActorBorrowedFoxCheckCommit>
		GetActorBorrowedFoxCheckCommits() =>
		_gameSessionKernel.GetActorBorrowedFoxCheckCommits();
	internal IReadOnlyList<ActorBorrowedWitchPotionUseCommit>
		GetActorBorrowedWitchPotionUseCommits() =>
		_gameSessionKernel.GetActorBorrowedWitchPotionUseCommits();
	internal IReadOnlyList<ActorBorrowedWitchPotionDeclineCommit>
		GetActorBorrowedWitchPotionDeclineCommits() =>
		_gameSessionKernel.GetActorBorrowedWitchPotionDeclineCommits();
	internal IReadOnlyList<ActorBorrowedCupidLoversCommit>
		GetActorBorrowedCupidLoversCommits() =>
		_gameSessionKernel.GetActorBorrowedCupidLoversCommits();
	internal IReadOnlyList<ActorBorrowedStutteringJudgeSignalSetupCommit>
		GetActorBorrowedStutteringJudgeSignalSetupCommits() =>
		_gameSessionKernel.GetActorBorrowedStutteringJudgeSignalSetupCommits();
	internal IReadOnlyList<ActorBorrowedStutteringJudgeSignalObservationCommit>
		GetActorBorrowedStutteringJudgeSignalObservationCommits() =>
		_gameSessionKernel.GetActorBorrowedStutteringJudgeSignalObservationCommits();
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

    public int RoleInPlayCount(MainRoleType type) =>
		_gameSessionKernel.GetPhysicalCharacterCardStates().Count(state =>
			state.Card.PrintedRole == type &&
			state.Zone is PhysicalCharacterCardZone.DealPool or
				PhysicalCharacterCardZone.PlayerOwned);

	public IReadOnlyList<PhysicalCharacterCardState>
		GetModeratorPhysicalCharacterCards() =>
		_gameSessionKernel.GetPhysicalCharacterCardStates();

	public IReadOnlyList<PhysicalCharacterCard>
		GetModeratorRemainingActorSetupCards() =>
		_gameSessionKernel.GetRemainingActorSetupCards();

	public IReadOnlyList<PhysicalCharacterCard>
		GetModeratorSpentActorSetupCards() =>
		_gameSessionKernel.GetSpentActorSetupCards();

	public ActorBorrowedRolePowerActivation?
		GetModeratorActiveActorBorrowedRolePowerActivation() =>
		_gameSessionKernel.GetActiveActorBorrowedRolePowerActivation();
    
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

	internal bool TrySpendActorSetupCard(
		Guid actingPlayerId,
		Guid selectedCardId,
		[NotNullWhen(true)]
		out ActorBorrowedRolePowerActivation? activation) =>
		_gameSessionKernel.TrySpendActorSetupCard(
			actingPlayerId,
			selectedCardId,
			out activation);

	internal bool TryExpireActorBorrowedRolePowerActivation() =>
		_gameSessionKernel.TryExpireActorBorrowedRolePowerActivation();

	internal void CommitActorBorrowedSeerCheck(
		RolePowerInstanceIdentity powerIdentity,
		Guid targetPlayerId,
		FactionAgentKnowledge targetAgentKnowledge) =>
		_gameSessionKernel.CommitActorBorrowedSeerCheck(
			powerIdentity,
			targetPlayerId,
			targetAgentKnowledge);

	internal void CommitActorBorrowedDefenderProtection(
		RolePowerInstanceIdentity powerIdentity,
		Guid targetPlayerId) =>
		_gameSessionKernel.CommitActorBorrowedDefenderProtection(
			powerIdentity,
			targetPlayerId);

	internal void CommitActorBorrowedFoxCheck(
		RolePowerInstanceIdentity powerIdentity,
		Guid centerPlayerId,
		FactionAgentKnowledge neighborhoodAgentKnowledge,
		OneUseRolePowerResourceIdentity? spentResourceIdentity) =>
		_gameSessionKernel.CommitActorBorrowedFoxCheck(
			powerIdentity,
			centerPlayerId,
			neighborhoodAgentKnowledge,
			spentResourceIdentity);

	internal void CommitActorBorrowedWitchPotionUse(
		RolePowerInstanceIdentity powerIdentity,
		OneUseRolePowerResourceIdentity spentResourceIdentity,
		Guid targetPlayerId) =>
		_gameSessionKernel.CommitActorBorrowedWitchPotionUse(
			powerIdentity,
			spentResourceIdentity,
			targetPlayerId);

	internal void CommitActorBorrowedWitchPotionDecline(
		RolePowerInstanceIdentity powerIdentity,
		OneUseRolePowerResourceIdentity offeredResourceIdentity) =>
		_gameSessionKernel.CommitActorBorrowedWitchPotionDecline(
			powerIdentity,
			offeredResourceIdentity);

	internal void CommitActorBorrowedCupidLovers(
		RolePowerInstanceIdentity powerIdentity,
		IReadOnlyCollection<Guid> playerIds,
		ActorBorrowedCupidLoversDisposition disposition) =>
		_gameSessionKernel.CommitActorBorrowedCupidLovers(
			powerIdentity,
			playerIds,
			disposition);

	internal void CommitActorBorrowedStutteringJudgeSignalSetup(
		RolePowerInstanceIdentity powerIdentity) =>
		_gameSessionKernel.CommitActorBorrowedStutteringJudgeSignalSetup(
			powerIdentity);

	internal void CommitActorBorrowedStutteringJudgeSignalObservation(
		RolePowerInstanceIdentity powerIdentity,
		bool signalOccurred,
		OneUseRolePowerResourceIdentity? spentResourceIdentity) =>
		_gameSessionKernel.CommitActorBorrowedStutteringJudgeSignalObservation(
			powerIdentity,
			signalOccurred,
			spentResourceIdentity);

	internal void CommitGameFact<TEntry>(
		Func<GameFactContext, TEntry> entryFactory)
		where TEntry : GameLogEntryBase, IGameFactLogEntry
		=> CommitSessionEntry(entryFactory, "game fact");

	internal void CommitFactionFactBatch(
		Func<GameFactContext, FactionFactsCommittedLogEntry> entryFactory) =>
		CommitSessionEntry(entryFactory, "Faction fact batch");

	internal void CommitActorBorrowedCupidInitialBeneficiaryClosure(
		Func<GameFactContext, FactionFactsCommittedLogEntry> entryFactory,
		ActorBorrowedCupidLoversCommit expectedDeferredCommit,
		ActorBorrowedCupidLoversDisposition resolvedDisposition)
	{
		ArgumentNullException.ThrowIfNull(entryFactory);
		ArgumentNullException.ThrowIfNull(expectedDeferredCommit);
		CommitSessionEntry(
			context => new
				ActorBorrowedCupidInitialBeneficiaryClosureCommandLogEntry
				{
					Timestamp = context.Timestamp,
					TurnNumber = context.TurnNumber,
					CurrentPhase = context.CurrentPhase,
					PublicClosureEntry = entryFactory(context),
					ExpectedDeferredCommit = expectedDeferredCommit,
					ResolvedDisposition = resolvedDisposition
				},
			"Actor borrowed Cupid Initial Beneficiary Closure");
	}

	internal bool TryRecordPhysicalCharacterCardOwnership(
		long expectedRoleLockInVersion,
		Guid playerId,
		Guid cardId)
	{
		if (expectedRoleLockInVersion != RoleLockIn.Version ||
			playerId == Guid.Empty ||
			cardId == Guid.Empty ||
			!GetPlayers().Any(player => player.Id == playerId) ||
			GetPlayerState(playerId).PhysicalCharacterCardId is not null)
		{
			return false;
		}

		var cardState = GetModeratorPhysicalCharacterCards()
			.SingleOrDefault(state => state.Card.Id == cardId);
		if (cardState is not
			{
				Zone: PhysicalCharacterCardZone.DealPool,
				OwnerPlayerId: null
			})
		{
			return false;
		}

		_gameSessionKernel.AddEntryAndUpdateState(
			new PhysicalCharacterCardOwnershipObservedLogEntry
			{
				Timestamp = DateTimeOffset.UtcNow,
				TurnNumber = TurnNumber,
				CurrentPhase = GetCurrentPhase(),
				RoleLockInVersion = expectedRoleLockInVersion,
				PlayerId = playerId,
				CardId = cardId,
				PrintedRole = cardState.Card.PrintedRole
			});
		return true;
	}

	internal bool TryCommitDevotedServantPublicSelfReveal(
		Guid actingPlayerId,
		Guid voteTargetId,
		OneUseRolePowerResourceIdentity resourceIdentity)
	{
		resourceIdentity.EnforceValidity();
		var actor = GetPlayers().SingleOrDefault(player =>
			player.Id == actingPlayerId);
		var target = GetPlayers().SingleOrDefault(player =>
			player.Id == voteTargetId);
		if (GetCurrentPhase() != GamePhase.Day ||
			actor is null || target is null ||
			actingPlayerId == voteTargetId ||
			actor.State.Health != PlayerHealth.Alive ||
			target.State.Health != PlayerHealth.Alive ||
			actor.State.HasStatusEffect(StatusEffectTypes.Lovers) ||
			actor.State.CurrentRole is not (null or MainRoleType.DevotedServant) ||
			actor.State.ModeratorKnownRole is not (null or MainRoleType.DevotedServant) ||
			actor.State.PubliclyRevealedRole is not null ||
			resourceIdentity.ActingPlayerId != actingPlayerId ||
			resourceIdentity.SourceRole != MainRoleType.DevotedServant ||
			GameHistoryLog.OfType<
				DevotedServantPublicSelfRevealCommittedLogEntry>().Any() ||
			GameHistoryLog.OfType<IOneUseRolePowerCommittedLogEntry>().Any(entry =>
				entry.ResourceIdentity == resourceIdentity))
		{
			return false;
		}

		var cardStates = GetModeratorPhysicalCharacterCards();
		var bindsOwnership = actor.State.PhysicalCharacterCardId is null;
		var card = bindsOwnership
			? cardStates.FirstOrDefault(state =>
				state.Zone == PhysicalCharacterCardZone.DealPool &&
				state.OwnerPlayerId is null &&
				state.Card.PrintedRole == MainRoleType.DevotedServant)
			: cardStates.SingleOrDefault(state =>
				state.Card.Id == actor.State.PhysicalCharacterCardId &&
				state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
				state.OwnerPlayerId == actingPlayerId &&
				state.Card.PrintedRole == MainRoleType.DevotedServant);
		if (card is null)
		{
			return false;
		}

		_gameSessionKernel.AddEntryAndUpdateState(
			new DevotedServantPublicSelfRevealCommittedLogEntry
			{
				Timestamp = DateTimeOffset.UtcNow,
				TurnNumber = TurnNumber,
				CurrentPhase = GetCurrentPhase(),
				RoleLockInVersion = RoleLockIn.Version,
				ActingPlayerId = actingPlayerId,
				VoteTargetId = voteTargetId,
				DevotedServantCardId = card.Card.Id,
				BindsCardOwnership = bindsOwnership,
				ResourceIdentity = resourceIdentity
			});
		return true;
	}

	internal bool TryCommitDevotedServantRoleTake(
		DevotedServantRoleTakeRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!CanCommitDevotedServantRoleTake(request))
		{
			return false;
		}

		var boundary = new FactionFactEffectiveBoundary(
			TurnNumber,
			GetCurrentPhase(),
			GameHistoryLog.Count());
		var powerInstanceId = CreateFreshPermanentRoleSwapPowerInstanceId();
		var facts = PermanentRoleSwapFactionFacts.CreateBatch(
			request.ActingPlayerId,
			request.Policy,
			request.Factions,
			boundary);
		_gameSessionKernel.AddEntryAndUpdateState(
			new DevotedServantRoleTakenCommittedLogEntry
			{
				Timestamp = DateTimeOffset.UtcNow,
				TurnNumber = TurnNumber,
				CurrentPhase = GetCurrentPhase(),
				RoleLockInVersion = request.ExpectedRoleLockInVersion,
				ActingPlayerId = request.ActingPlayerId,
				VoteTargetId = request.VoteTargetId,
				ObservedPrintedRole = request.ObservedPrintedRole,
				NewCurrentRole = request.NewCurrentRole,
				ExpectedTargetCurrentRole = request.ExpectedTargetCurrentRole,
				PhysicalCards = request.PhysicalCards,
				Policy = request.Policy,
				StateChanges = request.StateChanges,
				Source = PermanentRoleSwapFactionFacts.CreateSource(
					request.ActingPlayerId,
					powerInstanceId),
				Facts = facts,
				NewPowerInstanceId = powerInstanceId,
				PowerInstanceOrigin = RolePowerInstanceOrigin.Swapped
			});
		return true;
	}

	internal bool TryCommitPermanentRoleSwap(PermanentRoleSwapRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!CanCommitPermanentRoleSwap(request))
		{
			return false;
		}

		var boundary = new FactionFactEffectiveBoundary(
			TurnNumber,
			GetCurrentPhase(),
			GameHistoryLog.Count());
		var powerInstanceId = CreateFreshPermanentRoleSwapPowerInstanceId();
		var facts = PermanentRoleSwapFactionFacts.CreateBatch(
			request.PlayerId,
			request.Policy,
			request.Factions,
			boundary);
		_gameSessionKernel.AddEntryAndUpdateState(
			new PermanentRoleSwapCommittedLogEntry
			{
				Timestamp = DateTimeOffset.UtcNow,
				TurnNumber = TurnNumber,
				CurrentPhase = GetCurrentPhase(),
				RoleLockInVersion = request.ExpectedRoleLockInVersion,
				PlayerId = request.PlayerId,
				ExpectedCurrentRole = request.ExpectedCurrentRole,
				NewCurrentRole = request.NewCurrentRole,
				PhysicalCards = request.PhysicalCards,
				Policy = request.Policy,
				StateChanges = request.StateChanges,
				Source = PermanentRoleSwapFactionFacts.CreateSource(
					request.PlayerId,
					powerInstanceId),
				Facts = facts,
				NewPowerInstanceId = powerInstanceId,
				PowerInstanceOrigin = RolePowerInstanceOrigin.Swapped
			});
		return true;
	}

	internal bool TryCommitThiefOfferDecline(Guid playerId)
	{
		var offer1 = RoleLockIn.Offer1;
		var offer2 = RoleLockIn.Offer2;
		var player = GetPlayers().SingleOrDefault(candidate => candidate.Id == playerId);
		if (offer1 is null || offer2 is null || player is null ||
		    player.State.CurrentRole != MainRoleType.Thief ||
		    player.State.ModeratorKnownRole != MainRoleType.Thief ||
		    player.State.PhysicalCharacterCardId is not { } thiefCardId ||
		    GameHistoryLog.OfType<ThiefOfferDeclinedLogEntry>().Any() ||
		    GameHistoryLog.OfType<PermanentRoleSwapCommittedLogEntry>().Any(swap =>
			    swap.ExpectedCurrentRole == MainRoleType.Thief))
		{
			return false;
		}

		CommitSessionEntry(
			context => new ThiefOfferDeclinedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				RoleLockInVersion = RoleLockIn.Version,
				PlayerId = playerId,
				ThiefCardId = thiefCardId,
				Offer1CardId = offer1.Id,
				Offer2CardId = offer2.Id
			},
			"Thief offer decline");
		return true;
	}

	private Guid CreateFreshPermanentRoleSwapPowerInstanceId()
	{
		var reservedIds = GetPlayers()
			.Select(player => player.Id)
			.Concat(GameHistoryLog
				.OfType<IPermanentRoleSwapCommittedLogEntry>()
				.Select(entry => entry.NewPowerInstanceId))
			.ToHashSet();
		Guid candidate;
		do
		{
			candidate = Guid.NewGuid();
		}
		while (reservedIds.Contains(candidate));

		return candidate;
	}

	private bool CanCommitDevotedServantRoleTake(
		DevotedServantRoleTakeRequest request)
	{
		if (GetCurrentPhase() != GamePhase.Day ||
			request.ExpectedRoleLockInVersion != RoleLockIn.Version ||
			request.ActingPlayerId == Guid.Empty ||
			request.VoteTargetId == Guid.Empty ||
			request.ActingPlayerId == request.VoteTargetId ||
			!Enum.IsDefined(request.ObservedPrintedRole) ||
			!Enum.IsDefined(request.NewCurrentRole) ||
			(request.ExpectedTargetCurrentRole is { } expectedTargetRole &&
				!Enum.IsDefined(expectedTargetRole)) ||
			(request.ObservedPrintedRole != request.NewCurrentRole &&
				(request.ObservedPrintedRole != MainRoleType.Angel ||
				 request.NewCurrentRole != MainRoleType.SimpleVillager)) ||
			request.PhysicalCards is null ||
			request.Policy is null ||
			request.Factions is null ||
			request.StateChanges is null ||
			!request.Policy.IsExplicit ||
			!request.StateChanges.IsCoherentWith(request.Policy) ||
			request.Policy.PrivateRoleKnowledge !=
				PermanentRoleSwapDisposition.Change ||
			request.Policy.PublicRevealHistory !=
				PermanentRoleSwapDisposition.Preserve ||
			request.Policy.RolePowerState != PermanentRoleSwapDisposition.Change ||
			request.PhysicalCards.AdditionalSetAsideCardIds.Count != 0)
		{
			return false;
		}

		var actor = GetPlayers().SingleOrDefault(player =>
			player.Id == request.ActingPlayerId);
		var target = GetPlayers().SingleOrDefault(player =>
			player.Id == request.VoteTargetId);
		var knownTargetPrintedRole = target?.State.PhysicalCharacterCardRole ??
			target?.State.ModeratorKnownRole ??
			target?.State.CurrentRole;
		if (actor?.State is not
			{
				Health: PlayerHealth.Alive,
				CurrentRole: MainRoleType.DevotedServant,
				ModeratorKnownRole: MainRoleType.DevotedServant,
				PubliclyRevealedRole: MainRoleType.DevotedServant
			} ||
			target is null ||
			target.State.Health != PlayerHealth.Alive ||
			target.State.CurrentRole != request.ExpectedTargetCurrentRole ||
			target.State.PubliclyRevealedRole is not null ||
			(knownTargetPrintedRole is { } knownRole &&
				knownRole != request.ObservedPrintedRole) ||
			actor.State.PhysicalCharacterCardId !=
				request.PhysicalCards.OutgoingOwnedCardId ||
			!GameHistoryLog.OfType<DevotedServantPublicSelfRevealCommittedLogEntry>()
				.Any(entry =>
					entry.ActingPlayerId == request.ActingPlayerId &&
					entry.VoteTargetId == request.VoteTargetId) ||
			GameHistoryLog.OfType<DevotedServantRoleTakenCommittedLogEntry>().Any())
		{
			return false;
		}

		var cardStates = GetModeratorPhysicalCharacterCards()
			.ToDictionary(state => state.Card.Id);
		var movement = request.PhysicalCards;
		if (!cardStates.TryGetValue(movement.OutgoingOwnedCardId, out var outgoing) ||
			outgoing.Zone != PhysicalCharacterCardZone.PlayerOwned ||
			outgoing.OwnerPlayerId != request.ActingPlayerId ||
			outgoing.Card.PrintedRole != MainRoleType.DevotedServant ||
			!cardStates.TryGetValue(movement.AcquiredCardId, out var acquired) ||
			acquired.Card.PrintedRole != request.ObservedPrintedRole)
		{
			return false;
		}

		return movement.ExpectedAcquiredCardOwnerPlayerId is { } ownerId
			? ownerId == request.VoteTargetId &&
			  acquired.Zone == PhysicalCharacterCardZone.PlayerOwned &&
			  acquired.OwnerPlayerId == request.VoteTargetId &&
			  target.State.PhysicalCharacterCardId == acquired.Card.Id &&
			  target.State.PhysicalCharacterCardRole == acquired.Card.PrintedRole
			: acquired.Zone == PhysicalCharacterCardZone.DealPool &&
			  acquired.OwnerPlayerId is null &&
			  target.State.PhysicalCharacterCardId is null &&
			  target.State.PhysicalCharacterCardRole is null;
	}

	private bool CanCommitPermanentRoleSwap(PermanentRoleSwapRequest request)
	{
		if (request.ExpectedRoleLockInVersion != RoleLockIn.Version ||
			request.PlayerId == Guid.Empty ||
			!Enum.IsDefined(request.ExpectedCurrentRole) ||
			!Enum.IsDefined(request.NewCurrentRole) ||
			request.ExpectedCurrentRole == request.NewCurrentRole ||
			request.PhysicalCards is null ||
			request.Policy is null ||
			request.Factions is null ||
			request.StateChanges is null ||
			!request.Policy.IsExplicit ||
			!request.StateChanges.IsCoherentWith(request.Policy) ||
			request.Policy.PublicRevealHistory != PermanentRoleSwapDisposition.Preserve ||
			request.Policy.RolePowerState != PermanentRoleSwapDisposition.Change ||
			request.Policy.FactionBeneficiary is PermanentRoleSwapDisposition.Clear or PermanentRoleSwapDisposition.Unknown ||
			request.Policy.FactionAgents is PermanentRoleSwapDisposition.Clear or PermanentRoleSwapDisposition.Unknown)
		{
			return false;
		}

		var player = GetPlayers().SingleOrDefault(candidate => candidate.Id == request.PlayerId);
		if (player is null ||
			player.State.CurrentRole != request.ExpectedCurrentRole ||
			player.State.PhysicalCharacterCardId != request.PhysicalCards.OutgoingOwnedCardId)
		{
			return false;
		}

		var cardStates = GetModeratorPhysicalCharacterCards()
			.ToDictionary(cardState => cardState.Card.Id);
		var expectedAcquiredOwnerId =
			request.PhysicalCards.ExpectedAcquiredCardOwnerPlayerId;
		if (!cardStates.TryGetValue(request.PhysicalCards.OutgoingOwnedCardId, out var outgoing) ||
			outgoing.Zone != PhysicalCharacterCardZone.PlayerOwned ||
			outgoing.OwnerPlayerId != request.PlayerId ||
			!cardStates.TryGetValue(request.PhysicalCards.AcquiredCardId, out var acquired) ||
			!IsExpectedAcquiredCardState(acquired, expectedAcquiredOwnerId) ||
			acquired.Card.PrintedRole != request.NewCurrentRole ||
			request.PhysicalCards.AdditionalSetAsideCardIds.Any(cardId =>
				!cardStates.TryGetValue(cardId, out var cardState) ||
				cardState.OwnerPlayerId is not null ||
				cardState.Zone is PhysicalCharacterCardZone.PlayerOwned or PhysicalCharacterCardZone.SetAside))
		{
			return false;
		}
		if (expectedAcquiredOwnerId is { } acquiredOwnerId)
		{
			var acquiredOwner = GetPlayers().SingleOrDefault(candidate =>
				candidate.Id == acquiredOwnerId);
			if (acquiredOwner is null ||
				acquiredOwnerId == request.PlayerId ||
				acquiredOwner.State.PhysicalCharacterCardId != acquired.Card.Id ||
				acquiredOwner.State.PhysicalCharacterCardRole !=
					acquired.Card.PrintedRole)
			{
				return false;
			}
		}

		if (request.StateChanges.RelationshipEffectsToClear.Contains(
				StatusEffectTypes.Lovers))
		{
			var loversPair = GameHistoryLog
				.OfType<LoversPairCommittedLogEntry>()
				.SingleOrDefault();
			if (loversPair is null ||
				!loversPair.PlayerIds.Contains(request.PlayerId) ||
				loversPair.PlayerIds.Any(playerId =>
					!GetPlayerState(playerId).HasStatusEffect(
						StatusEffectTypes.Lovers)))
			{
				return false;
			}
		}

		return true;

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

    internal void CommitTargetPrivateRolePowerNightAction(
        NightActionType actionType,
        RolePowerInstanceIdentity powerIdentity,
        OneUseRolePowerResourceIdentity? spentResourceIdentity = null)
    {
        if (actionType == NightActionType.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(actionType));
        }

        powerIdentity.EnforceValidity();
        if (spentResourceIdentity is { } spentResource)
        {
            spentResource.EnforceValidity();
        }

        var entry = new TargetPrivateRolePowerCommittedLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase =
                _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
            ActionType = actionType,
            TargetIds = [],
            ActingPlayerId = powerIdentity.ActingPlayerId,
            SourceRole = powerIdentity.SourceRole,
            SourcePowerIdentifier =
                powerIdentity.SourcePowerIdentifier,
            PowerInstanceId = powerIdentity.PowerInstanceId,
            PowerInstanceOrigin =
                powerIdentity.PowerInstanceOrigin,
            SpentResourceIdentity = spentResourceIdentity
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

	internal void CommitLoversPair(
		IReadOnlyCollection<Guid> playerIds,
		RolePowerInstanceIdentity powerIdentity)
	{
		ArgumentNullException.ThrowIfNull(playerIds);
		if (playerIds.Count != 2 ||
		    playerIds.Any(playerId => playerId == Guid.Empty) ||
		    playerIds.Distinct().Count() != 2)
		{
			throw new ArgumentException(
				"The Lovers pair requires exactly two distinct Players.",
				nameof(playerIds));
		}

		powerIdentity.EnforceValidity();
		if (GameHistoryLog.OfType<LoversPairCommittedLogEntry>().Any())
		{
			throw new InvalidOperationException(
				"The Lovers pair is already committed.");
		}

		var canonicalPlayerIds = playerIds.Order().ToArray();
		_gameSessionKernel.AddEntryAndUpdateState(
			new LoversPairCommittedLogEntry
			{
				Timestamp = DateTimeOffset.UtcNow,
				TurnNumber = TurnNumber,
				CurrentPhase =
					_gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
				FirstPlayerId = canonicalPlayerIds[0],
				SecondPlayerId = canonicalPlayerIds[1],
				ActingPlayerId = powerIdentity.ActingPlayerId,
				SourceRole = powerIdentity.SourceRole,
				SourcePowerIdentifier =
					powerIdentity.SourcePowerIdentifier,
				PowerInstanceId = powerIdentity.PowerInstanceId,
				PowerInstanceOrigin =
					powerIdentity.PowerInstanceOrigin,
				LinkBoundary = new FactionFactEffectiveBoundary(
					TurnNumber,
					_gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
					GameHistoryLog.Count())
			});
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

	internal void RecordAngelExpiry()
	{
		_gameSessionKernel.AddEntryAndUpdateState(new AngelExpiredLogEntry
		{
			Timestamp = DateTimeOffset.UtcNow,
			TurnNumber = TurnNumber,
			CurrentPhase = GetCurrentPhase()
		});
	}


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
		var card = GetModeratorPhysicalCharacterCards()
			.SingleOrDefault(state =>
				state.Card.PrintedRole == MainRoleType.VillagerVillager &&
				state.Zone == PhysicalCharacterCardZone.DealPool &&
				state.OwnerPlayerId is null)
			?.Card ?? throw new InvalidOperationException(
				"No unowned Villager-Villager Physical Character Card is available.");
        var entry = new VillagerVillagerPublicFromDealLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = TurnNumber,
            CurrentPhase = _gameSessionKernel.PhaseStateCache.GetCurrentPhase(),
			PlayerId = playerId,
			RoleLockInVersion = RoleLockIn.Version,
			CardId = card.Id
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
