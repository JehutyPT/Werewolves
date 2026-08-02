using Werewolves.Core.GameLogic.Interfaces;
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

internal enum FoxRoleState
{
	Awake,
	AwaitingWakeAcknowledgement,
	AwaitingCenterSelection,
	AwaitingResultAcknowledgement,
	ReadyToSleep,
	Asleep
}

internal sealed class FoxRole
	: NightRoleHookListener<FoxRoleState>,
		ITargetPrivateRolePowerRecoveryCapability
{
	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		bool IsBorrowed);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private bool? _powerIsAvailable;

	private static readonly RolePowerDefinition NeighborhoodCheckPower = new(
		new RolePowerIdentifier("fox-neighborhood-check"),
		RolePowerCategory.Chosen);

	private static readonly Guid NeighborhoodCheckResourceId =
		Guid.Parse("dadbf4d0-fcb8-4e1b-857d-326634230227");

	internal FoxRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.FoxRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Fox);

	protected override FoxRoleState WokenUpStateEnum => FoxRoleState.Awake;

	protected override FoxRoleState ReadyToSleepStateEnum => FoxRoleState.ReadyToSleep;

	protected override FoxRoleState AsleepStateEnum => FoxRoleState.Asleep;

	protected override bool HasNightPowers => true;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedExecution(session, out _))
		{
			return ExecuteCore(session, input);
		}

		return base.Execute(session, input);
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			null,
			[
				FoxRoleState.Awake,
				FoxRoleState.Asleep
			],
			BeginNightAction),
		CreateStage(
			GameHook.NightMainActionLoop,
			FoxRoleState.Awake,
			[
				FoxRoleState.AwaitingWakeAcknowledgement,
				FoxRoleState.AwaitingCenterSelection,
				FoxRoleState.Asleep
			],
			ContinueAfterWakeOrIdentification),
		CreateStage(
			GameHook.NightMainActionLoop,
			FoxRoleState.AwaitingWakeAcknowledgement,
			FoxRoleState.AwaitingCenterSelection,
			HandleNightPowerUse),
		CreateStage(
			GameHook.NightMainActionLoop,
			FoxRoleState.AwaitingCenterSelection,
			[
				FoxRoleState.AwaitingResultAcknowledgement,
				FoxRoleState.ReadyToSleep
			],
			CommitCenterSelection),
		CreateStage(
			GameHook.NightMainActionLoop,
			FoxRoleState.AwaitingResultAcknowledgement,
			FoxRoleState.ReadyToSleep,
			(session, _) => PrepareSleepInstruction(session)),
		CreateStage(
			GameHook.NightMainActionLoop,
			FoxRoleState.ReadyToSleep,
			FoxRoleState.Asleep,
			HandleAsleepConfirmation),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			FoxRoleState.Asleep,
			(_, _) => HookListenerActionResult.Complete(FoxRoleState.Asleep))
	];

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		listenerState = string.Empty;
		if (hook == GameHook.NightMainActionLoop &&
			TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			switch (pendingInstruction)
			{
				case SelectPlayersInstruction
					{
						Semantic:
						ModeratorInstructionSemantic.SelectFoxCenter
					} centerSelection:
					ValidateBorrowedCenterSelectionInstruction(
						session,
						borrowedExecution,
						centerSelection);
					listenerState =
						FoxRoleState.AwaitingCenterSelection.ToString();
					return true;
				case ConfirmationInstruction
					{
						Semantic:
						ModeratorInstructionSemantic.RevealFoxResult
					} borrowedFeedback:
					var commit = GetBorrowedFoxCheckCommit(
						session,
						borrowedExecution);
					ValidateBorrowedCommit(
						session,
						borrowedExecution,
						commit);
					ValidateBorrowedFeedback(
						borrowedExecution,
						commit,
						borrowedFeedback);
					listenerState = FoxRoleState
						.AwaitingResultAcknowledgement
						.ToString();
					return true;
				case ConfirmationInstruction
					{
						Semantic:
						ModeratorInstructionSemantic.PutRoleToSleep
					} sleep:
					var commits = GetBorrowedFoxCheckCommitsThisNight(
							session,
							CreatePowerIdentity(borrowedExecution))
						.ToArray();
					if (commits.Length > 1)
					{
						throw new InvalidOperationException(
							"The Actor borrowed Fox sleep continuation has multiple committed checks.");
					}

					if (commits is [var committedCheck])
					{
						ValidateBorrowedCommit(
							session,
							borrowedExecution,
							committedCheck);
					}

					ValidateBorrowedSleep(borrowedExecution, sleep);
					listenerState = FoxRoleState.ReadyToSleep.ToString();
					return true;
			}
		}

		if (hook == GameHook.NightMainActionLoop &&
			pendingInstruction is SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.SelectFoxCenter
			} &&
			HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			listenerState =
				FoxRoleState.AwaitingCenterSelection.ToString();
			return true;
		}

		if (hook == GameHook.NightMainActionLoop &&
			pendingInstruction is ConfirmationInstruction
			{
				Semantic: ModeratorInstructionSemantic.RevealFoxResult
			} feedback &&
			HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			var commit = GetFoxCheckCommitsThisNight(session).SingleOrDefault()
				?? throw new InvalidOperationException(
					"The Fox feedback requires one committed target-private check.");
			ValidateOwnedCommit(session, commit);
			ValidateFeedback(commit, feedback);
			listenerState =
				FoxRoleState.AwaitingResultAcknowledgement.ToString();
			return true;
		}

		if (hook == GameHook.NightMainActionLoop &&
			pendingInstruction is ConfirmationInstruction
			{
				Semantic: ModeratorInstructionSemantic.PutRoleToSleep
			} &&
			HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			var commits = GetFoxCheckCommitsThisNight(session).ToArray();
			if (commits.Length > 1)
			{
				throw new InvalidOperationException(
					"The Fox sleep continuation has multiple committed checks.");
			}

			if (commits is [var commit])
			{
				ValidateOwnedCommit(session, commit);
			}

			listenerState = FoxRoleState.ReadyToSleep.ToString();
			return true;
		}

		return base.TryResolvePendingInstructionContinuation(
			hook,
			session,
			pendingInstruction,
			out listenerState);
	}

	bool ITargetPrivateRolePowerRecoveryCapability
		.TryValidateCommittedRecoveryBoundary(
			GameSession session,
			ModeratorInstruction? startingInstruction,
			ModeratorResponse input,
			TargetPrivateRolePowerRecoveryBoundary committedBoundary,
			ModeratorInstruction nextInstruction)
	{
		if (committedBoundary.ActionType != NightActionType.FoxCheck)
		{
			return false;
		}

		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			return TryValidateCommittedRecoveryBoundary(
				session,
				startingInstruction,
				input,
				committedBoundary,
				nextInstruction);
		}

		var commit = GetBorrowedFoxCheckCommit(session, execution);
		ValidateBorrowedRecoveryBoundary(
			session,
			execution,
			commit,
			committedBoundary);
		if (startingInstruction is not SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.SelectFoxCenter
			} centerSelection ||
			input.InstructionId != centerSelection.InstructionId ||
			input.Type != ExpectedInputType.PlayerSelection ||
			input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds ||
			selectedPlayerIds.Single() != commit.CenterPlayerId ||
			!centerSelection.SelectablePlayerIds.Contains(
				commit.CenterPlayerId) ||
			nextInstruction is not ConfirmationInstruction
			{
				Semantic: ModeratorInstructionSemantic.RevealFoxResult
			} feedback)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox commit does not match its accepted center and feedback continuation.");
		}

		ValidateBorrowedCenterSelectionInstruction(
			session,
			execution,
			centerSelection);
		ValidateBorrowedFeedback(execution, commit, feedback);
		return true;
	}

	private static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		TargetPrivateRolePowerRecoveryBoundary committedBoundary,
		ModeratorInstruction nextInstruction)
	{
		if (committedBoundary.ActionType != NightActionType.FoxCheck)
		{
			return false;
		}

		ValidateOwnedRecoveryBoundary(session, committedBoundary);
		if (startingInstruction is not SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.SelectFoxCenter,
				CountConstraint: var countConstraint,
				AffectedPlayerIds: { Count: 1 } affectedPlayerIds,
				RoleIdentification: null
			} centerSelection ||
			countConstraint != NumberRangeConstraint.SingleOptional ||
			input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds ||
			!centerSelection.SelectablePlayerIds.Contains(
				selectedPlayerIds.Single()) ||
			selectedPlayerIds.Single() == Guid.Empty ||
			committedBoundary.ActingPlayerId != affectedPlayerIds.Single() ||
			nextInstruction is not ConfirmationInstruction
			{
				Semantic: ModeratorInstructionSemantic.RevealFoxResult
			} feedback)
		{
			throw new InvalidOperationException(
				"The Fox target-private commit does not match its accepted check and feedback continuation.");
		}

		ValidateFeedback(committedBoundary, feedback);
		return true;
	}

	void ITargetPrivateRolePowerRecoveryCapability
		.ValidateRecoveryCursorIdentity(
			GameSession session,
			DomainRecoveryCursor cursor)
	{
		if (TryResolveBorrowedExecution(session, out var execution))
		{
			ValidateBorrowedRecoveryCursorIdentity(
				session,
				execution,
				cursor);
			return;
		}

		ValidateTargetPrivateRecoveryCursorIdentity(session, cursor);
	}

	private static void ValidateTargetPrivateRecoveryCursorIdentity(
		GameSession session,
		DomainRecoveryCursor cursor)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(cursor);
		if (cursor.Kind !=
				DomainRecoveryCursorKind.TargetPrivateRolePowerCommit ||
			cursor.SourceRole != MainRoleType.Fox ||
			cursor.CommittedActionType != NightActionType.FoxCheck ||
			cursor.ActingPlayerId == Guid.Empty ||
			!StringComparer.Ordinal.Equals(
				cursor.SourcePowerIdentifier,
				NeighborhoodCheckPower.Identifier.Value) ||
			cursor.PowerIdentity != CreatePowerIdentity(
				session.GetPlayer(cursor.ActingPlayerId),
				CreatePowerInstance(
					session,
					session.GetPlayer(cursor.ActingPlayerId))) ||
			cursor.OneUseResourceId != Guid.Empty &&
			cursor.OneUseResourceId != NeighborhoodCheckResourceId ||
			cursor.CommittedTargetIds.Count != 0 ||
			cursor.NextInstructionSemantic !=
				ModeratorInstructionSemantic.RevealFoxResult)
		{
			throw new InvalidOperationException(
				"The Fox recovery cursor has an invalid target-private Role Power identity.");
		}
	}

	private HookListenerActionResult BeginNightAction(
		GameSession session,
		ModeratorResponse input)
	{
		_powerIsAvailable = null;
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			if (!EvaluateAvailability(session, borrowedExecution))
			{
				return HookListenerActionResult.Complete(FoxRoleState.Asleep);
			}

			return PrepareWakeInstruction(
				borrowedExecution,
				FoxRoleState.Awake);
		}

		if (!GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
				session,
				MainRoleType.Fox))
		{
			return base.HandleRoleWakeupAndId(session, input);
		}

		var execution = ResolveNativeExecution(session);
		if (!EvaluateAvailability(session, execution))
		{
			return HookListenerActionResult.Complete(FoxRoleState.Asleep);
		}

		return PrepareWakeInstruction(execution, FoxRoleState.Awake);
	}

	private HookListenerActionResult ContinueAfterWakeOrIdentification(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedExecution(session, out _))
		{
			return HandleNightPowerUse(session, input);
		}

		if (!GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
				session,
				MainRoleType.Fox))
		{
			ProcessRoleIdentification(session, input);
			var identifiedExecution = ResolveNativeExecution(session);
			if (!EvaluateAvailability(session, identifiedExecution))
			{
				return HookListenerActionResult.Complete(FoxRoleState.Asleep);
			}

			return PrepareWakeInstruction(
				identifiedExecution,
				FoxRoleState.AwaitingWakeAcknowledgement);
		}

		return HandleNightPowerUse(session, input);
	}

	private HookListenerActionResult PrepareWakeInstruction(
		ExecutionContext execution,
		FoxRoleState nextState) =>
		HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(
					execution.IsBorrowed
						? GameStrings.ActorRoleName
						: PublicName),
				affectedPlayerIds: [execution.ActingPlayer.Id]),
			nextState);

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = ResolveExecution(session);
		return HookListenerActionResult.NeedInput(
			new SelectPlayersInstruction(
				ModeratorInstructionSemantic.SelectFoxCenter,
				session.GetPlayers()
					.WithHealth(PlayerHealth.Alive)
					.ToIdSet(),
				NumberRangeConstraint.SingleOptional,
				publicAnnouncement: null,
				privateInstruction: GameStrings.FoxCenterSelectionInstruction,
				affectedPlayerIds: [execution.ActingPlayer.Id])
			{
				EmptySelectionOptionLabel = GameStrings.DeclineOption
			},
			FoxRoleState.AwaitingCenterSelection);
	}

	private HookListenerActionResult CommitCenterSelection(
		GameSession session,
		ModeratorResponse input)
	{
		if (input.SelectedPlayerIds is not { Count: <= 1 } selectedPlayerIds)
		{
			throw new InvalidOperationException(
				"The Fox may select at most one living Player.");
		}

		if (selectedPlayerIds.Count == 0)
		{
			return PrepareSleepInstruction(session);
		}

		var execution = ResolveExecution(session);

		var livingPlayers = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.ToArray();
		if (livingPlayers.Any(player =>
				player.State.GetFactionAgentKnowledge(Faction.Werewolf) ==
				FactionAgentKnowledge.Unknown))
		{
			throw new InvalidOperationException(
				"The current living Werewolf Faction Agent facts are incomplete.");
		}

		var center = session.GetPlayer(selectedPlayerIds.Single());
		if (center.State.Health != PlayerHealth.Alive)
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
					? "The borrowed Role Power response is invalid or no longer available."
					: "The Fox center selection is unavailable.");
		}

		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			center.Id);
		var checkedPlayerIds = new HashSet<Guid> { center.Id };
		if (neighbors.Clockwise is { } clockwise)
		{
			checkedPlayerIds.Add(clockwise.Id);
		}

		if (neighbors.Counterclockwise is { } counterclockwise)
		{
			checkedPlayerIds.Add(counterclockwise.Id);
		}

		var isAffirmative = checkedPlayerIds.Any(playerId =>
			session.GetFactionAgentKnowledge(playerId, Faction.Werewolf) ==
			FactionAgentKnowledge.KnownAgent);
		var powerIdentity = CreatePowerIdentity(execution);
		var spentResourceIdentity = isAffirmative
			? (OneUseRolePowerResourceIdentity?)null
			: CreateResourceIdentity(
				execution.ActingPlayer,
				execution.PowerInstance);
		if (execution.IsBorrowed)
		{
			session.CommitActorBorrowedFoxCheck(
				powerIdentity,
				center.Id,
				isAffirmative
					? FactionAgentKnowledge.KnownAgent
					: FactionAgentKnowledge.KnownNonAgent,
				spentResourceIdentity);
		}
		else
		{
			session.CommitTargetPrivateRolePowerNightAction(
				NightActionType.FoxCheck,
				powerIdentity,
				spentResourceIdentity);
		}

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.RevealFoxResult,
					privateInstruction: isAffirmative
						? GameStrings.FoxAffirmativeFeedbackInstruction
						: GameStrings.FoxNegativeFeedbackInstruction,
					affectedPlayerIds: [execution.ActingPlayer.Id]),
			FoxRoleState.AwaitingResultAcknowledgement);
	}

	private bool EvaluateAvailability(
		GameSession session,
		ExecutionContext execution)
	{
		if (_powerIsAvailable is { } knownAvailability)
		{
			return knownAvailability;
		}

		if (GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				session,
				CreateResourceIdentity(
					execution.ActingPlayer,
					execution.PowerInstance)))
		{
			_powerIsAvailable = false;
			return false;
		}

		var executionContext = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				execution.ActingPlayer,
				MainRoleType.Fox,
				NeighborhoodCheckPower,
				execution.PowerInstance,
				new OneUseRolePowerResource(
					NeighborhoodCheckResourceId,
					execution.PowerInstance)));
		_powerIsAvailable =
			executionContext.AvailabilityResult.IsAvailable;
		return _powerIsAvailable.Value;
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
			FoxRoleState.ReadyToSleep);
	}

	private ExecutionContext ResolveExecution(GameSession session) =>
		TryResolveBorrowedExecution(session, out var borrowed)
			? borrowed
			: ResolveNativeExecution(session);

	private ExecutionContext ResolveNativeExecution(GameSession session)
	{
		var fox = GetFox(session);
		return new ExecutionContext(
			fox,
			CreatePowerInstance(session, fox),
			IsBorrowed: false);
	}

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.Fox)
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
				MainRoleType.Fox,
				NeighborhoodCheckPower),
			IsBorrowed: true);
		return true;
	}

	private static RolePowerInstance CreatePowerInstance(
		GameSession session,
		IPlayer fox) =>
		RolePowerInstance.CreateCurrent(
			session,
			fox,
			MainRoleType.Fox,
			NeighborhoodCheckPower);

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		IPlayer fox,
		RolePowerInstance powerInstance) =>
		new(
			fox.Id,
			MainRoleType.Fox,
			NeighborhoodCheckPower.Identifier.Value,
			powerInstance.Id,
			powerInstance.Origin);

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		ExecutionContext execution) =>
		CreatePowerIdentity(
			execution.ActingPlayer,
			execution.PowerInstance);

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		IPlayer fox,
		RolePowerInstance powerInstance) =>
		new(
			fox.Id,
			MainRoleType.Fox,
			NeighborhoodCheckPower.Identifier.Value,
			powerInstance.Id,
			powerInstance.Origin,
			NeighborhoodCheckResourceId);

	private static IEnumerable<ActorBorrowedFoxCheckCommit>
		GetBorrowedFoxCheckCommitsThisNight(
			GameSession session,
			RolePowerInstanceIdentity powerIdentity) =>
		session.GetActorBorrowedFoxCheckCommits()
			.Where(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.TurnNumber == session.TurnNumber &&
				commit.CurrentPhase == GamePhase.Night);

	private static ActorBorrowedFoxCheckCommit GetBorrowedFoxCheckCommit(
		GameSession session,
		ExecutionContext execution)
	{
		var commits = GetBorrowedFoxCheckCommitsThisNight(
				session,
				CreatePowerIdentity(execution))
			.ToArray();
		if (commits is not [var commit])
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox continuation requires exactly one private commit.");
		}

		return commit;
	}

	private static void ValidateBorrowedRecoveryBoundary(
		GameSession session,
		ExecutionContext execution,
		ActorBorrowedFoxCheckCommit commit,
		TargetPrivateRolePowerRecoveryBoundary boundary)
	{
		ValidateBorrowedCommit(session, execution, commit);
		if (boundary.CurrentPhase != GamePhase.Night ||
			boundary.TurnNumber != session.TurnNumber ||
			boundary.ActionType != NightActionType.FoxCheck ||
			boundary.PowerIdentity != CreatePowerIdentity(execution) ||
			boundary.PowerIdentity != commit.PowerIdentity ||
			boundary.SpentResourceIdentity != commit.SpentResourceIdentity)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox target-private commit has an invalid Role Power identity.");
		}
	}

	private static void ValidateBorrowedRecoveryCursorIdentity(
		GameSession session,
		ExecutionContext execution,
		DomainRecoveryCursor cursor)
	{
		ArgumentNullException.ThrowIfNull(cursor);
		var commit = GetBorrowedFoxCheckCommit(session, execution);
		ValidateBorrowedCommit(session, execution, commit);
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		var expectedResourceId =
			commit.SpentResourceIdentity?.OneUseResourceId ?? Guid.Empty;
		if (cursor.Kind != DomainRecoveryCursorKind.TargetPrivateRolePowerCommit ||
			cursor.SourceRole != MainRoleType.Fox ||
			cursor.CommittedActionType != NightActionType.FoxCheck ||
			!StringComparer.Ordinal.Equals(
				cursor.SourcePowerIdentifier,
				NeighborhoodCheckPower.Identifier.Value) ||
			cursor.PowerIdentity != CreatePowerIdentity(execution) ||
			cursor.OneUseResourceId != expectedResourceId ||
			cursor.ActorSetupCardId != activation.SelectedCardId ||
			cursor.ActorBorrowedActivationId != activation.ActivationId ||
			cursor.CommittedTargetIds is not { Count: 1 } centerIds ||
			centerIds.Single() != commit.CenterPlayerId ||
			cursor.NextInstructionSemantic !=
				ModeratorInstructionSemantic.RevealFoxResult)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox recovery cursor has an invalid target-private Role Power identity.");
		}
	}

	private static void ValidateBorrowedCommit(
		GameSession session,
		ExecutionContext execution,
		ActorBorrowedFoxCheckCommit commit)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		var expectedPowerIdentity = CreatePowerIdentity(execution);
		var expectedSpentResource = commit.NeighborhoodAgentKnowledge ==
			FactionAgentKnowledge.KnownNonAgent
				? CreateResourceIdentity(
					execution.ActingPlayer,
					execution.PowerInstance)
				: (OneUseRolePowerResourceIdentity?)null;
		var publicMarker = commit.PublicMarkerLogIndex >= 0
			? session.GameHistoryLog.ElementAtOrDefault(
				commit.PublicMarkerLogIndex)
			: null;
		if (!execution.IsBorrowed ||
			activation?.SourceRole != MainRoleType.Fox ||
			activation.ActingPlayerId != execution.ActingPlayer.Id ||
			commit.PowerIdentity != expectedPowerIdentity ||
			commit.ActorSetupCardId != activation.SelectedCardId ||
			commit.CenterPlayerId == Guid.Empty ||
			commit.NeighborhoodAgentKnowledge is not
				(FactionAgentKnowledge.KnownAgent or
				 FactionAgentKnowledge.KnownNonAgent) ||
			commit.SpentResourceIdentity != expectedSpentResource ||
			commit.TurnNumber != session.TurnNumber ||
			commit.CurrentPhase != GamePhase.Night ||
			publicMarker is not ActorBorrowedRolePowerCommittedLogEntry marker ||
			marker.Timestamp != commit.Timestamp ||
			marker.TurnNumber != commit.TurnNumber ||
			marker.CurrentPhase != commit.CurrentPhase)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox recovery boundary requires one correlated private check and sanitized public marker.");
		}

		if (execution.ActingPlayer.State.Health != PlayerHealth.Alive ||
			execution.ActingPlayer.State.CurrentRole != MainRoleType.Actor ||
			session.GetPlayer(commit.CenterPlayerId).State.Health !=
				PlayerHealth.Alive)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox check does not belong to the living Actor or a living center.");
		}
	}

	private static void ValidateBorrowedCenterSelectionInstruction(
		GameSession session,
		ExecutionContext execution,
		SelectPlayersInstruction selection)
	{
		if (selection.CountConstraint != NumberRangeConstraint.SingleOptional ||
			selection.RoleIdentification is not null ||
			selection.PublicAnnouncement is not null ||
			selection.PrivateInstruction !=
				GameStrings.FoxCenterSelectionInstruction ||
			selection.EmptySelectionOptionLabel != GameStrings.DeclineOption ||
			selection.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id ||
			!selection.SelectablePlayerIds.ToHashSet().SetEquals(
				session.GetPlayers()
					.WithHealth(PlayerHealth.Alive)
					.ToIdSet()))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox center-selection instruction is invalid.");
		}
	}

	private static void ValidateBorrowedFeedback(
		ExecutionContext execution,
		ActorBorrowedFoxCheckCommit commit,
		ConfirmationInstruction feedback)
	{
		var expectedPrivateInstruction = commit.NeighborhoodAgentKnowledge ==
			FactionAgentKnowledge.KnownAgent
				? GameStrings.FoxAffirmativeFeedbackInstruction
				: GameStrings.FoxNegativeFeedbackInstruction;
		if (feedback.PublicAnnouncement is not null ||
			!StringComparer.Ordinal.Equals(
				feedback.PrivateInstruction,
				expectedPrivateInstruction) ||
			feedback.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox feedback does not match its private commit.");
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
			sleep.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Fox sleep instruction is invalid.");
		}
	}

	private static IEnumerable<TargetPrivateRolePowerCommittedLogEntry>
		GetFoxCheckCommitsThisNight(GameSession session) =>
		GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				[NightActionType.FoxCheck])
			.OfType<TargetPrivateRolePowerCommittedLogEntry>();

	private static void ValidateOwnedCommit(
		GameSession session,
		TargetPrivateRolePowerCommittedLogEntry commit)
	{
		if (commit.TargetIds is { Count: > 0 })
		{
			throw new InvalidOperationException(
				"The Fox target-private check commit has an invalid Role Power identity.");
		}

		ValidateOwnedRecoveryBoundary(
			session,
			TargetPrivateRolePowerRecoveryBoundary.FromCommittedEntry(commit));
	}

	private static void ValidateOwnedRecoveryBoundary(
		GameSession session,
		TargetPrivateRolePowerRecoveryBoundary boundary)
	{
		if (boundary.ActionType != NightActionType.FoxCheck ||
			boundary.SourceRole != MainRoleType.Fox ||
			!StringComparer.Ordinal.Equals(
				boundary.SourcePowerIdentifier,
				NeighborhoodCheckPower.Identifier.Value) ||
			boundary.PowerIdentity != CreatePowerIdentity(
				session.GetPlayer(boundary.ActingPlayerId),
				CreatePowerInstance(
					session,
					session.GetPlayer(boundary.ActingPlayerId))) ||
			boundary.CurrentPhase != GamePhase.Night ||
			boundary.TurnNumber != session.TurnNumber)
		{
			throw new InvalidOperationException(
				"The Fox target-private check commit has an invalid Role Power identity.");
		}

		var fox = session.GetPlayer(boundary.ActingPlayerId);
		if (fox.State.Health != PlayerHealth.Alive ||
			fox.State.CurrentRole != MainRoleType.Fox)
		{
			throw new InvalidOperationException(
				"The Fox target-private check commit does not belong to the living Role holder.");
		}

		if (boundary.SpentResourceIdentity is { } spentResource &&
			spentResource != CreateResourceIdentity(
				fox,
				CreatePowerInstance(session, fox)))
		{
			throw new InvalidOperationException(
				"The Fox target-private check commit has an invalid spent Resource.");
		}
	}

	private static void ValidateFeedback(
		TargetPrivateRolePowerCommittedLogEntry commit,
		ConfirmationInstruction feedback) =>
		ValidateFeedback(
			TargetPrivateRolePowerRecoveryBoundary.FromCommittedEntry(commit),
			feedback);

	private static void ValidateFeedback(
		TargetPrivateRolePowerRecoveryBoundary boundary,
		ConfirmationInstruction feedback)
	{
		var expectedPrivateInstruction =
			boundary.SpentResourceIdentity == null
				? GameStrings.FoxAffirmativeFeedbackInstruction
				: GameStrings.FoxNegativeFeedbackInstruction;
		if (feedback.PublicAnnouncement != null ||
			!StringComparer.Ordinal.Equals(
				feedback.PrivateInstruction,
				expectedPrivateInstruction) ||
			feedback.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != boundary.ActingPlayerId)
		{
			throw new InvalidOperationException(
				"The Fox feedback does not match its committed target-private check.");
		}
	}

	private IPlayer GetFox(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException(
			"No living Fox is available.");
}
