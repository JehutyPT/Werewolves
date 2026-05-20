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
    internal static ModeratorInstruction StartDebate(GameSession session, ModeratorResponse input)
        => new ConfirmationInstruction(
            publicAnnouncement: GameStrings.DebateStartsPrompt,
            privateInstruction: GameStrings.DebateModeratorInstructions);

    internal static ModeratorInstruction RequestNormalVoteOutcome(GameSession session, ModeratorResponse input)
    {
        var alivePlayers = session.GetPlayers().WithHealth(PlayerHealth.Alive);

        return new SelectPlayersInstruction(
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

    internal static ModeratorInstruction? RequestRoleRevealIfNeeded(GameSession session, Guid playerId)
    {
        var votedPlayer = session.GetPlayer(playerId);
        if (votedPlayer.State.MainRole != null)
        {
            return null;
        }

        return new AssignRolesInstruction(
            [playerId],
            GameSessionQueries.GetUnassignedRoles(session),
            privateInstruction: GameStrings.RevealRolePromptSpecify);
    }

    internal static ModeratorInstruction ResolveNonTieVote(GameSession session, ModeratorResponse input)
    {
        var lynchedPlayerId = GameSessionQueries.GetCurrentVoteTarget(session)!.Value;
        var lynchedPlayer = session.GetPlayer(lynchedPlayerId);
        var lynchedPlayerState = lynchedPlayer.State;

        if (lynchedPlayerState.MainRole == null)
        {
            var lynchedPlayerRole = input.AssignedPlayerRoles!.Single().Value;
            session.AssignRole(lynchedPlayerId, lynchedPlayerRole);
        }

        if (lynchedPlayerState.IsImmuneToLynching)
        {
            var instruction = new ConfirmationInstruction(
                publicAnnouncement: lynchedPlayerState.LynchingImmunityAnnouncement!);

            session.ApplyStatusEffect(StatusEffectTypes.LynchingImmunityUsed, lynchedPlayerId);

            return instruction;
        }

        session.EliminatePlayer(lynchedPlayerId, EliminationReason.DayVote);

        return new ConfirmationInstruction(
            publicAnnouncement: GameStrings.SingleVictimEliminatedAnnounce.Format(lynchedPlayer.Name));
    }
}
