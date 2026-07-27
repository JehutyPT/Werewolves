using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Services;

internal static class DayPhaseHandlers
{
    internal static bool CanConductVote(GameSession session)
        => GameSessionQueries.GetEffectiveDayVoters(session).Count > 0;

    internal static ModeratorInstruction StartDebate(GameSession session, ModeratorResponse input)
        => new ConfirmationInstruction(
			ModeratorInstructionSemantic.StartDayDebate,
            publicAnnouncement: GameStrings.DebateStartsPrompt,
            privateInstruction: GameStrings.DebateModeratorInstructions);

    internal static ModeratorInstruction RequestNormalVoteOutcome(GameSession session, ModeratorResponse input)
    {
        var alivePlayers = session.GetPlayers().WithHealth(PlayerHealth.Alive);
        var activeRestriction =
            GameSessionQueries.GetActiveScapegoatVoterRestriction(session);
        var hasPendingJudgeObservation =
            GameSessionQueries.HasUnreportedStutteringJudgeSignalObservation(
                session);
        var privateInstruction = activeRestriction == null
            ? GameStrings.VoteStartsModeratorInstruction
            : GameStrings.ScapegoatEffectiveVotersInstruction.Format(
                string.Join(
                    Environment.NewLine,
                    GameSessionQueries.GetEffectiveDayVoters(session)
                        .Select(player => player.Name)));

        return new SelectPlayersInstruction(
            ModeratorInstructionSemantic.RecordDayVote,
            alivePlayers.ToIdSet(),
            NumberRangeConstraint.SingleOptional,
            publicAnnouncement: hasPendingJudgeObservation
                ? null
                : GameStrings.VoteStartsPublicInstruction,
            privateInstruction: privateInstruction)
        {
            EmptySelectionOptionLabel = GameStrings.DayVoteNoEliminationOption
        };
    }

    internal static void ExpireScapegoatVoterRestriction(
        GameSession session,
        ModeratorResponse input)
    {
        var restriction =
            GameSessionQueries.GetActiveScapegoatVoterRestriction(session);
        if (restriction != null)
        {
            session.ExpireScapegoatVoterRestriction(
                restriction.ScopeId);
        }
    }

    internal static Guid? RecordNormalVoteOutcome(GameSession session, ModeratorResponse input)
    {
        var selectedPlayer = input.SelectedPlayerIds!;

        if (selectedPlayer.Count == 0)
        {
            session.PerformDayVote(null);
            return null;
        }

        var playerId = selectedPlayer.First();
        session.PerformDayVote(playerId);

        return playerId;
    }

}
