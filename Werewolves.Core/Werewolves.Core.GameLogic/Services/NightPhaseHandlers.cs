using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Services;

internal static class NightPhaseHandlers
{
    internal static ModeratorInstruction StartNight(GameSession session, ModeratorResponse input)
        => new ConfirmationInstruction(
			ModeratorInstructionSemantic.StartNight,
            publicAnnouncement: GameStrings.NightStartsPrompt,
            privateInstruction: GameStrings.ConfirmNightStarted);

    internal static ModeratorInstruction FinishNightActions(GameSession session, ModeratorResponse input)
        => new ConfirmationInstruction(
			ModeratorInstructionSemantic.FinishNightActions,
            publicAnnouncement: GameStrings.NightActionsCompletePrompt);
}
