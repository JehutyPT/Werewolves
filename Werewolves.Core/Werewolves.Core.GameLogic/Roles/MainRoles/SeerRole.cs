using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

/// <summary>
/// Seer role implementation using the polymorphic hook listener pattern.
/// Inherits from StandardNightRoleHookListener for standard target selection workflow.
/// </summary>
internal class SeerRole : ImmediateFeedbackNightRoleHookListener
{
    public override ListenerIdentifier Id => ListenerIdentifier.Listener(MainRoleType.Seer);
    internal override string PublicName => GameStrings.SeerRoleName;
    protected override bool HasNightPowers => true;

    protected override ModeratorInstruction GenerateTargetSelectionInstruction(GameSession session, ModeratorResponse input)
    {
        var seerPlayer = GetAliveRolePlayers(session)?.FirstOrDefault();
        if (seerPlayer == null)
        {
            throw new InvalidOperationException("No alive Seer found for target selection.");
        }

        var potentialTargets = GetPotentialTargets(session, false);

        return new SelectPlayersInstruction(
            publicAnnouncement: GameStrings.SeerNightActionPrompt,
            countConstraint: NumberRangeConstraint.Single, 
            selectablePlayerIds: potentialTargets,
            affectedPlayerIds: new List<Guid> { seerPlayer.Id }
        );
    }

    protected override ModeratorInstruction ProcessTargetSelectionWithFeedback(GameSession session, ModeratorResponse input)
    {
		var targetId = input.SelectedPlayerIds!.First();
        var targetPlayer = session.GetPlayer(targetId);

        bool targetWakesWithWerewolves = targetPlayer.State.Team == Team.Werewolves;

        string privateFeedback = targetWakesWithWerewolves ?
            GameStrings.SeerResultWerewolfTeam : GameStrings.SeerResultNotWerewolfTeam;

        session.PerformNightAction(NightActionType.SeerCheck, targetId);

        return new ConfirmationInstruction(privateInstruction: privateFeedback);
	}
}
