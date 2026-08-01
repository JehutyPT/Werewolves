using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Services;

internal static class AngelLifecycleRules
{
    internal static bool IsVictoryEligible(
        GameSession session,
        VictoryCheckWindow window)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!IsActivated(session) || HasExpired(session))
        {
            return false;
        }

        var history = session.GameHistoryLog.ToArray();
        for (var index = 0; index < history.Length; index++)
        {
            if (history[index] is PlayerEliminatedLogEntry elimination &&
                IsEliminationForWindow(elimination, session.TurnNumber, window) &&
                WasRevealedAsPhysicalAngel(history, index, elimination.PlayerId))
            {
                return true;
            }
        }

        return false;
    }

    internal static void ExpireAfterResolvedWindow(
        GameSession session,
        VictoryCheckWindow window,
        bool angelVictoryEligible)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (window != VictoryCheckWindow.Dawn ||
            session.TurnNumber != 2 ||
            angelVictoryEligible ||
            !IsActivated(session) ||
            HasExpired(session))
        {
            return;
        }

        session.RecordAngelExpiry();
        ApplyKnownHolderProjection(session);
    }

    internal static void ApplyExpiredHolderProjection(
        GameSession session,
        IReadOnlyDictionary<Guid, MainRoleType> revealedRoles)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(revealedRoles);
        if (!HasExpired(session))
        {
            return;
        }

        foreach (var playerId in revealedRoles
            .Where(pair => pair.Value == MainRoleType.Angel)
            .Select(pair => pair.Key))
        {
            var player = session.GetPlayer(playerId);
            if (player.State.PhysicalCharacterCardRole == MainRoleType.Angel &&
                player.State.CurrentRole == MainRoleType.Angel)
            {
                session.AssignRole(playerId, MainRoleType.SimpleVillager);
            }
        }
    }

    internal static void EnforceValidHistory(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var history = session.GameHistoryLog.ToArray();
        var angelVictoryIndexes = history
            .Select((entry, index) => (entry, index))
            .Where(pair =>
                pair.entry is VictoryConditionMetLogEntry victory &&
                IncludesAngel(victory.GameResult))
            .Select(pair => pair.index)
            .ToArray();
        if (angelVictoryIndexes.Any(index =>
                !IsActivated(session) ||
                history[index] is not VictoryConditionMetLogEntry victory ||
                !HasQualifyingAngelElimination(
                    history,
                    index,
                    victory)))
        {
            throw new InvalidOperationException(
                "Angel victory history has no qualifying elimination at its resolved victory window.");
        }

        var expiryIndexes = history
            .Select((entry, index) => (entry, index))
            .Where(pair => pair.entry is AngelExpiredLogEntry)
            .Select(pair => pair.index)
            .ToArray();
        if (expiryIndexes.Length > 1)
        {
            throw new InvalidOperationException(
                "Angel expiry history contains duplicate commits.");
        }

        var angelWon = angelVictoryIndexes.Length > 0;
        if (expiryIndexes.Length == 0)
        {
            if (IsActivated(session) &&
                HasPassedNightTwoDawnWindow(session) &&
                !angelWon)
            {
                throw new InvalidOperationException(
                    "Angel expiry history is missing after the Night 2 Dawn victory window.");
            }

            return;
        }

        var expiryIndex = expiryIndexes[0];
        if (!IsActivated(session) || angelWon ||
            !IsResolvedNightTwoDawnImmediatelyBeforeExpiry(
                history,
                expiryIndex) ||
            HasQualifyingAngelElimination(history.Take(expiryIndex).ToArray()))
        {
            throw new InvalidOperationException(
                "Angel expiry history is inconsistent with the resolved Night 2 Dawn window.");
        }

        var holder = session.GetModeratorPhysicalCharacterCards()
            .SingleOrDefault(state =>
                state.Card.PrintedRole == MainRoleType.Angel &&
                state.Zone == PhysicalCharacterCardZone.PlayerOwned);
        if (holder?.OwnerPlayerId is not { } holderId)
        {
            return;
        }

        var projections = history
            .Select((entry, index) => (entry, index))
            .Where(pair =>
                pair.index > expiryIndex &&
                pair.entry is AssignRoleLogEntry
                {
                    AssignedMainRole: MainRoleType.SimpleVillager
                } assignment &&
                assignment.PlayerIds.Contains(holderId))
            .Select(pair => pair.index)
            .ToArray();
        var holderKnownAtExpiry = WasKnownAngelHolderBefore(
            history,
            expiryIndex,
            holderId);
        var holderKnownNow = session.GetPlayer(holderId).State.ModeratorKnownRole ==
            MainRoleType.Angel;
        var validProjection = holderKnownAtExpiry
            ? projections is [var projectionIndex] &&
              projectionIndex == expiryIndex + 1
            : holderKnownNow
                ? projections.Length == 1
                : projections.Length == 0;
        if (!validProjection)
        {
            throw new InvalidOperationException(
                "Angel holder history has an invalid post-expiry Simple Villager projection.");
        }
    }

    private static void ApplyKnownHolderProjection(GameSession session)
    {
        var holder = session.GetModeratorPhysicalCharacterCards()
            .SingleOrDefault(state =>
                state.Card.PrintedRole == MainRoleType.Angel &&
                state.Zone == PhysicalCharacterCardZone.PlayerOwned);
        if (holder?.OwnerPlayerId is not { } holderId)
        {
            return;
        }

        var player = session.GetPlayer(holderId);
        if (player.State.ModeratorKnownRole == MainRoleType.Angel &&
            player.State.CurrentRole == MainRoleType.Angel)
        {
            session.AssignRole(holderId, MainRoleType.SimpleVillager);
        }
    }

    private static bool IsActivated(GameSession session) =>
        session.RoleLockIn.RoleComposition.Any(card =>
            card.PrintedRole == MainRoleType.Angel);

    private static bool HasExpired(GameSession session) =>
        session.GameHistoryLog.OfType<AngelExpiredLogEntry>().Any();

    private static bool HasPassedNightTwoDawnWindow(GameSession session) =>
        session.TurnNumber > 2 ||
        session.TurnNumber == 2 &&
        session.GetCurrentPhase() == GamePhase.Day;

    private static bool IsEliminationForWindow(
        PlayerEliminatedLogEntry elimination,
        int currentTurn,
        VictoryCheckWindow window) =>
        (currentTurn, window, elimination.TurnNumber, elimination.CurrentPhase) switch
        {
            (1, VictoryCheckWindow.Dawn, 1, GamePhase.Dawn) => true,
            (2, VictoryCheckWindow.PreNight, 1, GamePhase.Day) => true,
            (2, VictoryCheckWindow.Dawn, 2, GamePhase.Dawn) => true,
            _ => false
        };

    private static bool HasQualifyingAngelElimination(
        IReadOnlyList<GameLogEntryBase> history)
    {
        for (var index = 0; index < history.Count; index++)
        {
            if (history[index] is PlayerEliminatedLogEntry elimination &&
                (elimination is { TurnNumber: 1, CurrentPhase: GamePhase.Dawn } or
                    { TurnNumber: 1, CurrentPhase: GamePhase.Day } or
                    { TurnNumber: 2, CurrentPhase: GamePhase.Dawn }) &&
                WasRevealedAsPhysicalAngel(history, index, elimination.PlayerId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasQualifyingAngelElimination(
        IReadOnlyList<GameLogEntryBase> history,
        int victoryIndex,
        VictoryConditionMetLogEntry victory)
    {
        if (!IsAngelVictoryBoundary(victory))
        {
            return false;
        }

        for (var index = 0; index < victoryIndex; index++)
        {
            if (history[index] is PlayerEliminatedLogEntry elimination &&
                IsEliminationForWindow(
                    elimination,
                    victory.TurnNumber,
                    victory.VictoryCheckWindow) &&
                WasRevealedAsPhysicalAngel(
                    history,
                    index,
                    elimination.PlayerId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAngelVictoryBoundary(
        VictoryConditionMetLogEntry victory) =>
        (victory.TurnNumber,
            victory.CurrentPhase,
            victory.VictoryCheckWindow) is
            (1, GamePhase.Day, VictoryCheckWindow.Dawn) or
            (2, GamePhase.Night, VictoryCheckWindow.PreNight) or
            (2, GamePhase.Day, VictoryCheckWindow.Dawn);

    private static bool IsResolvedNightTwoDawnImmediatelyBeforeExpiry(
        IReadOnlyList<GameLogEntryBase> history,
        int expiryIndex)
    {
        var boundaryIndex = expiryIndex - 1;
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

    private static bool WasKnownAngelHolderBefore(
        IReadOnlyList<GameLogEntryBase> history,
        int boundaryIndex,
        Guid playerId)
    {
        var preceding = history.Take(boundaryIndex);
        return preceding.OfType<RoleIdentificationLogEntry>()
                   .Any(entry =>
                       entry.Role == MainRoleType.Angel &&
                       entry.PlayerIds.Contains(playerId)) ||
               preceding.OfType<PermanentRoleSwapCommittedLogEntry>()
                   .Any(entry =>
                       entry.PlayerId == playerId &&
                       entry.NewCurrentRole == MainRoleType.Angel);
    }

    private static bool WasRevealedAsPhysicalAngel(
        IReadOnlyList<GameLogEntryBase> history,
        int eliminationIndex,
        Guid playerId)
    {
        var preceding = history.Take(eliminationIndex);
        var ownsPhysicalAngel = preceding
                .OfType<PhysicalCharacterCardOwnershipObservedLogEntry>()
                .Any(entry =>
                    entry.PlayerId == playerId &&
                    entry.PrintedRole == MainRoleType.Angel) ||
            preceding.OfType<PermanentRoleSwapCommittedLogEntry>()
                .Any(entry =>
                    entry.PlayerId == playerId &&
                    entry.NewCurrentRole == MainRoleType.Angel);
        return ownsPhysicalAngel &&
               preceding.OfType<RoleRevealLogEntry>()
                   .Any(entry =>
                       entry.RevealedRoles.TryGetValue(playerId, out var role) &&
                       role == MainRoleType.Angel);
    }

    private static bool IncludesAngel(GameResult result) => result switch
    {
        SingleFactionGameResult single => single.Faction == Faction.Angel,
        SharedVictoryGameResult shared => shared.Factions.Contains(Faction.Angel),
        _ => false
    };
}
