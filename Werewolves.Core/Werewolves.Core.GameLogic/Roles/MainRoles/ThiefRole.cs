using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum ThiefRoleState
{
	Awake,
	AwaitingOfferChoice,
	ReadyToSleep,
	Asleep
}

internal sealed class ThiefRole : NightRoleHookListener<ThiefRoleState>
{
	internal override string PublicName => GameStrings.ThiefRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Thief);

	protected override ThiefRoleState WokenUpStateEnum => ThiefRoleState.Awake;
	protected override ThiefRoleState ReadyToSleepStateEnum => ThiefRoleState.ReadyToSleep;
	protected override ThiefRoleState AsleepStateEnum => ThiefRoleState.Asleep;
	protected override bool HasNightPowers => false;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (session.TurnNumber != 1)
		{
			return HookListenerActionResult.Skip();
		}

		// A committed exchange removes the last active printed Thief card before
		// the pending sleep confirmation is accepted. Resume the already-active
		// listener directly so the hook loop can clear it and continue forward.
		return GetCurrentListenerState(session) == null
			? base.Execute(session, input)
			: ExecuteCore(session, input);
	}

	protected override HookListenerActionResult HandleRoleWakeupAndId(
		GameSession session,
		ModeratorResponse input)
	{
		var result = base.HandleRoleWakeupAndId(session, input);
		if (result.Instruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.WakeRole
		    })
		{
			return result;
		}

		var holder = GetCurrentThief(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName),
				affectedPlayerIds: [holder.Id]),
			ThiefRoleState.Awake);
	}

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		listenerState = string.Empty;
		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is SelectOptionsInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.ChooseThiefOffer
		    } &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			listenerState = ThiefRoleState.AwaitingOfferChoice.ToString();
			return true;
		}

		if (hook != GameHook.NightMainActionLoop ||
		    pendingInstruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
			    AffectedPlayerIds: [var playerId]
		    })
		{
			return base.TryResolvePendingInstructionContinuation(
				hook,
				session,
				pendingInstruction,
				out listenerState);
		}

		if (!ThiefOfferRules.HasValidCommittedChoice(session, playerId))
		{
			return false;
		}

		listenerState = ThiefRoleState.ReadyToSleep.ToString();
		return true;
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			null,
			[ThiefRoleState.Awake, ThiefRoleState.Asleep],
			HandleRoleWakeupAndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			ThiefRoleState.Awake,
			ThiefRoleState.AwaitingOfferChoice,
			HandleNightPowerUse_AndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			ThiefRoleState.AwaitingOfferChoice,
			ThiefRoleState.ReadyToSleep,
			CommitChoice),
		CreateStage(
			GameHook.NightMainActionLoop,
			ThiefRoleState.ReadyToSleep,
			ThiefRoleState.Asleep,
			HandleAsleepConfirmation),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			ThiefRoleState.Asleep,
			(_, _) => HookListenerActionResult.Complete(ThiefRoleState.Asleep))
	];

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		var holder = GetCurrentThief(session);
		var offer1 = session.RoleLockIn.Offer1
			?? throw new InvalidOperationException("Thief requires Offer1.");
		var offer2 = session.RoleLockIn.Offer2
			?? throw new InvalidOperationException("Thief requires Offer2.");
		var options = new List<ModeratorOption>
		{
			new(ThiefOfferOptionIds.Offer1, offer1.PrintedRole.GetPublicName()),
			new(ThiefOfferOptionIds.Offer2, offer2.PrintedRole.GetPublicName())
		};
		if (ThiefOfferRules.IsDeclineLegal(
			offer1.PrintedRole,
			offer2.PrintedRole))
		{
			options.Add(new ModeratorOption(
				ThiefOfferOptionIds.Decline,
				GameStrings.DeclineOption));
		}

		return HookListenerActionResult.NeedInput(
			new SelectOptionsInstruction(
				ModeratorInstructionSemantic.ChooseThiefOffer,
				options,
				NumberRangeConstraint.Single,
				privateInstruction: GameStrings.ThiefOfferSelectionInstruction,
				affectedPlayerIds: [holder.Id]),
			ThiefRoleState.AwaitingOfferChoice);
	}

	protected override void ProcessRoleIdentification(
		GameSession session,
		ModeratorResponse input)
	{
		var holderId = input.SelectedPlayerIds?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"Thief identification requires exactly one Player.");
		if (holderId == Guid.Empty)
		{
			throw new InvalidOperationException(
				"Thief identification requires exactly one Player.");
		}

		if (session.GetPlayerState(holderId).PhysicalCharacterCardId is null)
		{
			var thiefCard = session.GetModeratorPhysicalCharacterCards()
				.FirstOrDefault(state =>
					state.Zone == PhysicalCharacterCardZone.DealPool &&
					state.OwnerPlayerId is null &&
					state.Card.PrintedRole == MainRoleType.Thief)
				?.Card ?? throw new InvalidOperationException(
					"No unowned Thief Physical Character Card is available.");
			if (!session.TryRecordPhysicalCharacterCardOwnership(
					session.RoleLockIn.Version,
					holderId,
					thiefCard.Id))
			{
				throw new InvalidOperationException(
					"The identified Thief Physical Character Card could not be bound.");
			}
		}

		base.ProcessRoleIdentification(session, input);
	}

	private HookListenerActionResult CommitChoice(
		GameSession session,
		ModeratorResponse input)
	{
		var holder = GetCurrentThief(session);
		var selectedOptionId = input.SelectedOptionIds?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"The Thief choice requires one semantic option.");
		var offer1 = session.RoleLockIn.Offer1!;
		var offer2 = session.RoleLockIn.Offer2!;
		if (selectedOptionId == ThiefOfferOptionIds.Decline)
		{
			if (!ThiefOfferRules.TryCommitDecline(session, holder.Id))
			{
				throw new InvalidOperationException(
					"The Thief decline could not be committed.");
			}

			return PrepareSleepInstruction(holder.Id);
		}

		var selected = selectedOptionId switch
		{
			ThiefOfferOptionIds.Offer1 => (Selected: offer1, Other: offer2),
			ThiefOfferOptionIds.Offer2 => (Selected: offer2, Other: offer1),
			_ => throw new InvalidOperationException("The Thief option is unknown.")
		};
		var outgoing = session.GetModeratorPhysicalCharacterCards()
			.Single(state =>
				state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
				state.OwnerPlayerId == holder.Id &&
				state.Card.PrintedRole == MainRoleType.Thief)
			.Card;
		var request = PermanentRoleSwapRules.CreateThiefExchangeRequest(
			session,
			holder.Id,
			outgoing,
			selected.Selected,
			selected.Other);
		if (!PermanentRoleSwapRules.CanCommit(session, request) ||
		    !session.TryCommitPermanentRoleSwap(request))
		{
			throw new InvalidOperationException("The Thief exchange could not be committed.");
		}

		return PrepareSleepInstruction(holder.Id);
	}

	protected override HookListenerActionResult PrepareSleepInstruction(
		GameSession session) => PrepareSleepInstruction(GetCurrentThief(session).Id);

	private HookListenerActionResult PrepareSleepInstruction(Guid playerId) =>
		HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(PublicName),
				affectedPlayerIds: [playerId]),
			ThiefRoleState.ReadyToSleep);

	private static IPlayer GetCurrentThief(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.SingleOrDefault(player => player.State.CurrentRole == MainRoleType.Thief)
		?? throw new InvalidOperationException("No living Thief is available.");

}
