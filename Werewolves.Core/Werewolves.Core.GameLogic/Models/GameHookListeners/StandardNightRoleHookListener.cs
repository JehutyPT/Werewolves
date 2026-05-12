using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Models.GameHookListeners;

/// <summary>
/// Meant to represent simple night roles that only need to prompt for a target and process the selection.
/// Regardless of whether the target selection or processing are complex in nature or not.
/// </summary>
/// <typeparam name="T"></typeparam>
internal abstract class StandardNightRoleHookListener<T> : NightRoleHookListener<T> where T: struct, Enum
{
	protected abstract T AwaitingTargetSelectionEnum { get; }

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(GameHook.NightMainActionLoop, null, WokenUpStateEnum, HandleRoleWakeupAndId),
		CreateStage(GameHook.NightMainActionLoop, WokenUpStateEnum, AwaitingTargetSelectionEnum, HandleNightPowerUse_AndId),
		CreateStage(GameHook.NightMainActionLoop, AwaitingTargetSelectionEnum, ReadyToSleepStateEnum, HandleParseNightPowerConsequences),
		CreateStage(GameHook.NightMainActionLoop, ReadyToSleepStateEnum, AsleepStateEnum, HandleAsleepConfirmation),
		CreateEndStage(GameHook.NightMainActionLoop, AsleepStateEnum, (_, _) => HookListenerActionResult.Complete(AsleepStateEnum)),
	];

	protected abstract ModeratorInstruction GenerateTargetSelectionInstruction(GameSession session, ModeratorResponse input);

	protected abstract void ProcessTargetSelectionNoFeedback(GameSession session, ModeratorResponse input);

	protected override HookListenerActionResult HandleNightPowerUse(GameSession session, ModeratorResponse input) =>
		HandleTargetSelectionRequest(session, input);

	protected virtual HookListenerActionResult HandleTargetSelectionRequest(GameSession session, ModeratorResponse input)
	{
		var instruction = GenerateTargetSelectionInstruction(session, input);
		return HookListenerActionResult.NeedInput(instruction, AwaitingTargetSelectionEnum);
		
	}

	private HookListenerActionResult HandleParseNightPowerConsequences(GameSession session, ModeratorResponse input)
	{
		ProcessTargetSelectionNoFeedback(session, input);
		return PrepareSleepInstruction(session);
	}
}

internal abstract class StandardNightRoleHookListener : StandardNightRoleHookListener<StandardNightRoleState>
{
	protected override StandardNightRoleState WokenUpStateEnum => StandardNightRoleState.AwaitingAwakeConfirmation;
	protected override StandardNightRoleState AwaitingTargetSelectionEnum => StandardNightRoleState.AwaitingTargetSelection;
	protected override StandardNightRoleState ReadyToSleepStateEnum => StandardNightRoleState.AwaitingSleepConfirmation;
	protected override StandardNightRoleState AsleepStateEnum => StandardNightRoleState.Asleep;
	protected override bool HasNightPowers => true;
}