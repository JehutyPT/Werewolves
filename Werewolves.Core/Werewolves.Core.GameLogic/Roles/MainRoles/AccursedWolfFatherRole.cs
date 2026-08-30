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

internal enum AccursedWolfFatherRoleState
{
	Awake,
	AwaitingInfectionChoice,
	ReadyToSleep,
	Asleep
}

internal sealed class AccursedWolfFatherRole
	: RoleHookListener,
		IDeclaredRoleWorkflow
{
	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _workflowRuntime;

	private static readonly RolePowerDefinition InfectionPower = new(
		new RolePowerIdentifier("accursed-wolf-father-infection"),
		RolePowerCategory.Chosen);

	internal static RolePowerIdentifier InfectionPowerIdentifier =>
		InfectionPower.Identifier;

	internal static readonly Guid InfectionResourceId =
		Guid.Parse("a3d2e55e-0b97-4f4c-a38c-709c03ff1026");

	internal AccursedWolfFatherRole(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var identificationWait = RecoverableWait<
				AccursedWolfFatherRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				AccursedWolfFatherRoleState.Awake,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateIdentificationInstruction,
				static (_, instruction) =>
					instruction is SelectPlayersInstruction
					{
						RoleIdentification: MainRoleType.AccursedWolfFather
					},
				ValidateIdentificationInstruction,
				(_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => AccursedWolfFatherRoleState.Awake);
		var wakeWait = RecoverableWait<
				AccursedWolfFatherRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				AccursedWolfFatherRoleState.Awake,
				ModeratorInstructionSemantic.WakeRole,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateWakeInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.WakeRole &&
					HasExpectedAffectedRoleHolders(session, instruction),
				ValidateWakeInstruction,
				(_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => AccursedWolfFatherRoleState.Awake);
		var infectionChoiceWait = RecoverableWait<
				AccursedWolfFatherRoleState,
				SelectOptionsInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				AccursedWolfFatherRoleState.Awake,
				AccursedWolfFatherRoleState.AwaitingInfectionChoice,
				ModeratorInstructionSemantic
					.ChooseAccursedWolfFatherInfection,
				ExpectedInputType.OptionSelection,
				static _ => false,
				static (_, _) => { },
				CreateInfectionChoiceInstruction,
				(session, instruction) =>
					instruction is SelectOptionsInstruction
					{
						Semantic:
							ModeratorInstructionSemantic
								.ChooseAccursedWolfFatherInfection
					} &&
					HasExpectedAffectedRoleHolders(session, instruction),
				ValidateInfectionChoiceInstruction,
				(session, _, cursor) =>
					ValidateIdentificationHandoff(session, cursor),
				static _ => AccursedWolfFatherRoleState
					.AwaitingInfectionChoice);
		var replayableSleepWait = RecoverableWait<
				AccursedWolfFatherRoleState,
				ConfirmationInstruction>
			.Replayable(
				Id,
				GameHook.NightMainActionLoop,
				AccursedWolfFatherRoleState.Awake,
				AccursedWolfFatherRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				static (_, _) => false,
				ValidateReplayableSleepInstruction);
		var committedSleepWait = RecoverableWait<
				AccursedWolfFatherRoleState,
				ConfirmationInstruction>
			.OneUseDomainDurable(
				Id,
				GameHook.NightMainActionLoop,
				AccursedWolfFatherRoleState.AwaitingInfectionChoice,
				AccursedWolfFatherRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateSleepInstruction,
				(session, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic.PutRoleToSleep &&
					CountInfectionCommitsThisNight(session, 2) == 1 &&
					HasExpectedAffectedRoleHolders(session, instruction),
				ValidateCommittedSleepInstruction,
				ValidateOneUseRecoveryCursor,
				static _ => AccursedWolfFatherRoleState.ReadyToSleep,
				TryValidateCommittedRecoveryBoundary);

		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				identificationWait,
				wakeWait,
				infectionChoiceWait,
				replayableSleepWait,
				committedSleepWait,
				new RoleWorkflowDecisionStep<AccursedWolfFatherRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) => BeginCall(
						session,
						input,
						identificationWait,
						wakeWait)),
				new RoleWorkflowDecisionStep<AccursedWolfFatherRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					AccursedWolfFatherRoleState.Awake,
					static _ => true,
					(session, input) => PrepareNightPower(
						session,
						input,
						infectionChoiceWait,
						replayableSleepWait)),
				new RoleWorkflowDecisionStep<AccursedWolfFatherRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					AccursedWolfFatherRoleState.AwaitingInfectionChoice,
					static _ => true,
					(session, input) => CommitInfectionChoice(
						session,
						input,
						committedSleepWait,
						replayableSleepWait)),
				new RoleWorkflowCompletionStep<AccursedWolfFatherRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					AccursedWolfFatherRoleState.ReadyToSleep,
					AccursedWolfFatherRoleState.Asleep,
					static _ => true),
				new RoleWorkflowCompletionStep<AccursedWolfFatherRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					AccursedWolfFatherRoleState.Asleep,
					AccursedWolfFatherRoleState.Asleep,
					static _ => true)
			]);
	}

	internal override string PublicName =>
		GameStrings.AccursedWolfFatherRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.AccursedWolfFather);

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_workflowRuntime;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (session.Execution.GetCurrentListenerState<
			    AccursedWolfFatherRoleState>(Id) == null)
		{
			var retainedVictimId = GetRetainedVictimId(session);
			if (retainedVictimId == null)
			{
				return HookListenerActionResult.Skip();
			}

			var holder = GetAliveRolePlayers(session)?.SingleOrDefault();
			if (holder != null &&
			    IsSpent(session, CreateResourceIdentity(session, holder)))
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
			session.Execution
				.GetCurrentListenerState<AccursedWolfFatherRoleState>(Id));

	private static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		OneUseRolePowerCommittedLogEntry committedEntry,
		ConfirmationInstruction nextInstruction)
	{
		if (committedEntry.ActionType !=
		    NightActionType.AccursedWolfFatherInfection)
		{
			return false;
		}

		ValidateOwnedInfectionCommit(session, committedEntry);
		if (startingInstruction is not SelectOptionsInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic
					    .ChooseAccursedWolfFatherInfection,
			    SelectionRange: var selectionRange,
			    Options: var options
		    } ||
		    selectionRange != NumberRangeConstraint.Single ||
		    !options.Select(option => option.Id).SequenceEqual(
			    [
				    AccursedWolfFatherInfectionOptionIds.Infect,
				    AccursedWolfFatherInfectionOptionIds.Decline
			    ],
			    StringComparer.Ordinal) ||
		    input.SelectedOptionIds is not
			    { Count: 1 } selectedOptionIds ||
		    !StringComparer.Ordinal.Equals(
			    selectedOptionIds.Single(),
			    AccursedWolfFatherInfectionOptionIds.Infect) ||
		    nextInstruction.AffectedPlayerIds is not
			    { Count: 1 } sleepAffectedPlayerIds ||
		    sleepAffectedPlayerIds.Single() != committedEntry.ActingPlayerId)
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father commit must correlate to its accepted infection option.");
		}

		ValidateRetainedVictim(session, committedEntry);
		return true;
	}

	private void ValidateOneUseRecoveryCursor(
		GameSession session,
		ConfirmationInstruction instruction,
		DomainRecoveryCursor cursor)
	{
		ArgumentNullException.ThrowIfNull(cursor);
		var holder = GetHolder(session);
		if (cursor.Kind != DomainRecoveryCursorKind.OneUseRolePowerCommit ||
		    cursor.SourceRole != MainRoleType.AccursedWolfFather ||
		    cursor.CommittedActionType !=
		    NightActionType.AccursedWolfFatherInfection ||
		    cursor.ActorSetupCardId != Guid.Empty ||
		    cursor.ActorBorrowedActivationId != Guid.Empty ||
		    cursor.ResourceIdentity is not { } resourceIdentity ||
		    resourceIdentity != CreateResourceIdentity(session, holder))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father recovery cursor has an invalid One-Use Role Power identity.");
		}

		var commits = GetInfectionCommitsThisNight(session)
			.Where(commit =>
				commit.ResourceIdentity == resourceIdentity &&
				commit.TargetIds is { Count: 1 } targetIds &&
				cursor.CommittedTargetIds.SequenceEqual(targetIds))
			.ToArray();
		if (commits is not [var committedInfection])
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father recovery cursor does not match one committed infection action.");
		}

		ValidateOwnedInfectionCommit(session, committedInfection);
		ValidateRetainedVictim(session, committedInfection);
	}

	private HookListenerActionResult BeginCall(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<AccursedWolfFatherRoleState, SelectPlayersInstruction>
			identificationWait,
		RecoverableWait<AccursedWolfFatherRoleState, ConfirmationInstruction>
			wakeWait) =>
		IsCompleteHolderSetKnown(session)
			? wakeWait.Execute(session, input)
			: identificationWait.Execute(session, input);

	private HookListenerActionResult PrepareNightPower(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<AccursedWolfFatherRoleState, SelectOptionsInstruction>
			infectionChoiceWait,
		RecoverableWait<AccursedWolfFatherRoleState, ConfirmationInstruction>
			sleepWait)
	{
		if (!IsCompleteHolderSetKnown(session))
		{
			IdentifyCompleteLivingRoleHolderSet(
				session,
				input.SelectedPlayerIds?.ToHashSet()
				?? throw new InvalidOperationException(
					"Accursed Wolf-Father identification requires a Player selection."));
		}

		var holder = GetHolder(session);
		_ = GetRetainedVictimId(session)
			?? throw new InvalidOperationException(
				"The Accursed Wolf-Father infection requires one retained collective victim.");
		var resourceIdentity = CreateResourceIdentity(session, holder);
		if (IsSpent(session, resourceIdentity))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father infection resource is already spent.");
		}

		var instance = CreatePowerInstance(session, holder);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				holder,
				MainRoleType.AccursedWolfFather,
				InfectionPower,
				instance,
				new OneUseRolePowerResource(InfectionResourceId, instance)));
		return availability.AvailabilityResult.IsAvailable
			? infectionChoiceWait.Execute(session, input)
			: sleepWait.Execute(session, input);
	}

	private HookListenerActionResult CommitInfectionChoice(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<AccursedWolfFatherRoleState, ConfirmationInstruction>
			committedSleepWait,
		RecoverableWait<AccursedWolfFatherRoleState, ConfirmationInstruction>
			declinedSleepWait)
	{
		var holder = GetHolder(session);
		var victimId = GetRetainedVictimId(session)
			?? throw new InvalidOperationException(
				"The Accursed Wolf-Father infection requires one retained collective victim.");
		var resourceIdentity = CreateResourceIdentity(session, holder);
		if (IsSpent(session, resourceIdentity))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father infection resource is already spent.");
		}

		var selectedOptionId = input.SelectedOptionIds?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"The Accursed Wolf-Father infection requires one semantic option.");
		switch (selectedOptionId)
		{
			case AccursedWolfFatherInfectionOptionIds.Infect:
				if (HasInfectionIntentThisNight(session))
				{
					throw new InvalidOperationException(
						"Only one Accursed Wolf-Father infection may be committed per Night.");
				}

				session.CommitOneUseRolePowerNightAction(
					NightActionType.AccursedWolfFatherInfection,
					victimId,
					resourceIdentity);
				return committedSleepWait.Execute(session, input);
			case AccursedWolfFatherInfectionOptionIds.Decline:
				return declinedSleepWait.Execute(session, input);
			default:
				throw new InvalidOperationException(
					"The Accursed Wolf-Father infection option is unknown.");
		}
	}

	private SelectPlayersInstruction CreateIdentificationInstruction(
		GameSession session)
	{
		var selectablePlayerIds = GetIdentificationCandidates(session);
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		var committedLivingRoleHolderCount =
			GetCommittedLivingRoleHolderIds(session).Count;
		if (roleCount <= 0 ||
		    committedLivingRoleHolderCount > roleCount ||
		    selectablePlayerIds.Count < roleCount)
		{
			throw new InvalidOperationException(
				"Confirmed Role knowledge contradicts the required Living Role Holder count.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			selectablePlayerIds: selectablePlayerIds,
			countConstraint: NumberRangeConstraint.Exact(roleCount),
			publicAnnouncement: GameStrings.RoleWakesUp.Format(PublicName),
			privateInstruction: roleCount == 1
				? GameStrings.RoleSingleIdentificationPrompt.Format(PublicName)
				: GameStrings.RoleMultipleIdentificationPrompt.Format(
					PublicName),
			affectedPlayerIds: null,
			roleIdentification: MainRoleType.AccursedWolfFather);
	}

	private ConfirmationInstruction CreateWakeInstruction(GameSession session)
	{
		var holder = GetHolder(session);
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.WakeRole,
			GameStrings.RoleWakesUp.Format(PublicName),
			affectedPlayerIds: [holder.Id]);
	}

	private SelectOptionsInstruction CreateInfectionChoiceInstruction(
		GameSession session)
	{
		var holder = GetHolder(session);
		var victimId = GetRetainedVictimId(session)
			?? throw new InvalidOperationException(
				"The Accursed Wolf-Father infection requires one retained collective victim.");
		return new SelectOptionsInstruction(
			ModeratorInstructionSemantic.ChooseAccursedWolfFatherInfection,
			[
				new ModeratorOption(
					AccursedWolfFatherInfectionOptionIds.Infect,
					GameStrings.AccursedWolfFatherInfectOption),
				new ModeratorOption(
					AccursedWolfFatherInfectionOptionIds.Decline,
					GameStrings.DeclineOption)
			],
			NumberRangeConstraint.Single,
			privateInstruction:
				GameStrings.AccursedWolfFatherInfectionInstruction.Format(
					session.GetPlayer(victimId).Name),
			affectedPlayerIds: [holder.Id]);
	}

	private ConfirmationInstruction CreateSleepInstruction(GameSession session)
	{
		var holder = GetHolder(session);
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleGoesToSleepSingle.Format(PublicName),
			affectedPlayerIds: [holder.Id]);
	}

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		if (instruction.RoleIdentification !=
			    MainRoleType.AccursedWolfFather ||
		    instruction.AffectedPlayerIds != null ||
		    roleCount <= 0 ||
		    instruction.CountConstraint !=
			    NumberRangeConstraint.Exact(roleCount) ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father identification instruction has invalid workflow context.");
		}
	}

	private void ValidateWakeInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (instruction.PublicAnnouncement !=
			    GameStrings.RoleWakesUp.Format(PublicName) ||
		    instruction.PrivateInstruction != null ||
		    !HasExpectedAffectedRoleHolders(session, instruction))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father wake instruction has invalid workflow context.");
		}
	}

	private void ValidateInfectionChoiceInstruction(
		GameSession session,
		SelectOptionsInstruction instruction)
	{
		var victimId = GetRetainedVictimId(session);
		if (victimId == null ||
		    instruction.PublicAnnouncement != null ||
		    instruction.PrivateInstruction !=
			    GameStrings.AccursedWolfFatherInfectionInstruction.Format(
				    session.GetPlayer(victimId.Value).Name) ||
		    instruction.SelectionRange != NumberRangeConstraint.Single ||
		    !instruction.Options.Select(option => option.Id).SequenceEqual(
			    [
				    AccursedWolfFatherInfectionOptionIds.Infect,
				    AccursedWolfFatherInfectionOptionIds.Decline
			    ],
			    StringComparer.Ordinal) ||
		    !HasExpectedAffectedRoleHolders(session, instruction))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father infection choice has invalid workflow context.");
		}
	}

	private void ValidateReplayableSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		ValidateSleepInstructionShape(session, instruction);
		if (CountInfectionCommitsThisNight(session, 1) != 0)
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father replayable sleep has invalid workflow context.");
		}
	}

	private void ValidateCommittedSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		ValidateSleepInstructionShape(session, instruction);
		var commits = GetInfectionCommitsThisNight(session).ToArray();
		if (commits.Length > 1)
		{
			throw new InvalidOperationException(
				"The pending Accursed Wolf-Father sleep instruction has multiple infection commits.");
		}

		if (commits is not [var committedInfection])
		{
			throw new InvalidOperationException(
				"The pending Accursed Wolf-Father sleep instruction requires its committed infection.");
		}

		ValidateOwnedInfectionCommit(session, committedInfection);
		if (instruction.AffectedPlayerIds is not { Count: 1 } affectedPlayerIds ||
		    affectedPlayerIds.Single() != committedInfection.ActingPlayerId)
		{
			throw new InvalidOperationException(
				"The pending Accursed Wolf-Father sleep instruction does not belong to the living Role holder.");
		}

		ValidateRetainedVictim(session, committedInfection);
	}

	private void ValidateSleepInstructionShape(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (instruction.PublicAnnouncement !=
			    GameStrings.RoleGoesToSleepSingle.Format(PublicName) ||
		    instruction.PrivateInstruction != null ||
		    !HasExpectedAffectedRoleHolders(session, instruction))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father sleep instruction has invalid workflow context.");
		}
	}

	private static void ValidateCallHandoff(
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.AccursedWolfFather)
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father call has invalid accepted-observation handoff context.");
		}
	}

	private void ValidateIdentificationHandoff(
		GameSession session,
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.AccursedWolfFather ||
		    cursor.ObservedRole != MainRoleType.AccursedWolfFather ||
		    cursor.AcceptedObservationSemantic !=
			    ModeratorInstructionSemantic.IdentifyRoleHolders ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father continuation has invalid accepted-observation handoff context.");
		}

		var livingHolderIds = GetLivingHolderIds(session);
		if (livingHolderIds.Count == 0 ||
		    !session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			    .Any(entry =>
				    entry.TurnNumber == session.TurnNumber &&
				    entry.CurrentPhase == GamePhase.Night &&
				    entry.Role == MainRoleType.AccursedWolfFather &&
				    entry.PlayerIds.SetEquals(livingHolderIds)))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father identification continuation has invalid durable context.");
		}
	}

	private bool IsCompleteHolderSetKnown(GameSession session) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			MainRoleType.AccursedWolfFather);

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole ==
					MainRoleType.AccursedWolfFather ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole ==
					  MainRoleType.AccursedWolfFather ||
				  player.State.ModeratorKnownRole == null &&
				  RoleFactionKnowledge.GetPossibleRoles(session, player.Id)
					  .Contains(MainRoleType.AccursedWolfFather))))
			.ToIdSet();

	private HashSet<Guid> GetLivingHolderIds(GameSession session) =>
		GetAliveRolePlayers(session)?.Select(player => player.Id).ToHashSet()
		?? [];

	private bool HasExpectedAffectedRoleHolders(
		GameSession session,
		ModeratorInstruction instruction)
	{
		var livingHolderIds = GetLivingHolderIds(session);
		return livingHolderIds.Count > 0 &&
		       instruction.AffectedPlayerIds is { } affectedPlayerIds &&
		       livingHolderIds.SetEquals(affectedPlayerIds);
	}

	private int CountInfectionCommitsThisNight(
		GameSession session,
		int limit) =>
		GetInfectionCommitsThisNight(session).Take(limit).Count();

	private IPlayer GetHolder(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException(
			"No living Accursed Wolf-Father is available.");

	private static Guid? GetRetainedVictimId(GameSession session)
	{
		return GameSessionQueries.TryGetRetainedWerewolfVictimThisNight(
			session,
			out var victimId)
				? victimId
				: null;
	}

	private static IEnumerable<OneUseRolePowerCommittedLogEntry>
		GetInfectionCommitsThisNight(GameSession session) =>
		GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				[NightActionType.AccursedWolfFatherInfection])
			.OfType<OneUseRolePowerCommittedLogEntry>();

	private static bool HasInfectionIntentThisNight(GameSession session) =>
		GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				[NightActionType.AccursedWolfFatherInfection])
			.Any();

	private static bool IsSpent(
		GameSession session,
		OneUseRolePowerResourceIdentity resourceIdentity) =>
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			session,
			resourceIdentity);

	private static RolePowerInstance CreatePowerInstance(
		GameSession session,
		IPlayer holder) =>
		RolePowerInstance.CreateCurrent(
			session,
			holder,
			MainRoleType.AccursedWolfFather,
			InfectionPower);

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		GameSession session,
		IPlayer holder)
	{
		var instance = CreatePowerInstance(session, holder);
		return new OneUseRolePowerResourceIdentity(
			holder.Id,
			MainRoleType.AccursedWolfFather,
			InfectionPowerIdentifier.Value,
			instance.Id,
			instance.Origin,
			InfectionResourceId);
	}

	private static void ValidateOwnedInfectionCommit(
		GameSession session,
		OneUseRolePowerCommittedLogEntry committedEntry)
	{
		var identity = committedEntry.ResourceIdentity;
		if (identity != CreateResourceIdentity(
				session,
				session.GetPlayer(identity.ActingPlayerId)))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father infection commit has an invalid Role Power identity.");
		}
	}

	private static void ValidateRetainedVictim(
		GameSession session,
		OneUseRolePowerCommittedLogEntry committedEntry)
	{
		if (committedEntry.TargetIds is not [var committedTargetId] ||
		    !GameSessionQueries.TryGetRetainedWerewolfVictimThisNight(
			    session,
			    out var retainedVictimId) ||
		    retainedVictimId != committedTargetId)
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father commit must target the one retained collective victim.");
		}
	}
}
