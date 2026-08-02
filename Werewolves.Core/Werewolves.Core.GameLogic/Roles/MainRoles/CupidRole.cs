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
		var pair = GameSessionQueries.GetCommittedLoversPair(session);
		if (pair is null)
		{
			return EliminationCascadeReactionResult.Complete();
		}

		pair.EnforceValidity();
		var eliminatedLovers = pair.PlayerIds
			.Where(eliminatedPlayerIds.Contains)
			.ToArray();
		if (eliminatedLovers.Length != 1)
		{
			return EliminationCascadeReactionResult.Complete();
		}

		var survivingLoverId = pair.PlayerIds
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
		ModeratorResponse input) =>
		GetCurrentListenerState(session) == null
			? base.Execute(session, input)
			: ExecuteCore(session, input);

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

	private HookListenerActionResult BeginCall(
		GameSession session,
		ModeratorResponse _)
	{
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
		var holder = GetHolder(session);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				holder,
				MainRoleType.Cupid,
				LinkLoversPower,
				RolePowerInstance.CreateCurrent(
					session,
					holder,
					MainRoleType.Cupid,
					LinkLoversPower)));
		if (!availability.AvailabilityResult.IsAvailable)
		{
			return PrepareSleepInstruction(session);
		}

		var livingPlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.ToIdSet();
		if (livingPlayerIds.Count < 2)
		{
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
				affectedPlayerIds: [holder.Id]),
			CupidRoleState.AwaitingTargetSelection);
	}

	private HookListenerActionResult PrepareWakeInstruction(GameSession session)
	{
		var holder = GetHolder(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName),
				affectedPlayerIds: [holder.Id]),
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
		    cursor.PowerIdentity != CreateCurrentPowerIdentity(
			    session,
			    session.GetPlayer(cursor.ActingPlayerId)) ||
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

	protected override HookListenerActionResult PrepareSleepInstruction(
		GameSession session)
	{
		var holder = GetHolder(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(PublicName),
				affectedPlayerIds: [holder.Id]),
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
