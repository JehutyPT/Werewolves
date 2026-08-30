using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Queries;

internal readonly record struct PendingDawnElimination(
    IPlayer Player,
    EliminationReason Reason,
    int LogIndex);

internal readonly record struct CurrentDayVoteOutcome(
    Guid PlayerId,
    int VoteOrdinal,
    int LogIndex);

internal readonly record struct DirectionalLivingNeighbors(
    IPlayer? Clockwise,
    IPlayer? Counterclockwise);

internal readonly record struct IndexedAngelVictory(
    VictoryConditionMetLogEntry Entry,
    int LogIndex);

internal static class GameSessionQueries
{
	private readonly record struct IndexedFactionAgentFact(
		FactionFact Fact,
		FactionFactSource Source,
		int BatchIndex);

    internal static IEnumerable<TLogEntry> FindLogEntries<TLogEntry>(
        IGameSession session,
        NumberRangeConstraint? turnRange = null,
        GamePhase? phase = null,
        Func<TLogEntry, bool>? filter = null)
        where TLogEntry : GameLogEntryBase
    {
        var range = turnRange ?? NumberRangeConstraint.Any;

        if (range.Minimum < 0 || range.Maximum < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(turnRange), "turn range cannot be negative.");
        }

        IEnumerable<TLogEntry> query = session.GameHistoryLog
            .OfType<TLogEntry>()
            .Where(log =>
                log.TurnNumber >= range.Minimum &&
                log.TurnNumber <= range.Maximum);

        if (phase.HasValue)
        {
            query = query.Where(log => log.CurrentPhase == phase.Value);
        }

        if (filter != null)
        {
            query = query.Where(filter);
        }

