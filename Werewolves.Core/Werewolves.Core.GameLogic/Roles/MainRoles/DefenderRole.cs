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

internal enum DefenderRoleState
{
	Awake,
	AwaitingTargetSelection,
	ReadyToSleep,
	Asleep
}

internal sealed class DefenderRole
	: NightRoleHookListener<DefenderRoleState>
{
	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		bool IsBorrowed);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	private static readonly RolePowerDefinition ProtectionPower = new(
		new RolePowerIdentifier("defender-protection"),
		RolePowerCategory.Chosen);

	internal static RolePowerIdentifier ProtectionPowerIdentifier =>
		ProtectionPower.Identifier;

	internal DefenderRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.DefenderRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Defender);

	protected override DefenderRoleState WokenUpStateEnum =>
		DefenderRoleState.Awake;

	protected override DefenderRoleState ReadyToSleepStateEnum =>
		DefenderRoleState.ReadyToSleep;

	protected override DefenderRoleState AsleepStateEnum =>
		DefenderRoleState.Asleep;

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
				case ConfirmationInstruction
					{
						Semantic: ModeratorInstructionSemantic.WakeRole
					} wake:
					ValidateBorrowedWake(borrowedExecution, wake);
					listenerState = DefenderRoleState.Awake.ToString();
					return true;
				case SelectPlayersInstruction
					{
						Semantic:
							ModeratorInstructionSemantic.SelectDefenderTarget
					} selection:
					ValidateBorrowedSelectionInstruction(
						session,
						borrowedExecution,
						selection);
					listenerState =
						DefenderRoleState.AwaitingTargetSelection.ToString();
					return true;
				case ConfirmationInstruction
					{
						Semantic:
							ModeratorInstructionSemantic.PutRoleToSleep
					} sleep:
					ValidateBorrowedSleep(borrowedExecution, sleep);
					var commits = GetBorrowedProtectionCommitsThisNight(
							session,
							CreatePowerIdentity(borrowedExecution))
						.ToArray();
					if (commits.Length > 1)
					{
						throw new InvalidOperationException(
							"The pending Actor borrowed Defender sleep instruction has multiple private protection commits.");
					}

					if (commits is [var commit])
					{
						ValidateCommittedBorrowedProtection(
							session,
							borrowedExecution,
							commit);
					}

					listenerState = DefenderRoleState.ReadyToSleep.ToString();
					return true;
			}
		}

		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectDefenderTarget
		    } &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			listenerState =
				DefenderRoleState.AwaitingTargetSelection.ToString();
			return true;
		}

		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.PutRoleToSleep
		    } &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			var commits = GetProtectionCommitsThisNight(session).ToArray();
			if (commits.Length > 1)
			{
				throw new InvalidOperationException(
					"The pending Defender sleep instruction has multiple protection commits.");
			}

			if (commits is [var commit])
			{
				ValidateCommittedProtection(session, commit);
				if (pendingInstruction.AffectedPlayerIds is not
					    { Count: 1 } affectedPlayerIds ||
				    affectedPlayerIds.Single() != commit.ActingPlayerId)
				{
					throw new InvalidOperationException(
						"The pending Defender sleep instruction does not belong to the committed protection.");
				}
			}

			listenerState = DefenderRoleState.ReadyToSleep.ToString();
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
		RecurringRolePowerCommittedLogEntry committedEntry,
		ModeratorInstruction nextInstruction)
	{
		if (committedEntry.ActionType !=
		    NightActionType.DefenderProtect)
		{
			return false;
		}

		if (committedEntry.TargetIds is not [var committedTargetId] ||
		    startingInstruction is not SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectDefenderTarget,
			    CountConstraint: var countConstraint,
			    AffectedPlayerIds: { Count: 1 } affectedPlayerIds,
			    RoleIdentification: null
		    } targetSelection ||
		    countConstraint != NumberRangeConstraint.Single ||
		    input.SelectedPlayerIds is not
			    { Count: 1 } selectedPlayerIds ||
		    selectedPlayerIds.Single() != committedTargetId ||
		    !targetSelection.SelectablePlayerIds.Contains(
			    committedTargetId) ||
		    nextInstruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
			    AffectedPlayerIds: { Count: 1 } sleepAffectedPlayerIds
		    } ||
		    sleepAffectedPlayerIds.Single() !=
			    affectedPlayerIds.Single())
		{
			throw new InvalidOperationException(
				"The Defender commit must correlate to its accepted target and exact sleep continuation.");
		}

		ValidateCommittedProtection(session, committedEntry);
		if (committedEntry.ActingPlayerId !=
		    affectedPlayerIds.Single())
		{
			throw new InvalidOperationException(
				"The Defender commit does not belong to the instructed Role holder.");
		}

		return true;
	}

	internal static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		ActorBorrowedDefenderProtectionCommit committedProtection,
		ModeratorInstruction nextInstruction)
	{
		if (committedProtection.PowerIdentity.SourceRole !=
		    MainRoleType.Defender)
		{
			return false;
		}

		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			throw new InvalidOperationException(
				"No active Actor borrowed Defender Role Power is available for recovery.");
		}

		ValidateCommittedBorrowedProtection(
			session,
			execution,
			committedProtection);
		if (startingInstruction is not SelectPlayersInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.SelectDefenderTarget
		    } targetSelection ||
		    input.InstructionId != targetSelection.InstructionId ||
		    input.Type != ExpectedInputType.PlayerSelection ||
		    input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds ||
		    selectedPlayerIds.Single() != committedProtection.TargetPlayerId ||
		    !targetSelection.SelectablePlayerIds.Contains(
			    committedProtection.TargetPlayerId) ||
		    nextInstruction is not ConfirmationInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.PutRoleToSleep
		    } sleep)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Defender commit must correlate to its accepted target and exact Actor sleep continuation.");
		}

		ValidateBorrowedSelectionInstruction(
			session,
			execution,
			targetSelection);
		ValidateBorrowedSleep(execution, sleep);
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
		    cursor.SourceRole != MainRoleType.Defender ||
		    cursor.CommittedActionType !=
		    NightActionType.DefenderProtect ||
		    cursor.ActingPlayerId == Guid.Empty ||
		    !StringComparer.Ordinal.Equals(
			    cursor.SourcePowerIdentifier,
			    ProtectionPowerIdentifier.Value) ||
		    cursor.PowerIdentity is not { } powerIdentity ||
		    cursor.OneUseResourceId != Guid.Empty)
		{
			throw new InvalidOperationException(
				"The Defender recovery cursor has an invalid recurring Role Power identity.");
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
				"The Defender recovery cursor has an invalid recurring Role Power identity.");
		}
	}

	protected override HookListenerActionResult HandleRoleWakeupAndId(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			return HookListenerActionResult.NeedInput(
				new ConfirmationInstruction(
					ModeratorInstructionSemantic.WakeRole,
					GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName),
					affectedPlayerIds: [borrowedExecution.ActingPlayer.Id]),
				DefenderRoleState.Awake);
		}

		var result = base.HandleRoleWakeupAndId(session, input);
		if (result.Instruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.WakeRole
		    })
		{
			return result;
		}

		var holder = GetHolder(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName),
				affectedPlayerIds: [holder.Id]),
			DefenderRoleState.Awake);
	}

	protected override HookListenerActionResult HandleNightPowerUse_AndId(
		GameSession session,
		ModeratorResponse input) =>
		TryResolveBorrowedExecution(session, out _)
			? HandleNightPowerUse(session, input)
			: base.HandleNightPowerUse_AndId(session, input);

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			null,
			[DefenderRoleState.Awake, DefenderRoleState.Asleep],
			HandleRoleWakeupAndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			DefenderRoleState.Awake,
			[
				DefenderRoleState.AwaitingTargetSelection,
				DefenderRoleState.ReadyToSleep
			],
			HandleNightPowerUse_AndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			DefenderRoleState.AwaitingTargetSelection,
			DefenderRoleState.ReadyToSleep,
			CommitTargetSelection),
		CreateStage(
			GameHook.NightMainActionLoop,
			DefenderRoleState.ReadyToSleep,
			DefenderRoleState.Asleep,
			HandleAsleepConfirmation),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			DefenderRoleState.Asleep,
			(_, _) => HookListenerActionResult.Complete(
				DefenderRoleState.Asleep))
	];

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = ResolveExecution(session);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				execution.ActingPlayer,
				MainRoleType.Defender,
				ProtectionPower,
				execution.PowerInstance));
		if (!availability.AvailabilityResult.IsAvailable)
		{
			return PrepareSleepInstruction(session);
		}

		var eligibleTargets = GetEligibleTargets(
			session,
			CreatePowerIdentity(execution));
		if (eligibleTargets.Count == 0)
		{
			return PrepareSleepInstruction(session);
		}

		return HookListenerActionResult.NeedInput(
			new SelectPlayersInstruction(
				ModeratorInstructionSemantic.SelectDefenderTarget,
				selectablePlayerIds: eligibleTargets,
				countConstraint: NumberRangeConstraint.Single,
				privateInstruction:
					GameStrings.DefenderTargetSelectionInstruction,
				affectedPlayerIds: [execution.ActingPlayer.Id]),
			DefenderRoleState.AwaitingTargetSelection);
	}

	private HookListenerActionResult CommitTargetSelection(
		GameSession session,
		ModeratorResponse input)
	{
		if (input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds)
		{
			throw new InvalidOperationException(
				"The Defender must select exactly one Player.");
		}

		var execution = ResolveExecution(session);
		var powerIdentity = CreatePowerIdentity(execution);
		var hasCommittedProtection = execution.IsBorrowed
			? GetBorrowedProtectionCommitsThisNight(session, powerIdentity).Any()
			: GetProtectionCommitsThisNight(session).Any();
		if (hasCommittedProtection)
		{
			throw new InvalidOperationException(
				"Only one Defender protection may be committed per Night.");
		}

		var targetId = selectedPlayerIds.Single();
		if (!GetEligibleTargets(session, powerIdentity).Contains(targetId))
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
					? "The borrowed Role Power response is invalid or no longer available."
					: "The Defender target must be one legal living Player.");
		}

		if (execution.IsBorrowed)
		{
			session.CommitActorBorrowedDefenderProtection(
				powerIdentity,
				targetId);
		}
		else
		{
			session.CommitRecurringRolePowerNightAction(
				NightActionType.DefenderProtect,
				targetId,
				powerIdentity);
		}

		return PrepareSleepInstruction(session);
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
			DefenderRoleState.ReadyToSleep);
	}

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
				MainRoleType.Defender,
				ProtectionPower),
			IsBorrowed: false);
	}

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.Defender)
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
				MainRoleType.Defender,
				ProtectionPower),
			IsBorrowed: true);
		return true;
	}

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		ExecutionContext execution) => new(
			execution.ActingPlayer.Id,
			MainRoleType.Defender,
			ProtectionPower.Identifier.Value,
			execution.PowerInstance.Id,
			execution.PowerInstance.Origin);

	private IPlayer GetHolder(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException(
			"No living Defender is available.");

	private static HashSet<Guid> GetEligibleTargets(
		GameSession session,
		RolePowerInstanceIdentity powerIdentity)
	{
		var eligibleTargets = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole != MainRoleType.LittleGirl)
			.Select(player => player.Id)
			.ToHashSet();
		if (session.TurnNumber <= 1)
		{
			return eligibleTargets;
		}

		var previousMatchingTargetIds =
			powerIdentity.PowerInstanceOrigin ==
			RolePowerInstanceOrigin.Borrowed
				? session.GetActorBorrowedDefenderProtectionCommits()
					.Where(commit =>
						commit.CurrentPhase == GamePhase.Night &&
						commit.TurnNumber == session.TurnNumber - 1 &&
						commit.PowerIdentity == powerIdentity)
					.Select(commit => commit.TargetPlayerId)
					.ToArray()
				: session.GameHistoryLog
					.OfType<RecurringRolePowerCommittedLogEntry>()
					.Where(entry =>
						entry.CurrentPhase == GamePhase.Night &&
						entry.TurnNumber == session.TurnNumber - 1 &&
						entry.ActionType == NightActionType.DefenderProtect &&
						entry.PowerIdentity == powerIdentity)
					.SelectMany(entry => entry.TargetIds ?? [])
					.ToArray();
		if (previousMatchingTargetIds.Length > 1)
		{
			throw new InvalidOperationException(
				"The immediately preceding Night contains multiple protections for one Defender power instance.");
		}

		if (previousMatchingTargetIds is [var previousTargetId])
		{
			eligibleTargets.Remove(previousTargetId);
		}

		return eligibleTargets;
	}

	private static IEnumerable<RecurringRolePowerCommittedLogEntry>
		GetProtectionCommitsThisNight(GameSession session) =>
		GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				[NightActionType.DefenderProtect])
			.OfType<RecurringRolePowerCommittedLogEntry>();

	private static IEnumerable<ActorBorrowedDefenderProtectionCommit>
		GetBorrowedProtectionCommitsThisNight(
			GameSession session,
			RolePowerInstanceIdentity powerIdentity) =>
		session.GetActorBorrowedDefenderProtectionCommits()
			.Where(commit =>
				commit.CurrentPhase == GamePhase.Night &&
				commit.TurnNumber == session.TurnNumber &&
				commit.PowerIdentity == powerIdentity);

	private static void ValidateBorrowedRecoveryCursorIdentity(
		GameSession session,
		DomainRecoveryCursor cursor,
		RolePowerInstanceIdentity cursorPowerIdentity)
	{
		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Defender recovery cursor has no active borrowed execution.");
		}

		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		var expectedPowerIdentity = CreatePowerIdentity(execution);
		var commits = GetBorrowedProtectionCommitsThisNight(
				session,
				expectedPowerIdentity)
			.ToArray();
		if (cursorPowerIdentity != expectedPowerIdentity ||
		    cursor.ActorSetupCardId != activation.SelectedCardId ||
		    cursor.ActorBorrowedActivationId != activation.ActivationId ||
		    cursor.CommittedTargetIds is not [var committedTargetId] ||
		    cursor.NextInstructionSemantic !=
		    ModeratorInstructionSemantic.PutRoleToSleep ||
		    cursor.NextInstructionId == Guid.Empty ||
		    commits is not [var commit] ||
		    commit.TargetPlayerId != committedTargetId)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Defender recovery cursor has an invalid borrowed Role Power identity.");
		}

		ValidateCommittedBorrowedProtection(session, execution, commit);
	}

	private static void ValidateCommittedBorrowedProtection(
		GameSession session,
		ExecutionContext execution,
		ActorBorrowedDefenderProtectionCommit committedProtection)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		var expectedPowerIdentity = CreatePowerIdentity(execution);
		var publicMarker = committedProtection.PublicMarkerLogIndex >= 0
			? session.GameHistoryLog.ElementAtOrDefault(
				committedProtection.PublicMarkerLogIndex)
			: null;
		if (!execution.IsBorrowed ||
		    activation?.SourceRole != MainRoleType.Defender ||
		    committedProtection.PowerIdentity != expectedPowerIdentity ||
		    committedProtection.ActorSetupCardId != activation.SelectedCardId ||
		    committedProtection.TargetPlayerId == Guid.Empty ||
		    committedProtection.TurnNumber != session.TurnNumber ||
		    committedProtection.CurrentPhase != GamePhase.Night ||
		    publicMarker is not ActorBorrowedRolePowerCommittedLogEntry marker ||
		    marker.Timestamp != committedProtection.Timestamp ||
		    marker.TurnNumber != committedProtection.TurnNumber ||
		    marker.CurrentPhase != committedProtection.CurrentPhase)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Defender recovery boundary requires one correlated private protection commit and sanitized public marker.");
		}

		var actor = session.GetPlayer(expectedPowerIdentity.ActingPlayerId);
		if (actor.State.Health != PlayerHealth.Alive ||
		    actor.State.CurrentRole != MainRoleType.Actor ||
		    !GetEligibleTargets(session, expectedPowerIdentity).Contains(
			    committedProtection.TargetPlayerId))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Defender protection does not belong to the living Actor or a legal target.");
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
				"The Actor borrowed Defender wake instruction is invalid.");
		}
	}

	private static void ValidateBorrowedSelectionInstruction(
		GameSession session,
		ExecutionContext execution,
		SelectPlayersInstruction selection)
	{
		if (selection.CountConstraint != NumberRangeConstraint.Single ||
		    selection.RoleIdentification is not null ||
		    selection.PublicAnnouncement is not null ||
		    selection.PrivateInstruction !=
		    GameStrings.DefenderTargetSelectionInstruction ||
		    selection.AffectedPlayerIds is not [var affectedPlayerId] ||
		    affectedPlayerId != execution.ActingPlayer.Id ||
		    !selection.SelectablePlayerIds.ToHashSet().SetEquals(
			    GetEligibleTargets(session, CreatePowerIdentity(execution))))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Defender target instruction is invalid.");
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
				"The Actor borrowed Defender sleep instruction is invalid.");
		}
	}

	private static void ValidateCommittedProtection(
		GameSession session,
		RecurringRolePowerCommittedLogEntry committedEntry)
	{
		if (committedEntry.ActionType !=
			    NightActionType.DefenderProtect ||
		    committedEntry.TargetIds is not [var targetId] ||
		    committedEntry.SourceRole != MainRoleType.Defender ||
		    !StringComparer.Ordinal.Equals(
			    committedEntry.SourcePowerIdentifier,
			    ProtectionPowerIdentifier.Value) ||
		    committedEntry.PowerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(committedEntry.ActingPlayerId)))
		{
			throw new InvalidOperationException(
				"The Defender recovery boundary requires one owned recurring protection action.");
		}

		var holder = session.GetPlayer(committedEntry.ActingPlayerId);
		if (holder.State.Health != PlayerHealth.Alive ||
		    holder.State.CurrentRole != MainRoleType.Defender ||
		    !GetEligibleTargets(
			    session,
			    committedEntry.PowerIdentity).Contains(targetId))
		{
			throw new InvalidOperationException(
				"The Defender protection does not belong to the living current holder or a legal target.");
		}
	}

	private static RolePowerInstanceIdentity CreateCurrentPowerIdentity(
		GameSession session,
		IPlayer holder) =>
		RolePowerInstance.CreateCurrentIdentity(
			session,
			holder,
			MainRoleType.Defender,
			ProtectionPower);
}
