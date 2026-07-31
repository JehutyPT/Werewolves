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
			TargetPrivateRolePowerCommittedLogEntry committedEntry,
			ModeratorInstruction nextInstruction) =>
		TryValidateCommittedRecoveryBoundary(
			session,
			startingInstruction,
			input,
			committedEntry,
			nextInstruction);

	private static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		TargetPrivateRolePowerCommittedLogEntry committedEntry,
		ModeratorInstruction nextInstruction)
	{
		if (committedEntry.ActionType != NightActionType.FoxCheck)
		{
			return false;
		}

		ValidateOwnedCommit(session, committedEntry);
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
			committedEntry.ActingPlayerId != affectedPlayerIds.Single() ||
			nextInstruction is not ConfirmationInstruction
			{
				Semantic: ModeratorInstructionSemantic.RevealFoxResult
			} feedback)
		{
			throw new InvalidOperationException(
				"The Fox target-private commit does not match its accepted check and feedback continuation.");
		}

		ValidateFeedback(committedEntry, feedback);
		return true;
	}

	void ITargetPrivateRolePowerRecoveryCapability
		.ValidateRecoveryCursorIdentity(DomainRecoveryCursor cursor) =>
		ValidateTargetPrivateRecoveryCursorIdentity(cursor);

	private static void ValidateTargetPrivateRecoveryCursorIdentity(
		DomainRecoveryCursor cursor)
	{
		ArgumentNullException.ThrowIfNull(cursor);
		if (cursor.Kind !=
				DomainRecoveryCursorKind.TargetPrivateRolePowerCommit ||
			cursor.SourceRole != MainRoleType.Fox ||
			cursor.CommittedActionType != NightActionType.FoxCheck ||
			cursor.ActingPlayerId == Guid.Empty ||
			!StringComparer.Ordinal.Equals(
				cursor.SourcePowerIdentifier,
				NeighborhoodCheckPower.Identifier.Value) ||
			cursor.PowerInstanceId != cursor.ActingPlayerId ||
			cursor.PowerInstanceOrigin != RolePowerInstanceOrigin.Native ||
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
		if (!GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
				session,
				MainRoleType.Fox))
		{
			return base.HandleRoleWakeupAndId(session, input);
		}

		var fox = GetFox(session);
		if (!EvaluateAvailability(session, fox))
		{
			return HookListenerActionResult.Complete(FoxRoleState.Asleep);
		}

		return PrepareWakeInstruction(fox, FoxRoleState.Awake);
	}

	private HookListenerActionResult ContinueAfterWakeOrIdentification(
		GameSession session,
		ModeratorResponse input)
	{
		if (!GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
				session,
				MainRoleType.Fox))
		{
			ProcessRoleIdentification(session, input);
			var identifiedFox = GetFox(session);
			if (!EvaluateAvailability(session, identifiedFox))
			{
				return HookListenerActionResult.Complete(FoxRoleState.Asleep);
			}

			return PrepareWakeInstruction(
				identifiedFox,
				FoxRoleState.AwaitingWakeAcknowledgement);
		}

		return HandleNightPowerUse(session, input);
	}

	private HookListenerActionResult PrepareWakeInstruction(
		IPlayer fox,
		FoxRoleState nextState) =>
		HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName),
				affectedPlayerIds: [fox.Id]),
			nextState);

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		var fox = GetFox(session);
		return HookListenerActionResult.NeedInput(
			new SelectPlayersInstruction(
				ModeratorInstructionSemantic.SelectFoxCenter,
				session.GetPlayers()
					.WithHealth(PlayerHealth.Alive)
					.ToIdSet(),
				NumberRangeConstraint.SingleOptional,
				publicAnnouncement: null,
				privateInstruction: GameStrings.FoxCenterSelectionInstruction,
				affectedPlayerIds: [fox.Id])
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

		var fox = GetFox(session);
		if (selectedPlayerIds.Count == 0)
		{
			return PrepareSleepInstruction(session);
		}

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
				"The Fox center selection is unavailable.");
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
		var powerInstance = CreatePowerInstance(fox);
		var powerIdentity = CreatePowerIdentity(fox, powerInstance);
		var spentResourceIdentity = isAffirmative
			? (OneUseRolePowerResourceIdentity?)null
			: CreateResourceIdentity(fox, powerInstance);
		session.CommitTargetPrivateRolePowerNightAction(
			NightActionType.FoxCheck,
			powerIdentity,
			spentResourceIdentity);

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.RevealFoxResult,
				privateInstruction: isAffirmative
					? GameStrings.FoxAffirmativeFeedbackInstruction
					: GameStrings.FoxNegativeFeedbackInstruction,
				affectedPlayerIds: [fox.Id]),
			FoxRoleState.AwaitingResultAcknowledgement);
	}

	private bool EvaluateAvailability(GameSession session, IPlayer fox)
	{
		if (_powerIsAvailable is { } knownAvailability)
		{
			return knownAvailability;
		}

		var powerInstance = CreatePowerInstance(fox);
		if (GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				session,
				CreateResourceIdentity(fox, powerInstance)))
		{
			_powerIsAvailable = false;
			return false;
		}

		var executionContext = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				fox,
				MainRoleType.Fox,
				NeighborhoodCheckPower,
				powerInstance,
				new OneUseRolePowerResource(
					NeighborhoodCheckResourceId,
					powerInstance)));
		_powerIsAvailable =
			executionContext.AvailabilityResult.IsAvailable;
		return _powerIsAvailable.Value;
	}

	protected override HookListenerActionResult PrepareSleepInstruction(
		GameSession session)
	{
		var fox = GetFox(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(PublicName),
				affectedPlayerIds: [fox.Id]),
			FoxRoleState.ReadyToSleep);
	}

	private static RolePowerInstance CreatePowerInstance(IPlayer fox) =>
		RolePowerInstance.CreateNative(
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
		if (commit.ActionType != NightActionType.FoxCheck ||
			commit.TargetIds is { Count: > 0 } ||
			commit.SourceRole != MainRoleType.Fox ||
			!StringComparer.Ordinal.Equals(
				commit.SourcePowerIdentifier,
				NeighborhoodCheckPower.Identifier.Value) ||
			commit.PowerInstanceId != commit.ActingPlayerId ||
			commit.PowerInstanceOrigin != RolePowerInstanceOrigin.Native ||
			commit.CurrentPhase != GamePhase.Night ||
			commit.TurnNumber != session.TurnNumber)
		{
			throw new InvalidOperationException(
				"The Fox target-private check commit has an invalid Role Power identity.");
		}

		var fox = session.GetPlayer(commit.ActingPlayerId);
		if (fox.State.Health != PlayerHealth.Alive ||
			fox.State.CurrentRole != MainRoleType.Fox)
		{
			throw new InvalidOperationException(
				"The Fox target-private check commit does not belong to the living Role holder.");
		}

		if (commit.SpentResourceIdentity is { } spentResource &&
			spentResource != CreateResourceIdentity(
				fox,
				CreatePowerInstance(fox)))
		{
			throw new InvalidOperationException(
				"The Fox target-private check commit has an invalid spent Resource.");
		}
	}

	private static void ValidateFeedback(
		TargetPrivateRolePowerCommittedLogEntry commit,
		ConfirmationInstruction feedback)
	{
		var expectedPrivateInstruction =
			commit.SpentResourceIdentity == null
				? GameStrings.FoxAffirmativeFeedbackInstruction
				: GameStrings.FoxNegativeFeedbackInstruction;
		if (feedback.PublicAnnouncement != null ||
			!StringComparer.Ordinal.Equals(
				feedback.PrivateInstruction,
				expectedPrivateInstruction) ||
			feedback.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != commit.ActingPlayerId)
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
