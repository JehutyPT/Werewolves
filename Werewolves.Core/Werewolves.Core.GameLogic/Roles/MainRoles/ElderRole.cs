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
		IPlayer elder)
	{
		if (GameSessionQueries
		    .IsDevotedServantAcquiredRoleDormantForCurrentDay(
			    session,
			    elder.Id))
		{
			return false;
		}

		return EvaluatePower(session, elder, ResistancePower);
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
		    pendingInstruction is not ConfirmationInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic
					    .AnnounceVillagerRolePowerSuppression
		    } ||
		    pendingInstruction.InstructionId !=
		    suppression.AnnouncementInstructionId)
		{
			return false;
		}

		listenerState =
			ElderRoleState.AwaitingSuppressionAnnouncement.ToString();
		return true;
	}

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (!session.TryGetActiveGameHook(out var hook) ||
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
		if (!IsEligibleVillageVoteElimination(session, out var elder))
		{
			return HookListenerActionResult.Complete(ElderRoleState.Complete);
		}

		if (GameSessionQueries.GetVillagerRolePowerSuppression(session) != null)
		{
			throw new InvalidOperationException(
				"Villager Role Power Suppression was already committed.");
		}

		var announcementInstructionId = Guid.NewGuid();
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

	private bool IsEligibleVillageVoteElimination(
		GameSession session,
		out IPlayer elder)
	{
		elder = null!;
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

		elder = session.GetPlayer(targetId);
		if (elder.State.CurrentRole != MainRoleType.Elder ||
		    GameSessionQueries
			    .IsDevotedServantAcquiredRoleDormantForCurrentDay(
				    session,
				    elder.Id))
		{
			return false;
		}

		return EvaluatePower(session, elder, SuppressionPower);
	}

	private bool EvaluatePower(
		GameSession session,
		IPlayer elder,
		RolePowerDefinition power)
	{
		var instance = RolePowerInstance.CreateCurrent(
			session,
			elder,
			MainRoleType.Elder,
			power);
		return _availabilityGateway.Evaluate(
				new RolePowerAttempt(
					session,
					elder,
					MainRoleType.Elder,
					power,
					instance))
			.AvailabilityResult.IsAvailable;
	}

	private static ModeratorInstruction CreateSuppressionAnnouncement(
		Guid instructionId) =>
		new ConfirmationInstruction(
			ModeratorInstructionSemantic
				.AnnounceVillagerRolePowerSuppression,
			publicAnnouncement:
				GameStrings.VillagerRolePowerSuppressionAnnouncement,
			instructionId: instructionId);
}
