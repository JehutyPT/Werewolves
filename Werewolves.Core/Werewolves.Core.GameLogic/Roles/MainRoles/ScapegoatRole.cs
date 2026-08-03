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
			createPostCommitInstruction:
				CreateReactionEliminationAnnouncement,
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

		if (pendingInstruction.Semantic ==
				ModeratorInstructionSemantic.RevealScapegoatForTie &&
			TryGetActiveBorrowedActorId(session, out var borrowedActorId))
		{
			if (!MatchesBorrowedTieReveal(
					pendingInstruction,
					borrowedActorId))
			{
				return false;
			}

			listenerState =
				ScapegoatRoleState.AwaitingPublicReveal.ToString();
			return true;
		}

		var replacement = GetCurrentTieReplacement(session);
		if (replacement is { IsBorrowed: true })
		{
			if (pendingInstruction.Semantic == ModeratorInstructionSemantic
					.SelectScapegoatPermittedVoters)
			{
				if (!MatchesPermittedVoterSelection(
						pendingInstruction,
						GetPermittedVoterCandidates(session)))
				{
					return false;
				}

				listenerState = ScapegoatRoleState
					.AwaitingSacrificeCascade.ToString();
				return true;
			}

			if (pendingInstruction.Semantic == ModeratorInstructionSemantic
					.AnnounceScapegoatPermittedVoters)
			{
				var restriction =
					DayVoteRules.GetVoterEligibilityRestriction(
						session,
						replacement.ScopeId);
				if (restriction == null ||
					!MatchesPermittedVoterAnnouncement(
						session,
						pendingInstruction,
						restriction.PermittedVoterIds,
						restriction.AnnouncementInstructionId))
				{
					return false;
				}

				listenerState = ScapegoatRoleState
					.AwaitingSacrificeCascade.ToString();
				return true;
			}
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

	internal static void ValidateBorrowedPendingRecoveryInstruction(
		GameSession session)
	{
		var pendingInstruction = session.PendingModeratorInstruction;
		if (pendingInstruction == null)
		{
			return;
		}

		var isValid = true;
		switch (pendingInstruction.Semantic)
		{
			case ModeratorInstructionSemantic.RevealScapegoatForTie:
				if (!session.GetModeratorActorSetupCards().Cards.Any(card =>
						card.PrintedRole == MainRoleType.Scapegoat))
				{
					return;
				}

				if (!TryGetActiveBorrowedActorId(
						session,
						out var borrowedActorId))
				{
					isValid = false;
					break;
				}

				var vote = GameSessionQueries.GetCurrentDayVoteOutcome(session);
				isValid = vote is { PlayerId: var votedPlayerId } &&
					votedPlayerId == Guid.Empty &&
					GetCurrentTieReplacement(session) == null &&
					MatchesBorrowedTieReveal(
						pendingInstruction,
						borrowedActorId);
				break;
			case ModeratorInstructionSemantic
				.SelectScapegoatPermittedVoters:
				var selectionReplacement =
					GetCurrentTieReplacement(session);
				if (selectionReplacement == null)
				{
					isValid = false;
					break;
				}

				if (!selectionReplacement.IsBorrowed)
				{
					return;
				}

				isValid = DayVoteRules.GetVoterEligibilityRestriction(
						session,
						selectionReplacement.ScopeId) == null &&
					MatchesPermittedVoterSelection(
						pendingInstruction,
						GetPermittedVoterCandidates(session));
				break;
			case ModeratorInstructionSemantic
				.AnnounceScapegoatPermittedVoters:
				var announcementReplacement =
					GetCurrentTieReplacement(session);
				if (announcementReplacement is not { IsBorrowed: true })
				{
					return;
				}

				var restriction =
					DayVoteRules.GetVoterEligibilityRestriction(
						session,
						announcementReplacement.ScopeId);
				var borrowedRestrictions = session
					.GetActorBorrowedScapegoatVoterRestrictionCommits()
					.Where(commit =>
						commit.TieReplacementPublicMarkerLogIndex ==
						announcementReplacement.PublicMarkerLogIndex &&
						StringComparer.Ordinal.Equals(
							commit.CascadeScopeId,
							announcementReplacement.ScopeId))
					.ToArray();
				isValid = restriction != null &&
					borrowedRestrictions is [var borrowedRestriction] &&
					borrowedRestriction.AnnouncementInstructionId ==
						restriction.AnnouncementInstructionId &&
					!DayVoteRules
						.IsVoterEligibilityRestrictionAnnouncementAcknowledged(
							session,
							restriction.ScopeId,
							restriction.AnnouncementInstructionId) &&
					MatchesPermittedVoterAnnouncement(
						session,
						pendingInstruction,
						restriction.PermittedVoterIds,
						restriction.AnnouncementInstructionId);
				break;
			default:
				return;
		}

		if (!isValid)
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		}
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
		if (currentState == null && GetCurrentTieReplacement(session) != null)
		{
			return HookListenerActionResult.Skip();
		}

		var hasBorrowedScapegoat =
			TryGetBorrowedActorTieReplacement(session, out _);
		return currentState == null && !hasBorrowedScapegoat
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
		if (TryGetBorrowedActorTieReplacement(
				session,
				out var borrowedExecution))
		{
			var borrowedReveal = RoleKnowledgeHandlers.RequestPublicRoleReveal(
				session,
				[borrowedExecution.Actor],
				ModeratorInstructionSemantic.RevealScapegoatForTie,
				GameStrings.ActorRoleName);
			if (borrowedReveal != null)
			{
				return HookListenerActionResult.NeedInput(
					borrowedReveal,
					ScapegoatRoleState.AwaitingPublicReveal);
			}

			RecordBorrowedTieReplacement(
				session,
				borrowedExecution.PowerIdentity);
			return AdvanceSacrificeCascade(session, input);
		}

		var aliveScapegoats = GetAliveRolePlayers(session)?.ToArray();
		if (aliveScapegoats?.Any(player =>
				GameSessionQueries
					.IsDevotedServantAcquiredRoleDormantForCurrentDay(
						session,
						player.Id)) == true)
		{
			return HookListenerActionResult.Complete(
				ScapegoatRoleState.Complete);
		}

		var scapegoat = aliveScapegoats?.SingleOrDefault();
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
		if (TryGetBorrowedActorTieReplacement(
				session,
				out var borrowedExecution))
		{
			RoleKnowledgeHandlers.RecordPublicRoleReveal(
				session,
				[borrowedExecution.Actor],
				input);
			RecordBorrowedTieReplacement(
				session,
				borrowedExecution.PowerIdentity);
			return AdvanceSacrificeCascade(session, input);
		}

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
					session,
					scapegoat,
					MainRoleType.Scapegoat,
					TieReplacementPower,
					instance))
			.AvailabilityResult.IsAvailable;
	}

	private bool TryGetBorrowedActorTieReplacement(
		GameSession session,
		out BorrowedScapegoatTieReplacementExecution execution)
	{
		execution = default;
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation is not
			{
				ActingPlayerId: var actorId,
				SourceRole: MainRoleType.Scapegoat
			})
		{
			return false;
		}

		var actor = session.GetPlayer(actorId);
		if (actor.State.Health != PlayerHealth.Alive ||
			actor.State.CurrentRole != MainRoleType.Actor)
		{
			return false;
		}

		var instance = RolePowerInstance.CreateBorrowed(
			session,
			actor,
			MainRoleType.Scapegoat,
			TieReplacementPower);
		if (!_availabilityGateway.Evaluate(
				new RolePowerAttempt(
					session,
					actor,
					MainRoleType.Scapegoat,
					TieReplacementPower,
					instance))
			.AvailabilityResult.IsAvailable)
		{
			return false;
		}

		execution = new BorrowedScapegoatTieReplacementExecution(
			actor,
			new RolePowerInstanceIdentity(
				actor.Id,
				MainRoleType.Scapegoat,
				TieReplacementPower.Identifier.Value,
				instance.Id,
				instance.Origin));
		return true;
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

	private static void RecordBorrowedTieReplacement(
		GameSession session,
		RolePowerInstanceIdentity powerIdentity)
	{
		var vote = GameSessionQueries.GetCurrentDayVoteOutcome(session)
			?? throw new InvalidOperationException(
				"No current tied Day Vote is available for Actor Scapegoat replacement.");
		if (vote.PlayerId != Guid.Empty)
		{
			throw new InvalidOperationException(
				"The Actor's borrowed Scapegoat can replace only a tied Day Vote.");
		}

		session.CommitActorBorrowedScapegoatTieReplacement(
			powerIdentity,
			vote.LogIndex,
			vote.VoteOrdinal,
			CreateScopeId(session, vote.VoteOrdinal));
	}

	private static EliminationCascadeSeed CreateCascadeSeed(GameSession session)
	{
		var replacement = GetCurrentTieReplacement(session)
			?? throw new InvalidOperationException(
				"The Scapegoat sacrifice cascade requires a tie replacement fact.");
		return new EliminationCascadeSeed(
			replacement.ScopeId,
			replacement.VoteLogIndex,
			[
				new EliminationRequest(
					replacement.PlayerId,
					replacement.IsBorrowed
						? EliminationReason.EventElimination
						: EliminationReason.ScapegoatSacrifice)
			]);
	}

	private static ModeratorInstruction?
		CreateReactionEliminationAnnouncement(
			GameSession session,
			IReadOnlyCollection<EliminationRequest> eliminations)
	{
		var replacement = GetCurrentTieReplacement(session)
			?? throw new InvalidOperationException(
				"The Scapegoat sacrifice cascade requires a tie replacement fact.");
		if (eliminations.Any(elimination =>
				elimination.PlayerId == replacement.PlayerId))
		{
			return null;
		}

		var victimNames = string.Join(
			Environment.NewLine,
			eliminations.Select(elimination =>
				session.GetPlayer(elimination.PlayerId).Name));
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.AnnounceEliminationCascadeVictims,
			publicAnnouncement:
				GameStrings.MultipleVictimEliminatedAnnounce.Format(
					victimNames),
			affectedPlayerIds: eliminations
				.Select(elimination => elimination.PlayerId)
				.ToArray());
	}

	private static EliminationCascadePostCommitInteractionResult
		AdvancePostCommitVoterRestriction(
		GameSession session,
		IReadOnlyCollection<EliminationRequest> eliminations,
		ModeratorResponse? input)
	{
		var replacement = GetCurrentTieReplacement(session)
			?? throw new InvalidOperationException(
				"The Scapegoat voter restriction requires a tie replacement fact.");
		var eliminationReason = replacement.IsBorrowed
			? EliminationReason.EventElimination
			: EliminationReason.ScapegoatSacrifice;
		if (!eliminations.Any(elimination =>
				elimination.PlayerId == replacement.PlayerId &&
				elimination.Reason == eliminationReason))
		{
			return EliminationCascadePostCommitInteractionResult.Complete();
		}

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
			    input.SelectedPlayerIds is not { Count: > 0 } selected ||
			    selected.Any(playerId =>
				    !selection.SelectablePlayerIds.Contains(playerId)))
			{
				throw new InvalidOperationException(
					"The Scapegoat permitted-voter response is not correlated to the pending fixed candidate snapshot.");
			}

			var announcementInstructionId = Guid.NewGuid();
			if (replacement.BorrowedPowerIdentity is { } powerIdentity)
			{
				session.CommitActorBorrowedScapegoatVoterRestriction(
					powerIdentity,
					replacement.PublicMarkerLogIndex,
					replacement.ScopeId,
					selection.SelectablePlayerIds,
					selected,
					session.TurnNumber + 1,
					announcementInstructionId);
			}
			else
			{
				DayVoteRules.CommitVoterEligibilityRestriction(
					session,
					replacement.ScopeId,
					MainRoleType.Scapegoat,
					selection.SelectablePlayerIds,
					selected,
					session.TurnNumber + 1,
					announcementInstructionId);
			}
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

	private static SelectPlayersInstruction CreatePermittedVoterSelection(
		GameSession session) =>
		CreatePermittedVoterSelection(
			GetPermittedVoterCandidates(session));

	private static SelectPlayersInstruction CreatePermittedVoterSelection(
		IReadOnlySet<Guid> candidates,
		Guid instructionId = default)
	{
		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectScapegoatPermittedVoters,
			new HashSet<Guid>(candidates),
			NumberRangeConstraint.AtLeast(1),
			privateInstruction:
				GameStrings.ScapegoatPermittedVotersSelectionInstruction,
			affectedPlayerIds: candidates.ToArray(),
			instructionId: instructionId);
	}

	private static ConfirmationInstruction CreatePermittedVoterAnnouncement(
		GameSession session,
		DayVoteRules.VoterEligibilityRestriction restriction) =>
		CreatePermittedVoterAnnouncement(
			session,
			restriction.PermittedVoterIds,
			restriction.AnnouncementInstructionId);

	private static ConfirmationInstruction CreatePermittedVoterAnnouncement(
		GameSession session,
		IReadOnlyCollection<Guid> permittedVoterIds,
		Guid instructionId)
	{
		var orderedPermittedVoterIds = permittedVoterIds
			.OrderBy(playerId => playerId)
			.ToArray();
		var selectedNames = string.Join(
			Environment.NewLine,
			orderedPermittedVoterIds.Select(
				playerId => session.GetPlayer(playerId).Name));
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters,
			publicAnnouncement:
				GameStrings.ScapegoatPermittedVotersAnnouncement.Format(
					selectedNames),
			affectedPlayerIds: orderedPermittedVoterIds,
			instructionId: instructionId);
	}

	internal static bool MatchesBorrowedTieReveal(
		ModeratorInstruction? instruction,
		Guid actorId)
	{
		if (instruction is not (ConfirmationInstruction or
		    AssignRolesInstruction) ||
		    instruction.Semantic !=
		    ModeratorInstructionSemantic.RevealScapegoatForTie ||
		    instruction.AffectedPlayerIds is not [var affectedPlayerId] ||
		    affectedPlayerId != actorId ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PublicAnnouncement,
			    GameStrings.ActorRoleName) ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PrivateInstruction,
			    GameStrings.PublicRoleRevealInstruction) ||
		    instruction.SoundEffects.Count != 0)
		{
			return false;
		}

		return instruction is ConfirmationInstruction ||
			instruction is AssignRolesInstruction assignment &&
			assignment.PlayersForAssignment.Count == 1 &&
			assignment.PlayersForAssignment.Contains(actorId) &&
			assignment.RolesForAssignment.Contains(MainRoleType.Actor);
	}

	internal static bool MatchesPermittedVoterSelection(
		ModeratorInstruction? instruction,
		IReadOnlySet<Guid> candidates)
	{
		if (instruction is not SelectPlayersInstruction selection)
		{
			return false;
		}

		var expected = CreatePermittedVoterSelection(
			candidates,
			selection.InstructionId);
		return selection.Semantic == expected.Semantic &&
			selection.InstructionId == expected.InstructionId &&
			selection.CountConstraint == expected.CountConstraint &&
			selection.SelectablePlayerIds.SetEquals(
				expected.SelectablePlayerIds) &&
			selection.AffectedPlayerIds is { } affectedPlayerIds &&
			affectedPlayerIds.Count == candidates.Count &&
			affectedPlayerIds.ToHashSet().SetEquals(candidates) &&
			selection.RoleIdentification == expected.RoleIdentification &&
			StringComparer.Ordinal.Equals(
				selection.PublicAnnouncement,
				expected.PublicAnnouncement) &&
			StringComparer.Ordinal.Equals(
				selection.PrivateInstruction,
				expected.PrivateInstruction) &&
			StringComparer.Ordinal.Equals(
				selection.EmptySelectionOptionLabel,
				expected.EmptySelectionOptionLabel) &&
			selection.SoundEffects.SequenceEqual(expected.SoundEffects);
	}

	internal static bool MatchesPermittedVoterAnnouncement(
		GameSession session,
		ModeratorInstruction? instruction,
		IReadOnlyCollection<Guid> permittedVoterIds,
		Guid instructionId)
	{
		if (instruction is not ConfirmationInstruction announcement)
		{
			return false;
		}

		var expected = CreatePermittedVoterAnnouncement(
			session,
			permittedVoterIds,
			instructionId);
		return announcement.Semantic == expected.Semantic &&
			announcement.InstructionId == expected.InstructionId &&
			announcement.AffectedPlayerIds is { } affectedPlayerIds &&
			affectedPlayerIds.SequenceEqual(expected.AffectedPlayerIds!) &&
			StringComparer.Ordinal.Equals(
				announcement.PublicAnnouncement,
				expected.PublicAnnouncement) &&
			StringComparer.Ordinal.Equals(
				announcement.PrivateInstruction,
				expected.PrivateInstruction) &&
			announcement.SoundEffects.SequenceEqual(expected.SoundEffects);
	}

	private static HashSet<Guid> GetPermittedVoterCandidates(
		GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Select(player => player.Id)
			.ToHashSet();

	private static bool TryGetActiveBorrowedActorId(
		GameSession session,
		out Guid actorId)
	{
		if (session.GetModeratorActiveActorBorrowedRolePowerActivation() is
			{
				ActingPlayerId: var activeActorId,
				SourceRole: MainRoleType.Scapegoat
			} &&
			session.GetPlayer(activeActorId).State is
			{
				CurrentRole: MainRoleType.Actor,
				Health: PlayerHealth.Alive
			})
		{
			actorId = activeActorId;
			return true;
		}

		actorId = Guid.Empty;
		return false;
	}

	private static string CreateScopeId(
		GameSession session,
		int voteOrdinal) =>
		$"Day:{session.TurnNumber}:Vote:{voteOrdinal}";

	private static ScapegoatTieReplacementState? GetCurrentTieReplacement(
		GameSession session)
	{
		var native = GameSessionQueries.GetCurrentScapegoatTieReplacement(session);
		if (native != null)
		{
			return new ScapegoatTieReplacementState(
				native.ScapegoatPlayerId,
				native.VoteLogIndex,
				native.ScopeId,
				false,
				null,
				-1);
		}

		var vote = GameSessionQueries.GetCurrentDayVoteOutcome(session);
		if (vote is not
			{
				PlayerId: var playerId,
				VoteOrdinal: var voteOrdinal
			} || playerId != Guid.Empty)
		{
			return null;
		}

		var scopeId = CreateScopeId(session, voteOrdinal);
		var borrowed = session.GetActorBorrowedScapegoatTieReplacementCommits()
			.SingleOrDefault(commit => commit.CascadeScopeId == scopeId);
		return borrowed == null
			? null
			: new ScapegoatTieReplacementState(
				borrowed.PowerIdentity.ActingPlayerId,
				borrowed.TriggeringVoteOutcomeLogIndex,
				borrowed.CascadeScopeId,
				true,
				borrowed.PowerIdentity,
				borrowed.PublicMarkerLogIndex);
	}

	private readonly record struct BorrowedScapegoatTieReplacementExecution(
		IPlayer Actor,
		RolePowerInstanceIdentity PowerIdentity);

	private sealed record ScapegoatTieReplacementState(
		Guid PlayerId,
		int VoteLogIndex,
		string ScopeId,
		bool IsBorrowed,
		RolePowerInstanceIdentity? BorrowedPowerIdentity,
		int PublicMarkerLogIndex);
}
