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

internal enum WhiteWerewolfRoleState
{
	Awake,
	AwaitingTargetSelection,
	ReadyToSleep,
	Asleep
}

internal sealed class WhiteWerewolfRole
	: NightRoleHookListener<WhiteWerewolfRoleState>
{
	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	private static readonly RolePowerDefinition SoloAttackPower = new(
		new RolePowerIdentifier("white-werewolf-solo-attack"),
		RolePowerCategory.Chosen);

	internal static RolePowerIdentifier SoloAttackPowerIdentifier =>
		SoloAttackPower.Identifier;

	internal WhiteWerewolfRole(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.WhiteWerewolfRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.WhiteWerewolf);

	protected override WhiteWerewolfRoleState WokenUpStateEnum =>
		WhiteWerewolfRoleState.Awake;

	protected override WhiteWerewolfRoleState ReadyToSleepStateEnum =>
		WhiteWerewolfRoleState.ReadyToSleep;

	protected override WhiteWerewolfRoleState AsleepStateEnum =>
		WhiteWerewolfRoleState.Asleep;

	protected override bool HasNightPowers => true;

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			null,
			[
				WhiteWerewolfRoleState.Awake,
				WhiteWerewolfRoleState.Asleep
			],
			HandleRoleWakeupAndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			WhiteWerewolfRoleState.Awake,
			[
				WhiteWerewolfRoleState.AwaitingTargetSelection,
				WhiteWerewolfRoleState.ReadyToSleep,
				WhiteWerewolfRoleState.Asleep
			],
			HandleNightPowerUse_AndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			WhiteWerewolfRoleState.AwaitingTargetSelection,
			WhiteWerewolfRoleState.ReadyToSleep,
			CommitTargetSelection),
		CreateStage(
			GameHook.NightMainActionLoop,
			WhiteWerewolfRoleState.ReadyToSleep,
			WhiteWerewolfRoleState.Asleep,
			HandleAsleepConfirmation),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			WhiteWerewolfRoleState.Asleep,
			(_, _) => HookListenerActionResult.Complete(
				WhiteWerewolfRoleState.Asleep))
	];

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (GetCurrentListenerState(session) == null &&
		    session.TurnNumber > 1 &&
		    !IsSoloAttackNight(session.TurnNumber))
		{
			return HookListenerActionResult.Skip();
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
		    pendingInstruction is SelectPlayersInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.SelectWhiteWerewolfTarget
		    } &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			listenerState =
				WhiteWerewolfRoleState.AwaitingTargetSelection.ToString();
			return true;
		}

		if (hook != GameHook.NightMainActionLoop ||
		    pendingInstruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
			    AffectedPlayerIds: { Count: 1 } affectedPlayerIds
		    })
		{
			return base.TryResolvePendingInstructionContinuation(
				hook,
				session,
				pendingInstruction,
				out listenerState);
		}

		var holder = GetAliveRolePlayers(session)?.SingleOrDefault();
		if (holder == null || affectedPlayerIds.Single() != holder.Id)
		{
			return false;
		}

		var committedActions = GetAttackCommitsThisNight(session).ToArray();
		if (committedActions.Length > 1)
		{
			throw new InvalidOperationException(
				"The pending White Werewolf sleep instruction has multiple solo-attack commits.");
		}

		if (committedActions is [var committedAction])
		{
			ValidateCommittedAttack(session, committedAction);
			if (committedAction.ActingPlayerId != holder.Id)
			{
				throw new InvalidOperationException(
					"The White Werewolf attack commit does not belong to the instructed Role holder.");
			}
		}

		listenerState = WhiteWerewolfRoleState.ReadyToSleep.ToString();
		return true;
	}

	internal static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		RecurringRolePowerCommittedLogEntry committedEntry,
		ModeratorInstruction nextInstruction)
	{
		if (committedEntry.ActionType !=
		    NightActionType.WhiteWerewolfVictimSelection)
		{
			return false;
		}

		if (committedEntry.TargetIds is not [var committedTargetId] ||
		    startingInstruction is not SelectPlayersInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.SelectWhiteWerewolfTarget,
			    CountConstraint: var countConstraint,
			    AffectedPlayerIds: { Count: 1 } affectedPlayerIds,
			    RoleIdentification: null
		    } targetSelection ||
		    countConstraint != NumberRangeConstraint.SingleOptional ||
		    input.SelectedPlayerIds is not
			    { Count: 1 } selectedPlayerIds ||
		    selectedPlayerIds.Single() != committedTargetId ||
		    !targetSelection.SelectablePlayerIds.Contains(committedTargetId) ||
		    nextInstruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
			    AffectedPlayerIds: { Count: 1 } sleepAffectedPlayerIds
		    } ||
		    sleepAffectedPlayerIds.Single() != affectedPlayerIds.Single())
		{
			throw new InvalidOperationException(
				"The White Werewolf commit must correlate to its accepted target and exact sleep continuation.");
		}

		if (committedEntry.ActingPlayerId != affectedPlayerIds.Single())
		{
			throw new InvalidOperationException(
				"The White Werewolf commit does not belong to the instructed Role holder.");
		}

		ValidateCommittedAttack(session, committedEntry);
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
		    cursor.SourceRole != MainRoleType.WhiteWerewolf ||
		    cursor.CommittedActionType !=
		    NightActionType.WhiteWerewolfVictimSelection ||
		    cursor.ActingPlayerId == Guid.Empty ||
		    !StringComparer.Ordinal.Equals(
			    cursor.SourcePowerIdentifier,
			    SoloAttackPowerIdentifier.Value) ||
		    cursor.PowerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(cursor.ActingPlayerId)) ||
		    cursor.OneUseResourceId != Guid.Empty ||
		    cursor.NextInstructionSemantic !=
			    ModeratorInstructionSemantic.PutRoleToSleep)
		{
			throw new InvalidOperationException(
				"The White Werewolf recovery cursor has an invalid recurring Role Power identity.");
		}
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

		if (session.TurnNumber == 1)
		{
			return HookListenerActionResult.Complete(
				WhiteWerewolfRoleState.Asleep);
		}

		var holder = GetHolder(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName),
				affectedPlayerIds: [holder.Id]),
			WhiteWerewolfRoleState.Awake);
	}

	protected override HookListenerActionResult
		PrepareWakeupInstructionWithIdRequest(
			GameSession session)
	{
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		var selectablePlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.WhiteWerewolf ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == null ||
				  player.State.ModeratorKnownRole ==
				  MainRoleType.WhiteWerewolf)))
			.ToIdSet();
		if (roleCount <= 0 ||
		    GetCommittedLivingRoleHolderIds(session).Count > roleCount ||
		    selectablePlayerIds.Count < roleCount)
		{
			throw new InvalidOperationException(
				"Confirmed Role knowledge contradicts the required Living Role Holder count.");
		}

		return HookListenerActionResult.NeedInput(
			new SelectPlayersInstruction(
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				selectablePlayerIds,
				NumberRangeConstraint.Exact(roleCount),
				publicAnnouncement: null,
				privateInstruction:
					GameStrings.RoleSingleIdentificationPrompt.Format(
						PublicName),
				affectedPlayerIds: null,
				roleIdentification: MainRoleType.WhiteWerewolf),
			WhiteWerewolfRoleState.Awake);
	}

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		if (!IsSoloAttackNight(session.TurnNumber))
		{
			return HookListenerActionResult.Complete(
				WhiteWerewolfRoleState.Asleep);
		}

		var holder = GetHolder(session);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				holder,
				MainRoleType.WhiteWerewolf,
				SoloAttackPower,
				RolePowerInstance.CreateCurrent(
					session,
					holder,
					MainRoleType.WhiteWerewolf,
					SoloAttackPower)));
		if (!availability.AvailabilityResult.IsAvailable)
		{
			return PrepareSleepInstruction(session);
		}

		var eligibleTargets = GetEligibleTargets(session, holder.Id);
		if (eligibleTargets.Count == 0)
		{
			return PrepareSleepInstruction(session);
		}

		return HookListenerActionResult.NeedInput(
			new SelectPlayersInstruction(
				ModeratorInstructionSemantic.SelectWhiteWerewolfTarget,
				eligibleTargets,
				NumberRangeConstraint.SingleOptional,
				publicAnnouncement: null,
				privateInstruction:
					GameStrings.WhiteWerewolfTargetSelectionInstruction,
				affectedPlayerIds: [holder.Id])
			{
				EmptySelectionOptionLabel = GameStrings.DeclineOption
			},
			WhiteWerewolfRoleState.AwaitingTargetSelection);
	}

	protected override void ProcessRoleIdentification(
		GameSession session,
		ModeratorResponse input)
	{
		base.ProcessRoleIdentification(session, input);
		_ = InitialBeneficiaryClosureRules.TryCommitCurrentSession(session);
	}

	private HookListenerActionResult CommitTargetSelection(
		GameSession session,
		ModeratorResponse input)
	{
		if (input.SelectedPlayerIds is not { Count: <= 1 } selectedPlayerIds)
		{
			throw new InvalidOperationException(
				"The White Werewolf may select at most one Player.");
		}

		if (selectedPlayerIds.Count == 0)
		{
			return PrepareSleepInstruction(session);
		}

		if (GetAttackCommitsThisNight(session).Any())
		{
			throw new InvalidOperationException(
				"Only one White Werewolf attack may be committed per Night.");
		}

		var holder = GetHolder(session);
		var targetId = selectedPlayerIds.Single();
		if (!GetEligibleTargets(session, holder.Id).Contains(targetId))
		{
			throw new InvalidOperationException(
				"The White Werewolf target must be another living known Werewolf Faction Agent.");
		}

		var powerInstance = RolePowerInstance.CreateCurrent(
			session,
			holder,
			MainRoleType.WhiteWerewolf,
			SoloAttackPower);
		session.CommitRecurringRolePowerNightAction(
			NightActionType.WhiteWerewolfVictimSelection,
			targetId,
			new RolePowerInstanceIdentity(
				holder.Id,
				MainRoleType.WhiteWerewolf,
				SoloAttackPower.Identifier.Value,
				powerInstance.Id,
				powerInstance.Origin));
		return PrepareSleepInstruction(session);
	}

	protected override HookListenerActionResult PrepareSleepInstruction(
		GameSession session)
	{
		var holder = GetHolder(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(PublicName),
				affectedPlayerIds: [holder.Id]),
			WhiteWerewolfRoleState.ReadyToSleep);
	}

	private IPlayer GetHolder(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException(
			"No living White Werewolf is available.");

	private static HashSet<Guid> GetEligibleTargets(
		GameSession session,
		Guid holderId) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.Id != holderId &&
				player.State.GetFactionAgentKnowledge(Faction.Werewolf) ==
				FactionAgentKnowledge.KnownAgent)
			.ToIdSet();

	private static IEnumerable<RecurringRolePowerCommittedLogEntry>
		GetAttackCommitsThisNight(GameSession session) =>
		GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				[NightActionType.WhiteWerewolfVictimSelection])
			.OfType<RecurringRolePowerCommittedLogEntry>();

	private static bool IsSoloAttackNight(int turnNumber) =>
		turnNumber > 1 && turnNumber % 2 == 0;

	private static void ValidateCommittedAttack(
		GameSession session,
		RecurringRolePowerCommittedLogEntry committedEntry)
	{
		if (committedEntry.ActionType !=
			    NightActionType.WhiteWerewolfVictimSelection ||
		    committedEntry.TargetIds is not [var committedTargetId] ||
		    committedEntry.SourceRole != MainRoleType.WhiteWerewolf ||
		    !StringComparer.Ordinal.Equals(
			    committedEntry.SourcePowerIdentifier,
			    SoloAttackPowerIdentifier.Value) ||
		    committedEntry.ActingPlayerId == Guid.Empty ||
		    committedEntry.PowerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(committedEntry.ActingPlayerId)) ||
		    committedEntry.CurrentPhase != GamePhase.Night ||
		    committedEntry.TurnNumber != session.TurnNumber ||
		    !IsSoloAttackNight(committedEntry.TurnNumber))
		{
			throw new InvalidOperationException(
				"The White Werewolf recovery boundary requires one owned recurring solo-attack action.");
		}

		var holder = session.GetPlayer(committedEntry.ActingPlayerId);
		if (holder.State.Health != PlayerHealth.Alive ||
		    holder.State.MainRole != MainRoleType.WhiteWerewolf)
		{
			throw new InvalidOperationException(
				"The White Werewolf attack commit does not belong to the living Role holder.");
		}

		if (!GetEligibleTargets(session, holder.Id)
			    .Contains(committedTargetId))
		{
			throw new InvalidOperationException(
				"The White Werewolf attack target must be another living known Werewolf Faction Agent.");
		}
	}

	private static RolePowerInstanceIdentity CreateCurrentPowerIdentity(
		GameSession session,
		IPlayer holder) =>
		RolePowerInstance.CreateCurrentIdentity(
			session,
			holder,
			MainRoleType.WhiteWerewolf,
			SoloAttackPower);
}
