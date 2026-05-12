using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Models.GameHookListeners;


/// <summary>
/// Meant to represent night roles that require immediate feedback after target selection,
/// such as the Seer role.
/// </summary>
internal abstract class ImmediateFeedbackNightRoleHookListener : StandardNightRoleHookListener<ImmediateFeedbackNightRoleState>
{
	protected override ImmediateFeedbackNightRoleState WokenUpStateEnum => ImmediateFeedbackNightRoleState.AwaitingAwakeConfirmation;
	protected override ImmediateFeedbackNightRoleState AwaitingTargetSelectionEnum => ImmediateFeedbackNightRoleState.AwaitingTargetSelection;
	protected ImmediateFeedbackNightRoleState AwaitingModeratorFeedbackEnum => ImmediateFeedbackNightRoleState.AwaitingModeratorFeedback;
	protected override ImmediateFeedbackNightRoleState ReadyToSleepStateEnum => ImmediateFeedbackNightRoleState.AwaitingSleepConfirmation;
	protected override ImmediateFeedbackNightRoleState AsleepStateEnum => ImmediateFeedbackNightRoleState.Asleep;
	protected override bool HasNightPowers => true;

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(GameHook.NightMainActionLoop, null, WokenUpStateEnum, HandleRoleWakeupAndId),
		CreateStage(GameHook.NightMainActionLoop, WokenUpStateEnum, AwaitingTargetSelectionEnum, HandleNightPowerUse_AndId),
		CreateStage(GameHook.NightMainActionLoop, AwaitingTargetSelectionEnum, AwaitingModeratorFeedbackEnum, HandleParseNightPowerConsequences),
		CreateStage(GameHook.NightMainActionLoop, AwaitingModeratorFeedbackEnum, ReadyToSleepStateEnum, ConfirmModeratorFeedbackGiven),
		CreateStage(GameHook.NightMainActionLoop, ReadyToSleepStateEnum, AsleepStateEnum, HandleAsleepConfirmation),
		CreateEndStage(GameHook.NightMainActionLoop, AsleepStateEnum, (_, _) => HookListenerActionResult.Complete(AsleepStateEnum)),
	];

	protected abstract ModeratorInstruction ProcessTargetSelectionWithFeedback(GameSession session, ModeratorResponse input);

	private HookListenerActionResult HandleParseNightPowerConsequences(GameSession session, ModeratorResponse input)
	{
		var instruction = ProcessTargetSelectionWithFeedback(session, input);
		return HookListenerActionResult.NeedInput(instruction, AwaitingModeratorFeedbackEnum);
	}

	private HookListenerActionResult ConfirmModeratorFeedbackGiven(GameSession session, ModeratorResponse input)
		=> PrepareSleepInstruction(session);

	protected override void ProcessTargetSelectionNoFeedback(GameSession session, ModeratorResponse input) 
		=> throw new NotImplementedException("this class should not be calling ProcessTargetSelectionNoFeedback");
}
