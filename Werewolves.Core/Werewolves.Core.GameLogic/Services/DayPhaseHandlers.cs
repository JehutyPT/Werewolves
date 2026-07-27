using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Services;

internal static class DayPhaseHandlers
{
    internal static ModeratorInstruction StartDebate(GameSession session, ModeratorResponse input)
        => new ConfirmationInstruction(
			ModeratorInstructionSemantic.StartDayDebate,
            publicAnnouncement: GameStrings.DebateStartsPrompt,
            privateInstruction: GameStrings.DebateModeratorInstructions);

    internal static ModeratorInstruction RequestNormalVoteOutcome(GameSession session, ModeratorResponse input)
    {
        var alivePlayers = session.GetPlayers().WithHealth(PlayerHealth.Alive);

        return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.RecordDayVote,
            alivePlayers.ToIdSet(),
            NumberRangeConstraint.SingleOptional,
            publicAnnouncement: GameStrings.VoteStartsPublicInstruction,
            privateInstruction: GameStrings.VoteStartsModeratorInstruction)
        {
            EmptySelectionOptionLabel = GameStrings.DayVoteNoEliminationOption
        };
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
