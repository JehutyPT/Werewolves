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

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum WitchRoleState
{
	Awake,
	AwaitingHealingSelection,
	AwaitingPoisonSelection,
	ReadyToSleep,
	Asleep
}

internal sealed class WitchRole : NightRoleHookListener<WitchRoleState>
{
	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		bool IsBorrowed);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private bool _attackRosterDisclosed;

	private static readonly RolePowerDefinition PotionsPower = new(
		new RolePowerIdentifier("witch-potions"),
		RolePowerCategory.Chosen);

	internal static readonly Guid HealingResourceId =
		Guid.Parse("a9b9d885-3edc-4671-bec8-1ddabbe4de3e");

	internal static readonly Guid PoisonResourceId =
		Guid.Parse("da29bd31-bbe8-4abc-bb12-87b15df6df38");

	internal WitchRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.WitchRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Witch);

	protected override WitchRoleState WokenUpStateEnum => WitchRoleState.Awake;

	protected override WitchRoleState ReadyToSleepStateEnum => WitchRoleState.ReadyToSleep;

	protected override WitchRoleState AsleepStateEnum => WitchRoleState.Asleep;

	protected override bool HasNightPowers => true;

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
				WitchRoleState.Awake);
		}

		var result = base.HandleRoleWakeupAndId(session, input);
		if (result.Instruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.WakeRole
		    })
		{
			return result;
		}

		var witch = GetWitch(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName),
				affectedPlayerIds: [witch.Id]),
			WitchRoleState.Awake);
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
					listenerState = WitchRoleState.Awake.ToString();
					return true;
				case SelectPlayersInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.SelectWitchHealingTarget
				} healingSelection:
					ValidateBorrowedHealingInstruction(
						session,
						borrowedExecution,
						healingSelection);
					listenerState =
						WitchRoleState.AwaitingHealingSelection.ToString();
					return true;
				case SelectPlayersInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.SelectWitchPoisonTarget
				} poisonSelection:
					var (healedTargetId, poisonedTargetId) =
						ValidateBorrowedPotionUseCommits(
							session,
							borrowedExecution);
					if (poisonedTargetId.HasValue)
					{
						throw new InvalidOperationException(
							"The Actor borrowed Witch poison continuation cannot follow an already committed poison use.");
					}

					ValidateBorrowedPoisonInstruction(
						session,
						borrowedExecution,
						poisonSelection,
						healedTargetId);
					listenerState =
						WitchRoleState.AwaitingPoisonSelection.ToString();
					return true;
				case ConfirmationInstruction
				{
					Semantic: ModeratorInstructionSemantic.PutRoleToSleep
				} sleep:
					ValidateBorrowedPotionUseCommits(
						session,
						borrowedExecution);
					ValidateBorrowedSleep(
						session,
						borrowedExecution,
						sleep);
					listenerState = WitchRoleState.ReadyToSleep.ToString();
					return true;
			}
		}

		if (hook == GameHook.NightMainActionLoop &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			switch (pendingInstruction)
			{
				case SelectPlayersInstruction
				{
					Semantic:
						ModeratorInstructionSemantic
							.SelectWitchHealingTarget
				}:
					listenerState =
						WitchRoleState.AwaitingHealingSelection.ToString();
					return true;
				case SelectPlayersInstruction
				{
					Semantic:
						ModeratorInstructionSemantic
							.SelectWitchPoisonTarget
				}:
					listenerState =
						WitchRoleState.AwaitingPoisonSelection.ToString();
					return true;
				case ConfirmationInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.PutRoleToSleep
				}:
					listenerState = WitchRoleState.ReadyToSleep.ToString();
					return true;
			}
		}

		return base.TryResolvePendingInstructionContinuation(
			hook,
			session,
			pendingInstruction,
			out listenerState);
	}

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		var hasBorrowedExecution =
			TryResolveBorrowedExecution(session, out var execution);
		if (GetCurrentListenerState(session) == null)
		{
			_attackRosterDisclosed = false;
			if (!hasBorrowedExecution &&
			    GetAliveRolePlayers(session)?.SingleOrDefault() is { } witch)
			{
				execution = new ExecutionContext(
					witch,
					CreatePowerInstance(session, witch),
					IsBorrowed: false);
			}

			if (execution is not null)
			{
				if (IsSpent(
					    session,
					    CreateResourceIdentity(
						    execution.ActingPlayer,
						    execution.PowerInstance,
						    HealingResourceId)) &&
				    IsSpent(
					    session,
					    CreateResourceIdentity(
						    execution.ActingPlayer,
						    execution.PowerInstance,
						    PoisonResourceId)))
				{
					return HookListenerActionResult.Skip();
				}
			}
		}

		if (hasBorrowedExecution)
		{
			return ExecuteCore(session, input);
		}

		return base.Execute(session, input);
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
			[WitchRoleState.Awake, WitchRoleState.Asleep],
			HandleRoleWakeupAndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			WitchRoleState.Awake,
			[
				WitchRoleState.AwaitingHealingSelection,
				WitchRoleState.AwaitingPoisonSelection,
				WitchRoleState.ReadyToSleep
			],
			HandleNightPowerUse_AndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			WitchRoleState.AwaitingHealingSelection,
			[
				WitchRoleState.AwaitingPoisonSelection,
				WitchRoleState.ReadyToSleep
			],
			HandleHealingSelection),
		CreateStage(
			GameHook.NightMainActionLoop,
			WitchRoleState.AwaitingPoisonSelection,
			WitchRoleState.ReadyToSleep,
			HandlePoisonSelection),
		CreateStage(
			GameHook.NightMainActionLoop,
			WitchRoleState.ReadyToSleep,
			WitchRoleState.Asleep,
			HandleAsleepConfirmation),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			WitchRoleState.Asleep,
			(_, _) => HookListenerActionResult.Complete(WitchRoleState.Asleep))
	];

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = ResolveExecution(session);
		var attackTargets = GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		if (attackTargets.Count > 0 &&
		    TryEvaluateAvailableResource(
			    session,
			    execution.ActingPlayer,
			    execution.PowerInstance,
			    HealingResourceId))
		{
			_attackRosterDisclosed = true;
			return HookListenerActionResult.NeedInput(
				CreateHealingInstruction(execution.ActingPlayer, attackTargets),
				WitchRoleState.AwaitingHealingSelection);
		}

		return PreparePoisonOrSleep(
			session,
			execution,
			healedTargetId: null);
	}

	private HookListenerActionResult HandleHealingSelection(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = ResolveExecution(session);
		var selectedPlayerIds = input.SelectedPlayerIds
			?? throw new InvalidOperationException(
				execution.IsBorrowed
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "Witch healing requires a Player selection response.");
		if (selectedPlayerIds.Count > 1)
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "The Witch may heal at most one attacked Player.");
		}

		var targetId = selectedPlayerIds.SingleOrDefault();
		if (execution.IsBorrowed &&
		    selectedPlayerIds.Count == 1 &&
		    !GameSessionQueries.GetPhysicalAttackTargetsThisNight(session)
			    .Any(player => player.Id == targetId))
		{
			throw new InvalidOperationException(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
		}

		var nextInstruction = PreparePoisonOrSleep(
			session,
			execution,
			selectedPlayerIds.Count == 0 ? null : targetId);
		if (selectedPlayerIds.Count > 0)
		{
			CommitPotion(
				session,
				execution,
				HealingResourceId,
				NightActionType.WitchSave,
				targetId);
		}
		else if (execution.IsBorrowed)
		{
			CommitPotionDecline(
				session,
				execution,
				HealingResourceId);
		}

		return nextInstruction;
	}

	private HookListenerActionResult HandlePoisonSelection(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = ResolveExecution(session);
		var selectedPlayerIds = input.SelectedPlayerIds
			?? throw new InvalidOperationException(
				execution.IsBorrowed
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "Witch poison requires a Player selection response.");
		if (selectedPlayerIds.Count > 1)
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
					? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "The Witch may poison at most one living Player.");
		}

		if (execution.IsBorrowed && selectedPlayerIds.Count == 1)
		{
			var targetId = selectedPlayerIds.Single();
			var (healedTargetId, _) = ValidateBorrowedPotionUseCommits(
				session,
				execution);
			var target = session.GetPlayer(targetId);
			if (target.State.Health != PlayerHealth.Alive ||
			    targetId == execution.ActingPlayer.Id ||
			    targetId == healedTargetId)
			{
				throw new InvalidOperationException(
					GameStrings.ActorBorrowedRolePowerInvalidResponse);
			}
		}

		var nextInstruction = PrepareSleepInstruction(session);
		if (selectedPlayerIds.Count > 0)
		{
			CommitPotion(
				session,
				execution,
				PoisonResourceId,
				NightActionType.WitchKill,
				selectedPlayerIds.Single());
		}
		else if (execution.IsBorrowed)
		{
			CommitPotionDecline(
				session,
				execution,
				PoisonResourceId);
		}

		return nextInstruction;
	}

	private HookListenerActionResult PreparePoisonOrSleep(
		GameSession session,
		ExecutionContext execution,
		Guid? healedTargetId)
	{
		var poisonCandidates = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.Id != execution.ActingPlayer.Id &&
				player.Id != healedTargetId)
			.Select(player => player.Id)
			.ToHashSet();

		if (poisonCandidates.Count == 0 ||
		    !TryEvaluateAvailableResource(
			    session,
			    execution.ActingPlayer,
			    execution.PowerInstance,
			    PoisonResourceId))
		{
			return PrepareSleepInstruction(session);
		}

		var attackTargets = GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var attackRosterWasDisclosed =
			_attackRosterDisclosed ||
			session.PendingModeratorInstruction?.Semantic ==
				ModeratorInstructionSemantic.SelectWitchHealingTarget;
		var privateInstruction = attackRosterWasDisclosed || attackTargets.Count == 0
			? GameStrings.WitchPoisonSelectionInstruction
			: GameStrings.WitchAttackTargetsAndPoisonSelectionInstruction.Format(
				string.Join(", ", attackTargets.Select(player => player.Name)));
		_attackRosterDisclosed = true;

		return HookListenerActionResult.NeedInput(
			new SelectPlayersInstruction(
				ModeratorInstructionSemantic.SelectWitchPoisonTarget,
				poisonCandidates,
				NumberRangeConstraint.SingleOptional,
				privateInstruction: privateInstruction,
				affectedPlayerIds: [execution.ActingPlayer.Id])
			{
				EmptySelectionOptionLabel = GameStrings.DeclineOption
			},
			WitchRoleState.AwaitingPoisonSelection);
	}

	protected override HookListenerActionResult PrepareSleepInstruction(
		GameSession session)
	{
		var execution = ResolveExecution(session);
		var attackTargets = GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var healingIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			HealingResourceId);
		var poisonIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			PoisonResourceId);
		var wasAttackRosterDisclosed =
			_attackRosterDisclosed ||
			session.PendingModeratorInstruction?.Semantic is
				ModeratorInstructionSemantic.SelectWitchHealingTarget or
				ModeratorInstructionSemantic.SelectWitchPoisonTarget ||
			(execution.IsBorrowed
				? session.GetActorBorrowedWitchPotionUseCommits()
					.Any(commit =>
						commit.PowerIdentity == CreatePowerIdentity(execution) &&
						commit.TurnNumber == session.TurnNumber &&
						commit.CurrentPhase == GamePhase.Night &&
						(commit.SpentResourceIdentity == healingIdentity ||
						 commit.SpentResourceIdentity == poisonIdentity))
				: session.GameHistoryLog
					.OfType<OneUseRolePowerCommittedLogEntry>()
					.Any(entry =>
						entry.TurnNumber == session.TurnNumber &&
						entry.CurrentPhase == GamePhase.Night &&
						(entry.ResourceIdentity == healingIdentity ||
						 entry.ResourceIdentity == poisonIdentity)));
		var privateInstruction = !wasAttackRosterDisclosed && attackTargets.Count > 0
			? GameStrings.WitchAttackTargetsInstruction.Format(
				string.Join(", ", attackTargets.Select(player => player.Name)))
			: null;
		_attackRosterDisclosed = true;

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(
					execution.IsBorrowed
						? GameStrings.ActorRoleName
						: PublicName),
				privateInstruction,
				[execution.ActingPlayer.Id]),
			WitchRoleState.ReadyToSleep);
	}

	private bool TryEvaluateAvailableResource(
		GameSession session,
		IPlayer witch,
		RolePowerInstance instance,
		Guid resourceId)
	{
		var resourceIdentity = CreateResourceIdentity(
			witch,
			instance,
			resourceId);
		if (IsSpent(session, resourceIdentity))
		{
			return false;
		}

		var resource = new OneUseRolePowerResource(resourceId, instance);
		return _availabilityGateway.Evaluate(
				new RolePowerAttempt(
					session,
					witch,
					MainRoleType.Witch,
					PotionsPower,
					instance,
					resource))
			.AvailabilityResult.IsAvailable;
	}

	private static bool IsSpent(
		GameSession session,
		OneUseRolePowerResourceIdentity resourceIdentity) =>
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			session,
			resourceIdentity);

	private ExecutionContext ResolveExecution(GameSession session) =>
		TryResolveBorrowedExecution(session, out var borrowedExecution)
			? borrowedExecution
			: ResolveNativeExecution(session);

	private ExecutionContext ResolveNativeExecution(GameSession session)
	{
		var witch = GetWitch(session);
		return new ExecutionContext(
			witch,
			CreatePowerInstance(session, witch),
			IsBorrowed: false);
	}

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.Witch)
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
				MainRoleType.Witch,
				PotionsPower),
			IsBorrowed: true);
		return true;
	}

	private static RolePowerInstance CreatePowerInstance(
		GameSession session,
		IPlayer witch) =>
		RolePowerInstance.CreateCurrent(
			session,
			witch,
			MainRoleType.Witch,
			PotionsPower);

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		ExecutionContext execution) => new(
			execution.ActingPlayer.Id,
			MainRoleType.Witch,
			PotionsPower.Identifier.Value,
			execution.PowerInstance.Id,
			execution.PowerInstance.Origin);

	private static void ValidateBorrowedWake(
		ExecutionContext execution,
		ConfirmationInstruction wake)
	{
		if (wake.AffectedPlayerIds is not { Count: 1 } affectedPlayerIds ||
		    affectedPlayerIds.Single() != execution.ActingPlayer.Id ||
		    wake.PrivateInstruction is not null ||
		    wake.SoundEffects.Count != 0 ||
		    !StringComparer.Ordinal.Equals(
			    wake.PublicAnnouncement,
			    GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName)))
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Witch wake instruction is invalid.");
		}
	}

	private static void ValidateBorrowedHealingInstruction(
		GameSession session,
		ExecutionContext execution,
		SelectPlayersInstruction selection)
	{
		var (healedTargetId, poisonedTargetId) =
			ValidateBorrowedPotionUseCommits(session, execution);
		var attackTargets =
			GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var expectedTargets = attackTargets
			.Select(player => player.Id)
			.ToHashSet();
		var expectedPrivateInstruction =
			GameStrings.WitchHealingSelectionInstruction.Format(
				string.Join(", ", attackTargets.Select(player => player.Name)));
		if (healedTargetId.HasValue ||
		    poisonedTargetId.HasValue ||
		    selection.CountConstraint != NumberRangeConstraint.SingleOptional ||
		    selection.RoleIdentification.HasValue ||
		    selection.PublicAnnouncement is not null ||
		    !StringComparer.Ordinal.Equals(
			    selection.PrivateInstruction,
			    expectedPrivateInstruction) ||
		    !StringComparer.Ordinal.Equals(
			    selection.EmptySelectionOptionLabel,
			    GameStrings.DeclineOption) ||
		    selection.SoundEffects.Count != 0 ||
		    selection.AffectedPlayerIds is not { Count: 1 } affectedPlayerIds ||
		    affectedPlayerIds.Single() != execution.ActingPlayer.Id ||
		    !selection.SelectablePlayerIds.SetEquals(expectedTargets))
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Witch healing instruction is invalid.");
		}
	}

	private static void ValidateBorrowedPoisonInstruction(
		GameSession session,
		ExecutionContext execution,
		SelectPlayersInstruction selection,
		Guid? healedTargetId)
	{
		var expectedTargets = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.Id != execution.ActingPlayer.Id &&
				player.Id != healedTargetId)
			.Select(player => player.Id)
			.ToHashSet();
		var attackTargets =
			GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var attackRosterWasDisclosed = HasBorrowedPotionDecision(
			session,
			execution,
			HealingResourceId);
		var expectedPrivateInstruction =
			attackRosterWasDisclosed || attackTargets.Count == 0
				? GameStrings.WitchPoisonSelectionInstruction
				: GameStrings.WitchAttackTargetsAndPoisonSelectionInstruction.Format(
					string.Join(", ", attackTargets.Select(player => player.Name)));
		if (selection.CountConstraint != NumberRangeConstraint.SingleOptional ||
		    selection.RoleIdentification.HasValue ||
		    selection.PublicAnnouncement is not null ||
		    !StringComparer.Ordinal.Equals(
			    selection.PrivateInstruction,
			    expectedPrivateInstruction) ||
		    !StringComparer.Ordinal.Equals(
			    selection.EmptySelectionOptionLabel,
			    GameStrings.DeclineOption) ||
		    selection.SoundEffects.Count != 0 ||
		    selection.AffectedPlayerIds is not { Count: 1 } affectedPlayerIds ||
		    affectedPlayerIds.Single() != execution.ActingPlayer.Id ||
		    !selection.SelectablePlayerIds.SetEquals(expectedTargets))
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Witch poison instruction is invalid.");
		}
	}

	private static void ValidateBorrowedSleep(
		GameSession session,
		ExecutionContext execution,
		ConfirmationInstruction sleep)
	{
		var attackTargets =
			GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var attackRosterWasDisclosed = HasBorrowedPotionDecision(
			session,
			execution,
			HealingResourceId,
			PoisonResourceId);
		var expectedPrivateInstruction =
			!attackRosterWasDisclosed && attackTargets.Count > 0
				? GameStrings.WitchAttackTargetsInstruction.Format(
					string.Join(", ", attackTargets.Select(player => player.Name)))
				: null;
		if (sleep.AffectedPlayerIds is not { Count: 1 } affectedPlayerIds ||
		    affectedPlayerIds.Single() != execution.ActingPlayer.Id ||
		    !StringComparer.Ordinal.Equals(
			    sleep.PrivateInstruction,
			    expectedPrivateInstruction) ||
		    sleep.SoundEffects.Count != 0 ||
		    !StringComparer.Ordinal.Equals(
			    sleep.PublicAnnouncement,
			    GameStrings.RoleGoesToSleepSingle.Format(
				    GameStrings.ActorRoleName)))
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Witch sleep instruction is invalid.");
		}
	}

	private static bool HasBorrowedPotionDecision(
		GameSession session,
		ExecutionContext execution,
		params Guid[] resourceIds)
	{
		var powerIdentity = CreatePowerIdentity(execution);
		return session.GetActorBorrowedWitchPotionUseCommits().Any(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.TurnNumber == session.TurnNumber &&
				commit.CurrentPhase == GamePhase.Night &&
				resourceIds.Contains(
					commit.SpentResourceIdentity.OneUseResourceId)) ||
		       session.GetActorBorrowedWitchPotionDeclineCommits().Any(commit =>
				       commit.PowerIdentity == powerIdentity &&
				       commit.TurnNumber == session.TurnNumber &&
				       commit.CurrentPhase == GamePhase.Night &&
				       resourceIds.Contains(
					       commit.OfferedResourceIdentity.OneUseResourceId));
	}

	private static (Guid? HealedTargetId, Guid? PoisonedTargetId)
		ValidateBorrowedPotionUseCommits(
			GameSession session,
			ExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation()
			?? throw new InvalidOperationException(
				"No active Actor borrowed Witch Role Power is available.");
		var powerIdentity = CreatePowerIdentity(execution);
		var healingIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			HealingResourceId);
		var poisonIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			PoisonResourceId);
		var commits = session.GetActorBorrowedWitchPotionUseCommits()
			.Where(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.TurnNumber == session.TurnNumber &&
				commit.CurrentPhase == GamePhase.Night)
			.ToArray();
		Guid? healedTargetId = null;
		Guid? poisonedTargetId = null;
		foreach (var commit in commits)
		{
			if (commit.ActorSetupCardId != activation.SelectedCardId ||
			    commit.TargetPlayerId == Guid.Empty)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Witch potion commit has an invalid activation or target.");
			}

			if (commit.SpentResourceIdentity == healingIdentity)
			{
				if (healedTargetId.HasValue)
				{
					throw new InvalidOperationException(
						"The Actor borrowed Witch healing potion was committed more than once.");
				}

				healedTargetId = commit.TargetPlayerId;
			}
			else if (commit.SpentResourceIdentity == poisonIdentity)
			{
				if (poisonedTargetId.HasValue)
				{
					throw new InvalidOperationException(
						"The Actor borrowed Witch poison potion was committed more than once.");
				}

				poisonedTargetId = commit.TargetPlayerId;
			}
			else
			{
				throw new InvalidOperationException(
					"The Actor borrowed Witch potion commit has an invalid resource identity.");
			}
		}

		if (healedTargetId is { } healed &&
		    !GameSessionQueries.GetPhysicalAttackTargetsThisNight(session)
			    .Any(player => player.Id == healed))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Witch healing commit has an invalid target.");
		}

		if (poisonedTargetId is { } poisoned)
		{
			var target = session.GetPlayer(poisoned);
			if (target.State.Health != PlayerHealth.Alive ||
			    poisoned == execution.ActingPlayer.Id ||
			    poisoned == healedTargetId)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Witch poison commit has an invalid target.");
			}
		}

		return (healedTargetId, poisonedTargetId);
	}

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		IPlayer witch,
		RolePowerInstance instance,
		Guid resourceId) => new(
			witch.Id,
			MainRoleType.Witch,
			PotionsPower.Identifier.Value,
			instance.Id,
			instance.Origin,
			resourceId);

	private static void CommitPotion(
		GameSession session,
		ExecutionContext execution,
		Guid resourceId,
		NightActionType actionType,
		Guid targetId)
	{
		var resourceIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			resourceId);
		var powerIdentity = CreatePowerIdentity(execution);
		var isSpent = execution.IsBorrowed
			? session.GetActorBorrowedWitchPotionUseCommits().Any(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.SpentResourceIdentity == resourceIdentity)
			: IsSpent(session, resourceIdentity);
		if (isSpent)
		{
			throw new InvalidOperationException(
				execution.IsBorrowed
				? GameStrings.ActorBorrowedRolePowerInvalidResponse
					: "The selected Witch potion resource is already spent.");
		}

		if (execution.IsBorrowed)
		{
			session.CommitActorBorrowedWitchPotionUse(
				powerIdentity,
				resourceIdentity,
				targetId);
		}
		else
		{
			session.CommitOneUseRolePowerNightAction(
				actionType,
				targetId,
				resourceIdentity);
		}
	}

	private static void CommitPotionDecline(
		GameSession session,
		ExecutionContext execution,
		Guid resourceId)
	{
		var resourceIdentity = CreateResourceIdentity(
			execution.ActingPlayer,
			execution.PowerInstance,
			resourceId);
		session.CommitActorBorrowedWitchPotionDecline(
			CreatePowerIdentity(execution),
			resourceIdentity);
	}

	private static SelectPlayersInstruction CreateHealingInstruction(
		IPlayer witch,
		IReadOnlyList<IPlayer> attackTargets) =>
		new(
			ModeratorInstructionSemantic.SelectWitchHealingTarget,
			attackTargets.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.SingleOptional,
			privateInstruction: GameStrings.WitchHealingSelectionInstruction.Format(
				string.Join(", ", attackTargets.Select(player => player.Name))),
			affectedPlayerIds: [witch.Id])
		{
			EmptySelectionOptionLabel = GameStrings.DeclineOption
		};

	private IPlayer GetWitch(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException(
			"No alive Witch found for potion selection.");
}
