using FluentAssertions;
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
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedDefenderFoxTests
{
	private static readonly PhysicalCharacterCard DefenderCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000142"),
		MainRoleType.Defender);
	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000143"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard FoxCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000144"),
		MainRoleType.Fox);
	private static readonly SubPhaseManager<NightSubPhases> NightActionLoop =
		new(
			NightSubPhases.Start,
			[
				HookSubPhaseStage.HookStage(GameHook.NightMainActionLoop),
				NavigationSubPhaseStage.NavigationEndStageSilent(GamePhase.Dawn)
			]);

	[Fact]
	public void BorrowedDefender_SourceSlotUsesActorIdentityAndOffersActorSelfAsLegalTarget()
	{
		var (session, start, actorId, littleGirlId) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			DefenderCard.Id);
		var policy = new RecordingPolicy();
		IGameHookListener listener = new DefenderRole(
			new RolePowerAvailabilityGateway(policy));

		var wake = Advance(listener, session, actorSleep.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		wake.AffectedPlayerIds.Should().Equal(actorId);

		var targetSelection = Advance(listener, session, wake.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		targetSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectDefenderTarget);
		targetSelection.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		targetSelection.SelectablePlayerIds.Should()
			.Contain(actorId)
			.And.NotContain(littleGirlId);
		targetSelection.AffectedPlayerIds.Should().Equal(actorId);
		targetSelection.PrivateInstruction.Should().Be(
			GameStrings.DefenderTargetSelectionInstruction);

		var attempt = policy.ObservedAttempts.Should().ContainSingle().Subject;
		attempt.ActingPlayer.Id.Should().Be(actorId);
		attempt.ActingPlayer.State.CurrentRole.Should().Be(MainRoleType.Actor);
		attempt.SourceRole.Should().Be(MainRoleType.Defender);
		attempt.PowerInstance.Id.Should().Be(activation.ActivationId);
		attempt.PowerInstance.Origin.Should().Be(RolePowerInstanceOrigin.Borrowed);
		session.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.Defender);
	}

	[Fact]
	public void BorrowedDefender_MalformedSelectionUsesGenericErrorWithoutMutation()
	{
		var (session, start, actorId, _) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			DefenderCard.Id);
		IGameHookListener listener = new DefenderRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var wake = Advance(listener, session, actorSleep.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var selection = Advance(listener, session, wake.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var historyCount = session.GameHistoryLog.Count();
		var malformed = new ModeratorResponse
		{
			InstructionId = selection.InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = selection.SelectablePlayerIds.Take(2).ToHashSet()
		};

		var act = () => Advance(listener, session, malformed);

		act.Should().Throw<InvalidOperationException>().WithMessage(
			GameStrings.ActorBorrowedRolePowerInvalidResponse);
		session.GameHistoryLog.Should().HaveCount(historyCount);
		session.GetActorBorrowedDefenderProtectionCommits().Should().BeEmpty();
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
		session.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
	}

	[Fact]
	public void BorrowedDefender_StaleSelectionAfterCommitUsesGenericErrorWithoutDuplicate()
	{
		var (session, start, actorId, _) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			DefenderCard.Id);
		IGameHookListener listener = new DefenderRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var wake = Advance(listener, session, actorSleep.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var selection = Advance(listener, session, wake.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var selectedTargets = selection.SelectablePlayerIds.Take(2).ToArray();
		var powerIdentity = CreateBorrowedDefenderPowerIdentity(
			actorId,
			activation);
		session.CommitActorBorrowedDefenderProtection(
			powerIdentity,
			selectedTargets[0]);
		var committed = session.GetActorBorrowedDefenderProtectionCommits()
			.Should().ContainSingle().Subject;
		var historyCount = session.GameHistoryLog.Count();

		var act = () => Advance(
			listener,
			session,
			selection.CreateResponse([selectedTargets[1]]));

		act.Should().Throw<InvalidOperationException>().WithMessage(
			GameStrings.ActorBorrowedRolePowerInvalidResponse);
		session.GameHistoryLog.Should().HaveCount(historyCount);
		session.GetActorBorrowedDefenderProtectionCommits().Should()
			.Equal(committed);
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
	}

	[Fact]
	public void BorrowedDefender_CommitRecoversAtActorSleepAndBlocksAttackWithoutPublicLeak()
	{
		var (session, start, actorId, _) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			DefenderCard.Id);
		IGameHookListener listener = new DefenderRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var wake = Advance(listener, session, actorSleep.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var targetSelection = Advance(listener, session, wake.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var logCountBeforeProtection = session.GameHistoryLog.Count();
		session = RestorePendingInstruction(session, listener, targetSelection);

		var sleep = GameFlowManager.HandleInput(
				session,
				targetSelection.CreateResponse([actorId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		var publicCommit = session.GameHistoryLog.Skip(logCountBeforeProtection)
			.Should().ContainSingle().Subject;
		publicCommit.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		publicCommit.Should().NotBeAssignableTo<NightActionLogEntry>();
		publicCommit.ToString().Should().NotContain(MainRoleType.Defender.ToString());
		publicCommit.ToString().Should().NotContain(activation.ActivationId.ToString());
		publicCommit.ToString().Should().NotContain(actorId.ToString());
		session.GameHistoryLog.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.Defender);
		var privateCommit = session.GetActorBorrowedDefenderProtectionCommits()
			.Should().ContainSingle().Subject;
		privateCommit.PowerIdentity.ActingPlayerId.Should().Be(actorId);
		privateCommit.PowerIdentity.SourceRole.Should().Be(MainRoleType.Defender);
		privateCommit.PowerIdentity.PowerInstanceId.Should().Be(
			activation.ActivationId);
		privateCommit.PowerIdentity.PowerInstanceOrigin.Should().Be(
			RolePowerInstanceOrigin.Borrowed);
		privateCommit.ActorSetupCardId.Should().Be(DefenderCard.Id);
		privateCommit.TargetPlayerId.Should().Be(actorId);
		privateCommit.PublicMarkerLogIndex.Should().Be(logCountBeforeProtection);

		var recovered = new GameSession(session.Serialize());
		GameFlowManager.RestoreDurableContinuation(
			recovered,
			SupportedRoleCatalog.Admissions);
		var recoveredSleep = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredSleep.InstructionId.Should().Be(sleep.InstructionId);
		recoveredSleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		recoveredSleep.AffectedPlayerIds.Should().Equal(actorId);
		recovered.GameHistoryLog.Skip(logCountBeforeProtection)
			.Should().ContainSingle()
			.Which.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		recovered.GetActorBorrowedDefenderProtectionCommits().Should()
			.Equal(privateCommit);
		recovered.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);

		GameFlowManager.HandleInput(
			recovered,
			recoveredSleep.CreateResponse(),
			SupportedRoleCatalog.Admissions);
		recovered.GameHistoryLog.Skip(logCountBeforeProtection)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();

		recovered.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			actorId);
		NightInteractionResolver.ResolveNightPhase(recovered);

		recovered.GameHistoryLog.OfType<DawnVictimDeterminedLogEntry>().Should()
			.NotContain(entry => entry.PlayerId == actorId);
		recovered.GetPlayerState(actorId).Health.Should().Be(PlayerHealth.Alive);
	}

	[Fact]
	public void BorrowedFox_MalformedSelectionUsesGenericErrorWithoutMutation()
	{
		var (session, start, actorId, _) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			FoxCard.Id);
		var werewolfId = session.GetPlayers().Single(player =>
			player.Name == "Werewolf").Id;
		ArrangeKnownWerewolfAgentGroup(session, werewolfId);
		IGameHookListener listener = new FoxRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var wake = AdvancePastWerewolfToFoxWake(
			listener,
			session,
			actorSleep);
		var selection = Advance(listener, session, wake.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var historyCount = session.GameHistoryLog.Count();
		var malformed = new ModeratorResponse
		{
			InstructionId = selection.InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = selection.SelectablePlayerIds.Take(2).ToHashSet()
		};

		var act = () => Advance(listener, session, malformed);

		act.Should().Throw<InvalidOperationException>().WithMessage(
			GameStrings.ActorBorrowedRolePowerInvalidResponse);
		session.GameHistoryLog.Should().HaveCount(historyCount);
		session.GetActorBorrowedFoxCheckCommits().Should().BeEmpty();
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
		session.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
	}

	[Fact]
	public void BorrowedFox_KnownEmptyWerewolfOmissionRunsSourceSlotWithActorAudienceAndDeclineSleep()
	{
		var (session, start, actorId, _) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			FoxCard.Id);
		ArrangeKnownWerewolfAgentGroup(session, werewolfId: null);
		var policy = new RecordingPolicy();
		IGameHookListener listener = new FoxRole(
			new RolePowerAvailabilityGateway(policy));
		var wake = Advance(listener, session, actorSleep.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		session.GameHistoryLog.OfType<NightActionLogEntry>().Should()
			.NotContain(entry =>
				entry.ActionType == NightActionType.WerewolfVictimSelection);

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		wake.PrivateInstruction.Should().BeNull();
		wake.AffectedPlayerIds.Should().Equal(actorId);

		var centerSelection = Advance(listener, session, wake.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		centerSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectFoxCenter);
		centerSelection.CountConstraint.Should().Be(
			NumberRangeConstraint.SingleOptional);
		centerSelection.SelectablePlayerIds.Should().BeEquivalentTo(
			session.GetPlayers()
				.WithHealth(PlayerHealth.Alive)
				.Select(player => player.Id));
		centerSelection.PublicAnnouncement.Should().BeNull();
		centerSelection.PrivateInstruction.Should().Be(
			GameStrings.FoxCenterSelectionInstruction);
		centerSelection.AffectedPlayerIds.Should().Equal(actorId);
		centerSelection.EmptySelectionOptionLabel.Should().Be(
			GameStrings.DeclineOption);
		var attempt = policy.ObservedAttempts.Should().ContainSingle().Subject;
		attempt.ActingPlayer.Id.Should().Be(actorId);
		attempt.ActingPlayer.State.CurrentRole.Should().Be(MainRoleType.Actor);
		attempt.SourceRole.Should().Be(MainRoleType.Fox);
		attempt.PowerInstance.Id.Should().Be(activation.ActivationId);
		attempt.PowerInstance.Origin.Should().Be(RolePowerInstanceOrigin.Borrowed);
		attempt.OneUseResource.Should().NotBeNull();

		var sleep = Advance(
			listener,
			session,
			centerSelection.CreateResponse([]))
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
		session.GameHistoryLog.OfType<TargetPrivateRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.Fox);

		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
		session.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
	}

	[Fact]
	public void BorrowedFox_AcceptedWerewolfObservationRecoversAtActorWake()
	{
		var (session, start, actorId, _) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			FoxCard.Id);
		var werewolf = session.GetPlayers().Single(player =>
			player.Name == "Werewolf");
		IGameHookListener werewolfListener = session.GetOrCreateListener(
			ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf),
			() => new SimpleWerewolfRole(
				new RolePowerAvailabilityGateway(
					AllowAllRolePowerAvailabilityPolicy.Instance)));
		var observation = Advance(
			werewolfListener,
			session,
			actorSleep.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		session = RestorePendingInstruction(
			session,
			werewolfListener,
			observation);
		var victimSelection = GameFlowManager.HandleInput(
				session,
				observation.CreateResponse([werewolf.Id]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var werewolfSleep = GameFlowManager.HandleInput(
				session,
				victimSelection.CreateResponse([actorId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		var actorWake = GameFlowManager.HandleInput(
				session,
				werewolfSleep.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		actorWake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		actorWake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		actorWake.PublicAnnouncement.Should().NotContain(GameStrings.FoxRoleName);
		actorWake.PrivateInstruction.Should().BeNull();
		actorWake.AffectedPlayerIds.Should().Equal(actorId);
		var recovered = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(actorWake)
			.WithRecoveryCursors()
			.RehydrateGameSession();
		var recoveredWake = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredWake.InstructionId.Should().Be(actorWake.InstructionId);
		recoveredWake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		recoveredWake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		recoveredWake.PublicAnnouncement.Should().NotContain(
			GameStrings.FoxRoleName);
		recoveredWake.PrivateInstruction.Should().BeNull();
		recoveredWake.AffectedPlayerIds.Should().Equal(actorId);
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
		recovered.GetPlayerState(actorId).CurrentRole.Should().Be(
			MainRoleType.Actor);
		recovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.Fox);
	}

	[Fact]
	public void BorrowedFox_PermanentLossSurvivesNextOpeningExpiryAndCannotReactivate()
	{
		var (session, start, actorId, _) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			FoxCard.Id);
		var werewolf = session.GetPlayers().Single(player =>
			player.Name == "Werewolf");
		ArrangeKnownWerewolfAgentGroup(session, werewolf.Id);
		var policy = new RecordingPolicy();
		IGameHookListener listener = new FoxRole(
			new RolePowerAvailabilityGateway(policy));
		var wake = AdvancePastWerewolfToFoxWake(
			listener,
			session,
			actorSleep);
		var centerSelection = Advance(listener, session, wake.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			actorId);
		neighbors.Clockwise.Should().NotBeNull();
		neighbors.Counterclockwise.Should().NotBeNull();
		new[]
			{
				actorId,
				neighbors.Clockwise!.Id,
				neighbors.Counterclockwise!.Id
			}
			.ToHashSet()
			.Should().HaveCount(3)
			.And.NotContain(werewolf.Id);
		var attempt = policy.ObservedAttempts.Should().ContainSingle().Subject;
		var borrowedResource = CreateResourceIdentity(attempt);
		var nativeActorResource = borrowedResource with
		{
			PowerInstanceId = actorId,
			PowerInstanceOrigin = RolePowerInstanceOrigin.Native
		};
		var logCountBeforeCheck = session.GameHistoryLog.Count();
		session = RestorePendingInstruction(session, listener, centerSelection);

		var feedback = GameFlowManager.HandleInput(
				session,
				centerSelection.CreateResponse([actorId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		feedback.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealFoxResult);
		feedback.PublicAnnouncement.Should().BeNull();
		feedback.PrivateInstruction.Should().Be(
			GameStrings.FoxNegativeFeedbackInstruction);
		feedback.AffectedPlayerIds.Should().Equal(actorId);
		var publicCommit = session.GameHistoryLog.Skip(logCountBeforeCheck)
			.Should().ContainSingle().Subject;
		publicCommit.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		publicCommit.Should().NotBeAssignableTo<NightActionLogEntry>();
		publicCommit.ToString().Should().NotContain(MainRoleType.Fox.ToString());
		publicCommit.ToString().Should().NotContain(activation.ActivationId.ToString());
		publicCommit.ToString().Should().NotContain(actorId.ToString());
		publicCommit.ToString().Should().NotContain(
			GameStrings.FoxNegativeFeedbackInstruction);
		session.GameHistoryLog.OfType<TargetPrivateRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.Fox);
		borrowedResource.ActingPlayerId.Should().Be(actorId);
		borrowedResource.SourceRole.Should().Be(MainRoleType.Fox);
		borrowedResource.PowerInstanceId.Should().Be(activation.ActivationId);
		borrowedResource.PowerInstanceOrigin.Should().Be(
			RolePowerInstanceOrigin.Borrowed);
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				session,
				borrowedResource)
			.Should().BeTrue();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				session,
				nativeActorResource)
			.Should().BeFalse();

		var recovered = new GameSession(session.Serialize());
		var recoveredPolicy = new RecordingPolicy();
		var recoveredAdmissions = SupportedRoleCatalog.CreateAdmissions(
			new RolePowerAvailabilityGateway(recoveredPolicy));
		GameFlowManager.RestoreDurableContinuation(
			recovered,
			recoveredAdmissions);
		var recoveredFeedback = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredFeedback.InstructionId.Should().Be(feedback.InstructionId);
		recoveredFeedback.PublicAnnouncement.Should().BeNull();
		recoveredFeedback.PrivateInstruction.Should().Be(
			GameStrings.FoxNegativeFeedbackInstruction);
		recoveredFeedback.AffectedPlayerIds.Should().Equal(actorId);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
		recovered.GameHistoryLog.Skip(logCountBeforeCheck)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				recovered,
				borrowedResource)
			.Should().BeTrue();

		var feedbackAcknowledgement = recoveredFeedback.CreateResponse();
		feedbackAcknowledgement.Type.Should().Be(ExpectedInputType.Continue);
		var sleep = GameFlowManager.HandleInput(
				recovered,
				feedbackAcknowledgement,
				recoveredAdmissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.InstructionId.Should().NotBe(feedback.InstructionId);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		recovered.GameHistoryLog.Skip(logCountBeforeCheck)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		recovered.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
		recovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
				.NotContain(entry => entry.Role == MainRoleType.Fox);
		var permanentLoss = recovered.GetActorBorrowedFoxCheckCommits()
			.Should().ContainSingle().Subject;
		permanentLoss.SpentResourceIdentity.Should().Be(borrowedResource);
		AdvanceToNextNight(recovered);

		IGameHookListener nextActor = CreateActorRole();
		var nextActorWake = Advance(
			nextActor,
			recovered,
			start.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		var nextActorChoice = Advance(
			nextActor,
			recovered,
			nextActorWake.CreateResponse())
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		nextActorChoice.Options.Select(option => option.Id).Should()
			.NotContain(FoxCard.Id.ToString("D"));
		recovered.GetActorBorrowedFoxCheckCommits().Should()
			.Equal(permanentLoss);
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				recovered,
				borrowedResource)
			.Should().BeTrue();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				recovered,
				nativeActorResource)
			.Should().BeFalse();
		recovered.GameHistoryLog.Skip(logCountBeforeCheck)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
		recovered.GetModeratorSpentActorSetupCards().Should().Equal(FoxCard);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
	}

	[Fact]
	public void BorrowedFox_PositiveCheckRecoversPrivateFeedbackAndSleepWithoutSpendingOrDuplicating()
	{
		var (session, start, actorId, littleGirlId) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			FoxCard.Id);
		var werewolf = session.GetPlayers().Single(player =>
			player.Name == "Werewolf");
		ArrangeKnownWerewolfAgentGroup(session, werewolf.Id);
		var policy = new RecordingPolicy();
		IGameHookListener listener = new FoxRole(
			new RolePowerAvailabilityGateway(policy));
		var wake = AdvancePastWerewolfToFoxWake(
			listener,
			session,
			actorSleep);
		var centerSelection = Advance(listener, session, wake.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			littleGirlId);
		new[]
			{
				littleGirlId,
				neighbors.Clockwise!.Id,
				neighbors.Counterclockwise!.Id
			}
			.Should().Contain(werewolf.Id);
		var borrowedResource = CreateResourceIdentity(
			policy.ObservedAttempts.Should().ContainSingle().Subject);
		var logCountBeforeCheck = session.GameHistoryLog.Count();
		session = RestorePendingInstruction(session, listener, centerSelection);

		var feedback = GameFlowManager.HandleInput(
				session,
				centerSelection.CreateResponse([littleGirlId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		feedback.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealFoxResult);
		feedback.PublicAnnouncement.Should().BeNull();
		feedback.PrivateInstruction.Should().Be(
			GameStrings.FoxAffirmativeFeedbackInstruction);
		feedback.AffectedPlayerIds.Should().Equal(actorId);
		var publicMarker = session.GameHistoryLog.Skip(logCountBeforeCheck)
			.Should().ContainSingle().Subject;
		publicMarker.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		publicMarker.Should().NotBeAssignableTo<NightActionLogEntry>();
		publicMarker.ToString().Should().NotContain(MainRoleType.Fox.ToString());
		publicMarker.ToString().Should().NotContain(
			activation.ActivationId.ToString());
		publicMarker.ToString().Should().NotContain(actorId.ToString());
		publicMarker.ToString().Should().NotContain(
			GameStrings.FoxAffirmativeFeedbackInstruction);
		var privateCommit = session.GetActorBorrowedFoxCheckCommits()
			.Should().ContainSingle().Subject;
		privateCommit.CenterPlayerId.Should().Be(littleGirlId);
		privateCommit.NeighborhoodAgentKnowledge.Should().Be(
			FactionAgentKnowledge.KnownAgent);
		privateCommit.SpentResourceIdentity.Should().BeNull();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				session,
				borrowedResource)
			.Should().BeFalse();
		session.GameHistoryLog.OfType<TargetPrivateRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.Fox);

		var recovered = new GameSession(session.Serialize());
		var recoveredPolicy = new RecordingPolicy();
		var recoveredAdmissions = SupportedRoleCatalog.CreateAdmissions(
			new RolePowerAvailabilityGateway(recoveredPolicy));
		GameFlowManager.RestoreDurableContinuation(
			recovered,
			recoveredAdmissions);
		var recoveredFeedback = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredFeedback.InstructionId.Should().Be(feedback.InstructionId);
		recoveredFeedback.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealFoxResult);
		recoveredFeedback.PublicAnnouncement.Should().BeNull();
		recoveredFeedback.PrivateInstruction.Should().Be(
			GameStrings.FoxAffirmativeFeedbackInstruction);
		recoveredFeedback.AffectedPlayerIds.Should().Equal(actorId);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
		recovered.GetActorBorrowedFoxCheckCommits().Should()
			.Equal(privateCommit);
		recovered.GameHistoryLog.Skip(logCountBeforeCheck)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				recovered,
				borrowedResource)
			.Should().BeFalse();
		recovered.GameHistoryLog.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();

		var sleep = GameFlowManager.HandleInput(
				recovered,
				recoveredFeedback.CreateResponse(),
				recoveredAdmissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.InstructionId.Should().NotBe(feedback.InstructionId);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
		recovered.GetActorBorrowedFoxCheckCommits().Should()
			.Equal(privateCommit);
		recovered.GameHistoryLog.Skip(logCountBeforeCheck)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				recovered,
				borrowedResource)
			.Should().BeFalse();
		recovered.GameHistoryLog.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
	}

	private static ActorRole CreateActorRole() => new(
		new RolePowerAvailabilityGateway(
			new VillagerRolePowerSuppressionPolicy(
				AllowAllRolePowerAvailabilityPolicy.Instance)));

	private static (
		ActorBorrowedRolePowerActivation Activation,
		ConfirmationInstruction CompletionInstruction) PerformSpendOpening(
		IGameHookListener listener,
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid selectedCardId)
	{
		var wake = Advance(listener, session, start.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(listener, session, wake.CreateResponse())
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var sleep = Advance(
			listener,
			session,
			choice.CreateResponse(selectedCardId.ToString("D")))
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		return (activation, sleep);
	}

	private static ConfirmationInstruction AdvancePastWerewolfToFoxWake(
		IGameHookListener foxListener,
		GameSession session,
		ConfirmationInstruction actorSleep)
	{
		var werewolfWake = Advance(
				foxListener,
				session,
				actorSleep.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		werewolfWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		var victimSelection = Advance(
				foxListener,
				session,
				werewolfWake.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var werewolfSleep = Advance(
				foxListener,
				session,
				victimSelection.CreateResponse(
					[victimSelection.SelectablePlayerIds.First()]))
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		werewolfSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		return Advance(
				foxListener,
				session,
				werewolfSleep.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
	}

	private static void ArrangeKnownWerewolfAgentGroup(
		GameSession session,
		Guid? werewolfId,
		Guid? omittedPlayerId = null)
	{
		var boundary = new FactionFactEffectiveBoundary(
			session.TurnNumber,
			session.GetCurrentPhase(),
			session.GameHistoryLog.Count());
		session.CommitFactionFactBatch(context =>
			new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ScheduledObservation,
					FactionFactSource
						.WerewolfFactionAgentGroupObservationIdentifier),
				Facts =
				[
					.. session.GetPlayers()
						.Where(player => player.Id != omittedPlayerId)
						.Select(player => FactionFact.Agent(
							player.Id,
							Faction.Werewolf,
							player.Id == werewolfId
								? FactionAgentKnowledge.KnownAgent
								: FactionAgentKnowledge.KnownNonAgent,
							boundary))
				]
			});
	}

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		RolePowerAttempt attempt)
	{
		var resource = attempt.OneUseResource
			?? throw new InvalidOperationException(
				"The Fox availability attempt requires its one-use Resource.");
		return new OneUseRolePowerResourceIdentity(
			attempt.ActingPlayer.Id,
			attempt.SourceRole,
			attempt.SourcePower.Identifier.Value,
			attempt.PowerInstance.Id,
			attempt.PowerInstance.Origin,
			resource.Id);
	}

	private static RolePowerInstanceIdentity
		CreateBorrowedDefenderPowerIdentity(
			Guid actorId,
			ActorBorrowedRolePowerActivation activation) =>
		new(
			actorId,
			MainRoleType.Defender,
			DefenderRole.ProtectionPowerIdentifier.Value,
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed);

	private static void AdvanceToNextNight(GameSession session)
	{
		session.TransitionMainPhase(GamePhase.Dawn);
		session.TransitionMainPhase(GamePhase.Day);
		session.TransitionMainPhase(GamePhase.Night);
	}

	private static (
		GameSession Session,
		StartGameConfirmationInstruction Start,
		Guid ActorId,
		Guid LittleGirlId) CreateActorSession()
	{
		var setup = new ActorSetupCards(
			version: 7,
			new[] { DefenderCard, SeerCard, FoxCard });
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Little Girl", "Werewolf", "Villager 1", "Villager 2"],
			[
				MainRoleType.Actor,
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var littleGirlId = players[1].Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		session.AssignRole(littleGirlId, MainRoleType.LittleGirl);
		session.IdentifyRole([actorId], MainRoleType.Actor);
		session.IdentifyRole([littleGirlId], MainRoleType.LittleGirl);
		return (session, start, actorId, littleGirlId);
	}

	private static ModeratorInstruction? Advance(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response)
	{
		session.GetOrCreateListener(listener.Id, () => listener);
		return NightActionLoop.Execute(session, response).ModeratorInstruction;
	}

	private static GameSession RestorePendingInstruction(
		GameSession session,
		IGameHookListener listener,
		ModeratorInstruction instruction)
	{
		var recovered = new GameSession(
			RecoveryPayloadTestDriver.Capture(session)
				.WithPendingInstruction(instruction)
				.Serialize());
		recovered.GetOrCreateListener(listener.Id, () => listener);
		GameFlowManager.RestoreDurableContinuation(
			recovered,
			SupportedRoleCatalog.Admissions);
		return recovered;
	}

	private sealed class RecordingPolicy : IRolePowerAvailabilityPolicy
	{
		internal List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return RolePowerAvailabilityResult.Allowed;
		}
	}

}
