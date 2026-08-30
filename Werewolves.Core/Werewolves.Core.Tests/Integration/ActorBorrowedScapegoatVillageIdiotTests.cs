using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedScapegoatVillageIdiotTests
{
	private static readonly PhysicalCharacterCard ScapegoatCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000142"),
		MainRoleType.Scapegoat);
	private static readonly PhysicalCharacterCard VillageIdiotCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000143"),
		MainRoleType.VillageIdiot);
	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000144"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard FoxCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000145"),
		MainRoleType.Fox);
	[Fact]
	public void BorrowedScapegoat_TiedVote_RevealsActorAndFixesCandidatesBeforeForcedReaction()
	{
		var pending = CreatePendingBorrowedScapegoatVoterSelection();
		var selected = new HashSet<Guid> { pending.PermittedVoterId };

		var announcement = Advance(
				pending.Session,
				pending.Selection.CreateResponse(selected))
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters);
		announcement.AffectedPlayerIds.Should().BeEquivalentTo(selected);
		announcement.PublicAnnouncement.Should()
			.Contain(pending.Session.GetPlayer(pending.PermittedVoterId).Name)
			.And.NotContain(GameStrings.ScapegoatRoleName);
		pending.Reaction.InvocationCount.Should().Be(0);
		pending.Session.GetPlayerState(pending.ReactionVictimId).Health.Should()
			.Be(PlayerHealth.Alive);

		IGameSession committed = pending.Session;
		committed.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().HaveCount(2);
		committed.GameHistoryLog
			.OfType<ScapegoatTieReplacementLogEntry>()
			.Should().BeEmpty();
		committed.GameHistoryLog
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.Should().BeEmpty();
		var restriction = DayVoteRules.GetVoterEligibilityRestriction(
			pending.Session,
			pending.ScopeId);
		restriction.Should().NotBeNull();
		restriction!.CandidatePlayerIds.Should().BeEquivalentTo(
			pending.CandidateSnapshot);
		restriction.PermittedVoterIds.Should().BeEquivalentTo(selected);
		restriction.AnnouncementInstructionId.Should().Be(
			announcement.InstructionId);

		var reactionReveal = Advance(
				pending.Session,
				announcement.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		pending.Reaction.InvocationCount.Should().Be(1);
		reactionReveal.AffectedPlayerIds.Should().Equal(
			pending.ReactionVictimId);
		var cascadeAnnouncement = Advance(
				pending.Session,
				reactionReveal.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		cascadeAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceEliminationCascadeVictims);
		Advance(pending.Session, cascadeAnnouncement.CreateResponse());

		var completed = (IGameSession)pending.Session;
		completed.GetPlayerState(pending.ReactionVictimId).Health.Should().Be(
			PlayerHealth.Dead);
		completed.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == pending.ReactionVictimId &&
				entry.Reason == EliminationReason.EventElimination);
		completed.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().ContainSingle(entry => entry.ScopeId == pending.ScopeId);
		restriction.CandidatePlayerIds.Should().Contain(
			pending.ReactionVictimId);
	}

	[Fact]
	public void BorrowedScapegoat_TiedVotePrecedesUnknownNativeHolderObservation()
	{
		var fixture = CreateActiveBorrowedScapegoatVote(
			nativeScapegoatHolderKnowledgeUnknown: true);
		var activation = fixture.Session
			.GetModeratorActiveActorBorrowedRolePowerActivation();
		activation.Should().NotBeNull();
		activation!.ActingPlayerId.Should().Be(fixture.ActorId);
		activation.SourceRole.Should().Be(MainRoleType.Scapegoat);
		var unknownHolder = fixture.Session.GetPlayerState(
			fixture.ReactionVictimId);
		unknownHolder.CurrentRole.Should().BeNull();
		unknownHolder.ModeratorKnownRole.Should().BeNull();
		unknownHolder.PubliclyRevealedRole.Should().BeNull();

		var reveal = Advance(
				fixture.Session,
				fixture.Vote.CreateResponse([]))
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		reveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealScapegoatForTie);
		reveal.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.ObserveScapegoatHolderForTie);
		reveal.AffectedPlayerIds.Should().Equal(fixture.ActorId);
	}

	[Fact]
	public void BorrowedScapegoat_PendingSelectionAndCommittedAnnouncement_RoundTripExactlyOnce()
	{
		var pending = CreatePendingBorrowedScapegoatVoterSelection();
		var selected = new HashSet<Guid> { pending.PermittedVoterId };
		var recoveredSelectionReaction = new BorrowedScapegoatForcedReaction(
			pending.ActorId,
			pending.ReactionVictimId);
		var selectionService = CreateScapegoatService(
			recoveredSelectionReaction);
		var selectionGameId = selectionService.RehydrateSession(
			pending.Session.SerializeRecoverySnapshot());
		var recoveredSelection = selectionService
			.GetCurrentInstruction(selectionGameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		recoveredSelection.InstructionId.Should().Be(
			pending.Selection.InstructionId);
		recoveredSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectScapegoatPermittedVoters);
		recoveredSelection.SelectablePlayerIds.Should().BeEquivalentTo(
			pending.CandidateSnapshot);
		var announcement = selectionService.ProcessInstruction(
				selectionGameId,
				recoveredSelection.CreateResponse(selected))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var committedAnnouncementState = selectionService
			.GetGameStateView(selectionGameId)!;
		committedAnnouncementState.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().HaveCount(2);
		committedAnnouncementState.GameHistoryLog
			.OfType<ScapegoatTieReplacementLogEntry>()
			.Should().BeEmpty();
		committedAnnouncementState.GameHistoryLog
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.Should().BeEmpty();

		var announcementReaction = new BorrowedScapegoatForcedReaction(
			pending.ActorId,
			pending.ReactionVictimId);
		var announcementService = CreateScapegoatService(
			announcementReaction);
		var announcementGameId = announcementService.RehydrateSession(
			selectionService.SerializeSession(selectionGameId));
		var recoveredAnnouncement = announcementService
			.GetCurrentInstruction(announcementGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredAnnouncement.InstructionId.Should().Be(
			announcement.InstructionId);
		recoveredAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters);
		recoveredAnnouncement.PublicAnnouncement.Should().Be(
			announcement.PublicAnnouncement);
		recoveredAnnouncement.AffectedPlayerIds.Should().BeEquivalentTo(selected);
		var beforeInvalidAcknowledgment = announcementService.SerializeSession(
			announcementGameId);
		Action rejectStaleAcknowledgment = () =>
			announcementService.ProcessInstruction(
				announcementGameId,
				new ModeratorResponse
				{
					InstructionId = Guid.NewGuid(),
					Type = ExpectedInputType.Continue
				});

		rejectStaleAcknowledgment.Should().Throw<InvalidOperationException>();
		announcementService.SerializeSession(announcementGameId)
			.Should().Be(beforeInvalidAcknowledgment);
		var reactionReveal = announcementService.ProcessInstruction(
				announcementGameId,
				recoveredAnnouncement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var acknowledged = announcementService
			.GetGameStateView(announcementGameId)!;
		acknowledged.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().HaveCount(2);
		acknowledged.GameHistoryLog
			.OfType<
				VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry>()
			.Should().ContainSingle()
			.Which.AnnouncementInstructionId.Should().Be(
				recoveredAnnouncement.InstructionId);

		var afterAcknowledgmentReaction =
			new BorrowedScapegoatForcedReaction(
				pending.ActorId,
				pending.ReactionVictimId);
		var afterAcknowledgmentService = CreateScapegoatService(
			afterAcknowledgmentReaction);
		var afterAcknowledgmentGameId =
			afterAcknowledgmentService.RehydrateSession(
				announcementService.SerializeSession(announcementGameId));
		var restoredReactionReveal = afterAcknowledgmentService
			.GetCurrentInstruction(afterAcknowledgmentGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		restoredReactionReveal.InstructionId.Should().Be(
			reactionReveal.InstructionId);
		restoredReactionReveal.AffectedPlayerIds.Should().Equal(
			pending.ReactionVictimId);
		var beforeReplay = afterAcknowledgmentService.SerializeSession(
			afterAcknowledgmentGameId);
		Action replayAnnouncementAcknowledgment = () =>
			afterAcknowledgmentService.ProcessInstruction(
				afterAcknowledgmentGameId,
				recoveredAnnouncement.CreateResponse());

		replayAnnouncementAcknowledgment.Should()
			.Throw<InvalidOperationException>();
		afterAcknowledgmentService.SerializeSession(afterAcknowledgmentGameId)
			.Should()
			.Be(beforeReplay);
	}

	[Fact]
	public void BorrowedScapegoat_PendingVoterSelectionWithoutTieReplacementLineage_IsRejectedBeforeRegistration()
	{
		var pending = CreatePendingBorrowedScapegoatVoterSelection();
		var stripped = StripBorrowedScapegoatTieReplacementLineage(
			pending.Session.SerializeRecoverySnapshot());
		var service = CreateScapegoatService(
			new BorrowedScapegoatForcedReaction(
				pending.ActorId,
				pending.ReactionVictimId));

		Action rehydrate = () => service.RehydrateSession(stripped);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage(
				"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		service.GetCurrentInstruction(pending.Session.Id).Should().BeNull();
		service.GetGameStateView(pending.Session.Id).Should().BeNull();
	}

	[Fact]
	public void BorrowedScapegoat_PendingVoterAnnouncementWithoutTieReplacementLineage_IsRejectedBeforeRegistration()
	{
		var pending = CreatePendingBorrowedScapegoatVoterSelection();
		var announcement = Advance(
				pending.Session,
				pending.Selection.CreateResponse([pending.PermittedVoterId]))
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters);
		var stripped = StripBorrowedScapegoatTieReplacementLineage(
			pending.Session.SerializeRecoverySnapshot());
		var service = CreateScapegoatService(
			new BorrowedScapegoatForcedReaction(
				pending.ActorId,
				pending.ReactionVictimId));

		Action rehydrate = () => service.RehydrateSession(stripped);

		rehydrate.Should().Throw<InvalidOperationException>();
		service.GetCurrentInstruction(pending.Session.Id).Should().BeNull();
		service.GetGameStateView(pending.Session.Id).Should().BeNull();
	}

	[Theory]
	[InlineData(InvalidFixedVoterResponse.EmptySelection)]
	[InlineData(InvalidFixedVoterResponse.OutsideCandidate)]
	[InlineData(InvalidFixedVoterResponse.StaleInstruction)]
	public void BorrowedScapegoat_InvalidFixedVoterResponse_IsRejectedAtomically(
		InvalidFixedVoterResponse invalidResponse)
	{
		var pending = CreatePendingBorrowedScapegoatVoterSelection();
		var service = CreateScapegoatService(new BorrowedScapegoatForcedReaction(
			pending.ActorId,
			pending.ReactionVictimId));
		var gameId = service.RehydrateSession(
			pending.Session.SerializeRecoverySnapshot());
		var selection = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var response = new ModeratorResponse
		{
			InstructionId = invalidResponse ==
				InvalidFixedVoterResponse.StaleInstruction
					? Guid.NewGuid()
					: selection.InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = invalidResponse switch
			{
				InvalidFixedVoterResponse.EmptySelection => new HashSet<Guid>(),
				InvalidFixedVoterResponse.OutsideCandidate =>
					new HashSet<Guid> { pending.ActorId },
				InvalidFixedVoterResponse.StaleInstruction =>
					new HashSet<Guid> { pending.PermittedVoterId },
				_ => throw new ArgumentOutOfRangeException(
					nameof(invalidResponse))
			}
		};
		var before = service.SerializeSession(gameId);

		Action submit = () => service.ProcessInstruction(gameId, response);

		submit.Should().Throw<InvalidOperationException>();
		service.SerializeSession(gameId).Should().Be(before);
		service.GetCurrentInstruction(gameId).Should()
			.BeEquivalentTo(selection);
	}

	[Fact]
	public void BorrowedScapegoat_FixedVotersApplyFollowingDayAndExpireAtDayFinalization()
	{
		var pending = CreatePendingBorrowedScapegoatVoterSelection();
		var otherCandidates = pending.CandidateSnapshot
			.Except([pending.ReactionVictimId, pending.PermittedVoterId])
			.ToArray();
		var permittedWithoutVotingRightId = otherCandidates[0];
		var unselectedTargetId = otherCandidates[1];
		var selected = new HashSet<Guid>
		{
			pending.PermittedVoterId,
			pending.ReactionVictimId,
			permittedWithoutVotingRightId
		};

		var announcement = Advance(
				pending.Session,
				pending.Selection.CreateResponse(selected))
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		DayVoteRules.GetEffectiveVoters(pending.Session)
			.Select(player => player.Id)
			.Should().BeEquivalentTo(pending.CandidateSnapshot);
		var sameDayConsecutiveVote =
			DayPhaseHandlers.CreateRecordDayVoteInstruction(pending.Session);
		sameDayConsecutiveVote.PrivateInstruction.Should().Be(
			GameStrings.VoteStartsModeratorInstruction);
		sameDayConsecutiveVote.SelectablePlayerIds.Should().BeEquivalentTo(
			pending.CandidateSnapshot);

		pending.Session.SetPlayerVotingRight(
			permittedWithoutVotingRightId,
			hasVotingRight: false);
		var reactionReveal = Advance(
				pending.Session,
				announcement.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var cascadeAnnouncement = Advance(
				pending.Session,
				reactionReveal.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var nightStart = Advance(
				pending.Session,
				cascadeAnnouncement.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		nightStart.Semantic.Should().Be(ModeratorInstructionSemantic.StartNight);
		pending.Session.TurnNumber.Should().Be(2);
		pending.Session.TransitionMainPhase(GamePhase.Day);

		DayVoteRules.GetEffectiveVoters(pending.Session)
			.Select(player => player.Id)
			.Should().Equal(pending.PermittedVoterId);
		var followingDayVote =
			DayPhaseHandlers.CreateRecordDayVoteInstruction(pending.Session);
		followingDayVote.PrivateInstruction.Should()
			.Contain(pending.Session.GetPlayer(pending.PermittedVoterId).Name)
			.And.NotContain(
				pending.Session.GetPlayer(permittedWithoutVotingRightId).Name)
			.And.NotContain(
				pending.Session.GetPlayer(unselectedTargetId).Name);
		followingDayVote.SelectablePlayerIds.Should().Contain(
			[pending.PermittedVoterId, permittedWithoutVotingRightId,
				unselectedTargetId]);

		DayPhaseHandlers.ExpireVoterEligibilityRestriction(
			pending.Session,
			new ModeratorResponse
			{
				InstructionId = Guid.NewGuid(),
				Type = ExpectedInputType.Continue
			});

		DayVoteRules.GetActiveVoterEligibilityRestriction(pending.Session)
			.Should().BeNull();
		DayVoteRules.GetEffectiveVoters(pending.Session)
			.Select(player => player.Id)
			.Should().Contain(unselectedTargetId)
			.And.NotContain(permittedWithoutVotingRightId)
			.And.NotContain(pending.ReactionVictimId);
		((IGameSession)pending.Session).GameHistoryLog
			.OfType<VoterEligibilityRestrictionExpiredLogEntry>()
			.Should().ContainSingle(entry => entry.ScopeId == pending.ScopeId);
	}

	[Theory]
	[InlineData(BorrowedScapegoatTriggerBlock.Unavailable)]
	[InlineData(BorrowedScapegoatTriggerBlock.Suppressed)]
	public void BorrowedScapegoat_BlockedTieUsesOrdinaryContinuationWithoutPrivateCommit(
		BorrowedScapegoatTriggerBlock triggerBlock)
	{
		var fixture = CreateActiveBorrowedScapegoatVote(
			triggerBlock == BorrowedScapegoatTriggerBlock.Unavailable
				? new DenyAllRolePowerAvailabilityPolicy()
				: AllowAllRolePowerAvailabilityPolicy.Instance,
			suppressionActive:
				triggerBlock == BorrowedScapegoatTriggerBlock.Suppressed);

		var next = Advance(fixture.Session, fixture.Vote.CreateResponse([]));

		next.Semantic.Should().Be(ModeratorInstructionSemantic.StartNight);
		var state = (IGameSession)fixture.Session;
		state.GetPlayerState(fixture.ActorId).Health.Should().Be(
			PlayerHealth.Alive);
		state.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
			.Should().ContainSingle()
			.Which.ReportedOutcomePlayerId.Should().Be(Guid.Empty);
		state.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		state.GameHistoryLog.OfType<RoleRevealLogEntry>()
			.Should().NotContain(entry =>
				entry.RevealedRoles.ContainsKey(fixture.ActorId));
		fixture.Session.GetActorBorrowedScapegoatTieReplacementCommits()
			.Should().BeEmpty();
		fixture.Session.GetActorBorrowedScapegoatVoterRestrictionCommits()
			.Should().BeEmpty();
	}

	[Fact]
	public void BorrowedScapegoat_ArrangedHistoricalActivationExpiryPreservesRestrictionAndContinuation()
	{
		var pending = CreatePendingBorrowedScapegoatVoterSelection();
		var selected = new HashSet<Guid> { pending.PermittedVoterId };
		var announcement = Advance(
				pending.Session,
				pending.Selection.CreateResponse(selected))
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var restrictionBeforeExpiry = DayVoteRules
			.GetVoterEligibilityRestriction(pending.Session, pending.ScopeId);
		restrictionBeforeExpiry.Should().NotBeNull();
		var service = CreateScapegoatService(
			new BorrowedScapegoatForcedReaction(
				pending.ActorId,
				pending.ReactionVictimId));
		var gameId = service.RehydrateSession(
			pending.Session.SerializeRecoverySnapshot());
		var recoveredAnnouncement = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredAnnouncement.Should().BeEquivalentTo(announcement);
		var reactionReveal = service.ProcessInstruction(
				gameId,
				recoveredAnnouncement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var cascadeAnnouncement = service.ProcessInstruction(
				gameId,
				reactionReveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var nightStart = service.ProcessInstruction(
				gameId,
				cascadeAnnouncement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		nightStart.Semantic.Should().Be(ModeratorInstructionSemantic.StartNight);
		var arranged = (GameSession)service.GetGameStateView(gameId)!;
		arranged.GetCurrentPhase().Should().Be(GamePhase.Night);
		arranged.TurnNumber.Should().Be(2);
		// Test-owned historical-lineage arrangement: the dead Actor has no
		// natural next opening at which this activation could expire.
		arranged.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		var arrangedPayload = RecoveryPayloadTestDriver.Capture(arranged)
			.WithPendingInstruction(nightStart)
			.Serialize();

		var recoveryService = CreateScapegoatService(
			new BorrowedScapegoatForcedReaction(
				pending.ActorId,
				pending.ReactionVictimId));
		var recoveredGameId = recoveryService.RehydrateSession(
			arrangedPayload);
		var recoveredNightStart = recoveryService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredNightStart.Should().BeEquivalentTo(nightStart);
		var recovered = (GameSession)recoveryService
			.GetGameStateView(recoveredGameId)!;
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeNull();
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
			.Should().ContainSingle();
		recovered.GetActorBorrowedScapegoatTieReplacementCommits()
			.Should().ContainSingle();
		recovered.GetActorBorrowedScapegoatVoterRestrictionCommits()
			.Should().ContainSingle();
		DayVoteRules.GetVoterEligibilityRestriction(recovered, pending.ScopeId)
			.Should().BeEquivalentTo(restrictionBeforeExpiry!);
	}

	[Fact]
	public void BorrowedVillageIdiot_FirstVoteRevealsActorThenPardonsAndRemovesVotingPower()
	{
		var (session, start, actorId) = CreateActiveVillageIdiotActorSession();
		session.TransitionMainPhase(GamePhase.Day);

		var debate = Advance(session, start.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = Advance(session, debate.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var reveal = Advance(session, vote.CreateResponse([actorId]))
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		reveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDayVoteTargetRole);
		reveal.AffectedPlayerIds.Should().Equal(actorId);

		var pardon = Advance(session, reveal.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		pardon.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon);
		pardon.PublicAnnouncement.Should().Contain(GameStrings.ActorRoleName)
			.And.NotContain(GameStrings.VillageIdiotRoleName);
		pardon.AffectedPlayerIds.Should().Equal(actorId);

		IGameSession publicSession = session;
		var actor = publicSession.GetPlayerState(actorId);
		actor.CurrentRole.Should().Be(MainRoleType.Actor);
		actor.PubliclyRevealedRole.Should().Be(MainRoleType.Actor);
		actor.Health.Should().Be(PlayerHealth.Alive);
		actor.HasVotingRight.Should().BeFalse();
		actor.DurableVotingPower.Should().Be(0);
		publicSession.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		publicSession.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();
		publicSession.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == actorId);
	}

	[Fact]
	public void BorrowedVillageIdiot_PublicHistoryReconstructsVotingLossWithoutBorrowedLineage()
	{
		var (session, _, _, actorId) =
			CreatePendingBorrowedVillageIdiotPardon();
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()
			?? throw new InvalidOperationException(
				"The borrowed Village Idiot fixture lost its active lineage.");
		var service = new GameService();

		var recoveredGameId = service.RehydrateSession(
			session.SerializeRecoverySnapshot());
		var recovered = service.GetGameStateView(recoveredGameId)
			?? throw new InvalidOperationException(
				"The borrowed Village Idiot fixture was not recovered.");
		var playerIds = recovered.GetPlayers().Select(player => player.Id)
			.ToArray();
		var publicConsequence = recovered.GameHistoryLog
			.OfType<VotingRightChangedLogEntry>()
			.Should()
			.ContainSingle().Subject;
		publicConsequence.PlayerId.Should().Be(actorId);
		publicConsequence.HasVotingRight.Should().BeFalse();
		publicConsequence.DurableVotingPower.Should().Be(0);
		var consequenceProjection = new TestSessionMutator(playerIds);
		publicConsequence.Apply(consequenceProjection);

		var reconstructedActor = consequenceProjection
			.GetDerivedStates()[actorId];
		reconstructedActor.HasVotingRight.Should().BeFalse();
		reconstructedActor.DurableVotingPower.Should().Be(0);
		var cachedActor = recovered.GetPlayerState(actorId);
		cachedActor.HasVotingRight.Should().BeFalse();
		cachedActor.DurableVotingPower.Should().Be(0);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		recovered.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();
		var publicConsequencePayload = JsonSerializer.Serialize<GameLogEntryBase>(
			publicConsequence,
			new JsonSerializerOptions
			{
				Converters =
				{
					new GameLogEntryConverter(),
					new JsonStringEnumConverter()
				}
			});
		var publicConsequenceExposure = string.Concat(
			publicConsequence,
			"\n",
			publicConsequencePayload);
		publicConsequenceExposure.Should()
			.NotContain(GameStrings.VillageIdiotRoleName)
			.And.NotContain(MainRoleType.VillageIdiot.ToString())
			.And.NotContain("village-idiot-pardon")
			.And.NotContain(VillageIdiotCard.Id.ToString())
			.And.NotContain(activation.ActivationId.ToString())
			.And.NotContain(
				ActorBorrowedVillageIdiotPardonCommit.ExpectedResourceId
					.ToString());
	}

	[Fact]
	public void BorrowedVillageIdiot_SpentPardon_ConsecutiveVoteUsesFreshRosterAndEliminatesNormally()
	{
		var (session, start, actorId) = CreateActiveVillageIdiotActorSession();
		SeedRequiredFactionBeneficiaryFacts(session);
		session.TransitionMainPhase(GamePhase.Day);
		session.PerformDayActionNoTarget(DayPowerType.JudgeExtraVote);
		var debate = Advance(session, start.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var firstVote = Advance(session, debate.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var reveal = Advance(session, firstVote.CreateResponse([actorId]))
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var pardon = Advance(session, reveal.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		pardon.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon);
		var consecutiveVote = Advance(session, pardon.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		consecutiveVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		DayVoteRules.GetEffectiveVoters(session)
			.Select(player => player.Id)
			.Should().NotContain(actorId);
		consecutiveVote.SelectablePlayerIds.Should().Contain(actorId);

		var elimination = Advance(
				session,
				consecutiveVote.CreateResponse([actorId]))
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		elimination.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		IGameSession completed = session;
		completed.GetPlayerState(actorId).Health.Should().Be(PlayerHealth.Dead);
		completed.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		completed.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == actorId &&
				entry.Reason == EliminationReason.DayVote);
		completed.GameHistoryLog
			.OfType<EliminationCascadeBatchResolvedLogEntry>()
			.Where(entry => entry.ScopeId.StartsWith(
				"Day:1:Vote:",
				StringComparison.Ordinal))
			.Select(entry => (entry.ScopeId, entry.CommittedEliminations.Count))
			.Should().Equal(
				("Day:1:Vote:1", 0),
				("Day:1:Vote:2", 1));
	}

	[Theory]
	[InlineData(BorrowedVillageIdiotPardonBlock.Suppressed)]
	[InlineData(BorrowedVillageIdiotPardonBlock.Expired)]
	public void BorrowedVillageIdiot_BlockedActivationFallsThroughToOrdinaryElimination(
		BorrowedVillageIdiotPardonBlock block)
	{
		var (session, start, actorId) = CreateActiveVillageIdiotActorSession();
		SeedRequiredFactionBeneficiaryFacts(session);
		if (block == BorrowedVillageIdiotPardonBlock.Expired)
		{
			session.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		}
		session.TransitionMainPhase(GamePhase.Day);
		if (block == BorrowedVillageIdiotPardonBlock.Suppressed)
		{
			session.CommitGameFact(context =>
				new VillagerRolePowerSuppressionCommittedLogEntry
				{
					Timestamp = context.Timestamp,
					TurnNumber = context.TurnNumber,
					CurrentPhase = context.CurrentPhase,
					AnnouncementInstructionId = Guid.NewGuid()
				});
		}
		var debate = Advance(session, start.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = Advance(session, debate.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var reveal = Advance(session, vote.CreateResponse([actorId]))
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		var elimination = Advance(session, reveal.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		elimination.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		IGameSession completed = session;
		completed.GetPlayerState(actorId).Health.Should().Be(PlayerHealth.Dead);
		completed.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		completed.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == actorId &&
				entry.Reason == EliminationReason.DayVote);
	}

	[Fact]
	public void BorrowedVillageIdiot_PendingActorSafePardonRoundTripsAndAcknowledgesWithoutDuplicate()
	{
		var (session, pardon, _, actorId) =
			CreatePendingBorrowedVillageIdiotPardon();
		IGameSession interrupted = session;
		var markerCountBeforeRecovery = interrupted.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Count();
		markerCountBeforeRecovery.Should().Be(1);

		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			session.SerializeRecoverySnapshot());
		var recoveredPardon = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredPardon.InstructionId.Should().Be(pardon.InstructionId);
		recoveredPardon.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon);
		recoveredPardon.PublicAnnouncement.Should().Be(
			pardon.PublicAnnouncement);
		recoveredPardon.PrivateInstruction.Should().Be(
			pardon.PrivateInstruction);
		recoveredPardon.AffectedPlayerIds.Should().Equal(actorId);
		recoveredPardon.PublicAnnouncement.Should()
			.Contain(GameStrings.ActorRoleName)
			.And.NotContain(GameStrings.VillageIdiotRoleName);

		var acknowledgement = recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredPardon.CreateResponse());

		acknowledgement.IsSuccess.Should().BeTrue();
		acknowledgement.ModeratorInstruction.Should().NotBeNull();
		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		var actor = recovered.GetPlayerState(actorId);
		actor.CurrentRole.Should().Be(MainRoleType.Actor);
		actor.PubliclyRevealedRole.Should().Be(MainRoleType.Actor);
		actor.Health.Should().Be(PlayerHealth.Alive);
		actor.HasVotingRight.Should().BeFalse();
		actor.DurableVotingPower.Should().Be(0);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().HaveCount(markerCountBeforeRecovery);
		recovered.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == actorId);
	}

	[Fact]
	public void BorrowedVillageIdiot_SupersededRevealResponseIsRejectedWithoutMutationOrLeak()
	{
		var (session, pardon, supersededRevealResponse, actorId) =
			CreatePendingBorrowedVillageIdiotPardon();
		IGameSession interrupted = session;
		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			session.SerializeRecoverySnapshot());
		var recoveredPardon = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		var serializedBeforeStaleResponse = recoveredService.SerializeSession(
			recoveredGameId);
		var historyBeforeStaleResponse = recovered.GameHistoryLog.ToArray();

		Action submitSupersededResponse = () =>
			recoveredService.ProcessInstruction(
				recoveredGameId,
				supersededRevealResponse);

		submitSupersededResponse.Should().Throw<InvalidOperationException>()
			.WithMessage("*pending Moderator Instruction*");
		recoveredService.SerializeSession(recoveredGameId).Should().Be(
			serializedBeforeStaleResponse);
		recovered.GameHistoryLog.Should().Equal(historyBeforeStaleResponse);
		recoveredService.GetCurrentInstruction(recoveredGameId)!.InstructionId
			.Should().Be(recoveredPardon.InstructionId)
			.And.Be(pardon.InstructionId);
		var actor = recovered.GetPlayerState(actorId);
		actor.Health.Should().Be(PlayerHealth.Alive);
		actor.HasVotingRight.Should().BeFalse();
		actor.DurableVotingPower.Should().Be(0);
		var marker = recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		marker.ToString().Should()
			.NotContain(MainRoleType.VillageIdiot.ToString())
			.And.NotContain(VillageIdiotCard.Id.ToString())
			.And.NotContain(actorId.ToString());
		recoveredPardon.PublicAnnouncement.Should()
			.Contain(GameStrings.ActorRoleName)
			.And.NotContain(GameStrings.VillageIdiotRoleName);
	}

	private static PendingBorrowedScapegoatVoterSelection
		CreatePendingBorrowedScapegoatVoterSelection()
	{
		var fixture = CreateActiveBorrowedScapegoatVote();
		var session = fixture.Session;
		var players = session.GetPlayers().ToArray();
		var actorId = fixture.ActorId;
		var reactionVictimId = fixture.ReactionVictimId;
		var permittedVoterId = fixture.PermittedVoterId;
		var reaction = fixture.Reaction;

		var reveal = Advance(session, fixture.Vote.CreateResponse([]))
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		reveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealScapegoatForTie);
		reveal.AffectedPlayerIds.Should().Equal(actorId);
		reveal.PublicAnnouncement.Should()
			.Contain(GameStrings.ActorRoleName)
			.And.NotContain(GameStrings.ScapegoatRoleName);
		var beforeReveal = (IGameSession)session;
		beforeReveal.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();

		var selection = Advance(session, reveal.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		selection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectScapegoatPermittedVoters);
		selection.CountConstraint.Should().Be(
			NumberRangeConstraint.AtLeast(1));
		var candidateSnapshot = players
			.Where(player => player.Id != actorId)
			.Select(player => player.Id)
			.ToHashSet();
		selection.SelectablePlayerIds.Should().BeEquivalentTo(candidateSnapshot);
		reaction.InvocationCount.Should().Be(0);
		var actor = ((IGameSession)session).GetPlayerState(actorId);
		actor.CurrentRole.Should().Be(MainRoleType.Actor);
		actor.PubliclyRevealedRole.Should().Be(MainRoleType.Actor);
		actor.Health.Should().Be(PlayerHealth.Dead);
		var history = ((IGameSession)session).GameHistoryLog.ToArray();
		var revealIndex = Array.FindIndex(
			history,
			entry => entry is RoleRevealLogEntry roleReveal &&
				roleReveal.RevealedRoles.TryGetValue(
					actorId,
					out var revealedRole) &&
				revealedRole == MainRoleType.Actor);
		var markerIndex = Array.FindIndex(
			history,
			entry => entry is ActorBorrowedRolePowerCommittedLogEntry);
		var eliminationIndex = Array.FindIndex(
			history,
			entry => entry is PlayerEliminatedLogEntry
			{
				PlayerId: var eliminatedPlayerId,
				Reason: EliminationReason.EventElimination
			} && eliminatedPlayerId == actorId);
		revealIndex.Should().BeGreaterThanOrEqualTo(0);
		markerIndex.Should().BeGreaterThan(revealIndex);
		eliminationIndex.Should().BeGreaterThan(markerIndex);
		history.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle()
			.Which.ToString().Should()
			.NotContain(MainRoleType.Scapegoat.ToString())
			.And.NotContain(ScapegoatCard.Id.ToString())
			.And.NotContain(actorId.ToString());
		history.OfType<ScapegoatTieReplacementLogEntry>()
			.Should().BeEmpty();
		history.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == actorId &&
				entry.Reason == EliminationReason.EventElimination)
			.And.NotContain(entry =>
				entry.PlayerId == actorId &&
				entry.Reason == EliminationReason.ScapegoatSacrifice);

		return new PendingBorrowedScapegoatVoterSelection(
			session,
			selection,
			reaction,
			actorId,
			reactionVictimId,
			permittedVoterId,
			candidateSnapshot,
			"Day:1:Vote:1");
	}

	private static BorrowedScapegoatVoteFixture
		CreateActiveBorrowedScapegoatVote(
			IRolePowerAvailabilityPolicy? availabilityPolicy = null,
			bool suppressionActive = false,
			bool nativeScapegoatHolderKnowledgeUnknown = false)
	{
		var setup = new ActorSetupCards(
			version: 7,
			[ScapegoatCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[
				GameStrings.ActorRoleName,
				"Werewolf",
				"Reaction victim",
				"Permitted voter",
				"Villager 3",
				"Villager 4"
			],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var reactionVictimId = players[2].Id;
		var permittedVoterId = players[3].Id;
		foreach (var player in players)
		{
			if (nativeScapegoatHolderKnowledgeUnknown &&
				player.Id == reactionVictimId)
			{
				continue;
			}

			session.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Name == "Werewolf"
						? MainRoleType.SimpleWerewolf
						: MainRoleType.SimpleVillager);
		}

		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actorId,
			actorCard.Card.Id).Should().BeTrue();
		session.IdentifyRole([actorId], MainRoleType.Actor);
		session.TrySpendActorSetupCard(
			actorId,
			ScapegoatCard.Id,
			out _).Should().BeTrue();
		session.GetOrCreateListener(
			ListenerIdentifier.Listener(MainRoleType.Scapegoat),
			() => new ScapegoatRole(
				new RolePowerAvailabilityGateway(
					new VillagerRolePowerSuppressionPolicy(
						availabilityPolicy ??
						AllowAllRolePowerAvailabilityPolicy.Instance))));
		var reaction = new BorrowedScapegoatForcedReaction(
			actorId,
			reactionVictimId);
		EliminationCascadeRuntimeStore.Configure(
			session,
			[
				new EliminationCascadeReactionBinding(
					reaction,
					EliminationCascadeReactionBoundary.Forced)
			]);
		SeedRequiredFactionBeneficiaryFacts(session);
		session.TransitionMainPhase(GamePhase.Day);
		if (suppressionActive)
		{
			session.CommitGameFact(context =>
				new VillagerRolePowerSuppressionCommittedLogEntry
				{
					Timestamp = context.Timestamp,
					TurnNumber = context.TurnNumber,
					CurrentPhase = context.CurrentPhase,
					AnnouncementInstructionId = Guid.NewGuid()
				});
		}

		var debate = Advance(session, start.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = Advance(session, debate.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		return new BorrowedScapegoatVoteFixture(
			session,
			vote,
			reaction,
			actorId,
			reactionVictimId,
			permittedVoterId);
	}

	private static GameService CreateScapegoatService(
		BorrowedScapegoatForcedReaction reaction) =>
		new(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[
				new EliminationCascadeReactionBinding(
					reaction,
					EliminationCascadeReactionBoundary.Forced)
			]);

	private static string StripBorrowedScapegoatTieReplacementLineage(
		string serializedSession)
	{
		var options = new JsonSerializerOptions
		{
			Converters =
			{
				new GameResultConverter(),
				new GameLogEntryConverter(),
				new ModeratorInstructionConverter(),
				new JsonStringEnumConverter()
			}
		};
		var payload = JsonSerializer.Deserialize<GameSessionDto>(
			serializedSession,
			options)
			?? throw new InvalidOperationException(
				"The recovery test payload could not be deserialized.");
		if (payload.ActorBorrowedScapegoatTieReplacementCommits is not
				[var tieReplacement] ||
			tieReplacement.PublicMarkerLogIndex < 0 ||
			tieReplacement.PublicMarkerLogIndex >=
				payload.GameHistoryLog.Count ||
			payload.GameHistoryLog[tieReplacement.PublicMarkerLogIndex] is not
				ActorBorrowedRolePowerCommittedLogEntry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no correlated borrowed Scapegoat tie-replacement lineage.");
		}

		payload.ActorBorrowedScapegoatTieReplacementCommits.Clear();
		payload.GameHistoryLog.RemoveAt(
			tieReplacement.PublicMarkerLogIndex);
		foreach (var restriction in
			payload.ActorBorrowedScapegoatVoterRestrictionCommits)
		{
			if (restriction.PublicMarkerLogIndex >
				tieReplacement.PublicMarkerLogIndex)
			{
				restriction.PublicMarkerLogIndex--;
			}
		}

		return JsonSerializer.Serialize(payload, options);
	}

	private static (
		GameSession Session,
		ConfirmationInstruction Pardon,
		ModeratorResponse SupersededRevealResponse,
		Guid ActorId) CreatePendingBorrowedVillageIdiotPardon()
	{
		var (session, start, actorId) = CreateActiveVillageIdiotActorSession();
		SeedRequiredFactionBeneficiaryFacts(session);
		session.TransitionMainPhase(GamePhase.Day);
		var debate = Advance(session, start.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = Advance(session, debate.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var reveal = Advance(session, vote.CreateResponse([actorId]))
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var supersededRevealResponse = reveal.CreateResponse();
		var pardon = Advance(session, supersededRevealResponse)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		return (session, pardon, supersededRevealResponse, actorId);
	}

	private static void SeedRequiredFactionBeneficiaryFacts(
		GameSession session)
	{
		var players = session.GetPlayers().ToArray();
		var werewolfId = players.Single(player => player.Name == "Werewolf").Id;
		FactionFactEffectiveBoundary? agentGroupBoundary = null;
		session.CommitFactionFactBatch(context =>
		{
			var boundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			agentGroupBoundary = boundary;
			return new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ScheduledObservation,
					FactionFactSource
						.WerewolfFactionAgentGroupObservationIdentifier),
				Facts = players.Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						player.Id == werewolfId
							? FactionAgentKnowledge.KnownAgent
							: FactionAgentKnowledge.KnownNonAgent,
						boundary))
					.ToImmutableArray()
			};
		});

		InitialBeneficiaryClosureRules.TryCommitCurrentSession(
				session,
				agentGroupBoundary)
			.Should().Be(InitialBeneficiaryClosureResult.Committed);
		players.Should().OnlyContain(player =>
			session.GetFactionBeneficiaryKnowledge(player.Id).IsKnown);
	}

	private static (
		GameSession Session,
		StartGameConfirmationInstruction Start,
		Guid ActorId) CreateActiveVillageIdiotActorSession()
	{
		var setup = new ActorSetupCards(
			version: 7,
			[VillageIdiotCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Werewolf", "Villager 1", "Villager 2", "Villager 3", "Villager 4"],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		foreach (var player in players)
		{
			session.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Name == "Werewolf"
						? MainRoleType.SimpleWerewolf
						: MainRoleType.SimpleVillager);
		}

		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actorId,
			actorCard.Card.Id).Should().BeTrue();
		session.IdentifyRole([actorId], MainRoleType.Actor);
		session.TrySpendActorSetupCard(
			actorId,
			VillageIdiotCard.Id,
			out _).Should().BeTrue();
		session.GetOrCreateListener(
			ListenerIdentifier.Listener(MainRoleType.VillageIdiot),
			() => new VillageIdiotRole(
				new RolePowerAvailabilityGateway(
					new VillagerRolePowerSuppressionPolicy(
						AllowAllRolePowerAvailabilityPolicy.Instance))));
		return (session, start, actorId);
	}

	private static ModeratorInstruction Advance(
		GameSession session,
		ModeratorResponse response) =>
		GameFlowManager.HandleInput(
			session,
			response,
			SupportedRoleCatalog.Admissions).ModeratorInstruction
		?? throw new InvalidOperationException(
			"The Actor borrowed Village Idiot fixture expected an instruction.");

	private sealed record PendingBorrowedScapegoatVoterSelection(
		GameSession Session,
		SelectPlayersInstruction Selection,
		BorrowedScapegoatForcedReaction Reaction,
		Guid ActorId,
		Guid ReactionVictimId,
		Guid PermittedVoterId,
		HashSet<Guid> CandidateSnapshot,
		string ScopeId);

	private sealed record BorrowedScapegoatVoteFixture(
		GameSession Session,
		SelectPlayersInstruction Vote,
		BorrowedScapegoatForcedReaction Reaction,
		Guid ActorId,
		Guid ReactionVictimId,
		Guid PermittedVoterId);

	public enum InvalidFixedVoterResponse
	{
		EmptySelection,
		OutsideCandidate,
		StaleInstruction
	}

	public enum BorrowedScapegoatTriggerBlock
	{
		Unavailable,
		Suppressed
	}

	public enum BorrowedVillageIdiotPardonBlock
	{
		Suppressed,
		Expired
	}

	private sealed class DenyAllRolePowerAvailabilityPolicy
		: IRolePowerAvailabilityPolicy
	{
		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt) =>
			RolePowerAvailabilityResult.Denied;
	}

	private sealed class BorrowedScapegoatForcedReaction(
		Guid actorId,
		Guid victimId) : IEliminationCascadeReaction
	{
		public string ReactionId => nameof(BorrowedScapegoatForcedReaction);
		internal int InvocationCount { get; private set; }

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input)
		{
			if (!eliminatedPlayerIds.Contains(actorId))
			{
				return EliminationCascadeReactionResult.Complete();
			}

			InvocationCount++;
			return EliminationCascadeReactionResult.Complete(
			[
				new EliminationRequest(
					victimId,
					EliminationReason.EventElimination)
			]);
		}
	}

}
