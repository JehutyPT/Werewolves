using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
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

		if (state.Equals(WokenUpStateEnum) &&
		    !IsCompleteRoleHolderSetKnown(session))
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
		
		if (!IsCompleteRoleHolderSetKnown(session))
		{
			output = PrepareWakeupInstructionWithIdRequest(session);
		}
		// Wake for genuine first-night or recurring behavior once identification is complete.
		else if (session.TurnNumber == 1 || HasNightPowers)
		{
			output = PrepareWakeupInstruction(session);
		}
		// Otherwise, later-night Roles with no powers complete immediately.
		else
		{
			output = HookListenerActionResult.Complete(AsleepStateEnum);
		}

		return output;
	}
	#endregion

	#region Helper functions
	protected bool HasExpectedAffectedRoleHolders(
		GameSession session,
		ModeratorInstruction pendingInstruction)
	{
		var holders = GetAliveRolePlayers(session)?
			.Select(player => player.Id)
			.ToHashSet();
		return holders is { Count: > 0 } &&
		       pendingInstruction.AffectedPlayerIds is { } affectedPlayerIds &&
		       affectedPlayerIds.ToHashSet().SetEquals(holders);
	}

	private bool IsCompleteRoleHolderSetKnown(GameSession session)
		=> GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			(MainRoleType)Id);

	private HookListenerActionResult PrepareWakeupInstruction(GameSession session)
	{
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName)),
			WokenUpStateEnum);
	}

	protected virtual HookListenerActionResult PrepareWakeupInstructionWithIdRequest(GameSession session)
	{
		var defaultInstruction = PrepareWakeupInstruction(session);

		var role = (MainRoleType)Id;
		var selectablePlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == role ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == null ||
				  player.State.ModeratorKnownRole == role)))
			.ToIdSet();

		var publicText = defaultInstruction.Instruction!.PublicAnnouncement!;
		var privateInstruction = "";

		var roleCount = GetExpectedLivingRoleHolderCount(session);
		var committedLivingRoleHolderCount = GetCommittedLivingRoleHolderIds(session).Count;
		if (roleCount <= 0 ||
		    committedLivingRoleHolderCount > roleCount ||
		    selectablePlayerIds.Count < roleCount)
		{
			throw new InvalidOperationException(
				"Confirmed Role knowledge contradicts the required Living Role Holder count.");
		}

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
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				selectablePlayerIds: selectablePlayerIds,
				countConstraint: NumberRangeConstraint.Exact(roleCount),
				publicAnnouncement: publicText,
				privateInstruction: privateInstruction,
				affectedPlayerIds: null,
				roleIdentification: (MainRoleType)Id
			),
			WokenUpStateEnum);
	}

	protected virtual void ProcessRoleIdentification(GameSession session, ModeratorResponse input)
	{
		var selectedPlayerIds = input.SelectedPlayerIds!.ToHashSet();
		IdentifyCompleteLivingRoleHolderSet(session, selectedPlayerIds);
	}


	protected virtual HookListenerActionResult HandleAsleepConfirmation(GameSession session, ModeratorResponse input)
	{
		return HookListenerActionResult.Complete(AsleepStateEnum);
	}

	protected virtual HookListenerActionResult PrepareSleepInstruction(GameSession session)
	{
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(PublicName)),
			ReadyToSleepStateEnum);
	}

	#endregion
}
