using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
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
	private readonly RolePowerAvailabilityGateway _rolePowerAvailabilityGateway;

	private static readonly RolePowerDefinition WerewolfDetectionPower = new(
		new RolePowerIdentifier("seer-werewolf-detection"),
		RolePowerCategory.Chosen);

	internal SeerRole(RolePowerAvailabilityGateway rolePowerAvailabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(rolePowerAvailabilityGateway);
		_rolePowerAvailabilityGateway = rolePowerAvailabilityGateway;
	}

    public override ListenerIdentifier Id => ListenerIdentifier.Listener(MainRoleType.Seer);
    internal override string PublicName => GameStrings.SeerRoleName;
    protected override bool HasNightPowers => true;

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		listenerState = string.Empty;
		if (hook == GameHook.NightMainActionLoop &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			switch (pendingInstruction)
			{
				case SelectPlayersInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.SelectSeerTarget
				}:
					listenerState =
						ImmediateFeedbackNightRoleState
							.AwaitingTargetSelection
							.ToString();
					return true;
				case ConfirmationInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.PutRoleToSleep
				}:
					listenerState =
						ImmediateFeedbackNightRoleState
							.AwaitingSleepConfirmation
							.ToString();
					return true;
			}
		}

		return base.TryResolvePendingInstructionContinuation(
			hook,
			session,
			pendingInstruction,
			out listenerState);
	}

	protected override HookListenerActionResult HandleTargetSelectionRequest(
		GameSession session,
		ModeratorResponse input)
	{
		var seerPlayer = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No alive Seer found for Role Power availability.");
		var context = _rolePowerAvailabilityGateway.Evaluate(
			new RolePowerAttempt(
				seerPlayer,
				MainRoleType.Seer,
				WerewolfDetectionPower,
				RolePowerInstance.CreateCurrent(
					session,
					seerPlayer,
					MainRoleType.Seer,
					WerewolfDetectionPower)));

		return context.AvailabilityResult.IsAvailable
			? base.HandleTargetSelectionRequest(session, input)
			: HookListenerActionResult.NeedInput(
				new ConfirmationInstruction(
					ModeratorInstructionSemantic.PutRoleToSleep,
					GameStrings.RoleGoesToSleepSingle.Format(PublicName),
					affectedPlayerIds: [seerPlayer.Id]),
				ReadyToSleepStateEnum);
	}

    protected override ModeratorInstruction GenerateTargetSelectionInstruction(GameSession session, ModeratorResponse input)
    {
        var seerPlayer = GetAliveRolePlayers(session)?.FirstOrDefault();
        if (seerPlayer == null)
        {
            throw new InvalidOperationException("No alive Seer found for target selection.");
        }

        var potentialTargets = GetPotentialTargets(session, false);

        return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectSeerTarget,
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

        bool targetWakesWithWerewolves =
            session.GetFactionAgentKnowledge(targetId, Faction.Werewolf) ==
            FactionAgentKnowledge.KnownAgent;

        var privateFeedback = (targetWakesWithWerewolves
            ? GameStrings.SeerResultWerewolfTeam
            : GameStrings.SeerResultNotWerewolfTeam).Format(targetPlayer.Name);

        session.PerformNightAction(NightActionType.SeerCheck, targetId);

        return new ConfirmationInstruction(
			ModeratorInstructionSemantic.RevealSeerResult,
			privateInstruction: privateFeedback);
	}
}
