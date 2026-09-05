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

internal sealed class ElderRole : RoleHookListener, IDeclaredRoleWorkflow
{
	private static readonly RolePowerDefinition ResistancePower = new(
		new RolePowerIdentifier("elder-werewolf-attack-resistance"),
		RolePowerCategory.Reactive);
	private static readonly ActorBorrowedRolePowerSpec BorrowedResistanceSpec = new(
		MainRoleType.Elder,
		ResistancePower);

	private static readonly RolePowerDefinition SuppressionPower = new(
		new RolePowerIdentifier("elder-village-vote-suppression"),
		RolePowerCategory.Reactive);
	private static readonly ActorBorrowedRolePowerSpec BorrowedSuppressionSpec = new(
		MainRoleType.Elder,
		SuppressionPower);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _voteWorkflowRuntime;

	internal ElderRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var suppressionAnnouncementWait =
			RecoverableWait<ElderRoleState, ConfirmationInstruction>.Replayable(
				Id,
				GameHook.OnVoteConcluded,
				startState: null,
				ElderRoleState.AwaitingSuppressionAnnouncement,
				ModeratorInstructionSemantic
					.AnnounceVillagerRolePowerSuppression,
				ExpectedInputType.Continue,
				static _ => false,
				static (_, _) => { },
				CreateCommittedSuppressionAnnouncement,
				static (_, instruction) =>
					instruction.Semantic ==
					ModeratorInstructionSemantic
						.AnnounceVillagerRolePowerSuppression,
				ValidateSuppressionAnnouncementInstruction);
		_voteWorkflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.OnVoteConcluded,
			[
				suppressionAnnouncementWait,
				new RoleWorkflowDecisionStep<ElderRoleState>(
					Id,
					GameHook.OnVoteConcluded,
					startState: null,
					static _ => true,
					(session, input) => CommitSuppressionAndRequestAnnouncement(
						session,
						input,
						suppressionAnnouncementWait)),
				new RoleWorkflowDecisionStep<ElderRoleState>(
					Id,
					GameHook.OnVoteConcluded,
					ElderRoleState.AwaitingSuppressionAnnouncement,
					static _ => true,
					AcknowledgeSuppressionAnnouncement),
				new RoleWorkflowCompletionStep<ElderRoleState>(
					Id,
					GameHook.OnVoteConcluded,
					ElderRoleState.Complete,
					ElderRoleState.Complete,
					static _ => true)
			]);
	}

	internal override string PublicName => GameStrings.ElderRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Elder);

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_voteWorkflowRuntime;

	// The Elder keeps its first-Night hook registration, which declares no
	// workflow and therefore still produces no first-Night instruction. Its
	// Dawn resistance Role Identification stays centrally navigated.
	RoleWorkflowRuntime? IDeclaredRoleWorkflow.GetWorkflowRuntime(
		GameHook hook) => hook switch
	{
		GameHook.OnVoteConcluded => _voteWorkflowRuntime,
		_ => null
	};

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

		var borrowedUse = allowBorrowedActor &&
			attackedPlayer.State.CurrentRole == MainRoleType.Actor
				? ActorBorrowedRolePowers.ResolveActive(
					session,
					BorrowedResistanceSpec)
				: null;
		RolePowerInstance powerInstance;
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
			borrowedUse is not null &&
			borrowedUse.Actor.Id == attackedPlayer.Id)
		{
			powerInstance = borrowedUse.PowerInstance;
		}
		else
		{
			execution = default;
			return false;
		}

		var identity = borrowedUse?.PowerIdentity ??
			new RolePowerInstanceIdentity(
				attackedPlayer.Id,
				MainRoleType.Elder,
				ResistancePower.Identifier.Value,
				powerInstance.Id,
				powerInstance.Origin);
		var latestBorrowedResistance = borrowedUse is not null
			? GameSessionQueries
				.GetLatestActorBorrowedElderResistanceForActiveUse(
					session,
					borrowedUse)
			: null;
		if (latestBorrowedResistance is
			{ RestoringWitchSaveLogIndex: null })
		{
			execution = default;
			return false;
		}

		var isAvailable = borrowedUse is not null
			? _availabilityGateway.Evaluate(borrowedUse.CreateAttempt())
				.AvailabilityResult.IsAvailable
			: EvaluatePower(
				session,
				attackedPlayer,
				ResistancePower,
				powerInstance);
		if (!isAvailable)
		{
			execution = default;
			return false;
		}

		execution = new ElderResistanceExecution(
			identity,
			borrowedUse is not null);
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
		var borrowedUse = ActorBorrowedRolePowers.ResolveActive(
			session,
			BorrowedResistanceSpec);
		var latestBorrowedResistance = borrowedUse is null
			? null
			: GameSessionQueries
				.GetLatestActorBorrowedElderResistanceForActiveUse(
					session,
					borrowedUse);
		if (!execution.IsBorrowed ||
			borrowedUse is null ||
			borrowedUse.PowerIdentity != execution.PowerIdentity ||
			borrowedUse.Actor.Id != targetPlayerId ||
			latestBorrowedResistance is
				{ RestoringWitchSaveLogIndex: null } ||
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
			borrowedUse.PowerIdentity,
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
				  (player.State.ModeratorKnownRole == MainRoleType.Elder ||
				   player.State.ModeratorKnownRole == null &&
				   RoleFactionKnowledge.GetPossibleRoles(session, player.Id)
					   .Contains(MainRoleType.Elder)))))
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

		if (selectedPlayerIds.Any(playerId =>
			{
				var player = session.GetPlayer(playerId);
				return player.State.CurrentRole != MainRoleType.Elder &&
				       player.State.ModeratorKnownRole != MainRoleType.Elder &&
				       player.State.PhysicalCharacterCardRole != MainRoleType.Elder &&
				       !RoleFactionKnowledge.GetPossibleRoles(session, playerId)
					       .Contains(MainRoleType.Elder);
			}))
		{
			throw new InvalidOperationException(
				"Role Identification contradicts committed Role knowledge.");
		}

		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			selectedPlayerIds.ToHashSet(),
			MainRoleType.Elder);
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

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		_voteWorkflowRuntime.Execute(
			session,
			input,
			session.Execution.GetCurrentListenerState<ElderRoleState>(Id));

	private HookListenerActionResult CommitSuppressionAndRequestAnnouncement(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<ElderRoleState, ConfirmationInstruction>
			suppressionAnnouncementWait)
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

		return suppressionAnnouncementWait.Execute(session, input);
	}

	private static ConfirmationInstruction CreateCommittedSuppressionAnnouncement(
		GameSession session)
	{
		var suppression =
			GameSessionQueries.GetVillagerRolePowerSuppression(session)
			?? throw new InvalidOperationException(
				"The Elder suppression announcement requires a committed suppression fact.");
		return CreateSuppressionAnnouncement(
			suppression.AnnouncementInstructionId);
	}

	private static void ValidateSuppressionAnnouncementInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		var suppression =
			GameSessionQueries.GetVillagerRolePowerSuppression(session);
		if (suppression == null)
		{
			throw new RoleWorkflowInputRejectionException(
				"The Elder suppression announcement requires a committed suppression fact.");
		}

		if (GameSessionQueries
		    .IsVillagerRolePowerSuppressionAnnouncementAcknowledged(
			    session,
			    suppression.AnnouncementInstructionId))
		{
			throw new RoleWorkflowInputRejectionException(
				"The Elder suppression announcement was already acknowledged.");
		}

		if (!MatchesSuppressionAnnouncement(
			    instruction,
			    suppression.AnnouncementInstructionId))
		{
			throw new RoleWorkflowInputRejectionException(
				"The Elder suppression announcement does not match its committed suppression fact.");
		}
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

		if (actingPlayer.State.CurrentRole == MainRoleType.Elder)
		{
			var powerInstance = RolePowerInstance.CreateCurrent(
				session,
				actingPlayer,
				MainRoleType.Elder,
				SuppressionPower);
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
				IsBorrowed: false,
				vote.Value.LogIndex,
				scopeId);
			return true;
		}

		if (actingPlayer.State.CurrentRole != MainRoleType.Actor ||
			actingPlayer.State.Health != PlayerHealth.Dead)
		{
			return false;
		}

		var borrowedUse = ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			BorrowedSuppressionSpec,
			new BorrowedPostEliminationRolePowerContext
				.ElderVillageVoteSuppression(
					vote.Value.LogIndex,
					scopeId));
		if (borrowedUse is null)
		{
			return false;
		}

		if (!_availabilityGateway.Evaluate(borrowedUse.CreateAttempt())
			.AvailabilityResult.IsAvailable)
		{
			return false;
		}

		execution = new ElderSuppressionExecution(
			borrowedUse.PowerIdentity,
			IsBorrowed: true,
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