        return query;
    }

    internal static bool HasAngelExpired(IGameSession session) =>
        FindLogEntries<AngelExpiredLogEntry>(session).Any();

    internal static IReadOnlyList<int> GetAngelExpiryLogIndexes(
        IGameSession session) =>
        session.GameHistoryLog
            .Select((entry, index) => (Entry: entry, Index: index))
            .Where(item => item.Entry is AngelExpiredLogEntry)
            .Select(item => item.Index)
            .ToArray();

    internal static IReadOnlyList<IndexedAngelVictory> GetAngelVictories(
        IGameSession session) =>
        session.GameHistoryLog
            .Select((entry, index) => (Entry: entry, Index: index))
            .Where(item =>
                item.Entry is VictoryConditionMetLogEntry victory &&
                IncludesAngel(victory.GameResult))
            .Select(item => new IndexedAngelVictory(
                (VictoryConditionMetLogEntry)item.Entry,
                item.Index))
            .ToArray();

    internal static bool HasQualifyingAngelElimination(
        IGameSession session,
        int victoryTurnNumber,
        VictoryCheckWindow window,
        int? exclusiveEndLogIndex = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var history = session.GameHistoryLog.ToArray();
        var exclusiveEnd = exclusiveEndLogIndex ?? history.Length;
        return GetQualifyingAngelEliminations(history, exclusiveEnd)
            .Any(elimination => IsEliminationForWindow(
                elimination,
                victoryTurnNumber,
                window));
    }

    internal static bool HasQualifyingAngelEliminationThroughNightTwoDawn(
        IGameSession session,
        int exclusiveEndLogIndex)
    {
        ArgumentNullException.ThrowIfNull(session);
        var history = session.GameHistoryLog.ToArray();
        return GetQualifyingAngelEliminations(history, exclusiveEndLogIndex)
            .Any(elimination => elimination is
                { TurnNumber: 1, CurrentPhase: GamePhase.Dawn } or
                { TurnNumber: 1, CurrentPhase: GamePhase.Day } or
                { TurnNumber: 2, CurrentPhase: GamePhase.Dawn });
    }

    internal static bool IsResolvedNightTwoDawnImmediatelyBeforeAngelExpiry(
        IGameSession session,
        int expiryLogIndex)
    {
        ArgumentNullException.ThrowIfNull(session);
        var history = session.GameHistoryLog.ToArray();
        var boundaryIndex = expiryLogIndex - 1;
        if (boundaryIndex >= 0 &&
            history[boundaryIndex] is VictoryConditionMetLogEntry
            {
                TurnNumber: 2,
                CurrentPhase: GamePhase.Day,
                VictoryCheckWindow: VictoryCheckWindow.Dawn
            } victory &&
            !IncludesAngel(victory.GameResult))
        {
            boundaryIndex--;
        }

        return boundaryIndex >= 0 &&
            history[boundaryIndex] is PhaseTransitionLogEntry
            {
                PreviousPhase: GamePhase.Dawn,
                CurrentPhase: GamePhase.Day,
                TurnNumber: 2
            };
    }

    internal static bool WasAngelHolderKnownBefore(
        IGameSession session,
        int boundaryLogIndex,
        Guid playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var preceding = session.GameHistoryLog.Take(boundaryLogIndex);
        return preceding.OfType<RoleIdentificationLogEntry>()
                   .Any(entry =>
                       entry.Role == MainRoleType.Angel &&
                       entry.PlayerIds.Contains(playerId)) ||
               preceding.OfType<PermanentRoleSwapCommittedLogEntry>()
                   .Any(entry =>
                       entry.PlayerId == playerId &&
                       entry.NewCurrentRole == MainRoleType.Angel) ||
               preceding.OfType<DevotedServantRoleTakenCommittedLogEntry>()
                   .Any(entry =>
                       entry.ActingPlayerId == playerId &&
                       entry.ObservedPrintedRole == MainRoleType.Angel);
    }

    internal static IReadOnlyList<int>
        GetPostExpirySimpleVillagerProjectionIndexes(
            IGameSession session,
            int expiryLogIndex,
            Guid playerId) =>
        session.GameHistoryLog
            .Select((entry, index) => (Entry: entry, Index: index))
            .Where(item =>
                item.Index > expiryLogIndex &&
                item.Entry is AssignRoleLogEntry
                {
                    AssignedMainRole: MainRoleType.SimpleVillager
                } assignment &&
                assignment.PlayerIds.Contains(playerId))
            .Select(item => item.Index)
            .ToArray();

    private static IEnumerable<PlayerEliminatedLogEntry>
        GetQualifyingAngelEliminations(
            IReadOnlyList<GameLogEntryBase> history,
            int exclusiveEndLogIndex)
    {
        if (exclusiveEndLogIndex < 0 ||
            exclusiveEndLogIndex > history.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveEndLogIndex));
        }

        for (var index = 0; index < exclusiveEndLogIndex; index++)
        {
            if (history[index] is PlayerEliminatedLogEntry elimination &&
                OwnedPhysicalAngelAtElimination(
                     history,
                     index,
                    elimination.PlayerId))
            {
                yield return elimination;
            }
        }
    }

    private static bool OwnedPhysicalAngelAtElimination(
        IReadOnlyList<GameLogEntryBase> history,
        int eliminationIndex,
        Guid playerId)
    {
        var angelCardOwners = new Dictionary<Guid, Guid>();
        foreach (var entry in history.Take(eliminationIndex))
        {
            switch (entry)
            {
                case PhysicalCharacterCardOwnershipObservedLogEntry
                    {
                        PrintedRole: MainRoleType.Angel
                    } ownership:
                    angelCardOwners[ownership.CardId] = ownership.PlayerId;
                    break;
                case PermanentRoleSwapCommittedLogEntry swap:
                    angelCardOwners.Remove(
                        swap.PhysicalCards.OutgoingOwnedCardId);
                    if (swap.NewCurrentRole == MainRoleType.Angel)
                    {
                        angelCardOwners[swap.PhysicalCards.AcquiredCardId] =
                            swap.PlayerId;
                    }
                    break;
                case DevotedServantRoleTakenCommittedLogEntry roleTake:
                    angelCardOwners.Remove(
                        roleTake.PhysicalCards.OutgoingOwnedCardId);
                    if (roleTake.ObservedPrintedRole == MainRoleType.Angel)
                    {
                        angelCardOwners[roleTake.PhysicalCards.AcquiredCardId] =
                            roleTake.ActingPlayerId;
                    }
                    break;
            }
        }

        return angelCardOwners.Values.Contains(playerId);
    }

    private static bool IsEliminationForWindow(
        PlayerEliminatedLogEntry elimination,
        int victoryTurnNumber,
        VictoryCheckWindow window) =>
        (victoryTurnNumber,
            window,
            elimination.TurnNumber,
            elimination.CurrentPhase) switch
        {
            (1, VictoryCheckWindow.Dawn, 1, GamePhase.Dawn) => true,
            (2, VictoryCheckWindow.PreNight, 1, GamePhase.Day) => true,
            (2, VictoryCheckWindow.Dawn, 2, GamePhase.Dawn) => true,
            _ => false
        };

    private static bool IncludesAngel(GameResult result) => result switch
    {
        SingleFactionGameResult single => single.Faction == Faction.Angel,
        SharedVictoryGameResult shared => shared.Factions.Contains(Faction.Angel),
        _ => false
    };

	internal static IReadOnlyList<PermanentRoleSwapCommittedLogEntry>
		GetCommittedPermanentRoleSwaps(IGameSession session) =>
		FindLogEntries<PermanentRoleSwapCommittedLogEntry>(session).ToArray();

	internal static DevotedServantPublicSelfRevealCommittedLogEntry?
		GetCommittedDevotedServantPublicSelfRevealForTarget(
			IGameSession session,
			Guid voteTargetId) =>
		FindLogEntries<DevotedServantPublicSelfRevealCommittedLogEntry>(session)
			.SingleOrDefault(entry => entry.VoteTargetId == voteTargetId);

	internal static DevotedServantPublicSelfRevealCommittedLogEntry
		GetCommittedDevotedServantPublicSelfReveal(
			IGameSession session,
			Guid actingPlayerId,
			Guid voteTargetId) =>
		FindLogEntries<DevotedServantPublicSelfRevealCommittedLogEntry>(session)
			.Single(entry =>
				entry.ActingPlayerId == actingPlayerId &&
				entry.VoteTargetId == voteTargetId);

	internal static bool HasCommittedDevotedServantRoleTake(
		IGameSession session,
		Guid actingPlayerId,
		Guid voteTargetId) =>
		FindLogEntries<DevotedServantRoleTakenCommittedLogEntry>(session)
			.Any(entry =>
				entry.ActingPlayerId == actingPlayerId &&
				entry.VoteTargetId == voteTargetId);

	internal static bool HasDevotedServantRoleTakeForTarget(
		IGameSession session,
		Guid playerId) =>
		FindLogEntries<DevotedServantRoleTakenCommittedLogEntry>(session)
			.Any(entry => entry.VoteTargetId == playerId);

	internal static bool IsDevotedServantAcquiredRoleDormantForCurrentDay(
		IGameSession session,
		Guid playerId,
		GamePhase currentPhase) =>
		currentPhase == GamePhase.Day &&
		FindLogEntries<DevotedServantRoleTakenCommittedLogEntry>(session)
			.Any(entry =>
				entry.ActingPlayerId == playerId &&
				entry.TurnNumber == session.TurnNumber);

    internal static IReadOnlyList<PermanentRoleSwapCommittedLogEntry>
        GetCommittedThiefExchanges(
            IGameSession session,
            Guid playerId,
            long roleLockInVersion) =>
        FindLogEntries<PermanentRoleSwapCommittedLogEntry>(
                session,
                NumberRangeConstraint.Exact(1),
                GamePhase.Night,
                entry =>
                    entry.ExpectedCurrentRole == MainRoleType.Thief &&
                    entry.PlayerId == playerId &&
                    entry.RoleLockInVersion == roleLockInVersion)
            .ToArray();

    internal static IReadOnlyList<ThiefOfferDeclinedLogEntry>
        GetCommittedThiefOfferDeclines(
            IGameSession session,
            Guid playerId,
            long roleLockInVersion) =>
        FindLogEntries<ThiefOfferDeclinedLogEntry>(
                session,
                NumberRangeConstraint.Exact(1),
                GamePhase.Night,
                entry =>
                    entry.PlayerId == playerId &&
                    entry.RoleLockInVersion == roleLockInVersion)
            .ToArray();

    internal static IReadOnlyList<ThiefOfferDeclinedLogEntry>
        GetAllCommittedThiefOfferDeclines(IGameSession session) =>
        FindLogEntries<ThiefOfferDeclinedLogEntry>(session).ToArray();

    internal static LoversPairCommittedLogEntry? GetCommittedLoversPair(
        IGameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return GetCommittedLoversPairFromHistory(session.GameHistoryLog);
    }

	internal static IReadOnlyList<Guid>? GetCommittedLoversPlayerIds(
		GameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		var nativePair = GetCommittedLoversPair(session);
		var actorPairs = session.GetActorBorrowedCupidLoversCommits();
		if ((nativePair is not null ? 1 : 0) + actorPairs.Count > 1)
		{
			throw new InvalidOperationException(
				"The Session contains multiple Lovers pair commitments.");
		}

		return nativePair?.PlayerIds ?? actorPairs.SingleOrDefault()?.PlayerIds;
	}

    internal static LoversPairCommittedLogEntry?
        GetCommittedLoversPairFromHistory(
            IEnumerable<GameLogEntryBase> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return history
            .OfType<LoversPairCommittedLogEntry>()
            .SingleOrDefault();
    }

    internal static IReadOnlyList<LoversPairCommittedLogEntry>
        GetCommittedLoversPairsSince(
            IGameSession session,
            int startingLogCount)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (startingLogCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startingLogCount));
        }

        return session.GameHistoryLog
            .Skip(startingLogCount)
            .OfType<LoversPairCommittedLogEntry>()
            .ToArray();
    }

    internal static DirectionalLivingNeighbors GetDirectionalLivingNeighbors(
        IGameSession session,
        Guid referencePlayerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var seatingOrder = session.GetPlayers().ToArray();
        var referenceIndex = Array.FindIndex(
            seatingOrder,
            player => player.Id == referencePlayerId);
        if (referenceIndex < 0)
        {
            _ = session.GetPlayer(referencePlayerId);
        }

        return new DirectionalLivingNeighbors(
            FindNearestLivingPlayer(step: 1),
            FindNearestLivingPlayer(step: -1));

        IPlayer? FindNearestLivingPlayer(int step)
        {
            for (var offset = 1; offset < seatingOrder.Length; offset++)
            {
                var candidateIndex =
                    (referenceIndex + (step * offset) + seatingOrder.Length) %
                    seatingOrder.Length;
                var candidate = seatingOrder[candidateIndex];
                if (candidate.State.Health == PlayerHealth.Alive)
                {
                    return candidate;
                }
            }

            return null;
        }
    }

    internal static IEnumerable<IPlayer> GetPlayersTargetedLastNight(
        IGameSession session,
        NightActionType actionType,
        NumberRangeConstraint countConstraint)
    {
        var targetIds = FindLogEntries<NightActionLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber),
                GamePhase.Night,
                log => log.ActionType == actionType)
            .SelectMany(log => log.TargetIds ?? [])
            .ToList();

        countConstraint.Enforce(targetIds);

        return targetIds.Select(session.GetPlayer);
    }

    internal static IReadOnlyList<NightActionLogEntry> GetOrderedNightActionsThisNight(
        IGameSession session,
        IReadOnlyCollection<NightActionType> actionTypes) =>
        FindLogEntries<NightActionLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber),
                GamePhase.Night,
                log => actionTypes.Contains(log.ActionType))
            .ToArray();

    internal static bool HasNightActionTargetThisNight(
        IGameSession session,
        NightActionType actionType,
        Guid targetPlayerId) =>
        GetOrderedNightActionsThisNight(session, [actionType])
            .Any(entry =>
                entry.TargetIds is [var targetId] &&
                targetId == targetPlayerId);

    internal static StatusEffectLogEntry? GetLatestStatusEffectLifecycle(
        IGameSession session,
        Guid playerId,
        StatusEffectTypes effectType) =>
        FindLogEntries<StatusEffectLogEntry>(
                session,
                filter: entry =>
                    entry.PlayerId == playerId &&
                    entry.EffectType == effectType)
            .LastOrDefault();

    internal static bool HasActiveStatusEffectAppliedThisPhase(
        IGameSession session,
        StatusEffectTypes effectType,
        GamePhase currentPhase) =>
        FindLogEntries<StatusEffectLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber),
                currentPhase,
                entry =>
                    entry.EffectType == effectType &&
                    entry.IsActive)
            .Any(entry =>
                session.GetPlayer(entry.PlayerId).State.HasStatusEffect(effectType));

    internal static bool TryGetRetainedWerewolfVictimThisNight(
        IGameSession session,
        out Guid victimId)
    {
        var collectiveActions = GetOrderedNightActionsThisNight(
            session,
            [NightActionType.WerewolfVictimSelection]);
        if (collectiveActions is [var collectiveAction] &&
            collectiveAction.TargetIds is [var retainedVictimId] &&
            retainedVictimId != Guid.Empty)
        {
            victimId = retainedVictimId;
            return true;
        }

        victimId = Guid.Empty;
        return false;
    }

    internal static bool HasEliminatedKnownWerewolfFactionAgent(
        IGameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var players = session.GetPlayers().ToArray();
        var projection = FactionFactProjection.Create(
			session.GameHistoryLog.OfType<IFactionFactBatchLogEntry>(),
            players.Select(player => player.Id).ToArray());
        return players.Any(player =>
            player.State.Health == PlayerHealth.Dead &&
            projection.Agents[player.Id][Faction.Werewolf] ==
            FactionAgentKnowledge.KnownAgent);
    }

    internal static IReadOnlySet<Guid> RequireKnownFactionAgentIdsAtBoundary(
        IGameSession session,
        Faction faction,
        FactionFactEffectiveBoundary inclusiveBoundary)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!Enum.IsDefined(faction))
        {
            throw new ArgumentOutOfRangeException(nameof(faction));
        }

        var playerIds = session.GetPlayers()
            .Select(player => player.Id)
            .ToArray();
        var projection = FactionFactProjection.Create(
			session.GameHistoryLog.OfType<IFactionFactBatchLogEntry>(),
            playerIds,
            inclusiveBoundary);
        if (playerIds.Any(playerId =>
                projection.Agents[playerId][faction] ==
                FactionAgentKnowledge.Unknown))
        {
            throw new InvalidOperationException(
                "Faction Agent facts are not ready.");
        }

        return playerIds
            .Where(playerId =>
                projection.Agents[playerId][faction] ==
                FactionAgentKnowledge.KnownAgent)
            .ToHashSet();
    }

	internal static Guid? FindFirstClockwiseLivingKnownFactionAgent(
		IGameSession session,
		Guid referencePlayerId,
		Faction faction,
		FactionFactEffectiveBoundary historicalBoundary,
		int? exclusiveEndLogIndex = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		if (!Enum.IsDefined(faction))
		{
			throw new ArgumentOutOfRangeException(nameof(faction));
		}

		var seatingOrder = session.GetPlayers()
			.Select(player => player.Id)
			.ToArray();
		var referenceIndex = Array.IndexOf(seatingOrder, referencePlayerId);
		if (referenceIndex < 0)
		{
			_ = session.GetPlayer(referencePlayerId);
		}

		var history = session.GameHistoryLog.ToArray();
		var exclusiveEnd = exclusiveEndLogIndex ?? history.Length;
		if (exclusiveEnd < 0 || exclusiveEnd > history.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(exclusiveEndLogIndex));
		}

		var historyAtBoundary = history.Take(exclusiveEnd).ToArray();
		var factionEntries = historyAtBoundary
			.OfType<IFactionFactBatchLogEntry>()
			.ToArray();
		var latestProjection = FactionFactProjection.Create(
			factionEntries,
			seatingOrder);
		var historicalProjection = FactionFactProjection.Create(
			factionEntries,
			seatingOrder,
			historicalBoundary);
		if (seatingOrder.Any(playerId =>
				latestProjection.Agents[playerId][faction] ==
					FactionAgentKnowledge.Unknown ||
				historicalProjection.Agents[playerId][faction] ==
					FactionAgentKnowledge.Unknown))
		{
			throw new InvalidOperationException(
				"Faction Agent facts are not ready.");
		}

		var eliminatedPlayerIds = historyAtBoundary
			.OfType<PlayerEliminatedLogEntry>()
			.Select(entry => entry.PlayerId)
			.ToHashSet();
		for (var offset = 1; offset < seatingOrder.Length; offset++)
		{
			var candidateId = seatingOrder[
				(referenceIndex + offset) % seatingOrder.Length];
			if (!eliminatedPlayerIds.Contains(candidateId) &&
				(latestProjection.Agents[candidateId][faction] ==
						FactionAgentKnowledge.KnownAgent ||
				 historicalProjection.Agents[candidateId][faction] ==
						FactionAgentKnowledge.KnownAgent))
			{
				return candidateId;
			}
		}

		return null;
	}

	internal static bool IsQualifyingActorBorrowedElderResistanceTrigger(
		GameSession session,
		int triggeringLogIndex,
		int exclusiveUpperLogIndex,
		int turnNumber,
		Guid targetPlayerId)
	{
		ArgumentNullException.ThrowIfNull(session);
		var history = session.GameHistoryLog.ToArray();
		if (triggeringLogIndex < 0 ||
			triggeringLogIndex >= exclusiveUpperLogIndex ||
			exclusiveUpperLogIndex > history.Length)
		{
			return false;
		}

		var entry = history[triggeringLogIndex];
		if (entry is not NightActionLogEntry
			{
				CurrentPhase: GamePhase.Night,
				TargetIds: [var attackTargetId]
			} nightAction ||
			nightAction.TurnNumber != turnNumber ||
			attackTargetId != targetPlayerId)
		{
			return false;
		}

		if (entry is OneUseRolePowerCommittedLogEntry
			{
				SourceRole: MainRoleType.AccursedWolfFather,
				ActionType: NightActionType.AccursedWolfFatherInfection
			} infection &&
			StringComparer.Ordinal.Equals(
				infection.SourcePowerIdentifier,
				AccursedWolfFatherRole.InfectionPowerIdentifier.Value))
		{
			return true;
		}

		var defenderProtected = history
			.Take(exclusiveUpperLogIndex)
			.OfType<NightActionLogEntry>()
			.Any(protection =>
				protection.CurrentPhase == GamePhase.Night &&
				protection.TurnNumber == turnNumber &&
				protection.ActionType == NightActionType.DefenderProtect &&
				protection.TargetIds is [var protectedPlayerId] &&
				protectedPlayerId == targetPlayerId) ||
			session.GetActorBorrowedDefenderProtectionCommits().Any(protection =>
				protection.PublicMarkerLogIndex < exclusiveUpperLogIndex &&
				protection.CurrentPhase == GamePhase.Night &&
				protection.TurnNumber == turnNumber &&
				protection.TargetPlayerId == targetPlayerId);
		if (defenderProtected)
		{
			return false;
		}

		if (entry.GetType() == typeof(NightActionLogEntry) &&
			nightAction.ActionType == NightActionType.WerewolfVictimSelection)
		{
			return true;
		}

		if (entry is not RecurringRolePowerCommittedLogEntry
			{
				PowerInstanceOrigin: RolePowerInstanceOrigin.Native
			} recurringAttack ||
			recurringAttack.PowerInstanceId != recurringAttack.ActingPlayerId)
		{
			return false;
		}

		return recurringAttack switch
		{
			{
				ActionType: NightActionType.WhiteWerewolfVictimSelection,
				SourceRole: MainRoleType.WhiteWerewolf
			} when StringComparer.Ordinal.Equals(
				recurringAttack.SourcePowerIdentifier,
				"white-werewolf-solo-attack") => true,
			{
				ActionType: NightActionType.BigBadWolfVictimSelection,
				SourceRole: MainRoleType.BigBadWolf
			} when StringComparer.Ordinal.Equals(
				recurringAttack.SourcePowerIdentifier,
				"big-bad-wolf-additional-victim") => true,
			_ => false
		};
	}

	internal static bool IsQualifyingActorBorrowedElderResistanceRestoration(
		IGameSession session,
		int logIndex,
		int turnNumber,
		Guid targetPlayerId)
	{
		ArgumentNullException.ThrowIfNull(session);
		var history = session.GameHistoryLog.ToArray();
		return logIndex >= 0 &&
			logIndex < history.Length &&
			history[logIndex] is OneUseRolePowerCommittedLogEntry
			{
				CurrentPhase: GamePhase.Night,
				SourceRole: MainRoleType.Witch,
				SourcePowerIdentifier: "witch-potions",
				ActionType: NightActionType.WitchSave,
				TargetIds: [var healedTargetId]
			} witchSave &&
			witchSave.TurnNumber == turnNumber &&
			healedTargetId == targetPlayerId;
	}

    internal static int GetExpectedLivingRoleHolderCount(
        IGameSession session,
        MainRoleType role)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        var committedRoleHolderIds = session.GetPlayers()
            .Where(player => player.State.CurrentRole == role)
            .Select(player => player.Id)
            .ToHashSet();
        var accountedRoleHolderIds = session.GameHistoryLog
            .OfType<RoleIdentificationLogEntry>()
            .Where(entry => entry.Role == role)
            .SelectMany(entry => entry.PlayerIds)
            .ToHashSet();
        accountedRoleHolderIds.UnionWith(committedRoleHolderIds);
		var activePrintedRoleCardCount = session.GetModeratorPhysicalCharacterCards()
			.Count(cardState =>
				cardState.Card.PrintedRole == role &&
				cardState.Zone is PhysicalCharacterCardZone.DealPool or
					PhysicalCharacterCardZone.PlayerOwned);
        var unaccountedCompositionHolderCount = Math.Max(
            0,
			activePrintedRoleCardCount - accountedRoleHolderIds.Count);
        var committedLivingRoleHolderCount = session.GetPlayers()
            .Count(player =>
                player.State.CurrentRole == role &&
                player.State.Health == PlayerHealth.Alive);
        return committedLivingRoleHolderCount +
               unaccountedCompositionHolderCount;
    }

	internal static bool TryGetExactInitialLivingWerewolfAgentCapacity(
		IGameSession session,
		out int capacity)
	{
		ArgumentNullException.ThrowIfNull(session);
		capacity = 0;
		var players = session.GetPlayers().ToArray();
		if (session.TurnNumber != 1 ||
		    session.GetCurrentPhase() != GamePhase.Night ||
		    players.Any(player => player.State.Health != PlayerHealth.Alive))
		{
			return false;
		}

		var knownAgents = players
			.Where(player =>
				session.GetFactionAgentKnowledge(
					player.Id,
					Faction.Werewolf) == FactionAgentKnowledge.KnownAgent)
			.ToArray();
		var establishedAgentRoles = knownAgents
			.Select(GetEstablishedRole)
			.ToArray();
		if (establishedAgentRoles.Any(role => role is null))
		{
			return false;
		}
		var establishedAgentRoleValues = establishedAgentRoles
			.Select(role => role!.Value)
			.ToArray();

		var activeAgencyCardCounts = session
			.GetModeratorPhysicalCharacterCards()
			.Where(cardState =>
				(cardState.Zone is PhysicalCharacterCardZone.DealPool or
					PhysicalCharacterCardZone.PlayerOwned) &&
				RoleFactionKnowledge.EstablishesInitialWerewolfAgency(
					cardState.Card.PrintedRole))
			.GroupBy(cardState => cardState.Card.PrintedRole)
			.ToDictionary(group => group.Key, group => group.Count());
		var establishedAgencyAgentCounts = establishedAgentRoleValues
			.Where(RoleFactionKnowledge.EstablishesInitialWerewolfAgency)
			.GroupBy(role => role)
			.ToDictionary(group => group.Key, group => group.Count());
		if (establishedAgencyAgentCounts.Any(pair =>
			!activeAgencyCardCounts.TryGetValue(pair.Key, out var cardCount) ||
			pair.Value > cardCount))
		{
			return false;
		}

		var agencyRoleCapacity = activeAgencyCardCounts.Values.Sum();
		var establishedNonCardAgentCount = establishedAgentRoleValues
			.Count(role => !RoleFactionKnowledge
				.EstablishesInitialWerewolfAgency(role));
		capacity = agencyRoleCapacity + establishedNonCardAgentCount;
		var candidateCount = players.Count(player =>
			session.GetFactionAgentKnowledge(
				player.Id,
				Faction.Werewolf) != FactionAgentKnowledge.KnownNonAgent);
		return capacity > 0 && capacity <= candidateCount;
	}

    internal static bool IsCompleteLivingRoleHolderSetKnown(
        IGameSession session,
        MainRoleType role)
    {
        ArgumentNullException.ThrowIfNull(session);
        var expectedLivingRoleHolderCount =
            GetExpectedLivingRoleHolderCount(session, role);
		var livingRoleHolders = session.GetPlayers()
            .Where(player =>
                player.State.CurrentRole == role &&
                player.State.Health == PlayerHealth.Alive)
			.ToArray();
		var latestIdentification = session.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.LastOrDefault(entry => entry.Role == role);
		return (livingRoleHolders.Length == expectedLivingRoleHolderCount &&
		        livingRoleHolders.All(player =>
			        player.State.ModeratorKnownRole == role)) ||
		       (livingRoleHolders.Length == 0 &&
		        latestIdentification is { PlayerIds.Count: 0 });
	}

    internal static int GetCommittedLogIndex(
        IGameSession session,
        GameLogEntryBase committedEntry)
    {
        var matchingIndexes = session.GameHistoryLog
            .Select((entry, index) => (Entry: entry, Index: index))
            .Where(item => ReferenceEquals(item.Entry, committedEntry))
            .Select(item => item.Index)
            .ToArray();
        return matchingIndexes is [var index]
            ? index
            : throw new InvalidOperationException(
                "The committed Game Log entry must occur exactly once in the Session history.");
    }

    internal static IReadOnlyList<IPlayer> GetPhysicalAttackTargetsThisNight(
        IGameSession session)
    {
        var physicalAttackTypes = new HashSet<NightActionType>
        {
            NightActionType.WerewolfVictimSelection,
            NightActionType.WhiteWerewolfVictimSelection,
            NightActionType.BigBadWolfVictimSelection
        };

        return FindLogEntries<NightActionLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber),
                GamePhase.Night,
                log => physicalAttackTypes.Contains(log.ActionType))
            .SelectMany(log => log.TargetIds ?? [])
            .Distinct()
            .Select(session.GetPlayer)
            .ToArray();
    }

    internal static bool HasPlayerBeenVotedForPreviously(IGameSession session, Guid playerId)
        => FindLogEntries<VoteOutcomeReportedLogEntry>(
                session,
                NumberRangeConstraint.Range(1, session.TurnNumber - 1),
                filter: log => log.ReportedOutcomePlayerId == playerId)
            .Any();

    internal static bool HasStutteringJudgeSignalBeenEstablished(
        IGameSession session,
        Guid judgePlayerId) =>
        FindLogEntries<StutteringJudgeSignalEstablishedLogEntry>(
                session,
                filter: entry => entry.JudgePlayerId == judgePlayerId)
            .Any();

    internal static bool HasStutteringJudgeSignalBeenEstablished(
        IGameSession session,
        RolePowerInstanceIdentity powerIdentity)
    {
        const string consecutiveVotePowerIdentifier =
            "stuttering-judge-consecutive-vote";
        if (!powerIdentity.IsValid ||
            powerIdentity.SourceRole != MainRoleType.StutteringJudge ||
            powerIdentity.PowerInstanceOrigin !=
                RolePowerInstanceOrigin.Borrowed ||
            !StringComparer.Ordinal.Equals(
                powerIdentity.SourcePowerIdentifier,
                consecutiveVotePowerIdentifier) ||
            session is not GameSession concreteSession)
        {
            return false;
        }

        var activation =
            concreteSession.GetModeratorActiveActorBorrowedRolePowerActivation();
        if (activation is null ||
            activation.ActingPlayerId != powerIdentity.ActingPlayerId ||
            activation.SourceRole != powerIdentity.SourceRole ||
            activation.ActivationId != powerIdentity.PowerInstanceId ||
            activation.Origin != powerIdentity.PowerInstanceOrigin)
        {
            return false;
        }

        return concreteSession
            .GetActorBorrowedStutteringJudgeSignalSetupCommits()
            .Any(commit =>
                commit.PowerIdentity == powerIdentity &&
                commit.TurnNumber == session.TurnNumber &&
                commit.CurrentPhase == GamePhase.Night);
    }

    internal static bool HasStutteringJudgeSignalBeenObserved(
        IGameSession session,
        RolePowerInstanceIdentity powerIdentity)
    {
        const string consecutiveVotePowerIdentifier =
            "stuttering-judge-consecutive-vote";
        if (!powerIdentity.IsValid ||
            powerIdentity.SourceRole != MainRoleType.StutteringJudge ||
            powerIdentity.PowerInstanceOrigin !=
                RolePowerInstanceOrigin.Borrowed ||
            !StringComparer.Ordinal.Equals(
                powerIdentity.SourcePowerIdentifier,
                consecutiveVotePowerIdentifier) ||
            session is not GameSession concreteSession)
        {
            return false;
        }

        var activation =
            concreteSession.GetModeratorActiveActorBorrowedRolePowerActivation();
        if (activation is null ||
            activation.ActingPlayerId != powerIdentity.ActingPlayerId ||
            activation.SourceRole != powerIdentity.SourceRole ||
            activation.ActivationId != powerIdentity.PowerInstanceId ||
            activation.Origin != powerIdentity.PowerInstanceOrigin)
        {
            return false;
        }

        return concreteSession
            .GetActorBorrowedStutteringJudgeSignalObservationCommits()
            .Any(commit =>
                commit.PowerIdentity == powerIdentity &&
                commit.TurnNumber == session.TurnNumber &&
                commit.CurrentPhase == GamePhase.Day);
    }

    internal static bool HasUnreportedStutteringJudgeSignalObservation(
        IGameSession session)
    {
        if (GetCurrentDayVoteOutcome(session) != null)
        {
            return false;
        }

        var currentTurn = NumberRangeConstraint.Exact(session.TurnNumber);
        return session is GameSession concreteSession &&
               concreteSession
                   .GetActorBorrowedStutteringJudgeSignalObservationCommits()
                   .Any(commit =>
                       commit.TurnNumber == session.TurnNumber &&
                       commit.CurrentPhase == GamePhase.Day) ||
               FindLogEntries<StutteringJudgeSignalDidNotOccurLogEntry>(
                   session,
                   currentTurn,
                   GamePhase.Day)
               .Any() ||
               FindLogEntries<OneUseRolePowerDayActionCommittedLogEntry>(
                   session,
                   currentTurn,
                   GamePhase.Day,
                   entry =>
                       entry.ActionType == DayPowerType.JudgeExtraVote &&
                       entry.ResourceIdentity.SourceRole ==
                       MainRoleType.StutteringJudge)
               .Any();
    }

    internal static bool HasBearTamerGrowlOccurredThisDawn(
        IGameSession session) =>
        FindLogEntries<BearTamerGrowlOccurredLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber),
                GamePhase.Dawn)
            .Any();

	internal static VillagerRolePowerSuppressionCommittedLogEntry?
		GetVillagerRolePowerSuppression(IGameSession session) =>
		session.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.SingleOrDefault();

	internal static bool IsVillagerRolePowerSuppressionActive(
		IGameSession session) =>
		GetVillagerRolePowerSuppression(session) is not null;

	internal static bool
		IsVillagerRolePowerSuppressionAnnouncementAcknowledged(
			IGameSession session,
			Guid announcementInstructionId) =>
		session.GameHistoryLog
			.OfType<
				VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry>()
			.Any(entry =>
				entry.AnnouncementInstructionId == announcementInstructionId);

    internal static bool IsOneUseRolePowerResourceCommitted(
        IGameSession session,
        OneUseRolePowerResourceIdentity resourceIdentity) =>
        session.GameHistoryLog
	            .Any(entry =>
	                (entry is IOneUseRolePowerCommittedLogEntry oneUse &&
	                 oneUse.ResourceIdentity == resourceIdentity) ||
	                (entry is TargetPrivateRolePowerCommittedLogEntry
	                 {
	                     SpentResourceIdentity: { } spentResource
	                 } &&
	                 spentResource == resourceIdentity)) ||
	        session is GameSession concreteSession &&
	        (concreteSession.GetActorBorrowedFoxCheckCommits()
	             .Any(commit =>
	                 commit.SpentResourceIdentity == resourceIdentity) ||
	         concreteSession.GetActorBorrowedWitchPotionUseCommits()
	             .Any(commit =>
	                 commit.SpentResourceIdentity == resourceIdentity) ||
	         concreteSession
	             .GetActorBorrowedStutteringJudgeSignalObservationCommits()
	             .Any(commit =>
	                 commit.SpentResourceIdentity == resourceIdentity));

    internal static IEnumerable<IPlayer> GetPlayersEliminatedThisDawn(IGameSession session)
        => FindLogEntries<PlayerEliminatedLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber),
                GamePhase.Dawn)
            .Select(log => session.GetPlayer(log.PlayerId));

    internal static IReadOnlySet<Guid> GetDirectDawnEliminationPlayerIds(
        IGameSession session,
        EliminationReason reason)
    {
        var determinedVictimIds = FindLogEntries<DawnVictimDeterminedLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber),
                GamePhase.Dawn,
                entry => entry.Reason == reason)
            .Select(entry => entry.PlayerId)
            .ToHashSet();
        determinedVictimIds.IntersectWith(
            FindLogEntries<PlayerEliminatedLogEntry>(
                    session,
                    NumberRangeConstraint.Exact(session.TurnNumber),
                    GamePhase.Dawn,
                    entry => entry.Reason == reason)
                .Select(entry => entry.PlayerId));
        return determinedVictimIds;
    }

    internal static IReadOnlyList<PendingDawnElimination>
        GetPendingDawnEliminations(IGameSession session) =>
        session.GameHistoryLog
            .Select((entry, index) => (Entry: entry, Index: index))
            .Where(item =>
                item.Entry is DawnVictimDeterminedLogEntry
                {
                    TurnNumber: var turnNumber,
                    CurrentPhase: GamePhase.Dawn
                } &&
                turnNumber == session.TurnNumber)
            .Select(item =>
            {
                var entry = (DawnVictimDeterminedLogEntry)item.Entry;
                return new PendingDawnElimination(
                    session.GetPlayer(entry.PlayerId),
                    entry.Reason,
                    item.Index);
            })
            .ToArray();

    /// <summary>
    /// Returns all players eliminated during the Day phase of the current turn,
    /// including the vote target and any consequential eliminations.
    /// </summary>
    internal static IEnumerable<Guid> GetPlayerEliminatedThisVote(IGameSession session)
        => FindLogEntries<PlayerEliminatedLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber),
                GamePhase.Day)
            .Select(log => log.PlayerId);

	internal static List<MainRoleType> GetUnassignedRoles(IGameSession session)
	    => session.GetModeratorPhysicalCharacterCards()
			.Where(cardState =>
				cardState.Zone == PhysicalCharacterCardZone.DealPool)
				.Select(cardState => cardState.Card.PrintedRole)
				.ToList();

	internal static MainRoleType? GetEstablishedRole(IPlayer player) =>
		player.State.PhysicalCharacterCardRole ??
		player.State.ModeratorKnownRole ??
		player.State.CurrentRole;

	internal static List<MainRoleType> GetUnclaimedRoles(IGameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		var unclaimedRoles = GetUnassignedRoles(session);
		foreach (var player in session.GetPlayers())
		{
			if (player.State.PhysicalCharacterCardId is null &&
				GetEstablishedRole(player) is { } establishedRole)
			{
				unclaimedRoles.Remove(establishedRole);
			}
		}

		return unclaimedRoles;
	}

	internal static IReadOnlyList<MainRoleType> GetPossibleRoles(
		IGameSession session,
		Guid playerId)
	{
		ArgumentNullException.ThrowIfNull(session);
		session.GetPlayer(playerId);
		var possibleRoles = GetUnclaimedRoles(session);
		var agencyKnowledge = session.GetFactionAgentKnowledge(
			playerId,
			Faction.Werewolf);
		if (agencyKnowledge == FactionAgentKnowledge.KnownNonAgent)
		{
			return Array.AsReadOnly(possibleRoles
				.Where(role => !RoleFactionKnowledge
					.EstablishesInitialWerewolfAgency(role))
				.ToArray());
		}

		var earliestAgencyFact = FindEarliestWerewolfAgencyFact(
			session,
			playerId);
		if (agencyKnowledge == FactionAgentKnowledge.KnownAgent &&
			earliestAgencyFact is { } earliest &&
			IsInitialWerewolfAgencyProvenance(earliest.Source))
		{
			return Array.AsReadOnly(possibleRoles
				.Where(RoleFactionKnowledge.EstablishesInitialWerewolfAgency)
				.ToArray());
		}

		return Array.AsReadOnly(possibleRoles.ToArray());
	}

	internal static FactionAgentFactProvenance?
		GetEarliestWerewolfAgencyFact(
			IGameSession session,
			Guid playerId)
	{
		ArgumentNullException.ThrowIfNull(session);
		session.GetPlayer(playerId);
		return FindEarliestWerewolfAgencyFact(session, playerId) is { } earliest
			? new FactionAgentFactProvenance(earliest.Fact, earliest.Source)
			: null;
	}

	private static IndexedFactionAgentFact? FindEarliestWerewolfAgencyFact(
		IGameSession session,
		Guid playerId) =>
		session.GameHistoryLog
			.OfType<IFactionFactBatchLogEntry>()
			.SelectMany((entry, batchIndex) => entry.Facts
				.Where(fact =>
					fact.PlayerId == playerId &&
					fact.Type == FactionFactType.Agent &&
					fact.Faction == Faction.Werewolf &&
					fact.AgentKnowledge == FactionAgentKnowledge.KnownAgent)
				.Select(fact => new IndexedFactionAgentFact(
					fact,
					entry.Source,
					batchIndex)))
			.OrderBy(
				item => item.Fact.EffectiveBoundary,
				Comparer<FactionFactEffectiveBoundary>.Create(
					FactionFactProjection.CompareBoundaries))
			.ThenBy(item => item.BatchIndex)
			.Cast<IndexedFactionAgentFact?>()
			.FirstOrDefault();

	private static bool IsInitialWerewolfAgencyProvenance(
		FactionFactSource source) =>
		source.Kind == FactionFactSourceKind.SimulationStartState ||
		source.Kind == FactionFactSourceKind.ScheduledObservation &&
		StringComparer.Ordinal.Equals(
			source.Identifier,
			FactionFactSource
				.WerewolfFactionAgentGroupObservationIdentifier);

    internal static bool TryGetOnlyPossibleUnassignedRole(
        IGameSession session,
        int requiredAssignmentCount,
        out MainRoleType role)
    {
        role = default;

        if (requiredAssignmentCount <= 0)
        {
            return false;
        }

        var unassignedRoles = GetUnassignedRoles(session);
        if (unassignedRoles.Count < requiredAssignmentCount)
        {
            return false;
        }

        // Duplicate card copies of the same role type do not create a Moderator decision.
        var possibleRoleTypes = unassignedRoles.Distinct().ToList();
        if (possibleRoleTypes.Count != 1)
        {
            return false;
        }

        role = possibleRoleTypes.Single();
        return true;
    }

    internal static Guid? GetCurrentVoteTarget(IGameSession session)
    {
        var voteOutcome = GetCurrentDayVoteOutcome(session);
        if (voteOutcome is not { PlayerId: var playerId } ||
            playerId == Guid.Empty)
        {
            return null;
        }

        return playerId;
    }

    internal static CurrentDayVoteOutcome? GetCurrentDayVoteOutcome(
        IGameSession session)
    {
        var currentTurnVotes = session.GameHistoryLog
            .Select((entry, index) => (Entry: entry, Index: index))
            .Where(item =>
                item.Entry is VoteOutcomeReportedLogEntry
                {
                    TurnNumber: var turnNumber,
                    CurrentPhase: GamePhase.Day
                } &&
                turnNumber == session.TurnNumber)
            .ToArray();
        if (currentTurnVotes is not [.., var currentVote])
        {
            return null;
        }

        var entry = (VoteOutcomeReportedLogEntry)currentVote.Entry;
        return new CurrentDayVoteOutcome(
            entry.ReportedOutcomePlayerId,
            currentTurnVotes.Length,
            currentVote.Index);
    }

    internal static ScapegoatTieReplacementLogEntry?
        GetCurrentScapegoatTieReplacement(IGameSession session)
    {
        var vote = GetCurrentDayVoteOutcome(session);
        if (vote == null)
        {
            return null;
        }

        return FindLogEntries<ScapegoatTieReplacementLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber),
                GamePhase.Day,
                entry => entry.VoteOrdinal == vote.Value.VoteOrdinal &&
                         entry.VoteLogIndex == vote.Value.LogIndex)
            .SingleOrDefault();
    }

    internal static EliminationCascadeBatchResolvedLogEntry?
        GetEliminationCascadeBatchResolution(
            IGameSession session,
            string scopeId,
            int scopeStartLogIndex,
            IReadOnlyCollection<EliminationCascadeElimination>
                requestedEliminations) =>
        session.GameHistoryLog
            .Skip(scopeStartLogIndex + 1)
            .OfType<EliminationCascadeBatchResolvedLogEntry>()
            .SingleOrDefault(entry =>
                entry.ScopeId == scopeId &&
                entry.RequestedEliminations.SequenceEqual(
                    requestedEliminations));

    internal static bool IsEliminationCascadeComplete(
        IGameSession session,
        string scopeId) =>
        session.GameHistoryLog
            .OfType<EliminationCascadeCompletedLogEntry>()
            .Any(entry => entry.ScopeId == scopeId);

    private static IEnumerable<MainRoleType> GetRolesInPlay(IGameSession session)
    {
		return session.GetModeratorPhysicalCharacterCards()
			.Where(cardState =>
				cardState.Zone is PhysicalCharacterCardZone.DealPool or
					PhysicalCharacterCardZone.PlayerOwned)
			.Select(cardState => cardState.Card.PrintedRole);
    }
}
