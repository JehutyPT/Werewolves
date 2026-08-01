using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

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

internal static class GameSessionQueries
{
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

    internal static IReadOnlyList<PermanentRoleSwapCommittedLogEntry>
        GetCommittedPermanentRoleSwaps(IGameSession session) =>
        FindLogEntries<PermanentRoleSwapCommittedLogEntry>(session).ToArray();

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
        StatusEffectTypes effectType) =>
        FindLogEntries<StatusEffectLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber),
                session.GetCurrentPhase(),
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
        return livingRoleHolders.Length == expectedLivingRoleHolderCount &&
               livingRoleHolders.All(player =>
                   player.State.ModeratorKnownRole == role);
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
        IGameSession session) =>
        FindLogEntries<StutteringJudgeSignalEstablishedLogEntry>(session)
            .Any();

    internal static bool HasUnreportedStutteringJudgeSignalObservation(
        IGameSession session)
    {
        if (GetCurrentDayVoteOutcome(session) != null)
        {
            return false;
        }

        var currentTurn = NumberRangeConstraint.Exact(session.TurnNumber);
        return FindLogEntries<StutteringJudgeSignalDidNotOccurLogEntry>(
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
                 spentResource == resourceIdentity));

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
