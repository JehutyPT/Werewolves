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
	: NightRoleHookListener<CupidRoleState>,
	  IEliminationCascadeReaction
{
	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		bool IsBorrowed);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	private static readonly RolePowerDefinition LinkLoversPower = new(
		new RolePowerIdentifier("cupid-link-lovers"),
		RolePowerCategory.Chosen);

	internal static RolePowerIdentifier LinkLoversPowerIdentifier =>
		LinkLoversPower.Identifier;

	internal CupidRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.CupidRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Cupid);

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

	protected override CupidRoleState WokenUpStateEnum => CupidRoleState.Awake;

	protected override CupidRoleState ReadyToSleepStateEnum =>
		CupidRoleState.ReadyToSleep;

	protected override CupidRoleState AsleepStateEnum => CupidRoleState.Asleep;

	protected override bool HasNightPowers => true;

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			null,
			[
				CupidRoleState.AwaitingIdentification,
				CupidRoleState.Awake,
				CupidRoleState.AwaitingLoversRecognition,
				CupidRoleState.ReadyToSleep,
				CupidRoleState.Asleep
			],
			BeginCall),
		CreateStage(
			GameHook.NightMainActionLoop,
			CupidRoleState.AwaitingIdentification,
			CupidRoleState.Awake,
			CommitIdentificationAndWake),
		CreateStage(
			GameHook.NightMainActionLoop,
			CupidRoleState.Awake,
			[
				CupidRoleState.AwaitingTargetSelection,
				CupidRoleState.ReadyToSleep
			],
			HandleNightPowerUse),
		CreateStage(
			GameHook.NightMainActionLoop,
			CupidRoleState.AwaitingTargetSelection,
			CupidRoleState.AwaitingLoversRecognition,
			CommitTargetSelection),
		CreateStage(
			GameHook.NightMainActionLoop,
			CupidRoleState.AwaitingLoversRecognition,
			CupidRoleState.ReadyToSleep,
			PrepareLoversSleepInstruction),
		CreateStage(
			GameHook.NightMainActionLoop,
			CupidRoleState.ReadyToSleep,
			CupidRoleState.Asleep,
			(_, _) => HookListenerActionResult.Complete(CupidRoleState.Asleep)),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			CupidRoleState.Asleep,
			(_, _) => HookListenerActionResult.Complete(CupidRoleState.Asleep))
	];

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		if (hook == GameHook.NightMainActionLoop &&
		    TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			switch (pendingInstruction)
			{
				case ConfirmationInstruction
					{
						Semantic: ModeratorInstructionSemantic.WakeRole
					} wake:
					ValidateBorrowedWake(borrowedExecution, wake);
					listenerState = CupidRoleState.Awake.ToString();
					return true;
				case SelectPlayersInstruction
					{
						Semantic:
							ModeratorInstructionSemantic.SelectCupidLovers
					} selection:
					ValidateBorrowedSelectionInstruction(
						session,
						borrowedExecution,
						selection);
					listenerState =
						CupidRoleState.AwaitingTargetSelection.ToString();
					return true;
				case ConfirmationInstruction
					{
						Semantic:
							ModeratorInstructionSemantic.RecognizeLovers
					} recognition:
					var commit = GetBorrowedCommit(
						session,
						borrowedExecution);
					ValidateCommittedBorrowedPair(
						session,
						borrowedExecution,
						commit);
					ValidateBorrowedRecognition(commit, recognition);
					listenerState =
						CupidRoleState.AwaitingLoversRecognition.ToString();
					return true;
				case ConfirmationInstruction
					{
						Semantic:
							ModeratorInstructionSemantic.PutRoleToSleep
					} sleep:
					ValidateBorrowedSleep(borrowedExecution, sleep);
					var commits = GetBorrowedCommits(
							session,
							borrowedExecution)
						.ToArray();
					if (commits.Length > 1)
					{
						throw new InvalidOperationException(
							"The pending Actor borrowed Cupid sleep instruction has multiple private Lovers commits.");
					}

					if (commits is [var committedPair])
					{
						ValidateCommittedBorrowedPair(
							session,
							borrowedExecution,
							committedPair);
					}

					listenerState = CupidRoleState.ReadyToSleep.ToString();
					return true;
			}
		}

		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.IdentifyRoleHolders,
			    RoleIdentification: MainRoleType.Cupid
		    })
		{
			listenerState = CupidRoleState.AwaitingIdentification.ToString();
			return true;
		}

		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.RecognizeLovers
		    } &&
		    HasExpectedCommittedPair(session, pendingInstruction))
		{
			listenerState =
				CupidRoleState.AwaitingLoversRecognition.ToString();
			return true;
		}

		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.PutRoleToSleep
		    } &&
		    HasExpectedSleepAudience(session, pendingInstruction))
		{
			listenerState = CupidRoleState.ReadyToSleep.ToString();
			return true;
		}

		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectCupidLovers
		    } &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			listenerState = CupidRoleState.AwaitingTargetSelection.ToString();
			return true;
		}

		return base.TryResolvePendingInstructionContinuation(
			hook,
			session,
			pendingInstruction,
			out listenerState);
	}

	private HookListenerActionResult CommitTargetSelection(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			return CommitBorrowedTargetSelection(
				session,
				input,
				borrowedExecution);
		}

		if (session.TurnNumber != 1 ||
		    session.GetCurrentPhase() != GamePhase.Night ||
		    GameSessionQueries.GetCommittedLoversPair(session) is not null)
		{
			throw new InvalidOperationException(
				"Cupid may commit one Lovers pair on the first Night.");
		}

		var holder = GetHolder(session);
		if (session.PendingModeratorInstruction is not SelectPlayersInstruction
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

		return PrepareLoversRecognitionInstruction(
			selectedPlayerIds.Order().ToArray());
	}

	private HookListenerActionResult CommitBorrowedTargetSelection(
		GameSession session,
		ModeratorResponse input,
		ExecutionContext execution)
	{
		if (session.GetCurrentPhase() != GamePhase.Night)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid may commit one Lovers pair at its Night source slot.");
		}

		if (session.PendingModeratorInstruction is not
		    SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectCupidLovers
		    } pendingSelection ||
		    pendingSelection.InstructionId != input.InstructionId)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid selection no longer belongs to its instructed source slot.");
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
				"The Actor borrowed Cupid must select exactly two distinct living Players.");
		}

		var powerIdentity = CreatePowerIdentity(execution);
		if (session.GetActorBorrowedCupidLoversCommits().Any(commit =>
			    commit.PowerIdentity == powerIdentity))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid power already committed its Lovers pair.");
		}

		session.CommitActorBorrowedCupidLovers(
			powerIdentity,
			selectedPlayerIds);
		return PrepareLoversRecognitionInstruction(
			selectedPlayerIds.Order().ToArray());
	}

	private HookListenerActionResult BeginCall(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedExecution(session, out _))
		{
			return PrepareWakeInstruction(session);
		}

		if (session.TurnNumber != 1)
		{
			return HookListenerActionResult.Complete(CupidRoleState.Asleep);
		}

		if (GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			    session,
			    MainRoleType.Cupid))
		{
			return PrepareWakeInstruction(session);
		}

		var selectablePlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.Cupid ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == null ||
				  player.State.ModeratorKnownRole == MainRoleType.Cupid)))
			.ToIdSet();
		if (GetExpectedLivingRoleHolderCount(session) != 1 ||
		    selectablePlayerIds.Count == 0)
		{
			throw new InvalidOperationException(
				"Cupid identification requires exactly one possible living holder.");
		}

		return HookListenerActionResult.NeedInput(
			new SelectPlayersInstruction(
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				selectablePlayerIds,
				NumberRangeConstraint.Single,
				publicAnnouncement: null,
				privateInstruction:
					GameStrings.RoleSingleIdentificationPrompt.Format(PublicName),
				affectedPlayerIds: null,
				roleIdentification: MainRoleType.Cupid),
			CupidRoleState.AwaitingIdentification);
	}

	private HookListenerActionResult CommitIdentificationAndWake(
		GameSession session,
		ModeratorResponse input)
	{
		ProcessRoleIdentification(session, input);
		return PrepareWakeInstruction(session);
	}

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse _)
	{
		var execution = ResolveExecution(session);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				execution.ActingPlayer,
				MainRoleType.Cupid,
				LinkLoversPower,
				execution.PowerInstance));
		if (!availability.AvailabilityResult.IsAvailable)
		{
			return PrepareSleepInstruction(session);
		}

		var livingPlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.ToIdSet();
		if (livingPlayerIds.Count < 2)
		{
			if (execution.IsBorrowed)
			{
				return PrepareSleepInstruction(session);
			}

			throw new InvalidOperationException(
				"Cupid requires at least two living Players.");
		}

		return HookListenerActionResult.NeedInput(
			new SelectPlayersInstruction(
				ModeratorInstructionSemantic.SelectCupidLovers,
				livingPlayerIds,
				NumberRangeConstraint.Exact(2),
				publicAnnouncement: null,
				privateInstruction: GameStrings.CupidTargetSelectionInstruction,
				affectedPlayerIds: [execution.ActingPlayer.Id]),
			CupidRoleState.AwaitingTargetSelection);
	}

	private HookListenerActionResult PrepareWakeInstruction(GameSession session)
	{
		var execution = ResolveExecution(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(
					execution.IsBorrowed
						? GameStrings.ActorRoleName
						: PublicName),
				affectedPlayerIds: [execution.ActingPlayer.Id]),
			CupidRoleState.Awake);
	}

	internal static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		LoversPairCommittedLogEntry pair,
		ModeratorInstruction nextInstruction)
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
		    nextInstruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.RecognizeLovers,
			    AffectedPlayerIds: { Count: 2 } recognitionPlayerIds
		    } ||
		    !recognitionPlayerIds.ToHashSet().SetEquals(pair.PlayerIds))
		{
			throw new InvalidOperationException(
				"The Cupid pair commit must correlate to its exact selection and private recognition continuation.");
		}

		ValidateCommittedPair(session, pair);
		return true;
	}

	internal static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		ActorBorrowedCupidLoversCommit committedPair,
		ModeratorInstruction nextInstruction)
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
		    !selectedPlayerIds.IsSubsetOf(selection.SelectablePlayerIds) ||
		    nextInstruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.RecognizeLovers
		    } recognition)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid commit must correlate to its exact selection and private recognition continuation.");
		}

		ValidateBorrowedSelectionInstruction(session, execution, selection);
		ValidateBorrowedRecognition(committedPair, recognition);
		return true;
	}

	internal static void ValidateRecurringRecoveryCursorIdentity(
		GameSession session,
		DomainRecoveryCursor cursor)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(cursor);
		if (cursor.Kind !=
		    DomainRecoveryCursorKind.RecurringNativeRolePowerCommit ||
		    cursor.SourceRole != MainRoleType.Cupid ||
		    cursor.CommittedActionType != NightActionType.CupidLink ||
		    cursor.ActingPlayerId == Guid.Empty ||
		    !StringComparer.Ordinal.Equals(
			    cursor.SourcePowerIdentifier,
			    LinkLoversPowerIdentifier.Value) ||
		    cursor.PowerIdentity is not { } powerIdentity ||
		    cursor.OneUseResourceId != Guid.Empty ||
		    cursor.CommittedTargetIds.Count != 2 ||
		    cursor.CommittedTargetIds.Distinct().Count() != 2 ||
		    !cursor.CommittedTargetIds.SequenceEqual(
			    cursor.CommittedTargetIds.Order()))
		{
			throw new InvalidOperationException(
				"The Cupid recovery cursor has an invalid recurring Role Power identity.");
		}

		if (powerIdentity.PowerInstanceOrigin ==
		    RolePowerInstanceOrigin.Borrowed)
		{
			ValidateBorrowedRecoveryCursorIdentity(
				session,
				cursor,
				powerIdentity);
			return;
		}

		if (cursor.ActorSetupCardId != Guid.Empty ||
		    cursor.ActorBorrowedActivationId != Guid.Empty ||
		    powerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(cursor.ActingPlayerId)))
		{
			throw new InvalidOperationException(
				"The Cupid recovery cursor has an invalid recurring Role Power identity.");
		}
	}

	protected override HookListenerActionResult PrepareSleepInstruction(
		GameSession session)
	{
		var execution = ResolveExecution(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(
					execution.IsBorrowed
						? GameStrings.ActorRoleName
						: PublicName),
				affectedPlayerIds: [execution.ActingPlayer.Id]),
			CupidRoleState.ReadyToSleep);
	}

	private static HookListenerActionResult
		PrepareLoversRecognitionInstruction(
			IReadOnlyList<Guid> playerIds) =>
		HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.RecognizeLovers,
				publicAnnouncement: null,
				privateInstruction: GameStrings.LoversRecognitionInstruction,
				affectedPlayerIds: playerIds),
			CupidRoleState.AwaitingLoversRecognition);

	private HookListenerActionResult PrepareLoversSleepInstruction(
		GameSession session,
		ModeratorResponse _)
	{
		if (TryResolveBorrowedExecution(session, out var execution))
		{
			ValidateCommittedBorrowedPair(
				session,
				execution,
				GetBorrowedCommit(session, execution));
			return PrepareSleepInstruction(session);
		}

		var pair = GameSessionQueries.GetCommittedLoversPair(session)
			?? throw new InvalidOperationException(
				"The Lovers recognition requires one committed pair.");
		ValidateCommittedPair(session, pair);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.LoversSleepAnnouncement,
				affectedPlayerIds: pair.PlayerIds),
			CupidRoleState.ReadyToSleep);
	}

	private IPlayer GetHolder(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException("No living Cupid is available.");

	private ExecutionContext ResolveExecution(GameSession session) =>
		TryResolveBorrowedExecution(session, out var borrowed)
			? borrowed
			: ResolveNativeExecution(session);

	private ExecutionContext ResolveNativeExecution(GameSession session)
	{
		var holder = GetHolder(session);
		return new ExecutionContext(
			holder,
			RolePowerInstance.CreateCurrent(
				session,
				holder,
				MainRoleType.Cupid,
				LinkLoversPower),
			IsBorrowed: false);
	}

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.Cupid)
		{
			execution = null!;
			return false;
		}

		var actor = session.GetPlayer(activation.ActingPlayerId);
		execution = new ExecutionContext(
			actor,
			RolePowerInstance.CreateBorrowed(
				session,
				actor,
				MainRoleType.Cupid,
				LinkLoversPower),
			IsBorrowed: true);
		return true;
	}

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		ExecutionContext execution) => new(
			execution.ActingPlayer.Id,
			MainRoleType.Cupid,
			LinkLoversPower.Identifier.Value,
			execution.PowerInstance.Id,
			execution.PowerInstance.Origin);

	private static IEnumerable<ActorBorrowedCupidLoversCommit>
		GetBorrowedCommits(
			GameSession session,
			ExecutionContext execution) =>
		session.GetActorBorrowedCupidLoversCommits()
			.Where(commit =>
				commit.PowerIdentity == CreatePowerIdentity(execution));

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

	private static void ValidateBorrowedRecoveryCursorIdentity(
		GameSession session,
		DomainRecoveryCursor cursor,
		RolePowerInstanceIdentity cursorPowerIdentity)
	{
		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid recovery cursor has no active borrowed execution.");
		}

		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		var expectedPowerIdentity = CreatePowerIdentity(execution);
		var commits = GetBorrowedCommits(session, execution).ToArray();
		if (cursorPowerIdentity != expectedPowerIdentity ||
		    cursor.ActorSetupCardId != activation.SelectedCardId ||
		    cursor.ActorBorrowedActivationId != activation.ActivationId ||
		    cursor.CommittedTargetIds.Count != 2 ||
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

	private static void ValidateCommittedBorrowedPair(
		GameSession session,
		ExecutionContext execution,
		ActorBorrowedCupidLoversCommit committedPair)
	{
		committedPair.EnforceValidity();
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		var expectedPowerIdentity = CreatePowerIdentity(execution);
		if (!execution.IsBorrowed ||
		    activation?.SourceRole != MainRoleType.Cupid ||
		    committedPair.PowerIdentity != expectedPowerIdentity ||
		    committedPair.ActorSetupCardId != activation.SelectedCardId ||
		    committedPair.TurnNumber != session.TurnNumber ||
		    committedPair.CurrentPhase != GamePhase.Night ||
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

	private static void ValidateBorrowedWake(
		ExecutionContext execution,
		ConfirmationInstruction wake)
	{
		if (wake.PublicAnnouncement !=
			    GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName) ||
		    wake.PrivateInstruction is not null ||
		    wake.AffectedPlayerIds is not [var affectedPlayerId] ||
		    affectedPlayerId != execution.ActingPlayer.Id)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid wake instruction is invalid.");
		}
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
			throw new InvalidOperationException(
				"The borrowed Role Power response is invalid or no longer available.");
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
			throw new InvalidOperationException(
				"The Actor borrowed Cupid recognition instruction is invalid.");
		}
	}

	private static void ValidateBorrowedSleep(
		ExecutionContext execution,
		ConfirmationInstruction sleep)
	{
		if (sleep.PublicAnnouncement !=
			    GameStrings.RoleGoesToSleepSingle.Format(
				    GameStrings.ActorRoleName) ||
		    sleep.PrivateInstruction is not null ||
		    sleep.AffectedPlayerIds is not [var affectedPlayerId] ||
		    affectedPlayerId != execution.ActingPlayer.Id)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid sleep instruction is invalid.");
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

	private static bool HasExpectedCommittedPair(
		GameSession session,
		ModeratorInstruction instruction)
	{
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
			return false;
		}

		ValidateCommittedPair(session, pair);
		return true;
	}

	private bool HasExpectedSleepAudience(
		GameSession session,
		ModeratorInstruction instruction)
	{
		if (GameSessionQueries.GetCommittedLoversPair(session) is not null)
		{
			return HasExpectedCommittedPairSleep(session, instruction);
		}

		return StringComparer.Ordinal.Equals(
		       instruction.PublicAnnouncement,
		       GameStrings.RoleGoesToSleepSingle.Format(PublicName)) &&
		       instruction.PrivateInstruction is null &&
		       instruction.SoundEffects.Count == 0 &&
		       HasExpectedAffectedRoleHolders(session, instruction);
	}

	internal static bool HasExpectedCommittedPairSleep(
		GameSession session,
		ModeratorInstruction instruction)
	{
		if (TryResolveBorrowedExecution(session, out var execution))
		{
			var commits = GetBorrowedCommits(session, execution).ToArray();
			if (commits is not [var committedPair])
			{
				return false;
			}

			ValidateCommittedBorrowedPair(
				session,
				execution,
				committedPair);
			return instruction is ConfirmationInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.PutRoleToSleep,
					PublicAnnouncement: var publicAnnouncement,
					PrivateInstruction: null,
					AffectedPlayerIds: [var affectedPlayerId]
				} &&
				StringComparer.Ordinal.Equals(
					publicAnnouncement,
					GameStrings.RoleGoesToSleepSingle.Format(
						GameStrings.ActorRoleName)) &&
				instruction.SoundEffects.Count == 0 &&
				affectedPlayerId == execution.ActingPlayer.Id;
		}

		var pair = GameSessionQueries.GetCommittedLoversPair(session);
		if (pair is null ||
		    instruction is not ConfirmationInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.PutRoleToSleep,
			    AffectedPlayerIds: { Count: 2 } playerIds
		    } ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PublicAnnouncement,
			    GameStrings.LoversSleepAnnouncement) ||
		    instruction.PrivateInstruction is not null ||
		    instruction.SoundEffects.Count != 0 ||
		    !playerIds.ToHashSet().SetEquals(pair.PlayerIds))
		{
			return false;
		}

		ValidateCommittedPair(session, pair);
		return true;
	}

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
}
