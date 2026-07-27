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

    internal static bool WasDayAbilityTriggeredThisTurn(IGameSession session, DayPowerType powerType)
        => FindLogEntries<DayActionLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber),
                filter: log => log.ActionType == powerType)
            .Any();

    internal static bool HasPlayerBeenVotedForPreviously(IGameSession session, Guid playerId)
        => FindLogEntries<VoteOutcomeReportedLogEntry>(
                session,
                NumberRangeConstraint.Range(1, session.TurnNumber - 1),
                filter: log => log.ReportedOutcomePlayerId == playerId)
            .Any();

    internal static bool ShouldVoteRepeat(IGameSession session)
    {
        var hasJudgeVoted = WasDayAbilityTriggeredThisTurn(session, DayPowerType.JudgeExtraVote);
        var currentTurnVoteCount = FindLogEntries<VoteOutcomeReportedLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber))
            .Count();

        return hasJudgeVoted && currentTurnVoteCount == 1;
    }

    internal static IEnumerable<IPlayer> GetPlayersEliminatedThisDawn(IGameSession session)
        => FindLogEntries<PlayerEliminatedLogEntry>(
                session,
                NumberRangeConstraint.Exact(session.TurnNumber),
                GamePhase.Dawn)
            .Select(log => session.GetPlayer(log.PlayerId));

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
    {
        var assignedRoles = session.GetPlayers()
            .Select(p => p.State.MainRole)
            .Where(role => role.HasValue)
            .Select(role => role!.Value)
            .ToList();

        var unassignedRoles = GetRolesInPlay(session).ToList();
        foreach (var role in assignedRoles)
        {
            unassignedRoles.Remove(role);
        }

        return unassignedRoles;
    }

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

    private static IEnumerable<MainRoleType> GetRolesInPlay(IGameSession session)
    {
        foreach (var role in Enum.GetValues<MainRoleType>())
        {
            for (var i = 0; i < session.RoleInPlayCount(role); i++)
            {
                yield return role;
            }
        }
    }
}
