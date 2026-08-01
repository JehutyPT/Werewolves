using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
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

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum ScapegoatRoleState
{
	AwaitingHolderObservation,
	AwaitingPublicReveal,
	AwaitingSacrificeCascade,
	Complete
}

internal enum ScapegoatCascadeStageId
{
	Sacrifice
}

internal sealed class ScapegoatRole
	: RoleHookListener<ScapegoatRoleState>
{
	private static readonly RolePowerDefinition TieReplacementPower = new(
		new RolePowerIdentifier("scapegoat-tie-replacement"),
		RolePowerCategory.Automatic);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly SubPhaseStage _sacrificeCascade;

	internal ScapegoatRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
		_sacrificeCascade = EliminationCascadeStage.CascadeStage(
			ScapegoatCascadeStageId.Sacrifice,
			CreateCascadeSeed,
			ModeratorInstructionSemantic.RevealScapegoatForTie,
			advancePostCommitInteraction:
				AdvancePostCommitVoterRestriction,
			matchesPostCommitInteractionInstruction:
				instruction => instruction.Semantic is
					ModeratorInstructionSemantic
						.SelectScapegoatPermittedVoters or
					ModeratorInstructionSemantic
						.AnnounceScapegoatPermittedVoters);
	}

	internal override string PublicName => GameStrings.ScapegoatRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Scapegoat);

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		listenerState = string.Empty;
		if (hook != GameHook.OnVoteConcluded)
		{
			return false;
		}

		switch (pendingInstruction.Semantic)
		{
			case ModeratorInstructionSemantic.ObserveScapegoatHolderForTie
				when pendingInstruction is SelectPlayersInstruction
				{
					CountConstraint: var countConstraint
				} && countConstraint ==
				NumberRangeConstraint.SingleOptional:
				listenerState =
					ScapegoatRoleState.AwaitingHolderObservation.ToString();
				return true;
			case ModeratorInstructionSemantic.RevealScapegoatForTie
				when pendingInstruction is ConfirmationInstruction or
					AssignRolesInstruction:
				listenerState =
					ScapegoatRoleState.AwaitingPublicReveal.ToString();
				return true;
			case ModeratorInstructionSemantic.SelectScapegoatPermittedVoters
				when pendingInstruction is SelectPlayersInstruction:
			case ModeratorInstructionSemantic
				.AnnounceScapegoatPermittedVoters
				when pendingInstruction is ConfirmationInstruction:
				listenerState =
					ScapegoatRoleState.AwaitingSacrificeCascade.ToString();
				return true;
		}

		var replacement =
			GameSessionQueries.GetCurrentScapegoatTieReplacement(session);
		if (replacement != null &&
		    !GameSessionQueries.IsEliminationCascadeComplete(
			    session,
			    replacement.ScopeId))
		{
			listenerState =
				ScapegoatRoleState.AwaitingSacrificeCascade.ToString();
			return true;
		}

		return false;
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

		var vote = GameSessionQueries.GetCurrentDayVoteOutcome(session);
		if (vote is not { PlayerId: var playerId } ||
		    playerId != Guid.Empty)
		{
			return HookListenerActionResult.Skip();
		}

		var currentState = GetCurrentListenerState(session);
		if (currentState == null &&
		    GameSessionQueries.GetCurrentScapegoatTieReplacement(session) !=
		    null)
		{
			return HookListenerActionResult.Skip();
		}

		return currentState == null
			? base.Execute(session, input)
			: ExecuteCore(session, input);
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.OnVoteConcluded,
			null,
			[
				ScapegoatRoleState.AwaitingHolderObservation,
				ScapegoatRoleState.AwaitingPublicReveal,
				ScapegoatRoleState.AwaitingSacrificeCascade,
				ScapegoatRoleState.Complete
			],
			RequestTieReplacement),
		CreateStage(
			GameHook.OnVoteConcluded,
			ScapegoatRoleState.AwaitingHolderObservation,
			[
				ScapegoatRoleState.AwaitingSacrificeCascade,
				ScapegoatRoleState.Complete
			],
			RecordHolderObservationAndBeginSacrifice),
		CreateStage(
			GameHook.OnVoteConcluded,
			ScapegoatRoleState.AwaitingPublicReveal,
			[
				ScapegoatRoleState.AwaitingSacrificeCascade,
				ScapegoatRoleState.Complete
			],
			RecordRevealAndBeginSacrifice),
		CreateLoopStage(
			GameHook.OnVoteConcluded,
			ScapegoatRoleState.AwaitingSacrificeCascade,
			AdvanceSacrificeCascade),
		CreateEndStage(
			GameHook.OnVoteConcluded,
			ScapegoatRoleState.Complete,
			(_, _) => HookListenerActionResult.Complete(
				ScapegoatRoleState.Complete))
	];

	private HookListenerActionResult RequestTieReplacement(
		GameSession session,
		ModeratorResponse input)
	{
		var scapegoat = GetAliveRolePlayers(session)?.SingleOrDefault();
		if (scapegoat == null)
		{
			var candidates = session.GetPlayers()
				.WithHealth(PlayerHealth.Alive)
				.Where(IsNonContradictoryScapegoatCandidate)
				.Select(player => player.Id)
				.ToHashSet();
			if (candidates.Count == 0)
			{
				return HookListenerActionResult.Complete(
					ScapegoatRoleState.Complete);
			}

			var observation = new SelectPlayersInstruction(
				ModeratorInstructionSemantic.ObserveScapegoatHolderForTie,
				candidates,
				NumberRangeConstraint.SingleOptional,
				privateInstruction:
					GameStrings.ScapegoatHolderObservationInstruction,
				affectedPlayerIds: candidates.ToArray())
			{
				EmptySelectionOptionLabel =
					GameStrings.ScapegoatNoRevealOption
			};
			return HookListenerActionResult.NeedInput(
				observation,
				ScapegoatRoleState.AwaitingHolderObservation);
		}

		if (!IsTieReplacementAvailable(session, scapegoat))
		{
			return HookListenerActionResult.Complete(
				ScapegoatRoleState.Complete);
		}

		var reveal = RoleKnowledgeHandlers.RequestPublicRoleReveal(
			session,
			[scapegoat],
			ModeratorInstructionSemantic.RevealScapegoatForTie,
			GameStrings.ScapegoatTieRevealAnnouncement);
		if (reveal != null)
		{
			return HookListenerActionResult.NeedInput(
				reveal,
				ScapegoatRoleState.AwaitingPublicReveal);
		}

		RecordTieReplacement(session, scapegoat.Id);
		return AdvanceSacrificeCascade(session, input);
	}

	private HookListenerActionResult RecordHolderObservationAndBeginSacrifice(
		GameSession session,
		ModeratorResponse input)
	{
		if (input.SelectedPlayerIds is not { } selected ||
		    selected.Count > 1)
		{
			throw new InvalidOperationException(
				"The Scapegoat public-holder observation must select zero or one Player.");
		}

		if (selected.Count == 0)
		{
			return HookListenerActionResult.Complete(
				ScapegoatRoleState.Complete);
		}

		var player = session.GetPlayer(selected.Single());
		if (player.State.Health != PlayerHealth.Alive ||
		    !IsNonContradictoryScapegoatCandidate(player))
		{
			throw new InvalidOperationException(
				"The observed Scapegoat holder contradicts committed Role or health facts.");
		}

		if (!IsTieReplacementAvailable(session, player))
		{
			throw new InvalidOperationException(
				"The observed Scapegoat holder's Role Power is unavailable.");
		}

		session.IdentifyRole([player.Id], MainRoleType.Scapegoat);
		session.RevealRoles(new Dictionary<Guid, MainRoleType>
		{
			[player.Id] = MainRoleType.Scapegoat
		});
		RecordTieReplacement(session, player.Id);
		return AdvanceSacrificeCascade(session, input);
	}

	private HookListenerActionResult RecordRevealAndBeginSacrifice(
		GameSession session,
		ModeratorResponse input)
	{
		var scapegoat = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Scapegoat is available to replace the tied Vote.");
		RoleKnowledgeHandlers.RecordPublicRoleReveal(
			session,
			[scapegoat],
			input);
		RecordTieReplacement(session, scapegoat.Id);
		return AdvanceSacrificeCascade(session, input);
	}

	private HookListenerActionResult AdvanceSacrificeCascade(
		GameSession session,
		ModeratorResponse input)
	{
		var result = _sacrificeCascade.Execute(session, input);
		if (result is not StayInSubPhaseHandlerResult stay)
		{
			throw new InvalidOperationException(
				"The Scapegoat sacrifice cascade attempted to navigate.");
		}

		if (stay.StageComplete)
		{
			return HookListenerActionResult.Complete(
				ScapegoatRoleState.Complete);
		}

		return HookListenerActionResult.NeedInput(
			stay.ModeratorInstruction ??
			throw new InvalidOperationException(
				"The Scapegoat sacrifice cascade paused without an instruction."),
			ScapegoatRoleState.AwaitingSacrificeCascade);
	}

	private bool IsTieReplacementAvailable(
		GameSession session,
		IPlayer scapegoat)
	{
		var instance = RolePowerInstance.CreateCurrent(
			session,
			scapegoat,
			MainRoleType.Scapegoat,
			TieReplacementPower);
		return _availabilityGateway.Evaluate(
				new RolePowerAttempt(
					scapegoat,
					MainRoleType.Scapegoat,
					TieReplacementPower,
					instance))
			.AvailabilityResult.IsAvailable;
	}

	private static bool IsNonContradictoryScapegoatCandidate(
		IPlayer player) =>
		(player.State.CurrentRole is null or MainRoleType.Scapegoat) &&
		(player.State.ModeratorKnownRole is null or MainRoleType.Scapegoat) &&
		(player.State.PubliclyRevealedRole is null or
			MainRoleType.Scapegoat);

	private static void RecordTieReplacement(
		GameSession session,
		Guid scapegoatPlayerId)
	{
		var vote = GameSessionQueries.GetCurrentDayVoteOutcome(session)
			?? throw new InvalidOperationException(
				"No current tied Day Vote is available for Scapegoat replacement.");
		if (vote.PlayerId != Guid.Empty)
		{
			throw new InvalidOperationException(
				"The Scapegoat can replace only a tied Day Vote.");
		}

		session.CommitGameFact(context =>
			new ScapegoatTieReplacementLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				ScapegoatPlayerId = scapegoatPlayerId,
				VoteOrdinal = vote.VoteOrdinal,
				VoteLogIndex = vote.LogIndex,
				ScopeId = CreateScopeId(session, vote.VoteOrdinal)
			});
	}

	private static EliminationCascadeSeed CreateCascadeSeed(GameSession session)
	{
		var replacement =
			GameSessionQueries.GetCurrentScapegoatTieReplacement(session)
			?? throw new InvalidOperationException(
				"The Scapegoat sacrifice cascade requires a tie replacement fact.");
		return new EliminationCascadeSeed(
			replacement.ScopeId,
			replacement.VoteLogIndex,
			[
				new EliminationRequest(
					replacement.ScapegoatPlayerId,
					EliminationReason.ScapegoatSacrifice)
			]);
	}

	private static EliminationCascadePostCommitInteractionResult
		AdvancePostCommitVoterRestriction(
		GameSession session,
		IReadOnlyCollection<EliminationRequest> eliminations,
		ModeratorResponse? input)
	{
		if (!eliminations.Any(elimination =>
			    elimination.Reason ==
			    EliminationReason.ScapegoatSacrifice))
		{
			return EliminationCascadePostCommitInteractionResult.Complete();
		}

		var replacement =
			GameSessionQueries.GetCurrentScapegoatTieReplacement(session)
			?? throw new InvalidOperationException(
				"The Scapegoat voter restriction requires a tie replacement fact.");
		var restriction = DayVoteRules.GetVoterEligibilityRestriction(
			session,
			replacement.ScopeId);
		if (restriction == null)
		{
			if (input == null)
			{
				return EliminationCascadePostCommitInteractionResult.NeedInput(
					CreatePermittedVoterSelection(session));
			}

			if (session.PendingModeratorInstruction is not
			    SelectPlayersInstruction
			    {
				    Semantic:
				    ModeratorInstructionSemantic
					    .SelectScapegoatPermittedVoters
			    } selection ||
			    input.SelectedPlayerIds is not { Count: > 0 } selected)
			{
				throw new InvalidOperationException(
					"The Scapegoat permitted-voter response is not correlated to the pending fixed candidate snapshot.");
			}

			var announcementInstructionId = Guid.NewGuid();
			DayVoteRules.CommitVoterEligibilityRestriction(
				session,
				replacement.ScopeId,
				MainRoleType.Scapegoat,
				selection.SelectablePlayerIds,
				selected,
				session.TurnNumber + 1,
				announcementInstructionId);
			restriction =
				DayVoteRules.GetVoterEligibilityRestriction(
					session,
					replacement.ScopeId)
				?? throw new InvalidOperationException(
					"The committed Scapegoat voter restriction could not be restored.");
			return EliminationCascadePostCommitInteractionResult.NeedInput(
				CreatePermittedVoterAnnouncement(session, restriction));
		}

		if (DayVoteRules
		    .IsVoterEligibilityRestrictionAnnouncementAcknowledged(
			    session,
			    restriction.ScopeId,
			    restriction.AnnouncementInstructionId))
		{
			return EliminationCascadePostCommitInteractionResult.Complete();
		}

		if (input == null)
		{
			return EliminationCascadePostCommitInteractionResult.NeedInput(
				CreatePermittedVoterAnnouncement(session, restriction));
		}

		if (input.InstructionId != restriction.AnnouncementInstructionId)
		{
			throw new InvalidOperationException(
				"The Scapegoat permitted-voter announcement acknowledgment is stale or mismatched.");
		}

		DayVoteRules.AcknowledgeVoterEligibilityRestrictionAnnouncement(
			session,
			restriction.ScopeId,
			restriction.AnnouncementInstructionId);
		return EliminationCascadePostCommitInteractionResult.Complete();
	}

	private static ModeratorInstruction CreatePermittedVoterSelection(
		GameSession session)
	{
		var candidates = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Select(player => player.Id)
			.ToHashSet();
		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectScapegoatPermittedVoters,
			candidates,
			NumberRangeConstraint.AtLeast(1),
			privateInstruction:
				GameStrings.ScapegoatPermittedVotersSelectionInstruction,
			affectedPlayerIds: candidates.ToArray());
	}

	private static ModeratorInstruction CreatePermittedVoterAnnouncement(
		GameSession session,
		VoterEligibilityRestrictionCommittedLogEntry restriction)
	{
		var selectedNames = string.Join(
			Environment.NewLine,
			restriction.PermittedVoterIds.Select(
				playerId => session.GetPlayer(playerId).Name));
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters,
			publicAnnouncement:
				GameStrings.ScapegoatPermittedVotersAnnouncement.Format(
					selectedNames),
			affectedPlayerIds: restriction.PermittedVoterIds,
			instructionId: restriction.AnnouncementInstructionId);
	}

	private static string CreateScopeId(
		GameSession session,
		int voteOrdinal) =>
		$"Day:{session.TurnNumber}:Vote:{voteOrdinal}";
}
