using System.Collections.Immutable;
using FluentAssertions;
using Werewolves.Core.GameLogic;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedStutteringJudgeTests
{
	private static readonly PhysicalCharacterCard StutteringJudgeCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000145"),
		MainRoleType.StutteringJudge);
	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000146"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard FoxCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000147"),
		MainRoleType.Fox);

	private sealed class TestExecutionCommitKey : IGameFlowManagerKey;

	private static readonly TestExecutionCommitKey ExecutionCommitKey = new();

	private static readonly SubPhaseManager<NightSubPhases>
		NightMainActionLoop = new(
			NightSubPhases.Start,
			[
				HookSubPhaseStage.HookStage(GameHook.NightMainActionLoop),
				NavigationSubPhaseStage.NavigationEndStageSilent(GamePhase.Dawn)
			]);

	[Fact]
	public void BorrowedStutteringJudge_LaterNightSetupUsesActorAudienceAndActivationQualifiedCompletion()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var opening = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
		var activation = opening.Activation;
		IGameHookListener listener = new StutteringJudgeRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));

		var wake = opening.BorrowedRoleWake;
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		wake.PrivateInstruction.Should().BeNull();
		wake.AffectedPlayerIds.Should().Equal(actorId);

		var setup = Advance(
				listener,
				session,
				wake.CreateResponse(),
				publishInstruction: true).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		setup.Semantic.Should().Be(
			ModeratorInstructionSemantic.EstablishStutteringJudgeSignal);
		setup.PublicAnnouncement.Should().BeNull();
		setup.PrivateInstruction.Should().Be(
			GameStrings.StutteringJudgeSignalSetupInstruction);
		setup.AffectedPlayerIds.Should().Equal(actorId);
		var setupResponse = setup.CreateResponse();
		setupResponse.Type.Should().Be(ExpectedInputType.Continue);
		var logCountBeforeSetup = session.GameHistoryLog.Count();

		var sleep = Advance(
				listener,
				session,
				setupResponse,
				publishInstruction: true).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		var publicCommit = session.GameHistoryLog.Skip(logCountBeforeSetup)
			.Should().ContainSingle().Subject;
		publicCommit.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		session.GameHistoryLog.OfType<StutteringJudgeSignalEstablishedLogEntry>()
			.Should().BeEmpty();
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.StutteringJudge,
			"stuttering-judge-consecutive-vote",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
				session,
				powerIdentity)
			.Should().BeTrue();
		GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
				session,
				powerIdentity with { PowerInstanceId = Guid.NewGuid() })
			.Should().BeFalse();
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.StutteringJudge);
		session.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
	}

	[Fact]
	public void BorrowedStutteringJudge_PendingSetupRecoversAtActorAudienceAndCommitsOnce()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var opening = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
		var activation = opening.Activation;
		IGameHookListener listener = new StutteringJudgeRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var wake = opening.BorrowedRoleWake;
		var setup = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var serialized = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(setup)
			.Serialize();
		var historyCountBeforeSetup = session.GameHistoryLog.Count();
		var service = new GameService();

		var gameId = service.RehydrateSession(serialized);
		var recoveredSetup = service.GetCurrentInstruction(gameId).Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		recoveredSetup.InstructionId.Should().Be(setup.InstructionId);
		recoveredSetup.Semantic.Should().Be(
			ModeratorInstructionSemantic.EstablishStutteringJudgeSignal);
		recoveredSetup.PublicAnnouncement.Should().BeNull();
		recoveredSetup.PrivateInstruction.Should().Be(
			GameStrings.StutteringJudgeSignalSetupInstruction);
		recoveredSetup.AffectedPlayerIds.Should().Equal(actorId);

		var sleep = service.ProcessInstruction(
				gameId,
				recoveredSetup.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var recovered = (GameSession)service.GetGameStateView(gameId)!;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		recovered.GetActorBorrowedStutteringJudgeSignalSetupCommits().Should()
			.ContainSingle(commit =>
				commit.PowerIdentity.PowerInstanceId == activation.ActivationId);
		recovered.GameHistoryLog.Skip(historyCountBeforeSetup)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
	}

	[Fact]
	public void BorrowedStutteringJudge_SetupRecoversAtActorSleepWithoutDuplicate()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var opening = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
		var activation = opening.Activation;
		IGameHookListener listener = new StutteringJudgeRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var wake = opening.BorrowedRoleWake;
		var setup = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.StutteringJudge,
			"stuttering-judge-consecutive-vote",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		var historyCountBeforeSetup = session.GameHistoryLog.Count();
		session = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(setup)
			.RehydrateGameSession();

		var sleep = GameFlowManager.HandleInput(
				session,
				setup.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.PublicAnnouncement.Should().NotContain(
			GameStrings.StutteringJudgeRoleName);
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		var setupCommit = session
			.GetActorBorrowedStutteringJudgeSignalSetupCommits()
			.Single();
		setupCommit.PowerIdentity.Should().Be(powerIdentity);
		setupCommit.ActorSetupCardId.Should().Be(StutteringJudgeCard.Id);
		var marker = session.GameHistoryLog.Skip(historyCountBeforeSetup)
			.Should().ContainSingle().Subject;
		marker.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		marker.ToString().Should()
			.NotContain(GameStrings.StutteringJudgeRoleName)
			.And.NotContain(GameStrings.StutteringJudgeSignalSetupInstruction)
			.And.NotContain(actorId.ToString())
			.And.NotContain(activation.ActivationId.ToString())
			.And.NotContain(StutteringJudgeCard.Id.ToString());
		session.GameHistoryLog.OfType<StutteringJudgeSignalEstablishedLogEntry>()
			.Should().BeEmpty();

		var recoveryService = new GameService();
		var recoveredGameId = recoveryService.RehydrateSession(
			session.SerializeRecoverySnapshot());
		var recovered = (GameSession)recoveryService
			.GetGameStateView(recoveredGameId)!;
		var recoveredSleep = recoveryService
			.GetCurrentInstruction(recoveredGameId).Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		recoveredSleep.InstructionId.Should().Be(sleep.InstructionId);
		recoveredSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		recovered.GetActorBorrowedStutteringJudgeSignalSetupCommits().Should()
			.ContainSingle(commit => commit.PowerIdentity == powerIdentity);
		recovered.GameHistoryLog.Skip(historyCountBeforeSetup).Should()
			.ContainSingle(entry =>
				entry is ActorBorrowedRolePowerCommittedLogEntry);
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
	}

	[Fact]
	public void BorrowedStutteringJudge_SignalOccurredAfterFirstVoteCommitsAndRecoversConsecutiveVote()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var opening = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
		var activation = opening.Activation;
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.StutteringJudge,
			"stuttering-judge-consecutive-vote",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		var resourceIdentity = new OneUseRolePowerResourceIdentity(
			actorId,
			MainRoleType.StutteringJudge,
			"stuttering-judge-consecutive-vote",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed,
			Guid.Parse("85ff5eb7-61cf-4b33-894c-b9c37d58bace"));
		session.CommitActorBorrowedStutteringJudgeSignalSetup(powerIdentity);
		session.GetActorBorrowedStutteringJudgeSignalSetupCommits().Should()
			.ContainSingle(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.CurrentPhase == GamePhase.Night);
		session.TransitionMainPhase(GamePhase.Day);

		var debate = GameFlowManager.HandleInput(
				session,
				opening.BorrowedRoleWake.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var conductVote = GameFlowManager.HandleInput(
				session,
				debate.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		conductVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.ConductDayVote);
		var signal = GameFlowManager.HandleInput(
				session,
				conductVote.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		signal.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveStutteringJudgeSignal);
		signal.PublicAnnouncement.Should().BeNull();
		signal.PrivateInstruction.Should().Be(
			GameStrings.StutteringJudgeSignalObservationInstruction);
		signal.AffectedPlayerIds.Should().Equal(actorId);
		signal.Options.Select(option => option.Id).Should().Contain(
			StutteringJudgeSignalOptionIds.Occurred);
		session.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>().Should()
			.BeEmpty();
		var historyCountBeforeObservation = session.GameHistoryLog.Count();

		var firstVote = GameFlowManager.HandleInput(
				session,
				signal.CreateResponse(StutteringJudgeSignalOptionIds.Occurred),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		firstVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		ActorBorrowedStutteringJudgeSignalObservationCommit observation = session
			.GetActorBorrowedStutteringJudgeSignalObservationCommits()
			.Single(commit => commit.PowerIdentity == powerIdentity);
		observation.SignalOccurred.Should().BeTrue();
		observation.SpentResourceIdentity.Should().Be(resourceIdentity);
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			session,
			resourceIdentity).Should().BeTrue();
		var publicMarker = session.GameHistoryLog
			.Skip(historyCountBeforeObservation)
			.Should().ContainSingle().Subject;
		publicMarker.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		publicMarker.ToString().Should()
			.NotContain(MainRoleType.StutteringJudge.ToString())
			.And.NotContain(actorId.ToString())
			.And.NotContain(activation.ActivationId.ToString())
			.And.NotContain(resourceIdentity.OneUseResourceId.ToString());
		var cursor = RecoveryPayloadTestDriver.Capture(session)
			.DomainRecoveryCursor;
		cursor.Should().NotBeNull();
		cursor!.Kind.Should().Be(
			DomainRecoveryCursorKind.ActorBorrowedStutteringJudgeSignalObservationCommit);
		cursor.PowerIdentity.Should().Be(powerIdentity);
		cursor.ResourceIdentity.Should().Be(resourceIdentity);
		cursor.CommittedDayActionType.Should().Be(DayPowerType.JudgeExtraVote);
		cursor.NextInstructionId.Should().Be(firstVote.InstructionId);
		var targetRoleReveal = GameFlowManager.HandleInput(
				session,
				firstVote.CreateResponse([actorId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		targetRoleReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDayVoteTargetRole);
		targetRoleReveal.AffectedPlayerIds.Should().Equal(actorId);
		var firstAnnouncement = GameFlowManager.HandleInput(
				session,
				targetRoleReveal.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		firstAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		session.GetPlayerState(actorId).Health.Should().Be(PlayerHealth.Dead);
		session.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>().Should()
			.ContainSingle(entry => entry.ReportedOutcomePlayerId == actorId);

		var repeatedVote = GameFlowManager.HandleInput(
				session,
				firstAnnouncement.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		DayVoteRules.ShouldConductConsecutiveVote(session).Should().BeTrue();
		repeatedVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		repeatedVote.PublicAnnouncement.Should().Be(
			GameStrings.VoteStartsPublicInstruction);
		repeatedVote.SelectablePlayerIds.Should().NotContain(actorId);
		RecoveryPayloadTestDriver.Capture(session).DomainRecoveryCursor.Should()
			.BeNull();
		ActivateVillagerRolePowerSuppression(session);
		var serialized = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(repeatedVote)
			.Serialize();
		var recoveryService = new GameService();
		var recoveredGameId = recoveryService.RehydrateSession(serialized);
		var recovered = (GameSession)recoveryService
			.GetGameStateView(recoveredGameId)!;
		recovered.GetPlayerState(actorId).Health.Should().Be(PlayerHealth.Dead);
		GameSessionQueries.IsVillagerRolePowerSuppressionActive(recovered).Should()
			.BeTrue();
		var recoveredRepeatedVote = recoveryService
			.GetCurrentInstruction(recoveredGameId).Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		recoveredRepeatedVote.InstructionId.Should().Be(repeatedVote.InstructionId);
		recoveredRepeatedVote.SelectablePlayerIds.Should().NotContain(actorId);
		recovered.GetActorBorrowedStutteringJudgeSignalObservationCommits()
			.Should().ContainSingle(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.SpentResourceIdentity == resourceIdentity);
		recovered.GameHistoryLog.Skip(historyCountBeforeObservation)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
		recovered.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>().Should()
			.ContainSingle(entry => entry.ReportedOutcomePlayerId == actorId);
		DayVoteRules.ShouldConductConsecutiveVote(recovered).Should().BeTrue();

		var afterRepeat = GameFlowManager.HandleInput(
			recovered,
			recoveredRepeatedVote.CreateResponse([]),
			SupportedRoleCatalog.Admissions).ModeratorInstruction;
		afterRepeat!.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.RecordDayVote);
		recovered.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>().Should()
			.HaveCount(2);
		recovered.GetCurrentPhase().Should().Be(GamePhase.Night);
		recovered.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		var durableCommit = recovered
			.GetActorBorrowedStutteringJudgeSignalObservationCommits()
			.Should().ContainSingle().Subject;
		durableCommit.PowerIdentity.Should().Be(powerIdentity);
		durableCommit.SpentResourceIdentity.Should().Be(resourceIdentity);
	}

	[Fact]
	public void BorrowedStutteringJudge_PendingObservationRejectsMismatchedActiveActivationOnRecovery()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var opening = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
		var activation = opening.Activation;
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.StutteringJudge,
			"stuttering-judge-consecutive-vote",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		session.CommitActorBorrowedStutteringJudgeSignalSetup(powerIdentity);
		session.TransitionMainPhase(GamePhase.Day);
		var debate = GameFlowManager.HandleInput(
				session,
				opening.BorrowedRoleWake.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var conductVote = GameFlowManager.HandleInput(
				session,
				debate.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var signal = GameFlowManager.HandleInput(
				session,
				conductVote.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		session.TransitionMainPhase(GamePhase.Night);
		session.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		session.TrySpendActorSetupCard(actorId, SeerCard.Id, out _).Should().BeTrue();
		session.TransitionMainPhase(GamePhase.Day);
		var serialized = RecoveryPayloadTestDriver.Capture(session)
			.WithSubPhase(DaySubPhases.NormalVoting)
			.WithPendingInstruction(signal)
			.Serialize();
		var service = new GameService();

		var rehydrate = () => service.RehydrateSession(serialized);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*Stuttering Judge signal instruction*");
		service.GetGameStateView(session.Id).Should().BeNull();
	}

	[Fact]
	public void BorrowedStutteringJudge_CursorlessObservationRejectsSignalAndResourceTamper()
	{
		var session = CreateCursorlessCommittedJudgeObservationSession();
		RecoveryPayloadTestDriver.Parse(session.SerializeRecoverySnapshot())
			.DomainRecoveryCursor.Should().BeNull();
		Action rehydrateUntampered = () =>
			new GameSession(session.SerializeRecoverySnapshot());
		rehydrateUntampered.Should().NotThrow();
		var tampered = RecoveryPayloadTestDriver
			.Parse(session.SerializeRecoverySnapshot())
			.MutateActorBorrowedPrivateCommit(
				ActorBorrowedPrivateCommitMutation
					.JudgeObservationSignalAndResource)
			.Serialize();

		Action rehydrateTampered = () => new GameSession(tampered);

		rehydrateTampered.Should().Throw<InvalidOperationException>()
			.WithMessage("*Actor borrowed Role Power*");
	}

	[Theory]
	[InlineData(JudgeVoteRecoveryPresentationTamper.PublicAnnouncement)]
	[InlineData(JudgeVoteRecoveryPresentationTamper.PrivateInstruction)]
	[InlineData(JudgeVoteRecoveryPresentationTamper.EmptySelectionOptionLabel)]
	public void BorrowedStutteringJudge_TamperedRecordDayVotePresentationIsRejectedWithoutSessionMutation(
		JudgeVoteRecoveryPresentationTamper tamper)
	{
		var fixture = CreateCommittedJudgeObservationBoundary();
		var pending = fixture.RecordDayVoteInstruction;
		pending.PublicAnnouncement.Should().BeNull();
		pending.PrivateInstruction.Should().Be(
			GameStrings.VoteStartsModeratorInstruction);
		pending.EmptySelectionOptionLabel.Should().Be(
			GameStrings.DayVoteNoEliminationOption);
		var tampered = RecoveryPayloadTestDriver
			.Parse(fixture.Session.SerializeRecoverySnapshot())
			.RewritePendingPlayerSelectionPresentation(
				tamper == JudgeVoteRecoveryPresentationTamper.PublicAnnouncement
					? "Tampered public announcement."
					: pending.PublicAnnouncement,
				tamper == JudgeVoteRecoveryPresentationTamper.PrivateInstruction
					? "Tampered private instruction."
					: pending.PrivateInstruction,
				tamper == JudgeVoteRecoveryPresentationTamper.EmptySelectionOptionLabel
					? "Tampered empty-selection label."
					: pending.EmptySelectionOptionLabel)
			.Serialize();
		var service = new GameService();

		Action rehydrateTampered = () => service.RehydrateSession(tampered);

		rehydrateTampered.Should().Throw<InvalidOperationException>()
			.WithMessage("*Stuttering Judge recovery cursor*");
		service.GetGameStateView(fixture.Session.Id).Should().BeNull();
	}

	[Fact]
	public void BorrowedStutteringJudge_PriorDayScapegoatRestrictionRecoversFormattedVoteGuidance()
	{
		var fixture = CreateCommittedJudgeObservationBoundary(
			withPriorDayScapegoatRestriction: true);
		var pending = fixture.RecordDayVoteInstruction;
		var expectedPrivateInstruction =
			GameStrings.ScapegoatEffectiveVotersInstruction.Format(
				string.Join(
					Environment.NewLine,
					"Werewolf",
					"Villager 1"));
		pending.PrivateInstruction.Should().Be(expectedPrivateInstruction);
		var service = new GameService();

		var gameId = service.RehydrateSession(
			fixture.Session.SerializeRecoverySnapshot());

		var recoveredVote = service.GetCurrentInstruction(gameId).Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		recoveredVote.InstructionId.Should().Be(pending.InstructionId);
		recoveredVote.PrivateInstruction.Should().Be(expectedPrivateInstruction);
	}

	[Fact]
	public void BorrowedStutteringJudge_RecoveryRejectsTwoSpendTamperWithDifferentActiveActivation()
	{
		var fixture = CreateCommittedJudgeObservationBoundary();
		Action rehydrateUntampered = () =>
			new GameSession(fixture.Session.SerializeRecoverySnapshot());
		rehydrateUntampered.Should().NotThrow();
		var tampered = RecoveryPayloadTestDriver
			.Parse(fixture.Session.SerializeRecoverySnapshot())
			.InjectSecondActorSpendAsActiveActivation(
				SeerCard.Id,
				Guid.NewGuid())
			.Serialize();

		Action rehydrateTampered = () => new GameSession(tampered);

		rehydrateTampered.Should().Throw<InvalidOperationException>()
			.WithMessage("*Stuttering Judge recovery cursor*");
	}

	[Fact]
	public void BorrowedStutteringJudge_SignalDidNotOccurPreservesPowerAndDoesNotScheduleRepeat()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var opening = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
		var activation = opening.Activation;
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.StutteringJudge,
			"stuttering-judge-consecutive-vote",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		var resourceIdentity = new OneUseRolePowerResourceIdentity(
			actorId,
			MainRoleType.StutteringJudge,
			"stuttering-judge-consecutive-vote",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed,
			Guid.Parse("85ff5eb7-61cf-4b33-894c-b9c37d58bace"));
		session.CommitActorBorrowedStutteringJudgeSignalSetup(powerIdentity);
		session.TransitionMainPhase(GamePhase.Day);
		var debate = GameFlowManager.HandleInput(
				session,
				opening.BorrowedRoleWake.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var conductVote = GameFlowManager.HandleInput(
				session,
				debate.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		conductVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.ConductDayVote);
		var signal = GameFlowManager.HandleInput(
				session,
				conductVote.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		signal.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveStutteringJudgeSignal);
		signal.PublicAnnouncement.Should().BeNull();
		signal.PrivateInstruction.Should().Be(
			GameStrings.StutteringJudgeSignalObservationInstruction);
		signal.AffectedPlayerIds.Should().Equal(actorId);
		var historyCountBeforeObservation = session.GameHistoryLog.Count();
		var markerCountBeforeObservation = session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Count();

		var firstVote = GameFlowManager.HandleInput(
				session,
				signal.CreateResponse(
					StutteringJudgeSignalOptionIds.DidNotOccur),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		firstVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		var observation = session
			.GetActorBorrowedStutteringJudgeSignalObservationCommits()
			.Single();
		observation.PowerIdentity.Should().Be(powerIdentity);
		observation.SignalOccurred.Should().BeFalse();
		observation.SpentResourceIdentity.Should().BeNull();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			session,
			resourceIdentity).Should().BeFalse();
		DayVoteRules.ShouldConductConsecutiveVote(session).Should().BeFalse();
		var marker = session.GameHistoryLog.Skip(historyCountBeforeObservation)
			.Should().ContainSingle().Subject;
		marker.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		marker.ToString().Should()
			.NotContain(GameStrings.StutteringJudgeRoleName)
			.And.NotContain(actorId.ToString())
			.And.NotContain(activation.ActivationId.ToString())
			.And.NotContain(resourceIdentity.OneUseResourceId.ToString());
		session.GameHistoryLog.OfType<StutteringJudgeSignalDidNotOccurLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog
			.OfType<OneUseRolePowerDayActionCommittedLogEntry>().Should()
			.BeEmpty();

		var recoveryService = new GameService();
		var recoveredGameId = recoveryService.RehydrateSession(
			session.SerializeRecoverySnapshot());
		var recovered = (GameSession)recoveryService
			.GetGameStateView(recoveredGameId)!;
		var recoveredFirstVote = recoveryService
			.GetCurrentInstruction(recoveredGameId).Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		recoveredFirstVote.InstructionId.Should().Be(firstVote.InstructionId);
		recovered.GetActorBorrowedStutteringJudgeSignalObservationCommits()
			.Should().ContainSingle(commit =>
				commit.PowerIdentity == powerIdentity &&
				!commit.SignalOccurred &&
				commit.SpentResourceIdentity == null);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.HaveCount(markerCountBeforeObservation + 1);
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			recovered,
			resourceIdentity).Should().BeFalse();
		DayVoteRules.ShouldConductConsecutiveVote(recovered).Should().BeFalse();

		var afterFirstVote = GameFlowManager.HandleInput(
			recovered,
			recoveredFirstVote.CreateResponse([]),
			SupportedRoleCatalog.Admissions).ModeratorInstruction;
		afterFirstVote!.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.RecordDayVote);
		recovered.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>().Should()
			.ContainSingle();
		recovered.GetActorBorrowedStutteringJudgeSignalObservationCommits()
			.Should().ContainSingle();
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			recovered,
			resourceIdentity).Should().BeFalse();
	}

	[Fact]
	public void BorrowedStutteringJudge_PositiveSignalWithoutEligibleRepeatVotersSkipsSecondPhysicalVote()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var opening = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
		var activation = opening.Activation;
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.StutteringJudge,
			"stuttering-judge-consecutive-vote",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		var resourceIdentity = new OneUseRolePowerResourceIdentity(
			actorId,
			MainRoleType.StutteringJudge,
			"stuttering-judge-consecutive-vote",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed,
			Guid.Parse("85ff5eb7-61cf-4b33-894c-b9c37d58bace"));
		session.CommitActorBorrowedStutteringJudgeSignalSetup(powerIdentity);
		session.TransitionMainPhase(GamePhase.Day);
		var debate = GameFlowManager.HandleInput(
				session,
				opening.BorrowedRoleWake.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var conductVote = GameFlowManager.HandleInput(
				session,
				debate.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var signal = GameFlowManager.HandleInput(
				session,
				conductVote.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var firstVote = GameFlowManager.HandleInput(
				session,
				signal.CreateResponse(StutteringJudgeSignalOptionIds.Occurred),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		firstVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		session.GetActorBorrowedStutteringJudgeSignalObservationCommits()
			.Should().ContainSingle(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.SignalOccurred &&
				commit.SpentResourceIdentity == resourceIdentity);
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			session,
			resourceIdentity).Should().BeTrue();
		DayVoteRules.ShouldConductConsecutiveVote(session).Should().BeFalse();
		var markerCount = session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Count();
		foreach (var player in session.GetPlayers()
			         .Where(player => player.State.Health == PlayerHealth.Alive))
		{
			session.SetPlayerVotingRight(player.Id, hasVotingRight: false);
		}
		DayVoteRules.GetEffectiveVoters(session).Should().BeEmpty();

		var nextNight = GameFlowManager.HandleInput(
				session,
				firstVote.CreateResponse([]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		nextNight.Semantic.Should().Be(ModeratorInstructionSemantic.StartNight);
		session.GetCurrentPhase().Should().Be(GamePhase.Night);
		session.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>().Should()
			.ContainSingle(entry => entry.ReportedOutcomePlayerId == Guid.Empty);
		session.GameHistoryLog
			.OfType<VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<ScapegoatTieReplacementLogEntry>().Should()
			.BeEmpty();
		session.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Where(entry => entry.CurrentPhase == GamePhase.Day)
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<EliminationCascadeCompletedLogEntry>()
			.Where(entry => entry.ScopeId.StartsWith(
				$"Day:{session.TurnNumber - 1}:Vote:",
				StringComparison.Ordinal))
			.Should().BeEmpty();
		session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.HaveCount(markerCount);
		session.GetActorBorrowedStutteringJudgeSignalObservationCommits()
			.Should().ContainSingle();
		session.GameHistoryLog
			.OfType<OneUseRolePowerDayActionCommittedLogEntry>().Should()
			.BeEmpty();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			session,
			resourceIdentity).Should().BeTrue();
		session.GameHistoryLog.OfType<VictoryConditionMetLogEntry>().Should()
			.BeEmpty();
	}

	private static ActorRole CreateActorRole() => new(
		new RolePowerAvailabilityGateway(
			new VillagerRolePowerSuppressionPolicy(
				AllowAllRolePowerAvailabilityPolicy.Instance)));

	private static CommittedJudgeObservationBoundary
		CreateCommittedJudgeObservationBoundary(
			bool withPriorDayScapegoatRestriction = false)
	{
		var (session, start, actorId) = CreateLaterNightActorSession(
			withPriorDayScapegoatRestriction);
		var opening = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
		var activation = opening.Activation;
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.StutteringJudge,
			"stuttering-judge-consecutive-vote",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		session.CommitActorBorrowedStutteringJudgeSignalSetup(powerIdentity);
		session.TransitionMainPhase(GamePhase.Day);
		var debate = GameFlowManager.HandleInput(
				session,
				opening.BorrowedRoleWake.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var conductVote = GameFlowManager.HandleInput(
				session,
				debate.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var signal = GameFlowManager.HandleInput(
				session,
				conductVote.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var recordDayVoteInstruction = GameFlowManager.HandleInput(
				session,
				signal.CreateResponse(StutteringJudgeSignalOptionIds.Occurred),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		return new CommittedJudgeObservationBoundary(
			session,
			recordDayVoteInstruction);
	}

	internal static GameSession
		CreateCursorlessCommittedJudgeObservationSession()
	{
		var fixture = CreateCommittedJudgeObservationBoundary();
		var serialized = RecoveryPayloadTestDriver.Capture(fixture.Session)
			.WithPendingInstruction(fixture.RecordDayVoteInstruction)
			.WithRecoveryCursors()
			.Serialize();
		return new GameSession(serialized);
	}

	private static void ActivateVillagerRolePowerSuppression(
		GameSession session)
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

	private static BorrowedRolePowerOpening PerformSpendOpening(
		IGameHookListener listener,
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid selectedCardId)
	{
		var wake = Advance(
			listener,
			session,
			start.CreateResponse(),
			publishInstruction: true).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(
			listener,
			session,
			wake.CreateResponse(),
			publishInstruction: true).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var sleep = Advance(
			listener,
			session,
			choice.CreateResponse(selectedCardId.ToString("D")),
			publishInstruction: true).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		var borrowedRoleWake = Advance(
				listener,
				session,
				sleep.CreateResponse(),
				publishInstruction: true).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		borrowedRoleWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		return new BorrowedRolePowerOpening(activation, borrowedRoleWake);
	}

	private static (
		GameSession Session,
		StartGameConfirmationInstruction Start,
		Guid ActorId) CreateLaterNightActorSession(
			bool withPriorDayScapegoatRestriction = false)
	{
		var setup = new ActorSetupCards(
			version: 7,
			[StutteringJudgeCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[
				GameStrings.ActorRoleName,
				"Werewolf",
				"Villager 1",
				"Villager 2",
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
		var actorId = session.GetPlayers().First().Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		foreach (var player in session.GetPlayers().Where(player => player.Id != actorId))
		{
			session.AssignRole(
				player.Id,
				player.Name == "Werewolf"
					? MainRoleType.SimpleWerewolf
					: MainRoleType.SimpleVillager);
		}
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { actorId },
			MainRoleType.Actor);
		SeedRequiredFactionBeneficiaryFacts(session);
		session.TransitionMainPhase(GamePhase.Day);
		if (withPriorDayScapegoatRestriction)
		{
			var players = session.GetPlayers().ToArray();
			var restrictionScope =
				"test-actor-borrowed-judge-prior-day-scapegoat-restriction";
			var announcementInstructionId = Guid.NewGuid();
			DayVoteRules.CommitVoterEligibilityRestriction(
				session,
				restrictionScope,
				MainRoleType.Scapegoat,
				players.Select(player => player.Id).ToArray(),
				players
					.Where(player => player.Name is "Werewolf" or "Villager 1")
					.Select(player => player.Id)
					.ToArray(),
				session.TurnNumber + 1,
				announcementInstructionId);
			DayVoteRules.AcknowledgeVoterEligibilityRestrictionAnnouncement(
				session,
				restrictionScope,
				announcementInstructionId);
		}
		session.TransitionMainPhase(GamePhase.Night);
		session.TurnNumber.Should().Be(2);
		session.RoleInPlayCount(MainRoleType.StutteringJudge).Should().Be(0);
		return (session, start, actorId);
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

	private static HookAdvanceResult Advance(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response,
		bool publishInstruction = false)
	{
		session.GetOrCreateListener(listener.Id, () => listener);
		var consumedInstruction = publishInstruction
			? session.Execution.PendingInstruction ??
			  throw new InvalidOperationException(
				  "The Actor borrowed Stuttering Judge fixture requires one Pending Instruction.")
			: null;
		var result = NightMainActionLoop.Execute(session, response);
		if (consumedInstruction != null &&
			result is StayInSubPhaseHandlerResult
			{
				StageComplete: false,
				ModeratorInstruction: { } publishedInstruction
			})
		{
			var publicationResponse =
				response.InstructionId == consumedInstruction.InstructionId
					? response
					: new ModeratorResponse
					{
						InstructionId = consumedInstruction.InstructionId,
						Type = response.Type,
						SelectedPlayerIds = response.SelectedPlayerIds,
						AssignedPlayerRoles = response.AssignedPlayerRoles,
						SelectedOptionIds = response.SelectedOptionIds
					};
			session.CommitExecution(
				ExecutionCommitKey,
				ExecutionCommit.RetainRecoveryBoundary(
					session.Execution,
					consumedInstruction,
					publicationResponse,
					publishedInstruction));
		}

		return result switch
		{
			StayInSubPhaseHandlerResult
			{
				StageComplete: false,
				ModeratorInstruction: { } instruction
			} => new HookAdvanceResult(instruction),
			StayInSubPhaseHandlerResult { StageComplete: true } =>
				new HookAdvanceResult(result.ModeratorInstruction),
			_ => throw new InvalidOperationException(
				"The Night Main Action Loop fixture produced an unexpected result.")
		};
	}

	private sealed record BorrowedRolePowerOpening(
		ActorBorrowedRolePowerActivation Activation,
		ConfirmationInstruction BorrowedRoleWake);

	private sealed record HookAdvanceResult(ModeratorInstruction? Instruction);

	public enum JudgeVoteRecoveryPresentationTamper
	{
		PublicAnnouncement,
		PrivateInstruction,
		EmptySelectionOptionLabel
	}
	private sealed record CommittedJudgeObservationBoundary(
		GameSession Session,
		SelectPlayersInstruction RecordDayVoteInstruction);
}
