using System.Collections.Immutable;
using FluentAssertions;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
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

	private static readonly TestSubPhaseManagerKey SubPhaseKey = new();
	private static readonly TestHookSubPhaseKey HookKey = new();
	private static readonly TestGameFlowManagerKey FlowKey = new();

	[Fact]
	public void BorrowedStutteringJudge_LaterNightSetupUsesActorAudienceAndActivationQualifiedCompletion()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var activation = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
		IGameHookListener listener = new StutteringJudgeRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));

		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		wake.PrivateInstruction.Should().BeNull();
		wake.AffectedPlayerIds.Should().Equal(actorId);

		var setup = Advance(listener, session, wake.CreateResponse()).Instruction
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

		var sleep = Advance(listener, session, setupResponse).Instruction
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
	public void BorrowedStutteringJudge_SetupRecoversAtActorSleepWithoutDuplicate()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var activation = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
		IGameHookListener listener = new StutteringJudgeRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var setup = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.StutteringJudge,
			"stuttering-judge-consecutive-vote",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		var historyCountBeforeSetup = session.GameHistoryLog.Count();
		session.SetPendingModeratorInstruction(FlowKey, setup);

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

		var recovered = new GameSession(session.Serialize());
		GameFlowManager.RestoreDurableContinuation(
			recovered,
			SupportedRoleCatalog.Admissions);
		var recoveredSleep = recovered.PendingModeratorInstruction.Should()
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
		var activation = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
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
		session.SetPendingModeratorInstruction(FlowKey, start);

		var debate = GameFlowManager.HandleInput(
				session,
				start.CreateResponse(),
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
		var cursor = session.GetDomainRecoveryCursor(FlowKey);
		cursor.Should().NotBeNull();
		cursor!.Kind.Should().Be(
			DomainRecoveryCursorKind.ActorBorrowedStutteringJudgeSignalObservationCommit);
		cursor.PowerIdentity.Should().Be(powerIdentity);
		cursor.ResourceIdentity.Should().Be(resourceIdentity);
		cursor.CommittedDayActionType.Should().Be(DayPowerType.JudgeExtraVote);
		cursor.NextInstructionId.Should().Be(firstVote.InstructionId);
		var targetRoleAssignment = GameFlowManager.HandleInput(
				session,
				firstVote.CreateResponse([actorId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		targetRoleAssignment.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDayVoteTargetRole);
		targetRoleAssignment.PlayersForAssignment.Should().Equal(actorId);
		var firstAnnouncement = GameFlowManager.HandleInput(
				session,
				targetRoleAssignment.CreateResponse(
					new Dictionary<Guid, MainRoleType>
					{
						[actorId] = MainRoleType.Actor
					}),
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
		session.GetDomainRecoveryCursor(FlowKey).Should().BeNull();
		ActivateVillagerRolePowerSuppression(session);
		session.CaptureRecoveryBoundary(FlowKey);

		var recovered = new GameSession(session.Serialize());
		GameFlowManager.RestoreDurableContinuation(
			recovered,
			SupportedRoleCatalog.Admissions);
		recovered.GetPlayerState(actorId).Health.Should().Be(PlayerHealth.Dead);
		GameSessionQueries.IsVillagerRolePowerSuppressionActive(recovered).Should()
			.BeTrue();
		var recoveredRepeatedVote = recovered.PendingModeratorInstruction.Should()
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
	public void BorrowedStutteringJudge_SignalDidNotOccurPreservesPowerAndDoesNotScheduleRepeat()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var activation = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
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
		session.SetPendingModeratorInstruction(FlowKey, start);
		var debate = GameFlowManager.HandleInput(
				session,
				start.CreateResponse(),
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

		var recovered = new GameSession(session.Serialize());
		GameFlowManager.RestoreDurableContinuation(
			recovered,
			SupportedRoleCatalog.Admissions);
		var recoveredFirstVote = recovered.PendingModeratorInstruction.Should()
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
		var activation = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			StutteringJudgeCard.Id);
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
		session.SetPendingModeratorInstruction(FlowKey, start);
		var debate = GameFlowManager.HandleInput(
				session,
				start.CreateResponse(),
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

	private static ActorBorrowedRolePowerActivation PerformSpendOpening(
		IGameHookListener listener,
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid selectedCardId)
	{
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var sleep = Advance(
			listener,
			session,
			choice.CreateResponse(selectedCardId.ToString("D"))).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		Advance(listener, session, sleep.CreateResponse()).Outcome.Should()
			.Be(HookListenerOutcome.Complete);
		session.ClearCurrentListenerCache(HookKey);
		return activation;
	}

	private static (
		GameSession Session,
		StartGameConfirmationInstruction Start,
		Guid ActorId) CreateLaterNightActorSession()
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
		session.IdentifyRole([actorId], MainRoleType.Actor);
		SeedRequiredFactionBeneficiaryFacts(session);
		session.TransitionMainPhase(GamePhase.Day);
		session.TransitionMainPhase(GamePhase.Night);
		session.TurnNumber.Should().Be(2);
		session.RoleInPlayCount(MainRoleType.StutteringJudge).Should().Be(0);
		session.TryEnterSubPhaseStage(
			SubPhaseKey,
			GameHook.NightMainActionLoop.ToString()).Should().BeTrue();
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

	private static HookListenerActionResult Advance(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response)
	{
		var result = listener.Execute(session, response);
		if (result.Outcome != HookListenerOutcome.Skip)
		{
			session.TransitionListenerStateCache(
				HookKey,
				listener.Id,
				result.NextListenerPhase!);
		}

		return result;
	}

	private sealed class TestSubPhaseManagerKey : ISubPhaseManagerKey;
	private sealed class TestHookSubPhaseKey : IHookSubPhaseKey;
	private sealed class TestGameFlowManagerKey : IGameFlowManagerKey;
}
