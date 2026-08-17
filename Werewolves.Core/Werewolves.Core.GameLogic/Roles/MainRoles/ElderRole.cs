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

internal enum ElderRoleState
{
	AwaitingSuppressionAnnouncement,
	Complete
}

internal readonly record struct ElderResistanceExecution(
	RolePowerInstanceIdentity PowerIdentity,
	bool IsBorrowed);

internal readonly record struct ElderSuppressionExecution(
	RolePowerInstanceIdentity PowerIdentity,
	bool IsBorrowed,
	int TriggeringVoteOutcomeLogIndex,
	string CascadeScopeId);

internal sealed class ElderRole : RoleHookListener<ElderRoleState>
{
	private static readonly RolePowerDefinition ResistancePower = new(
		new RolePowerIdentifier("elder-werewolf-attack-resistance"),
		RolePowerCategory.Reactive);

	private static readonly RolePowerDefinition SuppressionPower = new(
		new RolePowerIdentifier("elder-village-vote-suppression"),
		RolePowerCategory.Reactive);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	internal ElderRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.ElderRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Elder);

	internal bool IsResistanceAvailable(
		GameSession session,
		IPlayer elder) =>
		TryResolveResistance(
			session,
			elder,
			allowBorrowedActor: false,
			out _);

	internal bool TryResolveResistance(
		GameSession session,
		IPlayer attackedPlayer,
		bool allowBorrowedActor,
		out ElderResistanceExecution execution)
	{
		if (GameSessionQueries
		    .IsDevotedServantAcquiredRoleDormantForCurrentDay(
			    session,
			    attackedPlayer.Id,
			    session.Execution.CurrentPhase))
		{
			execution = default;
			return false;
		}

		RolePowerInstance powerInstance;
		var isBorrowed = false;
		if (attackedPlayer.State.MainRole == MainRoleType.Elder &&
			!attackedPlayer.State.HasStatusEffect(
				StatusEffectTypes.ElderProtectionLost))
		{
			powerInstance = RolePowerInstance.CreateCurrent(
				session,
				attackedPlayer,
				MainRoleType.Elder,
				ResistancePower);
		}
		else if (allowBorrowedActor &&
			attackedPlayer.State.CurrentRole == MainRoleType.Actor &&
			session.GetModeratorActiveActorBorrowedRolePowerActivation() is
			{
				ActingPlayerId: var actingPlayerId,
				SourceRole: MainRoleType.Elder
			} &&
			actingPlayerId == attackedPlayer.Id)
		{
			powerInstance = RolePowerInstance.CreateBorrowed(
				session,
				attackedPlayer,
				MainRoleType.Elder,
				ResistancePower);
			isBorrowed = true;
		}
		else
		{
			execution = default;
			return false;
		}

		var identity = new RolePowerInstanceIdentity(
			attackedPlayer.Id,
			MainRoleType.Elder,
			ResistancePower.Identifier.Value,
			powerInstance.Id,
			powerInstance.Origin);
		var latestBorrowedResistance = isBorrowed
			? session.GetActorBorrowedElderResistanceCommits()
				.Where(commit => commit.PowerIdentity == identity)
				.MaxBy(commit => commit.PublicMarkerLogIndex)
			: null;
		if (latestBorrowedResistance is
			{ RestoringWitchSaveLogIndex: null })
		{
			execution = default;
			return false;
		}

		if (!EvaluatePower(
				session,
				attackedPlayer,
				ResistancePower,
				powerInstance))
		{
			execution = default;
			return false;
		}

		execution = new ElderResistanceExecution(identity, isBorrowed);
		return true;
	}

	internal void CommitBorrowedResistance(
		GameSession session,
		ElderResistanceExecution execution,
		Guid targetPlayerId,
		int triggeringNightActionLogIndex,
		int? restoringWitchSaveLogIndex)
	{
		var exclusiveUpperLogIndex = session.GameHistoryLog.Count();
		if (!execution.IsBorrowed ||
			!GameSessionQueries.IsQualifyingActorBorrowedElderResistanceTrigger(
				session,
				triggeringNightActionLogIndex,
				exclusiveUpperLogIndex,
				session.TurnNumber,
				targetPlayerId) ||
			restoringWitchSaveLogIndex is { } restorationLogIndex &&
				!GameSessionQueries
					.IsQualifyingActorBorrowedElderResistanceRestoration(
						session,
						restorationLogIndex,
						session.TurnNumber,
						targetPlayerId))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Role Power commit is stale or invalid.");
		}

		session.CommitActorBorrowedElderResistance(
			execution.PowerIdentity,
			targetPlayerId,
			triggeringNightActionLogIndex,
			restoringWitchSaveLogIndex);
	}

	internal static void ValidateBorrowedResistanceRecovery(GameSession session)
	{
		foreach (var commit in session.GetActorBorrowedElderResistanceCommits())
		{
			if (!GameSessionQueries
					.IsQualifyingActorBorrowedElderResistanceTrigger(
						session,
						commit.TriggeringNightActionLogIndex,
						commit.PublicMarkerLogIndex,
						commit.TurnNumber,
						commit.TargetPlayerId) ||
				commit.RestoringWitchSaveLogIndex is { } restorationLogIndex &&
					!GameSessionQueries
						.IsQualifyingActorBorrowedElderResistanceRestoration(
							session,
							restorationLogIndex,
							commit.TurnNumber,
							commit.TargetPlayerId))
			{
				throw new InvalidOperationException(
					"The stable recovery snapshot has invalid Actor borrowed Role Power state.");
			}
		}
	}

	internal ModeratorInstruction CreateResistanceIdentificationInstruction(
		GameSession session)
	{
		var expectedHolderCount =
			GameSessionQueries.GetExpectedLivingRoleHolderCount(
				session,
				MainRoleType.Elder);
		var committedHolderIds = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.State.CurrentRole == MainRoleType.Elder)
			.Select(player => player.Id)
			.ToHashSet();
		var selectablePlayerIds = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				(player.State.CurrentRole == MainRoleType.Elder ||
				 (player.State.CurrentRole == null &&
				  player.State.ModeratorKnownRole is null or
					  MainRoleType.Elder)))
			.Select(player => player.Id)
			.ToHashSet();

		if (expectedHolderCount != 1 ||
		    committedHolderIds.Count > expectedHolderCount ||
		    selectablePlayerIds.Count < expectedHolderCount)
		{
			throw new InvalidOperationException(
				"Confirmed Role knowledge contradicts the required living Elder holder count.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			selectablePlayerIds,
			NumberRangeConstraint.SingleOptional,
			privateInstruction:
				GameStrings.RoleSingleIdentificationPrompt.Format(PublicName),
			roleIdentification: MainRoleType.Elder);
	}

	internal void RecordResistanceHolderIdentification(
		GameSession session,
		ModeratorResponse input)
	{
		if (input.SelectedPlayerIds is not { Count: <= 1 } selectedPlayerIds)
		{
			throw new InvalidOperationException(
				"Elder Role Identification requires zero or one living holder.");
		}

		var committedHolderIds = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.State.CurrentRole == MainRoleType.Elder)
			.Select(player => player.Id)
			.ToHashSet();
		if (!committedHolderIds.IsSubsetOf(selectedPlayerIds))
		{
			throw new InvalidOperationException(
				"Elder Role Identification cannot replace a committed living holder.");
		}

		session.IdentifyRole(selectedPlayerIds.ToHashSet(), MainRoleType.Elder);
	}

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		listenerState = string.Empty;
		var suppression =
			GameSessionQueries.GetVillagerRolePowerSuppression(session);
		if (hook != GameHook.OnVoteConcluded ||
		    suppression == null ||
		    GameSessionQueries
			    .IsVillagerRolePowerSuppressionAnnouncementAcknowledged(
				    session,
				    suppression.AnnouncementInstructionId) ||
		    !MatchesSuppressionAnnouncement(
			    pendingInstruction,
			    suppression.AnnouncementInstructionId))
		{
			return false;
		}

		listenerState =
			ElderRoleState.AwaitingSuppressionAnnouncement.ToString();
		return true;
	}

	internal static void
		ValidateBorrowedPendingSuppressionRecoveryInstruction(
			GameSession session)
	{
		var suppression =
			GameSessionQueries.GetVillagerRolePowerSuppression(session);
		if (suppression == null ||
			GameSessionQueries
				.IsVillagerRolePowerSuppressionAnnouncementAcknowledged(
					session,
					suppression.AnnouncementInstructionId))
		{
			return;
		}

		var borrowedCommits = session
			.GetActorBorrowedElderSuppressionCommits()
			.Where(commit =>
				commit.AnnouncementInstructionId ==
				suppression.AnnouncementInstructionId)
			.ToArray();
		if (borrowedCommits.Length == 0)
		{
			return;
		}

		if (borrowedCommits.Length != 1 ||
			!MatchesSuppressionAnnouncement(
				session.Execution.PendingInstruction,
				suppression.AnnouncementInstructionId))
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		}
	}

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (!session.Execution.TryGetActiveGameHook(out var hook) ||
		    hook != GameHook.OnVoteConcluded)
		{
			return HookListenerActionResult.Skip();
		}

		return ExecuteCore(session, input);
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.OnVoteConcluded,
			null,
			[
				ElderRoleState.AwaitingSuppressionAnnouncement,
				ElderRoleState.Complete
			],
			CommitSuppressionAndRequestAnnouncement),
		CreateStage(
			GameHook.OnVoteConcluded,
			ElderRoleState.AwaitingSuppressionAnnouncement,
			ElderRoleState.Complete,
			AcknowledgeSuppressionAnnouncement),
		CreateEndStage(
			GameHook.OnVoteConcluded,
			ElderRoleState.Complete,
			(_, _) => HookListenerActionResult.Complete(
				ElderRoleState.Complete))
	];

	private HookListenerActionResult CommitSuppressionAndRequestAnnouncement(
		GameSession session,
		ModeratorResponse input)
	{
		if (!TryResolveVillageVoteSuppression(session, out var execution))
		{
			return HookListenerActionResult.Complete(ElderRoleState.Complete);
		}

		if (GameSessionQueries.GetVillagerRolePowerSuppression(session) != null)
		{
			throw new InvalidOperationException(
				"Villager Role Power Suppression was already committed.");
		}

		var announcementInstructionId = Guid.NewGuid();
		if (execution.IsBorrowed)
		{
			session.CommitActorBorrowedElderSuppression(
				execution.PowerIdentity,
				execution.TriggeringVoteOutcomeLogIndex,
				execution.CascadeScopeId,
				announcementInstructionId);
		}
		session.CommitGameFact(context =>
			new VillagerRolePowerSuppressionCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				AnnouncementInstructionId = announcementInstructionId
			});

		return HookListenerActionResult.NeedInput(
			CreateSuppressionAnnouncement(announcementInstructionId),
			ElderRoleState.AwaitingSuppressionAnnouncement);
	}

	private static HookListenerActionResult
		AcknowledgeSuppressionAnnouncement(
		GameSession session,
		ModeratorResponse input)
	{
		var suppression =
			GameSessionQueries.GetVillagerRolePowerSuppression(session)
			?? throw new InvalidOperationException(
				"The Elder suppression announcement requires a committed suppression fact.");
		if (input.InstructionId != suppression.AnnouncementInstructionId)
		{
			throw new InvalidOperationException(
				"The Elder suppression announcement acknowledgment is stale or mismatched.");
		}
		if (GameSessionQueries
		    .IsVillagerRolePowerSuppressionAnnouncementAcknowledged(
			    session,
			    suppression.AnnouncementInstructionId))
		{
			throw new InvalidOperationException(
				"The Elder suppression announcement was already acknowledged.");
		}

		session.CommitGameFact(context =>
			new VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				AnnouncementInstructionId =
					suppression.AnnouncementInstructionId
			});
		return HookListenerActionResult.Complete(ElderRoleState.Complete);
	}

	private bool TryResolveVillageVoteSuppression(
		GameSession session,
		out ElderSuppressionExecution execution)
	{
		execution = default;
		if (GameSessionQueries.GetVillagerRolePowerSuppression(session) != null)
		{
			return false;
		}

		var vote = GameSessionQueries.GetCurrentDayVoteOutcome(session);
		if (vote is not { PlayerId: var targetId } || targetId == Guid.Empty)
		{
			return false;
		}

		var scopeId =
			$"Day:{session.TurnNumber}:Vote:{vote.Value.VoteOrdinal}";
		if (!GameSessionQueries.IsEliminationCascadeComplete(session, scopeId) ||
		    !session.GameHistoryLog
			    .Skip(vote.Value.LogIndex + 1)
			    .OfType<PlayerEliminatedLogEntry>()
			    .Any(entry =>
				    entry.PlayerId == targetId &&
				    entry.Reason == EliminationReason.DayVote))
		{
			return false;
		}

		var actingPlayer = session.GetPlayer(targetId);
		if (GameSessionQueries
			.IsDevotedServantAcquiredRoleDormantForCurrentDay(
				session,
				actingPlayer.Id,
				session.Execution.CurrentPhase))
		{
			return false;
		}

		RolePowerInstance powerInstance;
		var isBorrowed = false;
		if (actingPlayer.State.CurrentRole == MainRoleType.Elder)
		{
			powerInstance = RolePowerInstance.CreateCurrent(
				session,
				actingPlayer,
				MainRoleType.Elder,
				SuppressionPower);
		}
		else if (actingPlayer.State.CurrentRole == MainRoleType.Actor &&
			session.GetModeratorActiveActorBorrowedRolePowerActivation() is
			{
				ActingPlayerId: var actorId,
				SourceRole: MainRoleType.Elder
			} &&
			actorId == actingPlayer.Id)
		{
			powerInstance = RolePowerInstance.CreateBorrowedAfterElimination(
				session,
				actingPlayer,
				MainRoleType.Elder,
				SuppressionPower,
				new BorrowedPostEliminationRolePowerContext
					.ElderVillageVoteSuppression(
						vote.Value.LogIndex,
						scopeId));
			isBorrowed = true;
		}
		else
		{
			return false;
		}

		if (!EvaluatePower(
				session,
				actingPlayer,
				SuppressionPower,
				powerInstance))
		{
			return false;
		}

		execution = new ElderSuppressionExecution(
			new RolePowerInstanceIdentity(
				actingPlayer.Id,
				MainRoleType.Elder,
				SuppressionPower.Identifier.Value,
				powerInstance.Id,
				powerInstance.Origin),
			isBorrowed,
			vote.Value.LogIndex,
			scopeId);
		return true;
	}

	private bool EvaluatePower(
		GameSession session,
		IPlayer elder,
		RolePowerDefinition power) =>
		EvaluatePower(
			session,
			elder,
			power,
			RolePowerInstance.CreateCurrent(
				session,
				elder,
				MainRoleType.Elder,
				power));

	private bool EvaluatePower(
		GameSession session,
		IPlayer elder,
		RolePowerDefinition power,
		RolePowerInstance instance)
	{
		return _availabilityGateway.Evaluate(
				new RolePowerAttempt(
					session,
					elder,
					MainRoleType.Elder,
					power,
					instance))
			.AvailabilityResult.IsAvailable;
	}

	private static ConfirmationInstruction CreateSuppressionAnnouncement(
		Guid instructionId) =>
		new ConfirmationInstruction(
			ModeratorInstructionSemantic
				.AnnounceVillagerRolePowerSuppression,
			publicAnnouncement:
				GameStrings.VillagerRolePowerSuppressionAnnouncement,
			instructionId: instructionId);

	internal static bool MatchesSuppressionAnnouncement(
		ModeratorInstruction? instruction,
		Guid instructionId)
	{
		var expected = CreateSuppressionAnnouncement(instructionId);
		return instruction is ConfirmationInstruction announcement &&
			announcement.Semantic == expected.Semantic &&
			announcement.InstructionId == expected.InstructionId &&
			announcement.AffectedPlayerIds is null &&
			StringComparer.Ordinal.Equals(
				announcement.PublicAnnouncement,
				expected.PublicAnnouncement) &&
			StringComparer.Ordinal.Equals(
				announcement.PrivateInstruction,
				expected.PrivateInstruction) &&
			announcement.SoundEffects.SequenceEqual(expected.SoundEffects);
	}
}
