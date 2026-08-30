using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum ActorRoleState
{
	Awake,
	AwaitingSetupCardChoice,
	ReadyToSleep,
	Asleep
}

internal sealed class ActorRole : RoleHookListener, IDeclaredRoleWorkflow
{
	private static readonly RolePowerDefinition SetupCardSelectionPower = new(
		new RolePowerIdentifier("actor-setup-card-selection"),
		RolePowerCategory.Chosen);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _workflowRuntime;

	internal ActorRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var identificationWait = RecoverableWait<
				ActorRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				ActorRoleState.Awake,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateIdentificationInstruction,
				static (_, instruction) =>
					instruction is SelectPlayersInstruction
					{
						Semantic:
							ModeratorInstructionSemantic.IdentifyRoleHolders,
						RoleIdentification: MainRoleType.Actor
					},
				ValidateIdentificationInstruction,
				static (_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => ActorRoleState.Awake);
		var wakeWait = RecoverableWait<
				ActorRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				ActorRoleState.Awake,
				ModeratorInstructionSemantic.WakeRole,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateWakeInstruction,
				ClaimsWake,
				ValidateWakeInstruction,
				static (_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => ActorRoleState.Awake);
		var setupCardChoiceWait = RecoverableWait<
				ActorRoleState,
				SelectOptionsInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				ActorRoleState.Awake,
				ActorRoleState.AwaitingSetupCardChoice,
				ModeratorInstructionSemantic.ChooseActorSetupCard,
				ExpectedInputType.OptionSelection,
				static _ => false,
				static (_, _) => { },
				CreateSetupCardChoiceInstruction,
				(session, instruction) =>
					instruction is SelectOptionsInstruction
					{
						Semantic:
							ModeratorInstructionSemantic.ChooseActorSetupCard
					} &&
					HasExpectedAffectedRoleHolders(session, instruction),
				ValidateSetupCardChoiceInstruction,
				ValidateIdentificationHandoff,
				static _ => ActorRoleState.AwaitingSetupCardChoice);
		var unspentSleepWait = RecoverableWait<
				ActorRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				ActorRoleState.Awake,
				ActorRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				ClaimsUnspentSleep,
				ValidateUnspentSleepInstruction,
				ValidateUnspentSleepHandoff,
				static _ => ActorRoleState.ReadyToSleep);
		var spentSleepWait = RecoverableWait<
				ActorRoleState,
				ConfirmationInstruction>
			.ActorSetupCardSpendDomainDurable(
				Id,
				GameHook.NightMainActionLoop,
				ActorRoleState.AwaitingSetupCardChoice,
				ActorRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				static (_, _) => false,
				ValidateSpentSleepInstruction,
				ValidateSetupCardSpendRecoveryCursor,
				static _ => ActorRoleState.ReadyToSleep,
				TryValidateSetupCardSpendRecoveryBoundary);

		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				identificationWait,
				wakeWait,
				setupCardChoiceWait,
				unspentSleepWait,
				spentSleepWait,
				new RoleWorkflowDecisionStep<ActorRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) => BeginCall(
						session,
						input,
						identificationWait,
						wakeWait)),
				new RoleWorkflowDecisionStep<ActorRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					ActorRoleState.Awake,
					static _ => true,
					(session, input) => PrepareNightPower(
						session,
						input,
						setupCardChoiceWait,
						unspentSleepWait)),
				new RoleWorkflowDecisionStep<ActorRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					ActorRoleState.AwaitingSetupCardChoice,
					static _ => true,
					(session, input) => CommitChoice(
						session,
						input,
						spentSleepWait,
						unspentSleepWait)),
				new RoleWorkflowCompletionStep<ActorRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					ActorRoleState.ReadyToSleep,
					ActorRoleState.Asleep,
					static _ => true)
			]);
	}

	internal override string PublicName => GameStrings.ActorRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Actor);

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_workflowRuntime;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (session.Execution.GetCurrentListenerState<ActorRoleState>(Id) ==
		    null)
		{
			session.TryExpireActorBorrowedRolePowerActivation();
			if (GameSessionQueries.IsVillagerRolePowerSuppressionActive(session))
			{
				return HookListenerActionResult.Skip();
			}

			var committedHolder = GetAliveRolePlayers(session)?.SingleOrDefault();
			if (session.GetModeratorRemainingActorSetupCards().Count > 0 &&
			    committedHolder != null &&
			    !IsSetupCardSelectionAvailable(session, committedHolder))
			{
				return HookListenerActionResult.Skip();
			}
		}

		return base.Execute(session, input);
	}

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		_workflowRuntime.Execute(
			session,
			input,
			session.Execution.GetCurrentListenerState<ActorRoleState>(Id));

	private HookListenerActionResult BeginCall(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<ActorRoleState, SelectPlayersInstruction>
			identificationWait,
		RecoverableWait<ActorRoleState, ConfirmationInstruction> wakeWait) =>
		IsCompleteHolderSetKnown(session)
			? wakeWait.Execute(session, input)
			: identificationWait.Execute(session, input);

	private HookListenerActionResult PrepareNightPower(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<ActorRoleState, SelectOptionsInstruction>
			setupCardChoiceWait,
		RecoverableWait<ActorRoleState, ConfirmationInstruction> sleepWait)
	{
		if (!IsCompleteHolderSetKnown(session))
		{
			IdentifyCompleteLivingRoleHolderSet(
				session,
				input.SelectedPlayerIds?.ToHashSet()
				?? throw new InvalidOperationException(
					"Actor identification requires a Player selection."));
		}

		return IsSetupCardChoiceOffered(session)
			? setupCardChoiceWait.Execute(session, input)
			: sleepWait.Execute(session, input);
	}

	private HookListenerActionResult CommitChoice(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<ActorRoleState, ConfirmationInstruction> spentSleepWait,
		RecoverableWait<ActorRoleState, ConfirmationInstruction>
			unspentSleepWait)
	{
		var selectedOptionIds = input.SelectedOptionIds
			?? throw new InvalidOperationException(
				"Actor setup-card choice requires an option selection response.");
		if (selectedOptionIds.Count > 1)
		{
			throw new InvalidOperationException(
				"Actor may select at most one setup card.");
		}

		if (selectedOptionIds.Count == 0)
		{
			return unspentSleepWait.Execute(session, input);
		}

		var selectedOptionId = selectedOptionIds.Single();
		if (!Guid.TryParseExact(selectedOptionId, "D", out var selectedCardId))
		{
			throw new InvalidOperationException(
				"Actor setup-card option identity is invalid.");
		}

		var holder = GetActor(session);
		if (!session.TrySpendActorSetupCard(holder.Id, selectedCardId, out _))
		{
			throw new InvalidOperationException(
				"Actor setup-card selection is no longer available.");
		}

		return spentSleepWait.Execute(session, input);
	}

	private SelectPlayersInstruction CreateIdentificationInstruction(
		GameSession session)
	{
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		var committedHolderCount =
			GetCommittedLivingRoleHolderIds(session).Count;
		var selectablePlayerIds = GetIdentificationCandidates(session);
		if (roleCount <= 0 ||
		    committedHolderCount > roleCount ||
		    selectablePlayerIds.Count < roleCount)
		{
			throw new InvalidOperationException(
				"Confirmed Role knowledge contradicts the required Living Role Holder count.");
		}

		var privateInstruction = roleCount == 1
			? GameStrings.RoleSingleIdentificationPrompt.Format(PublicName)
			: GameStrings.RoleMultipleIdentificationPrompt.Format(PublicName);
		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			selectablePlayerIds: selectablePlayerIds,
			countConstraint: NumberRangeConstraint.Exact(roleCount),
			publicAnnouncement: GameStrings.RoleWakesUp.Format(PublicName),
			privateInstruction: privateInstruction,
			affectedPlayerIds: null,
			roleIdentification: MainRoleType.Actor);
	}

	private ConfirmationInstruction CreateWakeInstruction(GameSession session) =>
		new(
			ModeratorInstructionSemantic.WakeRole,
			GameStrings.RoleWakesUp.Format(PublicName),
			affectedPlayerIds: [GetActor(session).Id]);

	private SelectOptionsInstruction CreateSetupCardChoiceInstruction(
		GameSession session) =>
		new(
			ModeratorInstructionSemantic.ChooseActorSetupCard,
			CreateSetupCardOptions(session),
			NumberRangeConstraint.SingleOptional,
			privateInstruction:
				GameStrings.ActorSetupCardSelectionInstruction,
			affectedPlayerIds: [GetActor(session).Id]);

	private ConfirmationInstruction CreateSleepInstruction(
		GameSession session) =>
		new(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleGoesToSleepSingle.Format(PublicName),
			affectedPlayerIds: [GetActor(session).Id]);

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		if (instruction.RoleIdentification != MainRoleType.Actor ||
		    instruction.AffectedPlayerIds != null ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)) ||
		    instruction.CountConstraint != NumberRangeConstraint.Exact(
			    GetExpectedLivingRoleHolderCount(session)))
		{
			throw new InvalidOperationException(
				"The Actor identification instruction has invalid workflow context.");
		}
	}

	private void ValidateWakeInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!HasExpectedWakeShape(session, instruction))
		{
			throw new InvalidOperationException(
				"The Actor wake instruction has invalid workflow context.");
		}
	}

	private void ValidateSetupCardChoiceInstruction(
		GameSession session,
		SelectOptionsInstruction instruction)
	{
		if (instruction.SelectionRange !=
			    NumberRangeConstraint.SingleOptional ||
		    instruction.PublicAnnouncement != null ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PrivateInstruction,
			    GameStrings.ActorSetupCardSelectionInstruction) ||
		    !instruction.Options.Select(option => option.Id).SequenceEqual(
			    session.GetModeratorRemainingActorSetupCards()
				    .Select(card => card.Id.ToString("D")),
			    StringComparer.Ordinal) ||
		    !HasExpectedAffectedRoleHolders(session, instruction))
		{
			throw new InvalidOperationException(
				"The Actor setup-card choice instruction has invalid workflow context.");
		}
	}

	private void ValidateUnspentSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!HasExpectedSleepShape(session, instruction) ||
		    session.GetModeratorActiveActorBorrowedRolePowerActivation() !=
			    null ||
		    HasCommittedSetupCardSpend(session))
		{
			throw new InvalidOperationException(
				"The pending Actor sleep instruction claims an unspent setup-card opening it cannot authenticate.");
		}
	}

	private void ValidateSpentSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!HasExpectedSleepShape(session, instruction) ||
		    !TryResolveCommittedSetupCardSpend(session, out _))
		{
			throw new InvalidOperationException(
				"The pending Actor sleep instruction claims a setup-card spend it cannot authenticate.");
		}
	}

	private void ValidateIdentificationHandoff(
		GameSession session,
		SelectOptionsInstruction instruction,
		AcceptedObservationRecoveryCursor cursor)
	{
		ValidateActorCursor(
			cursor,
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		RequireCommittedIdentification(session);
	}

	/// <summary>
	/// The unspent sleep continuation is reached either straight from the
	/// Role Identification handoff, when no setup card can be offered, or
	/// from a declined setup-card choice.
	/// </summary>
	private void ValidateUnspentSleepHandoff(
		GameSession session,
		ConfirmationInstruction instruction,
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.AcceptedObservationSemantic ==
		    ModeratorInstructionSemantic.IdentifyRoleHolders)
		{
			ValidateActorCursor(
				cursor,
				ModeratorInstructionSemantic.IdentifyRoleHolders);
			RequireCommittedIdentification(session);
			return;
		}

		ValidateActorCursor(
			cursor,
			ModeratorInstructionSemantic.ChooseActorSetupCard);
	}

	private void ValidateSetupCardSpendRecoveryCursor(
		GameSession session,
		ConfirmationInstruction instruction,
		DomainRecoveryCursor cursor)
	{
		ArgumentNullException.ThrowIfNull(cursor);
		if (!TryResolveCommittedSetupCardSpend(session, out var activation))
		{
			throw new InvalidOperationException(
				"The Actor setup-card spend cursor has no committed spend.");
		}

		if (cursor.Version != DomainRecoveryCursor.CurrentVersion ||
		    cursor.Kind !=
			    DomainRecoveryCursorKind.ActorSetupCardSpendCommit ||
		    cursor.CommittedActionType != NightActionType.Unknown ||
		    cursor.ActingPlayerId != activation!.ActingPlayerId ||
		    cursor.SourceRole != activation.SourceRole ||
		    cursor.ActorSetupCardId != activation.SelectedCardId ||
		    cursor.ActorBorrowedActivationId != activation.ActivationId ||
		    cursor.CommittedTargetIds.Count != 0 ||
		    cursor.PowerInstanceId != Guid.Empty ||
		    cursor.OneUseResourceId != Guid.Empty ||
		    cursor.PowerInstanceOrigin != null ||
		    !string.IsNullOrEmpty(cursor.SourcePowerIdentifier))
		{
			throw new InvalidOperationException(
				"The Actor setup-card spend cursor has invalid workflow context.");
		}
	}

	private bool TryValidateSetupCardSpendRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		ActorSetupCardSpendCommittedLogEntry committedBoundary,
		ConfirmationInstruction nextInstruction)
	{
		if (startingInstruction is not SelectOptionsInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.ChooseActorSetupCard
		    } selection)
		{
			return false;
		}

		if (selection.SelectionRange !=
			    NumberRangeConstraint.SingleOptional ||
		    selection.AffectedPlayerIds is not [var actorId] ||
		    input.InstructionId != selection.InstructionId ||
		    input.Type != ExpectedInputType.OptionSelection ||
		    input.SelectedOptionIds is not [var selectedOptionId] ||
		    !Guid.TryParseExact(selectedOptionId, "D", out var selectedCardId) ||
		    selection.Options.Count(option =>
			    StringComparer.Ordinal.Equals(option.Id, selectedOptionId)) !=
			    1 ||
		    nextInstruction.AffectedPlayerIds is not [var sleepingActorId] ||
		    sleepingActorId != actorId)
		{
			throw new InvalidOperationException(
				"The Actor setup-card spend must correlate to its exact accepted option and sleep continuation.");
		}

		if (committedBoundary.CurrentPhase != GamePhase.Night ||
		    committedBoundary.TurnNumber != session.TurnNumber ||
		    !TryResolveCommittedSetupCardSpend(session, out var activation) ||
		    activation!.ActingPlayerId != actorId ||
		    activation.SelectedCardId != selectedCardId)
		{
			throw new InvalidOperationException(
				"The Actor setup-card spend does not match its living holder, selected card, and active borrowed lineage.");
		}

		return true;
	}

	private static void ValidateCallHandoff(
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.Actor)
		{
			throw new InvalidOperationException(
				"The Actor call has invalid accepted-observation handoff context.");
		}
	}

	private static void ValidateActorCursor(
		AcceptedObservationRecoveryCursor cursor,
		ModeratorInstructionSemantic acceptedSemantic)
	{
		if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.AcceptedObservationSemantic != acceptedSemantic ||
		    cursor.ObservedRole != MainRoleType.Actor ||
		    cursor.ContinuationRole != MainRoleType.Actor ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The Actor continuation cursor has invalid workflow context.");
		}
	}

	private void RequireCommittedIdentification(GameSession session)
	{
		var livingHolderIds = GetLivingHolderIds(session);
		if (livingHolderIds.Count == 0 ||
		    !RoleFactionKnowledge.HasAcceptedRoleIdentification(
			    session,
			    MainRoleType.Actor))
		{
			throw new InvalidOperationException(
				"The Actor continuation has no committed identification.");
		}
	}

	/// <summary>
	/// Authenticates the committed Actor setup-card spend from the Moderator
	/// projection. The public spend marker is deliberately non-identifying, so
	/// the exact card, source Role, and borrowed lineage are re-derived here.
	/// </summary>
	private static bool TryResolveCommittedSetupCardSpend(
		GameSession session,
		out ActorBorrowedRolePowerActivation? activation)
	{
		activation = null;
		if (!HasCommittedSetupCardSpend(session) ||
		    session.GetModeratorActiveActorBorrowedRolePowerActivation() is not
			    { } active ||
		    active.ActingRole != MainRoleType.Actor)
		{
			return false;
		}

		var selectedCard = session.GetModeratorActorSetupCards().Cards
			.SingleOrDefault(card => card.Id == active.SelectedCardId);
		if (selectedCard is null ||
		    active.SourceRole != selectedCard.PrintedRole ||
		    session.GetPlayerState(active.ActingPlayerId) is not
		    {
			    Health: PlayerHealth.Alive,
			    CurrentRole: MainRoleType.Actor
		    } ||
		    session.GetModeratorSpentActorSetupCards().Count(card =>
			    card.Id == active.SelectedCardId) != 1)
		{
			return false;
		}

		activation = active;
		return true;
	}

	private static bool HasCommittedSetupCardSpend(GameSession session) =>
		session.GameHistoryLog
			.OfType<ActorSetupCardSpendCommittedLogEntry>()
			.Any(entry =>
				entry.TurnNumber == session.TurnNumber &&
				entry.CurrentPhase == GamePhase.Night);

	/// <summary>
	/// A Borrowed Role Power wakes under the Actor's own public name and
	/// audience, so only the absence of an active Borrowed Role Power
	/// activation — which the Actor expires before its own call — separates
	/// the Actor's wake from a borrowed source's wake.
	/// </summary>
	private bool ClaimsWake(
		GameSession session,
		ModeratorInstruction instruction) =>
		instruction is ConfirmationInstruction
		{
			Semantic: ModeratorInstructionSemantic.WakeRole
		} &&
		HasExpectedWakeShape(session, instruction);

	private bool HasExpectedWakeShape(
		GameSession session,
		ModeratorInstruction instruction) =>
		StringComparer.Ordinal.Equals(
			instruction.PublicAnnouncement,
			GameStrings.RoleWakesUp.Format(PublicName)) &&
		instruction.PrivateInstruction == null &&
		session.GetModeratorActiveActorBorrowedRolePowerActivation() == null &&
		HasExpectedAffectedRoleHolders(session, instruction);

	/// <summary>
	/// The Actor sleeps under its own audience whenever it neither holds nor
	/// has just committed a Borrowed Role Power activation, so a Borrowed Role
	/// Power's own sleep never reaches this wait.
	/// </summary>
	private bool ClaimsUnspentSleep(
		GameSession session,
		ModeratorInstruction instruction) =>
		instruction is ConfirmationInstruction confirmation &&
		HasExpectedSleepShape(session, confirmation) &&
		session.GetModeratorActiveActorBorrowedRolePowerActivation() == null &&
		!HasCommittedSetupCardSpend(session);

	private bool HasExpectedSleepShape(
		GameSession session,
		ConfirmationInstruction instruction) =>
		instruction.Semantic ==
			ModeratorInstructionSemantic.PutRoleToSleep &&
		HasExpectedAffectedRoleHolders(session, instruction);

	private bool IsCompleteHolderSetKnown(GameSession session) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			MainRoleType.Actor);

	private bool IsSetupCardChoiceOffered(GameSession session) =>
		session.GetModeratorRemainingActorSetupCards().Count > 0 &&
		IsSetupCardSelectionAvailable(session, GetActor(session));

	private static ModeratorOption[] CreateSetupCardOptions(
		GameSession session) =>
		session.GetModeratorRemainingActorSetupCards()
			.Select(card => new ModeratorOption(
				card.Id.ToString("D"),
				card.PrintedRole.GetPublicName()))
			.ToArray();

	private IPlayer GetActor(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException("No living Actor found.");

	private HashSet<Guid> GetLivingHolderIds(GameSession session) =>
		GetAliveRolePlayers(session)?.Select(player => player.Id).ToHashSet()
		?? [];

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.Actor ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == MainRoleType.Actor ||
				  player.State.ModeratorKnownRole == null &&
				  RoleFactionKnowledge.GetPossibleRoles(session, player.Id)
					  .Contains(MainRoleType.Actor))))
			.ToIdSet();

	private bool HasExpectedAffectedRoleHolders(
		GameSession session,
		ModeratorInstruction instruction)
	{
		var holders = GetLivingHolderIds(session);
		return holders.Count > 0 &&
		       instruction.AffectedPlayerIds is { } affectedPlayerIds &&
		       affectedPlayerIds.ToHashSet().SetEquals(holders);
	}

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
