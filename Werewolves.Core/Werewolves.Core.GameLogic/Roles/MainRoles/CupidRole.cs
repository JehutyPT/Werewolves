using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum CupidRoleState
{
	AwaitingIdentification,
	Awake,
	AwaitingTargetSelection,
	AwaitingLoversRecognition,
	ReadyToSleep,
	Asleep
}

internal sealed class CupidRole
	: RoleHookListener,
	  IDeclaredRoleWorkflow,
	  IEliminationCascadeReaction
{
	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		ActorBorrowedRolePowers.ActorBorrowedRolePowerUse? BorrowedUse)
	{
		internal bool IsBorrowed => BorrowedUse is not null;
	}

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _workflowRuntime;

	private static readonly RolePowerDefinition LinkLoversPower = new(
		new RolePowerIdentifier("cupid-link-lovers"),
		RolePowerCategory.Chosen);
	private static readonly ActorBorrowedRolePowerSpec BorrowedPowerSpec = new(
		MainRoleType.Cupid,
		LinkLoversPower);

	internal static RolePowerIdentifier LinkLoversPowerIdentifier =>
		LinkLoversPower.Identifier;

	internal CupidRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var identificationWait = RecoverableWait<
				CupidRoleState,
				SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				CupidRoleState.AwaitingIdentification,
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
						RoleIdentification: MainRoleType.Cupid
					},
				ValidateIdentificationInstruction,
				static (_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => CupidRoleState.AwaitingIdentification);
		var wakeWait = RecoverableWait<
				CupidRoleState,
				ConfirmationInstruction>
			.ReplayableWithAcceptedObservationHandoff(
				Id,
				GameHook.NightMainActionLoop,
				startState: null,
				CupidRoleState.Awake,
				ModeratorInstructionSemantic.WakeRole,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateWakeInstruction,
				ClaimsWake,
				ValidateWakeInstruction,
				static (_, _, cursor) => ValidateCallHandoff(cursor),
				static _ => CupidRoleState.Awake);
		var targetSelectionWait = RecoverableWait<
				CupidRoleState,
				SelectPlayersInstruction>
			.Replayable(
				Id,
				GameHook.NightMainActionLoop,
				CupidRoleState.Awake,
				CupidRoleState.AwaitingTargetSelection,
				ModeratorInstructionSemantic.SelectCupidLovers,
				ExpectedInputType.PlayerSelection,
				static _ => false,
				static (_, _) => { },
				CreateTargetSelectionInstruction,
				ClaimsTargetSelection,
				ValidateTargetSelectionInstruction);
		var replayableSleepWait = RecoverableWait<
				CupidRoleState,
				ConfirmationInstruction>
			.Replayable(
				Id,
				GameHook.NightMainActionLoop,
				CupidRoleState.Awake,
				CupidRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateHolderSleepInstruction,
				ClaimsUncommittedSleep,
				ValidateReplayableSleepInstruction);
		var nativeRecognitionWait = RecoverableWait<
				CupidRoleState,
				ConfirmationInstruction>
			.LoversPairDomainDurable(
				Id,
				GameHook.NightMainActionLoop,
				CupidRoleState.AwaitingTargetSelection,
				CupidRoleState.AwaitingLoversRecognition,
				ModeratorInstructionSemantic.RecognizeLovers,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateNativeRecognitionInstruction,
				static (session, instruction) =>
					instruction.Semantic ==
						ModeratorInstructionSemantic.RecognizeLovers &&
					!TryResolveBorrowedExecution(session, out _),
				ValidateNativeRecognitionInstruction,
				static (session, _, cursor) =>
					ValidateNativeRecoveryCursor(session, cursor),
				static _ => CupidRoleState.AwaitingLoversRecognition,
				TryValidateNativeCommittedRecoveryBoundary);
		var borrowedRecognitionWait = RecoverableWait<
				CupidRoleState,
				ConfirmationInstruction>
			.ActorBorrowedLoversDomainDurable(
				Id,
				GameHook.NightMainActionLoop,
				CupidRoleState.AwaitingTargetSelection,
				CupidRoleState.AwaitingLoversRecognition,
				ModeratorInstructionSemantic.RecognizeLovers,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateBorrowedRecognitionInstruction,
				static (session, instruction) =>
					instruction.Semantic ==
						ModeratorInstructionSemantic.RecognizeLovers &&
					TryResolveBorrowedExecution(session, out _),
				ValidateBorrowedRecognitionInstruction,
				static (session, _, cursor) =>
					ValidateBorrowedRecoveryCursor(session, cursor),
				static _ => CupidRoleState.AwaitingLoversRecognition,
				TryValidateBorrowedCommittedRecoveryBoundary);
		var committedSleepWait = RecoverableWait<
				CupidRoleState,
				ConfirmationInstruction>
			.Durable(
				Id,
				GameHook.NightMainActionLoop,
				CupidRoleState.AwaitingLoversRecognition,
				CupidRoleState.ReadyToSleep,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateCommittedSleepInstruction,
				static (_, _) => false,
				ValidateCommittedSleepInstruction,
				static (_, _, cursor) => ValidateRecognitionHandoff(cursor),
				static _ => CupidRoleState.ReadyToSleep);

		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				identificationWait,
				wakeWait,
				targetSelectionWait,
				replayableSleepWait,
				nativeRecognitionWait,
				borrowedRecognitionWait,
				committedSleepWait,
				new RoleWorkflowDecisionStep<CupidRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) => BeginCall(
						session,
						input,
						identificationWait,
						wakeWait)),
				new RoleWorkflowDecisionStep<CupidRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					CupidRoleState.AwaitingIdentification,
					static _ => true,
					(session, input) => CommitIdentificationAndWake(
						session,
						input,
						wakeWait)),
				new RoleWorkflowDecisionStep<CupidRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					CupidRoleState.Awake,
					static _ => true,
					(session, input) => HandleNightPowerUse(
						session,
						input,
						targetSelectionWait,
						replayableSleepWait)),
				new RoleWorkflowDecisionStep<CupidRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					CupidRoleState.AwaitingTargetSelection,
					static _ => true,
					(session, input) => CommitTargetSelection(
						session,
						input,
						nativeRecognitionWait,
						borrowedRecognitionWait)),
				new RoleWorkflowDecisionStep<CupidRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					CupidRoleState.AwaitingLoversRecognition,
					static _ => true,
					(session, input) =>
						committedSleepWait.Execute(session, input)),
				new RoleWorkflowCompletionStep<CupidRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					CupidRoleState.ReadyToSleep,
					CupidRoleState.Asleep,
					static _ => true),
				new RoleWorkflowCompletionStep<CupidRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					CupidRoleState.Asleep,
					CupidRoleState.Asleep,
					static _ => true)
			]);
	}

	internal override string PublicName => GameStrings.CupidRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Cupid);

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_workflowRuntime;

	public string ReactionId =>
		EliminationCascadeReactionIds.LoversHeartbreak;

	public EliminationCascadeReactionResult Advance(
		GameSession session,
		IReadOnlyCollection<Guid> eliminatedPlayerIds,
		ModeratorResponse input)
	{
		var loverPlayerIds =
			GameSessionQueries.GetCommittedLoversPlayerIds(session);
		if (loverPlayerIds is null)
		{
			return EliminationCascadeReactionResult.Complete();
		}

		var eliminatedLovers = loverPlayerIds
			.Where(eliminatedPlayerIds.Contains)
			.ToArray();
		if (eliminatedLovers.Length != 1)
		{
			return EliminationCascadeReactionResult.Complete();
		}

		var survivingLoverId = loverPlayerIds
			.Single(playerId => playerId != eliminatedLovers[0]);
		if (session.GetPlayerState(survivingLoverId).Health !=
		    PlayerHealth.Alive)
		{
			return EliminationCascadeReactionResult.Complete();
		}

		return EliminationCascadeReactionResult.Complete(
			[
				new EliminationRequest(
					survivingLoverId,
					EliminationReason.LoversHeartbreak)
			]);
	}

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedExecution(session, out _))
		{
			return ExecuteCore(session, input);
		}

		return GetCurrentListenerState(session) == null
			? base.Execute(session, input)
			: ExecuteCore(session, input);
	}

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		_workflowRuntime.Execute(
			session,
			input,
			GetCurrentListenerState(session));

	private CupidRoleState? GetCurrentListenerState(GameSession session) =>
		session.Execution.GetCurrentListenerState<CupidRoleState>(Id);

	#region Workflow steps

	private static HookListenerActionResult BeginCall(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<CupidRoleState, SelectPlayersInstruction>
			identificationWait,
		RecoverableWait<CupidRoleState, ConfirmationInstruction> wakeWait)
	{
		if (TryResolveBorrowedExecution(session, out _))
		{
			return wakeWait.Execute(session, input);
		}

		if (session.TurnNumber != 1)
		{
			return HookListenerActionResult.Complete(CupidRoleState.Asleep);
		}

		return GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			MainRoleType.Cupid)
			? wakeWait.Execute(session, input)
			: identificationWait.Execute(session, input);
	}

	private HookListenerActionResult CommitIdentificationAndWake(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<CupidRoleState, ConfirmationInstruction> wakeWait)
	{
		IdentifyCompleteLivingRoleHolderSet(
			session,
			input.SelectedPlayerIds?.ToHashSet()
			?? throw new InvalidOperationException(
				"Cupid identification requires one Player selection."));
		return wakeWait.Execute(session, input);
	}

	private HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<CupidRoleState, SelectPlayersInstruction>
			targetSelectionWait,
		RecoverableWait<CupidRoleState, ConfirmationInstruction> sleepWait)
	{
		var execution = ResolveExecution(session);
		var attempt = execution.BorrowedUse?.CreateAttempt() ?? new RolePowerAttempt(
			session,
			execution.ActingPlayer,
			MainRoleType.Cupid,
			LinkLoversPower,
			execution.PowerInstance);
		var availability = _availabilityGateway.Evaluate(
			attempt);
		if (!availability.AvailabilityResult.IsAvailable)
		{
			return sleepWait.Execute(session, input);
		}

		var livingPlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.ToIdSet();
		if (livingPlayerIds.Count < 2)
		{
			if (execution.IsBorrowed)
			{
				return sleepWait.Execute(session, input);
			}

			throw new InvalidOperationException(
				"Cupid requires at least two living Players.");
		}

		return targetSelectionWait.Execute(session, input);
	}

	private static HookListenerActionResult CommitTargetSelection(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<CupidRoleState, ConfirmationInstruction>
			nativeRecognitionWait,
		RecoverableWait<CupidRoleState, ConfirmationInstruction>
			borrowedRecognitionWait)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			CommitBorrowedLoversPair(session, input, borrowedExecution);
			return borrowedRecognitionWait.Execute(session, input);
		}

		CommitNativeLoversPair(session, input);
		return nativeRecognitionWait.Execute(session, input);
	}

	private static void CommitNativeLoversPair(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = session.Execution;
		if (session.TurnNumber != 1 ||
		    execution.CurrentPhase != GamePhase.Night ||
		    GameSessionQueries.GetCommittedLoversPair(session) is not null)
		{
			throw new InvalidOperationException(
				"Cupid may commit one Lovers pair on the first Night.");
		}

		var holder = GetHolder(session);
		if (execution.PendingInstruction is not SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectCupidLovers,
			    CountConstraint: var countConstraint,
			    AffectedPlayerIds: { Count: 1 } affectedPlayerIds
		    } pendingSelection ||
		    pendingSelection.InstructionId != input.InstructionId ||
		    countConstraint != NumberRangeConstraint.Exact(2) ||
		    affectedPlayerIds.Single() != holder.Id)
		{
			throw new InvalidOperationException(
				"The Cupid selection no longer belongs to the instructed living holder.");
		}

		var livingPlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.ToIdSet();
		if (input.SelectedPlayerIds is not { Count: 2 } selectedPlayerIds ||
		    selectedPlayerIds.Distinct().Count() != 2 ||
		    !selectedPlayerIds.IsSubsetOf(livingPlayerIds) ||
		    !selectedPlayerIds.IsSubsetOf(
			    pendingSelection.SelectablePlayerIds))
		{
			throw new InvalidOperationException(
				"Cupid must select exactly two distinct living Players.");
		}

		var powerIdentity = CreateCurrentPowerIdentity(session, holder);
		session.CommitLoversPair(selectedPlayerIds, powerIdentity);
		_ = InitialBeneficiaryClosureRules.TryCommitCurrentSession(session);
	}

	private static void CommitBorrowedLoversPair(
		GameSession session,
		ModeratorResponse input,
		ExecutionContext execution)
	{
		var executionView = session.Execution;
		if (executionView.CurrentPhase != GamePhase.Night)
		{
			throw new InvalidOperationException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}

		if (executionView.PendingInstruction is not
		    SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectCupidLovers
		    } pendingSelection ||
		    pendingSelection.InstructionId != input.InstructionId)
		{
			throw new InvalidOperationException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}

		ValidateBorrowedSelectionInstruction(
			session,
			execution,
			pendingSelection);
		var livingPlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.ToIdSet();
		if (input.Type != ExpectedInputType.PlayerSelection ||
		    input.SelectedPlayerIds is not { Count: 2 } selectedPlayerIds ||
		    selectedPlayerIds.Distinct().Count() != 2 ||
		    !selectedPlayerIds.IsSubsetOf(livingPlayerIds) ||
		    !selectedPlayerIds.IsSubsetOf(
			    pendingSelection.SelectablePlayerIds))
		{
			throw new InvalidOperationException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}

		var borrowedUse = execution.BorrowedUse
			?? throw new InvalidOperationException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		var powerIdentity = borrowedUse.PowerIdentity;
		if (GameSessionQueries.GetCorrelatedActorBorrowedCupidLoversCommits(
				session,
				borrowedUse).Count > 0)
		{
			throw new InvalidOperationException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}

		var disposition = ResolveBorrowedLoversDisposition(
			session,
			selectedPlayerIds);
		session.CommitActorBorrowedCupidLovers(
			powerIdentity,
			selectedPlayerIds,
			disposition);
	}

	private static ActorBorrowedCupidLoversDisposition
		ResolveBorrowedLoversDisposition(
			GameSession session,
			IReadOnlyCollection<Guid> playerIds)
	{
		if (session.TurnNumber == 1)
		{
			return ActorBorrowedCupidLoversDisposition
				.DeferredToInitialBeneficiaryClosure;
		}

		var beneficiaries = playerIds
			.Select(session.GetFactionBeneficiaryKnowledge)
			.ToArray();
		if (beneficiaries.Any(beneficiary => !beneficiary.IsKnown))
		{
			throw new InvalidOperationException(
				"Required Faction facts are not ready.");
		}

		return beneficiaries[0].Faction == beneficiaries[1].Faction
			? ActorBorrowedCupidLoversDisposition.SameFaction
			: ActorBorrowedCupidLoversDisposition.CrossFaction;
	}

	#endregion

	#region Instruction factories

	private SelectPlayersInstruction CreateIdentificationInstruction(
		GameSession session)
	{
		var selectablePlayerIds = GetIdentificationCandidates(session);
		if (GetExpectedLivingRoleHolderCount(session) != 1 ||
		    selectablePlayerIds.Count == 0)
		{
			throw new InvalidOperationException(
				"Cupid identification requires exactly one possible living holder.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			selectablePlayerIds,
			NumberRangeConstraint.Single,
			publicAnnouncement: null,
			privateInstruction:
				GameStrings.RoleSingleIdentificationPrompt.Format(PublicName),
			affectedPlayerIds: null,
			roleIdentification: MainRoleType.Cupid);
	}

	private ConfirmationInstruction CreateWakeInstruction(GameSession session)
	{
		var execution = ResolveExecution(session);
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.WakeRole,
			GameStrings.RoleWakesUp.Format(
				execution.IsBorrowed
					? GameStrings.ActorRoleName
					: PublicName),
			affectedPlayerIds: [execution.ActingPlayer.Id]);
	}

	private static SelectPlayersInstruction CreateTargetSelectionInstruction(
		GameSession session)
	{
		var execution = ResolveExecution(session);
		var livingPlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.ToIdSet();
		if (livingPlayerIds.Count < 2)
		{
			throw new InvalidOperationException(
				"Cupid requires at least two living Players.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectCupidLovers,
			livingPlayerIds,
			NumberRangeConstraint.Exact(2),
			publicAnnouncement: null,
			privateInstruction: GameStrings.CupidTargetSelectionInstruction,
			affectedPlayerIds: [execution.ActingPlayer.Id]);
	}

	private ConfirmationInstruction CreateHolderSleepInstruction(
		GameSession session)
	{
		var execution = ResolveExecution(session);
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleGoesToSleepSingle.Format(
				execution.IsBorrowed
					? GameStrings.ActorRoleName
					: PublicName),
			affectedPlayerIds: [execution.ActingPlayer.Id]);
	}

	private static ConfirmationInstruction CreateNativeRecognitionInstruction(
		GameSession session)
	{
		var pair = GameSessionQueries.GetCommittedLoversPair(session)
			?? throw new InvalidOperationException(
				"The Lovers recognition requires one committed pair.");
		return CreateRecognitionInstruction(pair.PlayerIds);
	}

	private static ConfirmationInstruction CreateBorrowedRecognitionInstruction(
		GameSession session)
	{
		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid recognition has no active borrowed execution.");
		}

		return CreateRecognitionInstruction(
			GetBorrowedCommit(session, execution).PlayerIds);
	}

	private static ConfirmationInstruction CreateRecognitionInstruction(
		IReadOnlyList<Guid> playerIds) =>
		new(
			ModeratorInstructionSemantic.RecognizeLovers,
			publicAnnouncement: null,
			privateInstruction: GameStrings.LoversRecognitionInstruction,
			affectedPlayerIds: playerIds);

	private ConfirmationInstruction CreateCommittedSleepInstruction(
		GameSession session)
	{
		if (TryResolveBorrowedExecution(session, out var execution))
		{
			ValidateCommittedBorrowedPair(
				session,
				execution,
				GetBorrowedCommit(session, execution));
			return CreateHolderSleepInstruction(session);
		}

		var pair = GameSessionQueries.GetCommittedLoversPair(session)
			?? throw new InvalidOperationException(
				"The Lovers recognition requires one committed pair.");
		ValidateCommittedPair(session, pair);
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.LoversSleepAnnouncement,
			affectedPlayerIds: pair.PlayerIds);
	}

	#endregion

	#region Claim predicates

	private bool ClaimsWake(
		GameSession session,
		ModeratorInstruction instruction) =>
		instruction.Semantic == ModeratorInstructionSemantic.WakeRole &&
		HasActingHolderAudience(session, instruction);

	private bool ClaimsTargetSelection(
		GameSession session,
		ModeratorInstruction instruction) =>
		instruction.Semantic ==
			ModeratorInstructionSemantic.SelectCupidLovers &&
		HasActingHolderAudience(session, instruction);

	private bool ClaimsUncommittedSleep(
		GameSession session,
		ModeratorInstruction instruction)
	{
		if (instruction.Semantic !=
		    ModeratorInstructionSemantic.PutRoleToSleep)
		{
			return false;
		}

		return TryResolveBorrowedExecution(session, out var borrowedExecution)
			? !GetBorrowedCommits(session, borrowedExecution).Any() &&
			  HasActingHolderAudience(session, instruction)
			: GameSessionQueries.GetCommittedLoversPair(session) is null &&
			  HasActingHolderAudience(session, instruction);
	}

	private bool HasActingHolderAudience(
		GameSession session,
		ModeratorInstruction instruction)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			return instruction.AffectedPlayerIds is [var borrowedAffectedId] &&
			       borrowedAffectedId == borrowedExecution.ActingPlayer.Id;
		}

		return HasExpectedAffectedRoleHolders(session, instruction);
	}

	#endregion

	#region Instruction validators

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		if (instruction.RoleIdentification != MainRoleType.Cupid ||
		    instruction.AffectedPlayerIds != null ||
		    instruction.PublicAnnouncement != null ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PrivateInstruction,
			    GameStrings.RoleSingleIdentificationPrompt.Format(PublicName)) ||
		    instruction.CountConstraint != NumberRangeConstraint.Single ||
		    GetExpectedLivingRoleHolderCount(session) != 1 ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)))
		{
			throw new InvalidOperationException(
				"The Cupid identification instruction has invalid workflow context.");
		}
	}

	private void ValidateWakeInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			if (!StringComparer.Ordinal.Equals(
				    instruction.PublicAnnouncement,
				    GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName)) ||
			    instruction.PrivateInstruction is not null ||
			    instruction.AffectedPlayerIds is not [var borrowedAffectedId] ||
			    borrowedAffectedId != borrowedExecution.ActingPlayer.Id)
			{
				throw new RoleWorkflowInputRejectionException(
					GameStrings.ActorBorrowedRolePowerInvalidResponse);
			}

			return;
		}

		if (!StringComparer.Ordinal.Equals(
			    instruction.PublicAnnouncement,
			    GameStrings.RoleWakesUp.Format(PublicName)) ||
		    instruction.PrivateInstruction is not null ||
		    !HasExpectedAffectedRoleHolders(session, instruction))
		{
			throw new InvalidOperationException(
				"The Cupid wake instruction has invalid workflow context.");
		}
	}

	private void ValidateTargetSelectionInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			ValidateBorrowedSelectionInstruction(
				session,
				borrowedExecution,
				instruction);
			return;
		}

		if (instruction.CountConstraint != NumberRangeConstraint.Exact(2) ||
		    instruction.RoleIdentification is not null ||
		    instruction.PublicAnnouncement is not null ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PrivateInstruction,
			    GameStrings.CupidTargetSelectionInstruction) ||
		    instruction.AffectedPlayerIds is not [var affectedPlayerId] ||
		    affectedPlayerId != GetHolder(session).Id)
		{
			throw new InvalidOperationException(
				"The Cupid selection no longer belongs to the instructed living holder.");
		}
	}

	private void ValidateReplayableSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			if (GetBorrowedCommits(session, borrowedExecution).Any() ||
			    !IsBorrowedHolderSleep(instruction, borrowedExecution))
			{
				throw new RoleWorkflowInputRejectionException(
					GameStrings.ActorBorrowedRolePowerInvalidResponse);
			}

			return;
		}

		if (GameSessionQueries.GetCommittedLoversPair(session) is not null ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PublicAnnouncement,
			    GameStrings.RoleGoesToSleepSingle.Format(PublicName)) ||
		    instruction.PrivateInstruction is not null ||
		    instruction.SoundEffects.Count != 0 ||
		    !HasExpectedAffectedRoleHolders(session, instruction))
		{
			throw new InvalidOperationException(
				"The Cupid sleep instruction has invalid workflow context.");
		}
	}

	private void ValidateCommittedSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			var commits = GetBorrowedCommits(session, borrowedExecution)
				.ToArray();
			if (commits is not [var borrowedPair] ||
			    !IsBorrowedHolderSleep(instruction, borrowedExecution))
			{
				throw new RoleWorkflowInputRejectionException(
					GameStrings.ActorBorrowedRolePowerInvalidResponse);
			}

			ValidateCommittedBorrowedPair(
				session,
				borrowedExecution,
				borrowedPair);
			return;
		}

		var pair = GameSessionQueries.GetCommittedLoversPair(session);
		if (pair is null ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PublicAnnouncement,
			    GameStrings.LoversSleepAnnouncement) ||
		    instruction.PrivateInstruction is not null ||
		    instruction.SoundEffects.Count != 0 ||
		    instruction.AffectedPlayerIds is not { Count: 2 } playerIds ||
		    !playerIds.ToHashSet().SetEquals(pair.PlayerIds))
		{
			throw new InvalidOperationException(
				"The Cupid committed sleep instruction has invalid workflow context.");
		}

		ValidateCommittedPair(session, pair);
	}

	private static void ValidateNativeRecognitionInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (TryResolveBorrowedExecution(session, out _))
		{
			throw new InvalidOperationException(
				"The Cupid recognition instruction has invalid workflow context.");
		}

		var pair = GameSessionQueries.GetCommittedLoversPair(session);
		if (pair is null ||
		    instruction.PublicAnnouncement is not null ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PrivateInstruction,
			    GameStrings.LoversRecognitionInstruction) ||
		    instruction.SoundEffects.Count != 0 ||
		    instruction.AffectedPlayerIds is not { Count: 2 } playerIds ||
		    !playerIds.ToHashSet().SetEquals(pair.PlayerIds))
		{
			throw new InvalidOperationException(
				"The Cupid recognition instruction has invalid workflow context.");
		}

		ValidateCommittedPair(session, pair);
	}

	private static void ValidateBorrowedRecognitionInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}

		var commit = GetBorrowedCommit(session, execution);
		ValidateCommittedBorrowedPair(session, execution, commit);
		ValidateBorrowedRecognition(commit, instruction);
	}

	private static void ValidateBorrowedSelectionInstruction(
		GameSession session,
		ExecutionContext execution,
		SelectPlayersInstruction selection)
	{
		var livingPlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.ToIdSet();
		if (selection.CountConstraint != NumberRangeConstraint.Exact(2) ||
		    selection.RoleIdentification is not null ||
		    selection.PublicAnnouncement is not null ||
		    selection.PrivateInstruction !=
		    GameStrings.CupidTargetSelectionInstruction ||
		    selection.AffectedPlayerIds is not [var affectedPlayerId] ||
		    affectedPlayerId != execution.ActingPlayer.Id ||
		    !selection.SelectablePlayerIds.ToHashSet().SetEquals(
			    livingPlayerIds))
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	private static void ValidateBorrowedRecognition(
		ActorBorrowedCupidLoversCommit committedPair,
		ConfirmationInstruction recognition)
	{
		if (recognition.PublicAnnouncement is not null ||
		    recognition.PrivateInstruction !=
		    GameStrings.LoversRecognitionInstruction ||
		    recognition.SoundEffects.Count != 0 ||
		    recognition.AffectedPlayerIds is not { Count: 2 } playerIds ||
		    !playerIds.ToHashSet().SetEquals(committedPair.PlayerIds))
		{
			throw new RoleWorkflowInputRejectionException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}
	}

	private static bool IsBorrowedHolderSleep(
		ConfirmationInstruction sleep,
		ExecutionContext execution) =>
		StringComparer.Ordinal.Equals(
			sleep.PublicAnnouncement,
			GameStrings.RoleGoesToSleepSingle.Format(
				GameStrings.ActorRoleName)) &&
		sleep.PrivateInstruction is null &&
		sleep.SoundEffects.Count == 0 &&
		sleep.AffectedPlayerIds is [var affectedPlayerId] &&
		affectedPlayerId == execution.ActingPlayer.Id;

	#endregion

	#region Declared recovery contracts

	private static void ValidateCallHandoff(
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != MainRoleType.Cupid)
		{
			throw new InvalidOperationException(
				"The Cupid call has invalid accepted-observation handoff context.");
		}
	}

	private static void ValidateRecognitionHandoff(
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version !=
			    AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.AcceptedObservationSemantic !=
		    ModeratorInstructionSemantic.RecognizeLovers ||
		    cursor.ObservedRole != MainRoleType.Cupid ||
		    cursor.ContinuationRole != MainRoleType.Cupid ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The Cupid recognition has invalid accepted-observation handoff context.");
		}
	}

	private static void ValidateRecurringCursorShape(DomainRecoveryCursor cursor)
	{
		if (cursor.Kind !=
		    DomainRecoveryCursorKind.RecurringNativeRolePowerCommit ||
		    cursor.SourceRole != MainRoleType.Cupid ||
		    cursor.CommittedActionType != NightActionType.CupidLink ||
		    cursor.ActingPlayerId == Guid.Empty ||
		    !StringComparer.Ordinal.Equals(
			    cursor.SourcePowerIdentifier,
			    LinkLoversPowerIdentifier.Value) ||
		    cursor.PowerIdentity is null ||
		    cursor.OneUseResourceId != Guid.Empty ||
		    cursor.CommittedTargetIds.Count != 2 ||
		    cursor.CommittedTargetIds.Distinct().Count() != 2 ||
		    !cursor.CommittedTargetIds.SequenceEqual(
			    cursor.CommittedTargetIds.Order()))
		{
			throw new InvalidOperationException(
				"The Cupid recovery cursor has an invalid recurring Role Power identity.");
		}
	}

	private static void ValidateNativeRecoveryCursor(
		GameSession session,
		DomainRecoveryCursor cursor)
	{
		ValidateRecurringCursorShape(cursor);
		if (cursor.PowerIdentity is not { } powerIdentity ||
		    powerIdentity.PowerInstanceOrigin ==
		    RolePowerInstanceOrigin.Borrowed ||
		    cursor.ActorSetupCardId != Guid.Empty ||
		    cursor.ActorBorrowedActivationId != Guid.Empty ||
		    powerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(cursor.ActingPlayerId)))
		{
			throw new InvalidOperationException(
				"The Cupid recovery cursor has an invalid recurring Role Power identity.");
		}
	}

	private static void ValidateBorrowedRecoveryCursor(
		GameSession session,
		DomainRecoveryCursor cursor)
	{
		ValidateRecurringCursorShape(cursor);
		if (cursor.PowerIdentity is not { } cursorPowerIdentity ||
		    cursorPowerIdentity.PowerInstanceOrigin !=
		    RolePowerInstanceOrigin.Borrowed)
		{
			throw new InvalidOperationException(
				"The Cupid recovery cursor has an invalid recurring Role Power identity.");
		}

		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid recovery cursor has no active borrowed execution.");
		}

		var borrowedUse = execution.BorrowedUse
			?? throw new InvalidOperationException(
				"The Actor borrowed Cupid recovery cursor has no active borrowed execution.");
		var commits = GetBorrowedCommits(session, execution).ToArray();
		if (cursorPowerIdentity != borrowedUse.PowerIdentity ||
		    cursor.ActorSetupCardId != borrowedUse.ActorSetupCardId ||
		    cursor.ActorBorrowedActivationId !=
		    borrowedUse.PowerIdentity.PowerInstanceId ||
		    cursor.NextInstructionSemantic !=
		    ModeratorInstructionSemantic.RecognizeLovers ||
		    cursor.NextInstructionId == Guid.Empty ||
		    commits is not [var commit] ||
		    !cursor.CommittedTargetIds.SequenceEqual(commit.PlayerIds))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid recovery cursor has an invalid borrowed Role Power identity.");
		}

		ValidateCommittedBorrowedPair(session, execution, commit);
	}

	private static bool TryValidateNativeCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		LoversPairCommittedLogEntry pair,
		ConfirmationInstruction nextInstruction)
	{
		if (startingInstruction is not SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectCupidLovers,
			    CountConstraint: var countConstraint,
			    AffectedPlayerIds: { Count: 1 } affectedPlayerIds,
			    RoleIdentification: null
		    } targetSelection ||
		    countConstraint != NumberRangeConstraint.Exact(2) ||
		    input.SelectedPlayerIds is not { Count: 2 } selectedPlayerIds ||
		    !selectedPlayerIds.SetEquals(pair.PlayerIds) ||
		    !selectedPlayerIds.IsSubsetOf(
			    targetSelection.SelectablePlayerIds) ||
		    pair.ActingPlayerId != affectedPlayerIds.Single() ||
		    nextInstruction.AffectedPlayerIds is not
			    { Count: 2 } recognitionPlayerIds ||
		    !recognitionPlayerIds.ToHashSet().SetEquals(pair.PlayerIds))
		{
			throw new InvalidOperationException(
				"The Cupid pair commit must correlate to its exact selection and private recognition continuation.");
		}

		ValidateCommittedPair(session, pair);
		return true;
	}

	private static bool TryValidateBorrowedCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		ActorBorrowedCupidLoversCommit committedPair,
		ConfirmationInstruction nextInstruction)
	{
		if (committedPair.PowerIdentity.SourceRole != MainRoleType.Cupid)
		{
			return false;
		}

		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			throw new InvalidOperationException(
				"No active Actor borrowed Cupid Role Power is available for recovery.");
		}

		ValidateCommittedBorrowedPair(session, execution, committedPair);
		if (startingInstruction is not SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectCupidLovers
		    } selection ||
		    input.InstructionId != selection.InstructionId ||
		    input.Type != ExpectedInputType.PlayerSelection ||
		    input.SelectedPlayerIds is not { Count: 2 } selectedPlayerIds ||
		    !selectedPlayerIds.ToHashSet().SetEquals(committedPair.PlayerIds) ||
		    !selectedPlayerIds.IsSubsetOf(selection.SelectablePlayerIds))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid commit must correlate to its exact selection and private recognition continuation.");
		}

		ValidateBorrowedSelectionInstruction(session, execution, selection);
		ValidateBorrowedRecognition(committedPair, nextInstruction);
		return true;
	}

	#endregion

	#region Helpers

	private static IPlayer GetHolder(GameSession session) =>
		session.GetPlayers()
			.WithRole(ListenerIdentifier.Listener(MainRoleType.Cupid))
			.WithHealth(PlayerHealth.Alive)
			.SingleOrDefault()
		?? throw new InvalidOperationException("No living Cupid is available.");

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.Cupid ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == MainRoleType.Cupid ||
				  player.State.ModeratorKnownRole == null &&
				  GameSessionQueries.GetPossibleRoles(session, player.Id)
					  .Contains(MainRoleType.Cupid))))
			.ToIdSet();

	private bool HasExpectedAffectedRoleHolders(
		GameSession session,
		ModeratorInstruction instruction)
	{
		var holders = GetAliveRolePlayers(session)?
			.Select(player => player.Id)
			.ToHashSet();
		return holders is { Count: > 0 } &&
		       instruction.AffectedPlayerIds is { } affectedPlayerIds &&
		       affectedPlayerIds.ToHashSet().SetEquals(holders);
	}

	private static ExecutionContext ResolveExecution(GameSession session) =>
		TryResolveBorrowedExecution(session, out var borrowed)
			? borrowed
			: ResolveNativeExecution(session);

	private static ExecutionContext ResolveNativeExecution(
		GameSession session)
	{
		var holder = GetHolder(session);
		return new ExecutionContext(
			holder,
			RolePowerInstance.CreateCurrent(
				session,
				holder,
				MainRoleType.Cupid,
				LinkLoversPower),
			BorrowedUse: null);
	}

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		var borrowedUse = ActorBorrowedRolePowers.ResolveActive(
			session,
			BorrowedPowerSpec);
		if (borrowedUse is null)
		{
			execution = null!;
			return false;
		}

		execution = new ExecutionContext(
			borrowedUse.Actor,
			borrowedUse.PowerInstance,
			borrowedUse);
		return true;
	}

	private static IEnumerable<ActorBorrowedCupidLoversCommit>
		GetBorrowedCommits(
			GameSession session,
			ExecutionContext execution)
	{
		var borrowedUse = execution.BorrowedUse
			?? throw new InvalidOperationException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		return GameSessionQueries.GetCorrelatedActorBorrowedCupidLoversCommits(
			session,
			borrowedUse);
	}

	private static ActorBorrowedCupidLoversCommit GetBorrowedCommit(
		GameSession session,
		ExecutionContext execution)
	{
		var commits = GetBorrowedCommits(session, execution).ToArray();
		if (commits is not [var commit])
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid continuation requires exactly one private Lovers commit.");
		}

		return commit;
	}

	private static void ValidateCommittedBorrowedPair(
		GameSession session,
		ExecutionContext execution,
		ActorBorrowedCupidLoversCommit committedPair)
	{
		if (execution.BorrowedUse is not { } borrowedUse ||
		    !borrowedUse.Correlates(committedPair) ||
		    committedPair.PlayerIds.Any(playerId =>
			    !session.GetPlayerState(playerId)
				    .HasStatusEffect(StatusEffectTypes.Lovers)))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid recovery boundary requires one activation-qualified private Lovers pair with both durable statuses.");
		}

		switch (committedPair.Disposition)
		{
			case ActorBorrowedCupidLoversDisposition
				.DeferredToInitialBeneficiaryClosure:
				break;
			case ActorBorrowedCupidLoversDisposition.SameFaction:
				var firstFaction = session.RequireKnownFactionBeneficiary(
					committedPair.FirstPlayerId);
				var secondFaction = session.RequireKnownFactionBeneficiary(
					committedPair.SecondPlayerId);
				if (firstFaction != secondFaction ||
				    firstFaction == Faction.CrossFactionLovers)
				{
					throw new InvalidOperationException(
						"The Actor borrowed Cupid Same-Faction pair does not match current Beneficiary facts.");
				}

				break;
			case ActorBorrowedCupidLoversDisposition.CrossFaction:
				if (session.RequireKnownFactionBeneficiary(
					    committedPair.FirstPlayerId) !=
				    Faction.CrossFactionLovers ||
				    session.RequireKnownFactionBeneficiary(
					    committedPair.SecondPlayerId) !=
				    Faction.CrossFactionLovers)
				{
					throw new InvalidOperationException(
						"The Actor borrowed Cupid Cross-Faction pair does not match current Beneficiary facts.");
				}

				break;
			default:
				throw new InvalidOperationException(
					"The Actor borrowed Cupid Lovers disposition is invalid.");
		}
	}

	private static RolePowerInstanceIdentity CreateCurrentPowerIdentity(
		GameSession session,
		IPlayer holder) =>
		RolePowerInstance.CreateCurrentIdentity(
			session,
			holder,
			MainRoleType.Cupid,
			LinkLoversPower);

	private static void ValidateCommittedPair(
		GameSession session,
		LoversPairCommittedLogEntry pair)
	{
		pair.EnforceValidity();
		if (pair.PowerIdentity != CreateCurrentPowerIdentity(
				session,
				session.GetPlayer(pair.ActingPlayerId)) ||
		    !StringComparer.Ordinal.Equals(
			    pair.SourcePowerIdentifier,
			    LinkLoversPowerIdentifier.Value) ||
		    pair.PlayerIds.Any(playerId =>
			    !session.GetPlayerState(playerId)
				    .HasStatusEffect(StatusEffectTypes.Lovers)))
		{
			throw new InvalidOperationException(
				"The committed Lovers pair does not match Cupid's current power and both durable statuses.");
		}
	}

	#endregion
}
