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

	internal static void ValidateRecurringRecoveryCursorIdentity(
		DomainRecoveryCursor cursor)
	{
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
		    cursor.PowerInstanceId != cursor.ActingPlayerId ||
		    cursor.PowerInstanceOrigin !=
		    RolePowerInstanceOrigin.Native ||
		    cursor.OneUseResourceId != Guid.Empty)
		{
			throw new InvalidOperationException(
				"The Defender recovery cursor has an invalid recurring Role Power identity.");
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

		var holder = GetHolder(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName),
				affectedPlayerIds: [holder.Id]),
			DefenderRoleState.Awake);
	}

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
		var holder = GetHolder(session);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				holder,
				MainRoleType.Defender,
				ProtectionPower,
				RolePowerInstance.CreateNative(
					holder,
					MainRoleType.Defender,
					ProtectionPower)));
		if (!availability.AvailabilityResult.IsAvailable)
		{
			return PrepareSleepInstruction(session);
		}

		var eligibleTargets = GetEligibleTargets(
			session,
			CreateNativePowerIdentity(holder));
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
				affectedPlayerIds: [holder.Id]),
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

		if (GetProtectionCommitsThisNight(session).Any())
		{
			throw new InvalidOperationException(
				"Only one Defender protection may be committed per Night.");
		}

		var holder = GetHolder(session);
		var powerIdentity = CreateNativePowerIdentity(holder);
		var targetId = selectedPlayerIds.Single();
		if (!GetEligibleTargets(session, powerIdentity).Contains(targetId))
		{
			throw new InvalidOperationException(
				"The Defender target must be one legal living Player.");
		}

		session.CommitRecurringRolePowerNightAction(
			NightActionType.DefenderProtect,
			targetId,
			powerIdentity);
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
			DefenderRoleState.ReadyToSleep);
	}

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

		var previousMatchingCommits = session.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Where(entry =>
				entry.CurrentPhase == GamePhase.Night &&
				entry.TurnNumber == session.TurnNumber - 1 &&
				entry.ActionType == NightActionType.DefenderProtect &&
				entry.PowerIdentity == powerIdentity)
			.ToArray();
		if (previousMatchingCommits.Length > 1)
		{
			throw new InvalidOperationException(
				"The immediately preceding Night contains multiple protections for one Defender power instance.");
		}

		if (previousMatchingCommits is [var previousCommit] &&
		    previousCommit.TargetIds is [var previousTargetId])
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
		    committedEntry.PowerInstanceId !=
			    committedEntry.ActingPlayerId ||
		    committedEntry.PowerInstanceOrigin !=
			    RolePowerInstanceOrigin.Native)
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

	private static RolePowerInstanceIdentity CreateNativePowerIdentity(
		IPlayer holder)
	{
		var powerInstance = RolePowerInstance.CreateNative(
			holder,
			MainRoleType.Defender,
			ProtectionPower);
		return new RolePowerInstanceIdentity(
			holder.Id,
			MainRoleType.Defender,
			ProtectionPower.Identifier.Value,
			powerInstance.Id,
			powerInstance.Origin);
	}
}
