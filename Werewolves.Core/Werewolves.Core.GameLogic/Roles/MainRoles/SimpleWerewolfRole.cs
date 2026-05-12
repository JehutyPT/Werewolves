using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

/// <summary>
/// Simple Werewolf role implementation using the polymorphic hook listener pattern.
/// Inherits from StandardNightRoleHookListener for standard target selection workflow.
/// </summary>
internal class SimpleWerewolfRole : StandardNightRoleHookListener
{
    
    internal override string PublicName => GameStrings.SimpleWerewolfRoleName;
    public override ListenerIdentifier Id => ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf);
    protected override bool HasNightPowers => true;

    protected override ModeratorInstruction GenerateTargetSelectionInstruction(GameSession session, ModeratorResponse input)
    {
        var werewolves = GetAliveRolePlayers(session);
        if (werewolves == null || !werewolves.Any())
        {
            throw new InvalidOperationException("No alive werewolves found for target selection.");
        }

        var potentialTargets = GetPotentialTargets(session, false);

        return new SelectPlayersInstruction(
            publicAnnouncement: GameStrings.WerewolvesChooseVictimPrompt,
            selectablePlayerIds: potentialTargets,
            affectedPlayerIds: werewolves.Select(w => w.Id).ToList(),
            countConstraint: NumberRangeConstraint.Single
        );
    }

    protected override void ProcessTargetSelectionNoFeedback(GameSession session, ModeratorResponse input)
    {
        var victimId = input.SelectedPlayerIds!.First();

        session.PerformNightAction(NightActionType.WerewolfVictimSelection, victimId);
    }
}
