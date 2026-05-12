using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using static Werewolves.Core.GameLogic.Models.InternalMessages.HookListenerActionResult;

namespace Werewolves.Core.GameLogic.Models.GameHookListeners;

internal enum NightRoleIdOnlyState
{
	Awake,
	Asleep
}

internal abstract class NightRoleIdOnlyHookListener : NightRoleHookListener<NightRoleIdOnlyState>
{
	protected sealed override bool HasNightPowers => false;
	protected override NightRoleIdOnlyState WokenUpStateEnum => NightRoleIdOnlyState.Awake;
	protected override NightRoleIdOnlyState ReadyToSleepStateEnum => NightRoleIdOnlyState.Awake;
	protected override NightRoleIdOnlyState AsleepStateEnum => NightRoleIdOnlyState.Asleep;

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(GameHook.NightMainActionLoop, null, WokenUpStateEnum, HandleRoleWakeupAndId),
		CreateStage(GameHook.NightMainActionLoop, WokenUpStateEnum, AsleepStateEnum, HandleNightPowerUse_AndId),
		CreateEndStage(GameHook.NightMainActionLoop, AsleepStateEnum, (_, _) => Complete(AsleepStateEnum))
	];

	protected override HookListenerActionResult HandleNightPowerUse(GameSession session,
		ModeratorResponse input) =>
		Complete(AsleepStateEnum);
}