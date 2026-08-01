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

internal enum PiperRoleState
{
	AwaitingIdentification,
	Awake,
	AwaitingTargetSelection,
	ReadyToSleep,
	AwaitingCharmedRecognition,
	Asleep
}

internal sealed class PiperRole
	: NightRoleHookListener<PiperRoleState>
{
	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	private static readonly RolePowerDefinition CharmPower = new(
		new RolePowerIdentifier("piper-charm"),
		RolePowerCategory.Chosen);

	internal static RolePowerIdentifier CharmPowerIdentifier =>
		CharmPower.Identifier;

	internal PiperRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.PiperRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Piper);

	protected override PiperRoleState WokenUpStateEnum => PiperRoleState.Awake;

	protected override PiperRoleState ReadyToSleepStateEnum =>
		PiperRoleState.ReadyToSleep;

	protected override PiperRoleState AsleepStateEnum => PiperRoleState.Asleep;

	protected override bool HasNightPowers => true;

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			null,
			[
				PiperRoleState.AwaitingIdentification,
				PiperRoleState.Awake,
				PiperRoleState.Asleep
			],
			BeginCall),
		CreateStage(
			GameHook.NightMainActionLoop,
			PiperRoleState.AwaitingIdentification,
			PiperRoleState.Awake,
			CommitIdentificationAndWake),
			CreateStage(
				GameHook.NightMainActionLoop,
				PiperRoleState.Awake,
				[
					PiperRoleState.AwaitingTargetSelection,
					PiperRoleState.ReadyToSleep
				],
				HandleNightPowerUse),
			CreateStage(
				GameHook.NightMainActionLoop,
				PiperRoleState.AwaitingTargetSelection,
				PiperRoleState.ReadyToSleep,
				CommitTargetSelection),
			CreateStage(
				GameHook.NightMainActionLoop,
				PiperRoleState.ReadyToSleep,
				[
					PiperRoleState.AwaitingCharmedRecognition,
					PiperRoleState.Asleep
				],
				HandlePiperSleepConfirmation),
			CreateStage(
				GameHook.NightMainActionLoop,
				PiperRoleState.AwaitingCharmedRecognition,
				PiperRoleState.Asleep,
				(_, _) => HookListenerActionResult.Complete(
					PiperRoleState.Asleep)),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			PiperRoleState.Asleep,
			(_, _) => HookListenerActionResult.Complete(PiperRoleState.Asleep))
	];

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.IdentifyRoleHolders,
			    RoleIdentification: MainRoleType.Piper
		    })
		{
			listenerState = PiperRoleState.AwaitingIdentification.ToString();
			return true;
		}

		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectPiperTargets
		    } &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			listenerState = PiperRoleState.AwaitingTargetSelection.ToString();
			return true;
		}

		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.PutRoleToSleep
		    } &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			var commits = GetCharmCommitsThisNight(session).ToArray();
			if (commits.Length > 1)
			{
				throw new InvalidOperationException(
					"The pending Piper sleep instruction has multiple charm commits.");
			}

			if (commits is [var commit])
			{
				ValidateCommittedCharm(session, commit);
			}

			listenerState = PiperRoleState.ReadyToSleep.ToString();
			return true;
		}

		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is ConfirmationInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.RecognizeCharmedPlayers
		    } &&
		    HasExpectedCharmedRoster(session, pendingInstruction))
		{
			listenerState =
				PiperRoleState.AwaitingCharmedRecognition.ToString();
			return true;
		}

		return base.TryResolvePendingInstructionContinuation(
			hook,
			session,
			pendingInstruction,
			out listenerState);
	}

	private HookListenerActionResult BeginCall(
		GameSession session,
		ModeratorResponse input)
	{
		if (GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			    session,
			    MainRoleType.Piper))
		{
			return PrepareWakeInstruction(session);
		}

		var selectablePlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.Piper ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == null ||
				  player.State.ModeratorKnownRole == MainRoleType.Piper)))
			.ToIdSet();
		if (GetExpectedLivingRoleHolderCount(session) != 1 ||
		    selectablePlayerIds.Count == 0)
		{
			throw new InvalidOperationException(
				"Piper identification requires exactly one possible living holder.");
		}

		return HookListenerActionResult.NeedInput(
			new SelectPlayersInstruction(
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				selectablePlayerIds,
				NumberRangeConstraint.Single,
				publicAnnouncement: null,
				privateInstruction:
					GameStrings.RoleSingleIdentificationPrompt.Format(
						PublicName),
				affectedPlayerIds: null,
				roleIdentification: MainRoleType.Piper),
			PiperRoleState.AwaitingIdentification);
	}

	private HookListenerActionResult CommitIdentificationAndWake(
		GameSession session,
		ModeratorResponse input)
	{
		ProcessRoleIdentification(session, input);
		return PrepareWakeInstruction(session);
	}

	protected override void ProcessRoleIdentification(
		GameSession session,
		ModeratorResponse input)
	{
		base.ProcessRoleIdentification(session, input);
		_ = InitialBeneficiaryClosureRules.TryCommitCurrentSession(session);
	}

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		var holder = GetHolder(session);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				holder,
				MainRoleType.Piper,
				CharmPower,
				RolePowerInstance.CreateCurrent(
					session,
					holder,
					MainRoleType.Piper,
					CharmPower)));
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
				ModeratorInstructionSemantic.SelectPiperTargets,
				selectablePlayerIds: eligibleTargets,
				countConstraint:
					NumberRangeConstraint.Exact(
						Math.Min(2, eligibleTargets.Count)),
				privateInstruction: GameStrings.PiperTargetSelectionInstruction,
				affectedPlayerIds: [holder.Id]),
			PiperRoleState.AwaitingTargetSelection);
	}

	private HookListenerActionResult CommitTargetSelection(
		GameSession session,
		ModeratorResponse input)
	{
		if (GetCharmCommitsThisNight(session).Any())
		{
			throw new InvalidOperationException(
				"Only one Piper charm action may be committed per Night.");
		}

		var holder = GetHolder(session);
		if (session.PendingModeratorInstruction is not SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectPiperTargets,
			    AffectedPlayerIds: { Count: 1 } affectedPlayerIds
		    } pendingSelection ||
		    pendingSelection.InstructionId != input.InstructionId ||
		    affectedPlayerIds.Single() != holder.Id)
		{
			throw new InvalidOperationException(
				"The Piper target selection no longer belongs to the instructed living holder.");
		}

		var eligibleTargets = GetEligibleTargets(session, holder.Id);
		var expectedCount = Math.Min(2, eligibleTargets.Count);
		if (input.SelectedPlayerIds is not { } selectedPlayerIds ||
		    selectedPlayerIds.Count != expectedCount ||
		    !selectedPlayerIds.IsSubsetOf(eligibleTargets))
		{
			throw new InvalidOperationException(
				"The Piper must select the exact required set of legal living Players.");
		}

		var powerIdentity = CreateCurrentPowerIdentity(session, holder);
		session.CommitRecurringRolePowerNightAction(
			NightActionType.PiperCharm,
			selectedPlayerIds,
			powerIdentity);
		foreach (var targetId in session.GetPlayers()
			         .Select(player => player.Id)
			         .Where(selectedPlayerIds.Contains))
		{
			session.ApplyStatusEffect(StatusEffectTypes.Charmed, targetId);
		}

		return PrepareSleepInstruction(session);
	}

	private HookListenerActionResult HandlePiperSleepConfirmation(
		GameSession session,
		ModeratorResponse input)
	{
		var livingCharmedPlayers = GetLivingCharmedPlayers(session);
		if (livingCharmedPlayers.Count == 0)
		{
			return HookListenerActionResult.Complete(PiperRoleState.Asleep);
		}

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.RecognizeCharmedPlayers,
				GameStrings.PiperCharmedRecognitionAnnouncement,
				GameStrings.PiperLivingCharmedRosterInstruction,
				livingCharmedPlayers
					.Select(player => player.Id)
					.ToArray()),
			PiperRoleState.AwaitingCharmedRecognition);
	}

	internal static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		RecurringRolePowerCommittedLogEntry committedEntry,
		ModeratorInstruction nextInstruction)
	{
		if (committedEntry.ActionType != NightActionType.PiperCharm)
		{
			return false;
		}

		if (committedEntry.TargetIds is not { Count: > 0 } committedTargetIds ||
		    startingInstruction is not SelectPlayersInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.SelectPiperTargets,
			    CountConstraint: var countConstraint,
			    AffectedPlayerIds: { Count: 1 } affectedPlayerIds,
			    RoleIdentification: null
		    } targetSelection ||
		    countConstraint !=
		    NumberRangeConstraint.Exact(committedTargetIds.Count) ||
		    input.SelectedPlayerIds is not { } selectedPlayerIds ||
		    !selectedPlayerIds.SetEquals(committedTargetIds) ||
		    !selectedPlayerIds.IsSubsetOf(targetSelection.SelectablePlayerIds) ||
		    nextInstruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
			    AffectedPlayerIds: { Count: 1 } sleepAffectedPlayerIds
		    } ||
		    sleepAffectedPlayerIds.Single() != affectedPlayerIds.Single() ||
		    committedEntry.ActingPlayerId != affectedPlayerIds.Single())
		{
			throw new InvalidOperationException(
				"The Piper commit must correlate to its accepted targets and exact sleep continuation.");
		}

		ValidateCommittedCharm(session, committedEntry);
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
		    cursor.SourceRole != MainRoleType.Piper ||
		    cursor.CommittedActionType != NightActionType.PiperCharm ||
		    cursor.ActingPlayerId == Guid.Empty ||
		    !StringComparer.Ordinal.Equals(
			    cursor.SourcePowerIdentifier,
			    CharmPowerIdentifier.Value) ||
		    cursor.PowerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(cursor.ActingPlayerId)) ||
		    cursor.OneUseResourceId != Guid.Empty)
		{
			throw new InvalidOperationException(
				"The Piper recovery cursor has an invalid recurring Role Power identity.");
		}
	}

	private HookListenerActionResult PrepareWakeInstruction(GameSession session)
	{
		var holder = GetHolder(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName),
				affectedPlayerIds: [holder.Id]),
			PiperRoleState.Awake);
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
			PiperRoleState.ReadyToSleep);
	}

	private IPlayer GetHolder(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException(
			"No living Piper is available.");

	private static RolePowerInstanceIdentity CreateCurrentPowerIdentity(
		GameSession session,
		IPlayer holder) =>
		RolePowerInstance.CreateCurrentIdentity(
			session,
			holder,
			MainRoleType.Piper,
			CharmPower);

	private static HashSet<Guid> GetEligibleTargets(
		GameSession session,
		Guid holderId) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.Id != holderId &&
				!player.State.HasStatusEffect(StatusEffectTypes.Charmed))
			.ToIdSet();

	private static IReadOnlyList<IPlayer> GetLivingCharmedPlayers(
		GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.WithStatusEffect(StatusEffectTypes.Charmed)
			.ToArray();

	private static bool HasExpectedCharmedRoster(
		GameSession session,
		ModeratorInstruction pendingInstruction) =>
		pendingInstruction.AffectedPlayerIds is { Count: > 0 }
			affectedPlayerIds &&
		affectedPlayerIds.ToHashSet().SetEquals(
			GetLivingCharmedPlayers(session).Select(player => player.Id));

	private static IEnumerable<RecurringRolePowerCommittedLogEntry>
		GetCharmCommitsThisNight(GameSession session) =>
		GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				[NightActionType.PiperCharm])
			.OfType<RecurringRolePowerCommittedLogEntry>();

	private static void ValidateCommittedCharm(
		GameSession session,
		RecurringRolePowerCommittedLogEntry committedEntry)
	{
		if (committedEntry.ActionType != NightActionType.PiperCharm ||
		    committedEntry.TargetIds is not { Count: 1 or 2 } targetIds ||
		    targetIds.Distinct().Count() != targetIds.Count ||
		    committedEntry.SourceRole != MainRoleType.Piper ||
		    !StringComparer.Ordinal.Equals(
			    committedEntry.SourcePowerIdentifier,
			    CharmPowerIdentifier.Value) ||
		    committedEntry.PowerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(committedEntry.ActingPlayerId)))
		{
			throw new InvalidOperationException(
				"The Piper recovery boundary requires one owned recurring charm action.");
		}

		var holder = session.GetPlayer(committedEntry.ActingPlayerId);
		if (holder.State.Health != PlayerHealth.Alive ||
		    holder.State.CurrentRole != MainRoleType.Piper ||
		    targetIds.Any(targetId =>
			    !session.GetPlayerState(targetId)
				    .HasStatusEffect(StatusEffectTypes.Charmed)))
		{
			throw new InvalidOperationException(
				"The Piper charm commit does not match the living holder and Charmed targets.");
		}
	}
}
