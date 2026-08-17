using Werewolves.Core.GameLogic.Queries;
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
        if (!IsActivated(session) ||
            GameSessionQueries.HasAngelExpired(session))
        {
            return false;
        }

        return GameSessionQueries.HasQualifyingAngelElimination(
            session,
            session.TurnNumber,
            window);
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
            GameSessionQueries.HasAngelExpired(session))
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
        if (!GameSessionQueries.HasAngelExpired(session))
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
        var angelVictories = GameSessionQueries.GetAngelVictories(session);
        if (angelVictories.Any(victory =>
                !IsActivated(session) ||
                !IsAngelVictoryBoundary(victory.Entry) ||
                !GameSessionQueries.HasQualifyingAngelElimination(
                    session,
                    victory.Entry.TurnNumber,
                    victory.Entry.VictoryCheckWindow,
                    victory.LogIndex)))
        {
            throw new InvalidOperationException(
                "Angel victory history has no qualifying elimination at its resolved victory window.");
        }

        var expiryIndexes = GameSessionQueries.GetAngelExpiryLogIndexes(session);
        if (expiryIndexes.Count > 1)
        {
            throw new InvalidOperationException(
                "Angel expiry history contains duplicate commits.");
        }

        var angelWon = angelVictories.Count > 0;
        if (expiryIndexes.Count == 0)
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
            !GameSessionQueries
                .IsResolvedNightTwoDawnImmediatelyBeforeAngelExpiry(
                    session,
                    expiryIndex) ||
            GameSessionQueries
                .HasQualifyingAngelEliminationThroughNightTwoDawn(
                    session,
                    expiryIndex))
        {
            throw new InvalidOperationException(
                "Angel expiry history is inconsistent with the resolved Night 2 Dawn window.");
        }

        if (GetPhysicalAngelHolderId(session) is not { } holderId)
        {
            return;
        }

        var projections = GameSessionQueries
            .GetPostExpirySimpleVillagerProjectionIndexes(
                session,
                expiryIndex,
                holderId);
        var holderKnownAtExpiry = GameSessionQueries.WasAngelHolderKnownBefore(
            session,
            expiryIndex,
            holderId);
        var holderKnownNow = session.GetPlayer(holderId).State.ModeratorKnownRole ==
            MainRoleType.Angel;
        var validProjection = holderKnownAtExpiry
            ? projections is [var projectionIndex] &&
              projectionIndex == expiryIndex + 1
            : holderKnownNow
                ? projections.Count == 1
                : projections.Count == 0;
        if (!validProjection)
        {
            throw new InvalidOperationException(
                "Angel holder history has an invalid post-expiry Simple Villager projection.");
        }
    }

    private static void ApplyKnownHolderProjection(GameSession session)
    {
        if (GetPhysicalAngelHolderId(session) is not { } holderId)
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

    private static Guid? GetPhysicalAngelHolderId(GameSession session) =>
        session.GetModeratorPhysicalCharacterCards()
            .SingleOrDefault(state =>
                state.Card.PrintedRole == MainRoleType.Angel &&
                state.Zone == PhysicalCharacterCardZone.PlayerOwned)
            ?.OwnerPlayerId;

    private static bool HasPassedNightTwoDawnWindow(GameSession session) =>
        session.TurnNumber > 2 ||
        session.TurnNumber == 2 &&
        session.Execution.CurrentPhase == GamePhase.Day;

    private static bool IsAngelVictoryBoundary(
        VictoryConditionMetLogEntry victory) =>
        (victory.TurnNumber,
            victory.CurrentPhase,
            victory.VictoryCheckWindow) is
            (1, GamePhase.Day, VictoryCheckWindow.Dawn) or
            (2, GamePhase.Night, VictoryCheckWindow.PreNight) or
            (2, GamePhase.Day, VictoryCheckWindow.Dawn);
}
