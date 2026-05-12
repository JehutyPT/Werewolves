using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Models.GameHookListeners;

internal abstract class NightRoleHookListener<T> : RoleHookListener<T> where T : struct, Enum
{
	#region Abstract Members
	/// <summary>
	/// The enum this role uses to indicate it is requesting its player(s) to wake up.
	/// </summary>
	protected abstract T WokenUpStateEnum { get; }
	protected abstract T ReadyToSleepStateEnum { get; }
	protected abstract T AsleepStateEnum { get; }
	protected abstract bool HasNightPowers { get; }

	protected override List<RoleStateMachineStage> DefineStateMachineStages() => 
	[
		CreateStage(GameHook.NightMainActionLoop, null, [WokenUpStateEnum, AsleepStateEnum], HandleRoleWakeupAndId),
		CreateOpenEndedStage(GameHook.NightMainActionLoop, WokenUpStateEnum, HandleNightPowerUse_AndId),
		CreateStage(GameHook.NightMainActionLoop, ReadyToSleepStateEnum, AsleepStateEnum, HandleAsleepConfirmation),
		CreateEndStage(GameHook.NightMainActionLoop, AsleepStateEnum, (_, _) => HookListenerActionResult.Complete(AsleepStateEnum)),
	];

	/// <summary>
	/// Defines the behaviour when the role has just finished waking up,
	/// already after any potential identification process.
	/// </summary>
	/// <param name="session"></param>
	/// <param name="input"></param>
	/// <returns></returns>
	protected abstract HookListenerActionResult HandleNightPowerUse(GameSession session, ModeratorResponse input);

	#endregion

	#region Default State Machine Advancement
	
	protected virtual HookListenerActionResult HandleNightPowerUse_AndId(GameSession session, ModeratorResponse input)
	{
		var state = GetCurrentListenerState(session);
		
		if (session.TurnNumber == 1 && state.Equals(WokenUpStateEnum))
		{
			ProcessRoleIdentification(session, input);
			//fall through intentionally to HandleNightPowerUse so the identification process flows seamlessly
		}

		var output = HandleNightPowerUse(session, input);

		return output;
	}

	protected virtual HookListenerActionResult HandleRoleWakeupAndId(GameSession session, ModeratorResponse input)
	{
		HookListenerActionResult output;
		
		// wake up the role. piggyback the id request if it's the first night
		if (session.TurnNumber == 1 || HasNightPowers)
		{
			output = session.TurnNumber == 1
				? PrepareWakeupInstructionWithIdRequest(session)
				: PrepareWakeupInstruction(session);
			
		}
		// otherwise, if it's not the first night and the role has no night powers, complete immediately
		else
		{
			output = HookListenerActionResult.Complete(AsleepStateEnum);
		}

		return output;
	}
	#endregion
	
	#region Helper functions
	private HookListenerActionResult PrepareWakeupInstruction(GameSession session)
	{
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(GameStrings.RoleWakesUp.Format(PublicName)),
			WokenUpStateEnum);
	}

	protected virtual HookListenerActionResult PrepareWakeupInstructionWithIdRequest(GameSession session)
	{
		var defaultInstruction = PrepareWakeupInstruction(session);

		var playersWithoutRole = 
			session.GetPlayers().
				WithHealth(PlayerHealth.Alive).
				WithRole(null).
				ToIdSet();

		var publicText = defaultInstruction.Instruction!.PublicAnnouncement!;
		var privateInstruction = "";

		var roleCount = session.RoleInPlayCount(Id);
		if (roleCount == 1)
		{
			privateInstruction = GameStrings.RoleSingleIdentificationPrompt.Format(PublicName);
		}
		else
		{
			privateInstruction = GameStrings.RoleMultipleIdentificationPrompt.Format(PublicName);
		}

		return HookListenerActionResult.NeedInput(
			new SelectPlayersInstruction(
				playersWithoutRole,
				NumberRangeConstraint.Exact(roleCount),
				publicText
			),
			WokenUpStateEnum);
	}

	protected virtual void ProcessRoleIdentification(GameSession session, ModeratorResponse input)
	{
		session.AssignRole(input.SelectedPlayerIds!, Id);
	}


	protected virtual HookListenerActionResult HandleAsleepConfirmation(GameSession session, ModeratorResponse input)
	{
		return HookListenerActionResult.Complete(AsleepStateEnum);
	}

	protected virtual HookListenerActionResult PrepareSleepInstruction(GameSession session)
	{
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(GameStrings.RoleGoesToSleepSingle.Format(PublicName)),
			ReadyToSleepStateEnum);
	}

	#endregion
}