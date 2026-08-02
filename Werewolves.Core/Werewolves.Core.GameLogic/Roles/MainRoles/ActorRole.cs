using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum ActorRoleState
{
	Awake,
	AwaitingSetupCardChoice,
	ReadyToSleep,
	Asleep
}

internal sealed class ActorRole : NightRoleHookListener<ActorRoleState>
{
	private static readonly RolePowerDefinition SetupCardSelectionPower = new(
		new RolePowerIdentifier("actor-setup-card-selection"),
		RolePowerCategory.Chosen);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	internal ActorRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.ActorRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Actor);

	protected override ActorRoleState WokenUpStateEnum => ActorRoleState.Awake;
	protected override ActorRoleState ReadyToSleepStateEnum =>
		ActorRoleState.ReadyToSleep;
	protected override ActorRoleState AsleepStateEnum => ActorRoleState.Asleep;
	protected override bool HasNightPowers => true;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (GetCurrentListenerState(session) == null)
		{
			session.TryExpireActorBorrowedRolePowerActivation();
			if (session.GetModeratorRemainingActorSetupCards().Count == 0)
			{
				return HookListenerActionResult.Skip();
			}
			if (GameSessionQueries.IsVillagerRolePowerSuppressionActive(session))
			{
				return HookListenerActionResult.Skip();
			}

			var committedHolder = GetAliveRolePlayers(session)?.SingleOrDefault();
			if (committedHolder != null &&
			    !IsSetupCardSelectionAvailable(session, committedHolder))
			{
				return HookListenerActionResult.Skip();
			}
		}

		return base.Execute(session, input);
	}

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is SelectOptionsInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.ChooseActorSetupCard
		    } &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			listenerState = ActorRoleState.AwaitingSetupCardChoice.ToString();
			return true;
		}

		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.PutRoleToSleep
		    } &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			listenerState = ActorRoleState.ReadyToSleep.ToString();
			return true;
		}

		return base.TryResolvePendingInstructionContinuation(
			hook,
			session,
			pendingInstruction,
			out listenerState);
	}

	internal static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		ModeratorInstruction nextInstruction,
		out ActorBorrowedRolePowerActivation? activation)
	{
		activation = null;
		if (startingInstruction is not SelectOptionsInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.ChooseActorSetupCard
		    } selection)
		{
			return false;
		}

		if (selection.SelectionRange != NumberRangeConstraint.SingleOptional ||
		    selection.AffectedPlayerIds is not [var actorId] ||
		    input.InstructionId != selection.InstructionId ||
		    input.Type != ExpectedInputType.OptionSelection ||
		    input.SelectedOptionIds is not [var selectedOptionId] ||
		    !Guid.TryParseExact(selectedOptionId, "D", out var selectedCardId) ||
		    selection.Options.Count(option =>
			    StringComparer.Ordinal.Equals(option.Id, selectedOptionId)) != 1 ||
		    nextInstruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
			    AffectedPlayerIds: [var sleepingActorId]
		    } ||
		    sleepingActorId != actorId)
		{
			throw new InvalidOperationException(
				"The Actor setup-card spend must correlate to its exact accepted option and sleep continuation.");
		}

		var selectedCard = session.GetModeratorActorSetupCards().Cards
			.SingleOrDefault(card => card.Id == selectedCardId);
		var active = session
			.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (selectedCard is null ||
		    active is null ||
		    active.ActingPlayerId != actorId ||
		    active.ActingRole != MainRoleType.Actor ||
		    active.SelectedCardId != selectedCardId ||
		    active.SourceRole != selectedCard.PrintedRole ||
		    session.GetPlayerState(actorId) is not
		    {
			    Health: PlayerHealth.Alive,
			    CurrentRole: MainRoleType.Actor
		    } ||
		    session.GetModeratorSpentActorSetupCards().Count(card =>
			    card.Id == selectedCardId) != 1)
		{
			throw new InvalidOperationException(
				"The Actor setup-card spend does not match its living holder, selected card, and active borrowed lineage.");
		}

		activation = active;
		return true;
	}

	internal static bool HasExpectedDeclinedChoiceSleep(
		GameSession session,
		ModeratorInstruction pendingInstruction)
	{
		if (session.GetModeratorActiveActorBorrowedRolePowerActivation() is not null ||
		    pendingInstruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
			    AffectedPlayerIds: [var actorId]
		    })
		{
			return false;
		}

		return session.GetPlayers().Count(player =>
			player.Id == actorId &&
			player.State.Health == PlayerHealth.Alive &&
			player.State.CurrentRole == MainRoleType.Actor) == 1;
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			null,
			[ActorRoleState.Awake, ActorRoleState.Asleep],
			HandleRoleWakeupAndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			ActorRoleState.Awake,
			ActorRoleState.AwaitingSetupCardChoice,
			HandleNightPowerUse_AndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			ActorRoleState.AwaitingSetupCardChoice,
			ActorRoleState.ReadyToSleep,
			CommitChoice),
		CreateStage(
			GameHook.NightMainActionLoop,
			ActorRoleState.ReadyToSleep,
			ActorRoleState.Asleep,
			HandleAsleepConfirmation),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			ActorRoleState.Asleep,
			(_, _) => HookListenerActionResult.Complete(ActorRoleState.Asleep))
	];

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

		var holder = GetActor(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName),
				affectedPlayerIds: [holder.Id]),
			ActorRoleState.Awake);
	}

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		var holder = GetActor(session);
		if (!IsSetupCardSelectionAvailable(session, holder))
		{
			return PrepareSleepInstruction(session);
		}
		var options = session.GetModeratorRemainingActorSetupCards()
			.Select(card => new ModeratorOption(
				card.Id.ToString("D"),
				card.PrintedRole.GetPublicName()))
			.ToArray();

		return HookListenerActionResult.NeedInput(
			new SelectOptionsInstruction(
				ModeratorInstructionSemantic.ChooseActorSetupCard,
				options,
				NumberRangeConstraint.SingleOptional,
				privateInstruction:
					GameStrings.ActorSetupCardSelectionInstruction,
				affectedPlayerIds: [holder.Id]),
			ActorRoleState.AwaitingSetupCardChoice);
	}

	private HookListenerActionResult CommitChoice(
		GameSession session,
		ModeratorResponse input)
	{
		var selectedOptionIds = input.SelectedOptionIds
			?? throw new InvalidOperationException(
				"Actor setup-card choice requires an option selection response.");
		if (selectedOptionIds.Count > 1)
		{
			throw new InvalidOperationException(
				"Actor may select at most one setup card.");
		}
		if (selectedOptionIds.Count == 1)
		{
			var selectedOptionId = selectedOptionIds.Single();
			if (!Guid.TryParseExact(selectedOptionId, "D", out var selectedCardId))
			{
				throw new InvalidOperationException(
					"Actor setup-card option identity is invalid.");
			}

			var holder = GetActor(session);
			if (!session.TrySpendActorSetupCard(
					holder.Id,
					selectedCardId,
					out _))
			{
				throw new InvalidOperationException(
					"Actor setup-card selection is no longer available.");
			}
		}

		return PrepareSleepInstruction(session);
	}

	protected override HookListenerActionResult PrepareSleepInstruction(
		GameSession session) =>
		HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(PublicName),
				affectedPlayerIds: [GetActor(session).Id]),
			ActorRoleState.ReadyToSleep);

	private IPlayer GetActor(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException("No living Actor found.");

	private bool IsSetupCardSelectionAvailable(
		GameSession session,
		IPlayer holder)
	{
		var instance = RolePowerInstance.CreateCurrent(
			session,
			holder,
			MainRoleType.Actor,
			SetupCardSelectionPower);
		return _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				holder,
				MainRoleType.Actor,
				SetupCardSelectionPower,
				instance)).AvailabilityResult.IsAvailable;
	}
}
