using FluentAssertions;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedCommitProjectionTests
{
	private static readonly PhysicalCharacterCard DefenderCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000241"),
		MainRoleType.Defender);
	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000242"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard FoxCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000243"),
		MainRoleType.Fox);
	private static readonly PhysicalCharacterCard WitchCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000244"),
		MainRoleType.Witch);
	private static readonly PhysicalCharacterCard CupidCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000245"),
		MainRoleType.Cupid);
	private static readonly PhysicalCharacterCard StutteringJudgeCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000246"),
		MainRoleType.StutteringJudge);
	private static readonly PhysicalCharacterCard ElderCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000247"),
		MainRoleType.Elder);

	[Theory]
	[InlineData(PublicBorrowedEntryKind.Recurring)]
	[InlineData(PublicBorrowedEntryKind.TargetPrivate)]
	[InlineData(PublicBorrowedEntryKind.OneUse)]
	public void RehydrateSession_PublicBorrowedRolePowerEntryIsRejected(
		PublicBorrowedEntryKind entryKind)
	{
		var fixture = CreateCommittedSeerSession();
		var forgedEntry = CreateForgedPublicEntry(
			entryKind,
			fixture.Session,
			fixture.PowerIdentity,
			fixture.TargetPlayerId);
		var tampered = RecoveryPayloadTestDriver
			.Parse(fixture.Session.Serialize())
			.AppendPublicRolePowerCommit(forgedEntry)
			.Serialize();

		Action rehydrate = () => new GameSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*Actor borrowed Role Power state*");
	}

	[Fact]
	public void RehydrateSession_ActorBorrowedMarkerAndPrivateCommitCountMismatchIsRejected()
	{
		var fixture = CreateCommittedSeerSession();
		var tampered = RecoveryPayloadTestDriver
			.Parse(fixture.Session.Serialize())
			.RemoveLatestActorBorrowedRolePowerMarker()
			.Serialize();

		Action rehydrate = () => new GameSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*Actor borrowed Role Power state*");
	}

	[Fact]
	public void RehydrateSession_CursorlessActorBorrowedDefenderRetargetIsRejected()
	{
		var fixture = CreateCommittedDefenderSession();
		var tampered = RecoveryPayloadTestDriver
			.Parse(fixture.Session.Serialize())
			.RetargetActorBorrowedDefenderCommit(fixture.AlternateTargetPlayerId)
			.Serialize();

		Action rehydrate = () => new GameSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*Actor borrowed Role Power state*");
	}

	[Theory]
	[InlineData(ActorBorrowedPrivateCommitMutation.SeerTarget)]
	[InlineData(ActorBorrowedPrivateCommitMutation.SeerResult)]
	[InlineData(ActorBorrowedPrivateCommitMutation.DefenderTarget)]
	[InlineData(ActorBorrowedPrivateCommitMutation.FoxCenter)]
	[InlineData(ActorBorrowedPrivateCommitMutation.FoxResultAndResource)]
	[InlineData(ActorBorrowedPrivateCommitMutation.WitchUseTarget)]
	[InlineData(ActorBorrowedPrivateCommitMutation.WitchUseResource)]
	[InlineData(ActorBorrowedPrivateCommitMutation.WitchDeclineResource)]
	[InlineData(ActorBorrowedPrivateCommitMutation.CupidPair)]
	[InlineData(ActorBorrowedPrivateCommitMutation.CupidDisposition)]
	[InlineData(ActorBorrowedPrivateCommitMutation.JudgeSetupPowerLineage)]
	[InlineData(ActorBorrowedPrivateCommitMutation.JudgeObservationSignalAndResource)]
	public void RehydrateSession_CursorlessStableBoundaryWithMutatedActorPrivateCommitIsRejected(
		ActorBorrowedPrivateCommitMutation mutation)
	{
		var session = CreateCommittedActorSession(mutation);
		var stableSnapshot = session.Serialize();
		Action rehydrateUntampered = () => new GameSession(stableSnapshot);
		rehydrateUntampered.Should().NotThrow();
		var tampered = RecoveryPayloadTestDriver
			.Parse(stableSnapshot)
			.MutateActorBorrowedPrivateCommit(mutation)
			.Serialize();

		GameSession? recovered = null;
		Action rehydrateTampered = () => recovered = new GameSession(tampered);

		rehydrateTampered.Should().Throw<InvalidOperationException>();
		recovered.Should().BeNull();
		session.Serialize().Should().Be(stableSnapshot);
	}

	[Theory]
	[InlineData(ActorBorrowedPrivateCommitMutation.HunterFinalShotTarget)]
	[InlineData(ActorBorrowedPrivateCommitMutation.ElderResistanceNightActionIndex)]
	[InlineData(ActorBorrowedPrivateCommitMutation.ElderSuppressionAnnouncement)]
	[InlineData(ActorBorrowedPrivateCommitMutation.ScapegoatTiePowerLineage)]
	[InlineData(ActorBorrowedPrivateCommitMutation.ScapegoatRestrictionAnnouncement)]
	[InlineData(ActorBorrowedPrivateCommitMutation.VillageIdiotPardonActingPlayerLineage)]
	[InlineData(ActorBorrowedPrivateCommitMutation.BearTamerGrowlActingPlayerLineage)]
	[InlineData(ActorBorrowedPrivateCommitMutation.KnightRustySwordTarget)]
	public void RehydrateSession_Issue143PrivateCommitTamperIsRejectedBeforeRegistrationWithoutExposure(
		ActorBorrowedPrivateCommitMutation mutation)
	{
		var sourceObserver = new RecordingStateChangeObserver();
		var sourceSession = CreateCommittedIssue143ActorSession(
			mutation,
			sourceObserver);
		var stableSnapshot = sourceSession.Serialize();
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var validService = new GameService();

		var validGameId = validService.RehydrateSession(stableSnapshot);
		var validState = validService.GetGameStateView(validGameId)
			.Should().BeOfType<GameSession>().Subject;

		validGameId.Should().Be(sourceSession.Id);
		validState.Serialize().Should().Be(stableSnapshot);
		AssertIssue143PrivateCommitProjection(mutation, validState);
		var tampered = RecoveryPayloadTestDriver
			.Parse(stableSnapshot)
			.MutateActorBorrowedPrivateCommit(mutation)
			.Serialize();
		var tamperedService = new GameService();

		Action rehydrateTampered = () =>
			tamperedService.RehydrateSession(tampered);

		var failure = rehydrateTampered.Should()
			.Throw<InvalidOperationException>().Which;
		failure.Message.Should().Be(
			"The stable recovery snapshot has invalid Actor borrowed Role Power state.");
		AssertIssue143RecoveryTextIsSourceSafe(
			failure.Message,
			mutation,
			sourceSession);
		tamperedService.GetCurrentInstruction(sourceSession.Id).Should().BeNull();
		tamperedService.GetGameStateView(sourceSession.Id).Should().BeNull();
		var unavailable = tamperedService.ProcessInstruction(
			sourceSession.Id,
			new ModeratorResponse
			{
				InstructionId = Guid.NewGuid(),
				Type = ExpectedInputType.Continue
			});
		unavailable.IsSuccess.Should().BeFalse();
		var publicError = unavailable.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		publicError.Semantic.Should().Be(
			ModeratorInstructionSemantic.GameSessionNotFound);
		AssertIssue143RecoveryTextIsSourceSafe(
			string.Concat(
				publicError.PublicAnnouncement,
				"\n",
				publicError.PrivateInstruction),
			mutation,
			sourceSession);
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	[Fact]
	public void RehydrateSession_BorrowedElderInfectionResistanceAuthenticatesInfectionNotDefenderProtectedCollectiveIntent()
	{
		var source = CreateCommittedElderResistanceSession(
			new RecordingStateChangeObserver());
		var recoveryService = new GameService();

		var recoveredGameId = recoveryService.RehydrateSession(source.Serialize());
		var recovered = recoveryService.GetGameStateView(recoveredGameId)
			.Should().BeOfType<GameSession>().Subject;
		var resistance = recovered.GetActorBorrowedElderResistanceCommits()
			.Should().ContainSingle().Subject;
		var history = recovered.GameHistoryLog.ToArray();
		var defenderProtectionIndex = Array.FindIndex(
			history,
			entry => entry is NightActionLogEntry
			{
				ActionType: NightActionType.DefenderProtect,
				TargetIds: [var targetPlayerId]
			} && targetPlayerId == resistance.TargetPlayerId);
		var protectedCollectiveIndex = Array.FindIndex(
			history,
			entry => entry.GetType() == typeof(NightActionLogEntry) &&
				entry is NightActionLogEntry
				{
					ActionType: NightActionType.WerewolfVictimSelection,
					TargetIds: [var targetPlayerId]
				} && targetPlayerId == resistance.TargetPlayerId);
		var infectionIndex = Array.FindIndex(
			history,
			entry => entry is OneUseRolePowerCommittedLogEntry
			{
				SourceRole: MainRoleType.AccursedWolfFather,
				ActionType: NightActionType.AccursedWolfFatherInfection,
				TargetIds: [var targetPlayerId]
			} && targetPlayerId == resistance.TargetPlayerId);

		defenderProtectionIndex.Should().BeGreaterThanOrEqualTo(0);
		protectedCollectiveIndex.Should().BeGreaterThan(defenderProtectionIndex);
		infectionIndex.Should().BeGreaterThan(protectedCollectiveIndex);
		resistance.TriggeringNightActionLogIndex.Should()
			.Be(infectionIndex)
			.And.NotBe(protectedCollectiveIndex);
		recovered.GetPlayerState(resistance.TargetPlayerId).Health.Should().Be(
			PlayerHealth.Alive);
		recovered.GetPlayerState(resistance.TargetPlayerId).HasStatusEffect(
			StatusEffectTypes.LycanthropyInfection).Should().BeFalse();
	}

	[Fact]
	public void BorrowedElderResistance_DefenderBlockedPhysicalAttackCannotAuthenticateCommit()
	{
		var setup = new ActorSetupCards(
			version: 18,
			[ElderCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Werewolf", "Villager A", "Villager B", "Villager C"],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
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
		session.TrySpendActorSetupCard(
			actorId,
			ElderCard.Id,
			out var activation).Should().BeTrue();
		session.PerformNightAction(NightActionType.DefenderProtect, actorId);
		session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			actorId);
		var history = session.GameHistoryLog.ToArray();
		var blockedAttackIndex = Array.FindIndex(
			history,
			entry => entry.GetType() == typeof(NightActionLogEntry) &&
				entry is NightActionLogEntry
				{
					ActionType: NightActionType.WerewolfVictimSelection,
					TargetIds: [var targetPlayerId]
				} && targetPlayerId == actorId);
		blockedAttackIndex.Should().BeGreaterThanOrEqualTo(0);
		session.TransitionMainPhase(GamePhase.Dawn);
		Action commit = () => session.CommitActorBorrowedElderResistance(
			new RolePowerInstanceIdentity(
				actorId,
				MainRoleType.Elder,
				"elder-werewolf-attack-resistance",
				activation!.ActivationId,
				RolePowerInstanceOrigin.Borrowed),
			actorId,
			blockedAttackIndex);

			commit.Should().NotThrow();
			session.GetActorBorrowedElderResistanceCommits().Should().ContainSingle();
			session.GameHistoryLog.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
				.Should().ContainSingle();
			var serialized = RecoveryPayloadTestDriver.Capture(session).Serialize();
			var service = new GameService();
			Action rehydrate = () => service.RehydrateSession(serialized);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage(
				"The stable recovery snapshot has invalid Actor borrowed Role Power state.");
	}

	[Fact]
	public void Observer_ActorBorrowedCommitReceivesOnlyPropertyFreePublicMarker()
	{
		var observer = new RecordingStateChangeObserver();
		_ = CreateCommittedSeerSession(observer);

		var actorBorrowedEntries = observer.LogEntries
			.Where(entry => entry.GetType().Name.StartsWith(
				"ActorBorrowed",
				StringComparison.Ordinal))
			.ToArray();
		var marker = actorBorrowedEntries.Should().ContainSingle().Subject
			.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>().Subject;
		marker.ToString().Should().Be("ActorBorrowedRolePowerCommitted");
		Convert.FromBase64String(marker.IntegrityCommitment).Should().HaveCount(32);
		marker.IntegrityCommitment.Should()
			.NotContain(SeerCard.Id.ToString())
			.And.NotContain("seer-werewolf-detection");
		marker.GetType().GetProperties(
			System.Reflection.BindingFlags.Instance |
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.DeclaredOnly).Should().BeEmpty();
	}

	[Fact]
	public void Observer_ActorSetupCardSpendReceivesOnlyPropertyFreePublicMarker()
	{
		var observer = new RecordingStateChangeObserver();
		var fixture = CreateCommittedSeerSession(observer);

		var marker = observer.LogEntries
			.OfType<ActorSetupCardSpendCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		var publicText = marker.ToString();
		publicText.Should().Be("ActorSetupCardSpendCommitted")
			.And.NotContain(SeerCard.Id.ToString())
			.And.NotContain(fixture.PowerIdentity.SourceRole.ToString())
			.And.NotContain(fixture.PowerIdentity.SourcePowerIdentifier)
			.And.NotContain(fixture.PowerIdentity.PowerInstanceId.ToString())
			.And.NotContain(fixture.PowerIdentity.ActingPlayerId.ToString())
			.And.NotContain(fixture.TargetPlayerId.ToString());
		marker.GetType().GetProperties(
			System.Reflection.BindingFlags.Instance |
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.DeclaredOnly).Should().BeEmpty();
	}

	[Fact]
	public void Observer_ActorBorrowedActivationExpiryReceivesOnlyPropertyFreePublicMarker()
	{
		var observer = new RecordingStateChangeObserver();
		var fixture = CreateCommittedSeerSession(observer);
		fixture.Session.TryExpireActorBorrowedRolePowerActivation()
			.Should().BeTrue();

		var marker = observer.LogEntries
			.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
			.Should().ContainSingle().Subject;
		var publicText = marker.ToString();
		publicText.Should().Be("ActorBorrowedRolePowerActivationExpired")
			.And.NotContain(SeerCard.Id.ToString())
			.And.NotContain(fixture.PowerIdentity.SourceRole.ToString())
			.And.NotContain(fixture.PowerIdentity.SourcePowerIdentifier)
			.And.NotContain(fixture.PowerIdentity.PowerInstanceId.ToString())
			.And.NotContain(fixture.PowerIdentity.ActingPlayerId.ToString())
			.And.NotContain(fixture.TargetPlayerId.ToString());
		marker.GetType().GetProperties(
			System.Reflection.BindingFlags.Instance |
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.DeclaredOnly).Should().BeEmpty();
	}

	private static GameSession CreateCommittedIssue143ActorSession(
		ActorBorrowedPrivateCommitMutation mutation,
		RecordingStateChangeObserver sourceObserver) => mutation switch
	{
		ActorBorrowedPrivateCommitMutation.HunterFinalShotTarget =>
			CreateCommittedHunterFinalShotSession(sourceObserver),
		ActorBorrowedPrivateCommitMutation.ElderResistanceNightActionIndex =>
			CreateCommittedElderResistanceSession(sourceObserver),
		ActorBorrowedPrivateCommitMutation.ElderSuppressionAnnouncement =>
			CreateCommittedElderSuppressionSession(sourceObserver),
		ActorBorrowedPrivateCommitMutation.ScapegoatTiePowerLineage =>
			CreateCommittedScapegoatSession(
				ActorBorrowedScapegoatRecoveryStep.PermittedVoterSelection,
				sourceObserver),
		ActorBorrowedPrivateCommitMutation.ScapegoatRestrictionAnnouncement =>
			CreateCommittedScapegoatSession(
				ActorBorrowedScapegoatRecoveryStep.PermittedVoterAnnouncement,
				sourceObserver),
		ActorBorrowedPrivateCommitMutation.VillageIdiotPardonActingPlayerLineage =>
			CreateCommittedVillageIdiotPardonSession(sourceObserver),
		ActorBorrowedPrivateCommitMutation.BearTamerGrowlActingPlayerLineage =>
			CreateCommittedBearTamerGrowlSession(sourceObserver),
		ActorBorrowedPrivateCommitMutation.KnightRustySwordTarget =>
			CreateCommittedKnightScheduleSession(sourceObserver),
		_ => throw new ArgumentOutOfRangeException(nameof(mutation))
	};

	private static GameSession CreateCommittedHunterFinalShotSession(
		RecordingStateChangeObserver sourceObserver)
	{
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedHunterPendingSelectorSnapshot(sourceObserver);
		var service = new GameService();
		var gameId = service.RehydrateSession(snapshot.SerializedSession);
		var selector = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var targetPlayerId = selector.SelectablePlayerIds.First();

		service.ProcessInstruction(
			gameId,
			selector.CreateResponse([targetPlayerId])).IsSuccess.Should().BeTrue();
		var committed = service.GetGameStateView(gameId)
			.Should().BeOfType<GameSession>().Subject;
		committed.GetActorBorrowedHunterFinalShotCommits()
			.Should().ContainSingle().Which.TargetPlayerId.Should().Be(targetPlayerId);
		return committed;
	}

	private static GameSession CreateCommittedElderResistanceSession(
		RecordingStateChangeObserver sourceObserver)
	{
		var setup = new ActorSetupCards(
			version: 17,
			[ElderCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Werewolf", "Wolf Father", "Villager A", "Villager B", "Villager C"],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var source = new GameSession(
			sessionId,
			start,
			config,
			sourceObserver);
		var players = source.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var werewolfId = players[1].Id;
		var wolfFatherId = players[2].Id;
		for (var index = 0; index < players.Length; index++)
		{
			source.AssignRole(
				players[index].Id,
				index == 0
					? MainRoleType.Actor
					: index == 1
						? MainRoleType.SimpleWerewolf
						: index == 2
							? MainRoleType.AccursedWolfFather
							: MainRoleType.SimpleVillager);
		}

		var actorCard = source.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		source.TryRecordPhysicalCharacterCardOwnership(
			source.RoleLockIn.Version,
			actorId,
			actorCard.Card.Id).Should().BeTrue();
		source.IdentifyRole([actorId], MainRoleType.Actor);
		source.IdentifyRole([wolfFatherId], MainRoleType.AccursedWolfFather);
		source.PerformNightAction(NightActionType.DefenderProtect, actorId);
		var serializedSource = RecoveryPayloadTestDriver.Capture(source)
			.WithPendingInstruction(start)
			.Serialize();

		var service = new GameService();
		var gameId = service.RehydrateSession(serializedSource);
		var recoveredStart = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<StartGameConfirmationInstruction>().Subject;
		var nightStart = service.ProcessInstruction(
				gameId,
				recoveredStart.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var builder = GameTestBuilder.ForExistingGame(service, gameId);
		builder.ConfirmNightStart().IsSuccess.Should().BeTrue();
		var werewolfObservation = builder
			.CompleteActorNightAction(actorId, ElderCard.Id)
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var victimSelection = service.ProcessInstruction(
				gameId,
				werewolfObservation.CreateResponse([werewolfId, wolfFatherId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var werewolfSleep = service.ProcessInstruction(
				gameId,
				victimSelection.CreateResponse([actorId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var wolfFatherWake = service.ProcessInstruction(
				gameId,
				werewolfSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var infectionChoice = service.ProcessInstruction(
				gameId,
				wolfFatherWake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectOptionsInstruction>().Subject;
		var wolfFatherSleep = service.ProcessInstruction(
				gameId,
				infectionChoice.CreateResponse(
					AccursedWolfFatherInfectionOptionIds.Infect))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = service.ProcessInstruction(
				gameId,
				wolfFatherSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		service.ProcessInstruction(
			gameId,
			finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		var committed = service.GetGameStateView(gameId)
			.Should().BeOfType<GameSession>().Subject;
		committed.GetActorBorrowedElderResistanceCommits()
			.Should().ContainSingle();
		return committed;
	}

	private static GameSession CreateCommittedElderSuppressionSession(
		RecordingStateChangeObserver sourceObserver)
	{
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedElderPendingSuppressionAnnouncementSnapshot(
				sourceObserver);
		var committed = RehydrateIssue143Fixture(snapshot.SerializedSession);
		committed.GetActorBorrowedElderSuppressionCommits()
			.Should().ContainSingle();
		return committed;
	}

	private static GameSession CreateCommittedScapegoatSession(
		ActorBorrowedScapegoatRecoveryStep step,
		RecordingStateChangeObserver sourceObserver)
	{
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedScapegoatPendingSnapshot(step, sourceObserver);
		var committed = RehydrateIssue143Fixture(snapshot.SerializedSession);
		committed.GetActorBorrowedScapegoatTieReplacementCommits()
			.Should().ContainSingle();
		if (step == ActorBorrowedScapegoatRecoveryStep.PermittedVoterAnnouncement)
		{
			committed.GetActorBorrowedScapegoatVoterRestrictionCommits()
				.Should().ContainSingle();
		}
		else
		{
			committed.GetActorBorrowedScapegoatVoterRestrictionCommits()
				.Should().BeEmpty();
		}
		return committed;
	}

	private static GameSession CreateCommittedVillageIdiotPardonSession(
		RecordingStateChangeObserver sourceObserver)
	{
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedVillageIdiotPendingPardonSnapshot(sourceObserver);
		var committed = RehydrateIssue143Fixture(snapshot.SerializedSession);
		committed.GetActorBorrowedVillageIdiotPardonCommits()
			.Should().ContainSingle();
		return committed;
	}

	private static GameSession CreateCommittedBearTamerGrowlSession(
		RecordingStateChangeObserver sourceObserver)
	{
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedBearTamerPendingGrowlSnapshot(sourceObserver);
		var service = new GameService();
		var gameId = service.RehydrateSession(snapshot.SerializedSession);
		var growl = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		service.ProcessInstruction(
			gameId,
			growl.CreateResponse()).IsSuccess.Should().BeTrue();
		var committed = service.GetGameStateView(gameId)
			.Should().BeOfType<GameSession>().Subject;
		committed.GetActorBorrowedBearTamerGrowlCommits()
			.Should().ContainSingle();
		return committed;
	}

	private static GameSession CreateCommittedKnightScheduleSession(
		RecordingStateChangeObserver sourceObserver)
	{
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedKnightPendingRustySwordAnnouncementSnapshot(
				sourceObserver);
		var committed = RehydrateIssue143Fixture(snapshot.SerializedSession);
		committed.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle();
		return committed;
	}

	private static GameSession RehydrateIssue143Fixture(
		string serializedSession)
	{
		var service = new GameService();
		var gameId = service.RehydrateSession(serializedSession);
		return service.GetGameStateView(gameId)
			.Should().BeOfType<GameSession>().Subject;
	}

	private static void AssertIssue143PrivateCommitProjection(
		ActorBorrowedPrivateCommitMutation mutation,
		GameSession session)
	{
		switch (mutation)
		{
			case ActorBorrowedPrivateCommitMutation.HunterFinalShotTarget:
				session.GetActorBorrowedHunterFinalShotCommits()
					.Should().ContainSingle();
				break;
			case ActorBorrowedPrivateCommitMutation.ElderResistanceNightActionIndex:
				session.GetActorBorrowedElderResistanceCommits()
					.Should().ContainSingle();
				break;
			case ActorBorrowedPrivateCommitMutation.ElderSuppressionAnnouncement:
				session.GetActorBorrowedElderSuppressionCommits()
					.Should().ContainSingle();
				break;
			case ActorBorrowedPrivateCommitMutation.ScapegoatTiePowerLineage:
				session.GetActorBorrowedScapegoatTieReplacementCommits()
					.Should().ContainSingle();
				session.GetActorBorrowedScapegoatVoterRestrictionCommits()
					.Should().BeEmpty();
				break;
			case ActorBorrowedPrivateCommitMutation.ScapegoatRestrictionAnnouncement:
				session.GetActorBorrowedScapegoatTieReplacementCommits()
					.Should().ContainSingle();
				session.GetActorBorrowedScapegoatVoterRestrictionCommits()
					.Should().ContainSingle();
				break;
			case ActorBorrowedPrivateCommitMutation.VillageIdiotPardonActingPlayerLineage:
				session.GetActorBorrowedVillageIdiotPardonCommits()
					.Should().ContainSingle();
				break;
			case ActorBorrowedPrivateCommitMutation.BearTamerGrowlActingPlayerLineage:
				session.GetActorBorrowedBearTamerGrowlCommits()
					.Should().ContainSingle();
				break;
			case ActorBorrowedPrivateCommitMutation.KnightRustySwordTarget:
				session.GetActorBorrowedKnightRustySwordScheduleCommits()
					.Should().ContainSingle();
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(mutation));
		}
	}

	private static void AssertIssue143RecoveryTextIsSourceSafe(
		string text,
		ActorBorrowedPrivateCommitMutation mutation,
		GameSession sourceSession)
	{
		switch (mutation)
		{
			case ActorBorrowedPrivateCommitMutation.HunterFinalShotTarget:
			{
				var commit = sourceSession
					.GetActorBorrowedHunterFinalShotCommits().Single();
				text.Should().NotContain(GameStrings.HunterRoleName)
					.And.NotContain(MainRoleType.Hunter.ToString())
					.And.NotContain(commit.PowerIdentity.SourcePowerIdentifier)
					.And.NotContain(commit.ActorSetupCardId.ToString())
					.And.NotContain(commit.PowerIdentity.PowerInstanceId.ToString())
					.And.NotContain(commit.CascadeScopeId)
					.And.NotContain(commit.TargetPlayerId.ToString());
				foreach (var playerId in commit.TriggeringPlayerIds)
				{
					text.Should().NotContain(playerId.ToString());
				}
				break;
			}
			case ActorBorrowedPrivateCommitMutation.ElderResistanceNightActionIndex:
			{
				var commit = sourceSession
					.GetActorBorrowedElderResistanceCommits().Single();
				text.Should().NotContain(GameStrings.ElderRoleName)
					.And.NotContain(MainRoleType.Elder.ToString())
					.And.NotContain(commit.PowerIdentity.SourcePowerIdentifier)
					.And.NotContain(commit.ActorSetupCardId.ToString())
					.And.NotContain(commit.PowerIdentity.PowerInstanceId.ToString())
					.And.NotContain(commit.TargetPlayerId.ToString());
				break;
			}
			case ActorBorrowedPrivateCommitMutation.ElderSuppressionAnnouncement:
			{
				var commit = sourceSession
					.GetActorBorrowedElderSuppressionCommits().Single();
				text.Should().NotContain(GameStrings.ElderRoleName)
					.And.NotContain(MainRoleType.Elder.ToString())
					.And.NotContain(commit.PowerIdentity.SourcePowerIdentifier)
					.And.NotContain(commit.ActorSetupCardId.ToString())
					.And.NotContain(commit.PowerIdentity.PowerInstanceId.ToString())
					.And.NotContain(commit.PowerIdentity.ActingPlayerId.ToString())
					.And.NotContain(commit.CascadeScopeId)
					.And.NotContain(commit.AnnouncementInstructionId.ToString());
				break;
			}
			case ActorBorrowedPrivateCommitMutation.ScapegoatTiePowerLineage:
			{
				var commit = sourceSession
					.GetActorBorrowedScapegoatTieReplacementCommits().Single();
				text.Should().NotContain(GameStrings.ScapegoatRoleName)
					.And.NotContain(MainRoleType.Scapegoat.ToString())
					.And.NotContain(commit.PowerIdentity.SourcePowerIdentifier)
					.And.NotContain(commit.ActorSetupCardId.ToString())
					.And.NotContain(commit.PowerIdentity.PowerInstanceId.ToString())
					.And.NotContain(commit.PowerIdentity.ActingPlayerId.ToString())
					.And.NotContain(commit.CascadeScopeId);
				break;
			}
			case ActorBorrowedPrivateCommitMutation.ScapegoatRestrictionAnnouncement:
			{
				var commit = sourceSession
					.GetActorBorrowedScapegoatVoterRestrictionCommits().Single();
				text.Should().NotContain(GameStrings.ScapegoatRoleName)
					.And.NotContain(MainRoleType.Scapegoat.ToString())
					.And.NotContain(commit.PowerIdentity.SourcePowerIdentifier)
					.And.NotContain(commit.ActorSetupCardId.ToString())
					.And.NotContain(commit.PowerIdentity.PowerInstanceId.ToString())
					.And.NotContain(commit.PowerIdentity.ActingPlayerId.ToString())
					.And.NotContain(commit.CascadeScopeId)
					.And.NotContain(commit.AnnouncementInstructionId.ToString());
				foreach (var playerId in commit.CandidatePlayerIds
					.Concat(commit.PermittedVoterIds))
				{
					text.Should().NotContain(playerId.ToString());
				}
				break;
			}
			case ActorBorrowedPrivateCommitMutation.VillageIdiotPardonActingPlayerLineage:
			{
				var commit = sourceSession
					.GetActorBorrowedVillageIdiotPardonCommits().Single();
				text.Should().NotContain(GameStrings.VillageIdiotRoleName)
					.And.NotContain(MainRoleType.VillageIdiot.ToString())
					.And.NotContain(commit.PowerIdentity.SourcePowerIdentifier)
					.And.NotContain(commit.ActorSetupCardId.ToString())
					.And.NotContain(commit.PowerIdentity.PowerInstanceId.ToString())
					.And.NotContain(commit.PowerIdentity.ActingPlayerId.ToString())
					.And.NotContain(
						commit.SpentResourceIdentity.OneUseResourceId.ToString());
				break;
			}
			case ActorBorrowedPrivateCommitMutation.BearTamerGrowlActingPlayerLineage:
			{
				var commit = sourceSession
					.GetActorBorrowedBearTamerGrowlCommits().Single();
				text.Should().NotContain(GameStrings.BearTamerRoleName)
					.And.NotContain(MainRoleType.BearTamer.ToString())
					.And.NotContain(commit.PowerIdentity.SourcePowerIdentifier)
					.And.NotContain(commit.ActorSetupCardId.ToString())
					.And.NotContain(commit.PowerIdentity.PowerInstanceId.ToString())
					.And.NotContain(commit.PowerIdentity.ActingPlayerId.ToString());
				break;
			}
			case ActorBorrowedPrivateCommitMutation.KnightRustySwordTarget:
			{
				var commit = sourceSession
					.GetActorBorrowedKnightRustySwordScheduleCommits().Single();
				text.Should().NotContain(GameStrings.KnightWithRustySwordRoleName)
					.And.NotContain(MainRoleType.KnightWithRustySword.ToString())
					.And.NotContain(commit.PowerIdentity.SourcePowerIdentifier)
					.And.NotContain(commit.ActorSetupCardId.ToString())
					.And.NotContain(commit.PowerIdentity.PowerInstanceId.ToString())
					.And.NotContain(commit.PowerIdentity.ActingPlayerId.ToString())
					.And.NotContain(commit.CascadeScopeId)
					.And.NotContain(commit.TargetPlayerId.ToString());
				break;
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(mutation));
		}
	}

	private static CommittedSeerFixture CreateCommittedSeerSession(
		IStateChangeObserver? observer = null)
	{
		var setup = new ActorSetupCards(
			version: 7,
			[DefenderCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Werewolf", "Villager 1", "Villager 2", "Villager 3"],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var session = new GameSession(
			sessionId,
			new StartGameConfirmationInstruction(sessionId),
			config,
			observer);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var targetId = players[1].Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		ArrangeCompleteWerewolfAgentFacts(session);
		session.TrySpendActorSetupCard(actorId, SeerCard.Id, out var activation)
			.Should().BeTrue();
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.Seer,
			"seer-werewolf-detection",
			activation!.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		session.CommitActorBorrowedSeerCheck(
			powerIdentity,
			targetId,
			FactionAgentKnowledge.KnownNonAgent);
		if (observer == null)
		{
			session = RecoveryPayloadTestDriver.Capture(session)
				.RehydrateGameSession();
		}
		return new CommittedSeerFixture(session, powerIdentity, targetId);
	}

	private static CommittedDefenderFixture CreateCommittedDefenderSession()
	{
		var setup = new ActorSetupCards(
			version: 7,
			[DefenderCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Werewolf", "Villager 1", "Villager 2", "Villager 3"],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var session = new GameSession(
			sessionId,
			new StartGameConfirmationInstruction(sessionId),
			config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		session.TrySpendActorSetupCard(actorId, DefenderCard.Id, out var activation)
			.Should().BeTrue();
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.Defender,
			"defender-protection",
			activation!.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		session.CommitActorBorrowedDefenderProtection(
			powerIdentity,
			players[1].Id);
		session = RecoveryPayloadTestDriver.Capture(session).RehydrateGameSession();
		return new CommittedDefenderFixture(session, players[2].Id);
	}

	private static GameSession CreateCommittedActorSession(
		ActorBorrowedPrivateCommitMutation mutation)
	{
		if (mutation == ActorBorrowedPrivateCommitMutation
			.JudgeObservationSignalAndResource)
		{
			return ActorBorrowedStutteringJudgeTests
				.CreateCursorlessCommittedJudgeObservationSession();
		}

		var sourceCard = mutation switch
		{
			ActorBorrowedPrivateCommitMutation.SeerTarget or
			ActorBorrowedPrivateCommitMutation.SeerResult => SeerCard,
			ActorBorrowedPrivateCommitMutation.DefenderTarget => DefenderCard,
			ActorBorrowedPrivateCommitMutation.FoxCenter or
			ActorBorrowedPrivateCommitMutation.FoxResultAndResource => FoxCard,
			ActorBorrowedPrivateCommitMutation.WitchUseTarget or
			ActorBorrowedPrivateCommitMutation.WitchUseResource or
			ActorBorrowedPrivateCommitMutation.WitchDeclineResource => WitchCard,
			ActorBorrowedPrivateCommitMutation.CupidPair or
			ActorBorrowedPrivateCommitMutation.CupidDisposition => CupidCard,
			ActorBorrowedPrivateCommitMutation.JudgeSetupPowerLineage =>
				StutteringJudgeCard,
			_ => throw new ArgumentOutOfRangeException(nameof(mutation))
		};
		var setupCards = new[]
			{
				sourceCard,
				DefenderCard,
				SeerCard,
				FoxCard
			}
			.GroupBy(card => card.PrintedRole)
			.Select(group => group.First())
			.Take(3)
			.ToArray();
		var setup = new ActorSetupCards(version: 8, setupCards);
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Werewolf", "Villager 1", "Villager 2", "Villager 3"],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var session = new GameSession(
			sessionId,
			new StartGameConfirmationInstruction(sessionId),
			config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		FactionFactEffectiveBoundary? initialAgentGroupBoundary = null;
		if (sourceCard.PrintedRole == MainRoleType.Seer ||
			mutation == ActorBorrowedPrivateCommitMutation.CupidDisposition)
		{
			initialAgentGroupBoundary =
				ArrangeCompleteWerewolfAgentFacts(session);
		}

		session.TrySpendActorSetupCard(actorId, sourceCard.Id, out var activation)
			.Should().BeTrue();
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			sourceCard.PrintedRole,
			SourcePowerIdentifier(sourceCard.PrintedRole),
			activation!.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		switch (mutation)
		{
			case ActorBorrowedPrivateCommitMutation.SeerTarget:
			case ActorBorrowedPrivateCommitMutation.SeerResult:
				session.CommitActorBorrowedSeerCheck(
					powerIdentity,
					players[1].Id,
					FactionAgentKnowledge.KnownNonAgent);
				break;
			case ActorBorrowedPrivateCommitMutation.DefenderTarget:
				session.CommitActorBorrowedDefenderProtection(
					powerIdentity,
					players[1].Id);
				break;
			case ActorBorrowedPrivateCommitMutation.FoxCenter:
			case ActorBorrowedPrivateCommitMutation.FoxResultAndResource:
				session.CommitActorBorrowedFoxCheck(
					powerIdentity,
					players[1].Id,
					FactionAgentKnowledge.KnownAgent,
					spentResourceIdentity: null);
				break;
			case ActorBorrowedPrivateCommitMutation.WitchUseTarget:
			case ActorBorrowedPrivateCommitMutation.WitchUseResource:
				session.CommitActorBorrowedWitchPotionUse(
					powerIdentity,
					CreateResourceIdentity(
						powerIdentity,
						ActorBorrowedWitchPotionUseCommit.PoisonResourceId),
					players[1].Id);
				break;
			case ActorBorrowedPrivateCommitMutation.WitchDeclineResource:
				session.CommitActorBorrowedWitchPotionDecline(
					powerIdentity,
					CreateResourceIdentity(
						powerIdentity,
						ActorBorrowedWitchPotionUseCommit.HealingResourceId));
				break;
			case ActorBorrowedPrivateCommitMutation.CupidPair:
			case ActorBorrowedPrivateCommitMutation.CupidDisposition:
				var lovers = players.Skip(1).Take(2)
					.Select(player => player.Id)
					.Order()
					.ToArray();
				session.CommitActorBorrowedCupidLovers(
					powerIdentity,
					lovers,
					ActorBorrowedCupidLoversDisposition
						.DeferredToInitialBeneficiaryClosure);
				break;
			case ActorBorrowedPrivateCommitMutation.JudgeSetupPowerLineage:
				session.CommitActorBorrowedStutteringJudgeSignalSetup(powerIdentity);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(mutation));
		}
		if (mutation == ActorBorrowedPrivateCommitMutation.CupidDisposition)
		{
			InitialBeneficiaryClosureRules.TryCommitCurrentSession(
					session,
					initialAgentGroupBoundary)
				.Should().Be(InitialBeneficiaryClosureResult.Committed);
			session.GetActorBorrowedCupidLoversCommits().Single().Disposition
				.Should().Be(ActorBorrowedCupidLoversDisposition.SameFaction);
		}

		return RecoveryPayloadTestDriver.Capture(session).RehydrateGameSession();
	}

	private static string SourcePowerIdentifier(MainRoleType sourceRole) =>
		sourceRole switch
		{
			MainRoleType.Seer => "seer-werewolf-detection",
			MainRoleType.Defender => "defender-protection",
			MainRoleType.Fox => "fox-neighborhood-check",
			MainRoleType.Witch =>
				ActorBorrowedWitchPotionUseCommit.ExpectedSourcePowerIdentifier,
			MainRoleType.Cupid =>
				ActorBorrowedCupidLoversCommit.ExpectedSourcePowerIdentifier,
			MainRoleType.StutteringJudge =>
				ActorBorrowedStutteringJudgeSignalSetupCommit
					.ExpectedSourcePowerIdentifier,
			_ => throw new ArgumentOutOfRangeException(nameof(sourceRole))
		};

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		RolePowerInstanceIdentity powerIdentity,
		Guid resourceId) => new(
			powerIdentity.ActingPlayerId,
			powerIdentity.SourceRole,
			powerIdentity.SourcePowerIdentifier,
			powerIdentity.PowerInstanceId,
			powerIdentity.PowerInstanceOrigin,
			resourceId);

	private static FactionFactEffectiveBoundary
		ArrangeCompleteWerewolfAgentFacts(GameSession session)
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
					.. session.GetPlayers().Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						FactionAgentKnowledge.KnownNonAgent,
						boundary))
				]
			});
		return boundary;
	}

	private static GameLogEntryBase CreateForgedPublicEntry(
		PublicBorrowedEntryKind entryKind,
		GameSession session,
		RolePowerInstanceIdentity powerIdentity,
		Guid targetPlayerId)
	{
		var timestamp = session.GameHistoryLog.Last().Timestamp.AddTicks(1);
		return entryKind switch
		{
			PublicBorrowedEntryKind.Recurring =>
				new RecurringRolePowerCommittedLogEntry
				{
					Timestamp = timestamp,
					TurnNumber = session.TurnNumber,
					CurrentPhase = GamePhase.Night,
					ActionType = NightActionType.SeerCheck,
					TargetIds = [targetPlayerId],
					ActingPlayerId = powerIdentity.ActingPlayerId,
					SourceRole = powerIdentity.SourceRole,
					SourcePowerIdentifier =
						powerIdentity.SourcePowerIdentifier,
					PowerInstanceId = powerIdentity.PowerInstanceId,
					PowerInstanceOrigin = RolePowerInstanceOrigin.Borrowed
				},
			PublicBorrowedEntryKind.TargetPrivate =>
				new TargetPrivateRolePowerCommittedLogEntry
				{
					Timestamp = timestamp,
					TurnNumber = session.TurnNumber,
					CurrentPhase = GamePhase.Night,
					ActionType = NightActionType.SeerCheck,
					TargetIds = [],
					ActingPlayerId = powerIdentity.ActingPlayerId,
					SourceRole = powerIdentity.SourceRole,
					SourcePowerIdentifier =
						powerIdentity.SourcePowerIdentifier,
					PowerInstanceId = powerIdentity.PowerInstanceId,
					PowerInstanceOrigin = RolePowerInstanceOrigin.Borrowed
				},
			PublicBorrowedEntryKind.OneUse =>
				new OneUseRolePowerCommittedLogEntry
				{
					Timestamp = timestamp,
					TurnNumber = session.TurnNumber,
					CurrentPhase = GamePhase.Night,
					ActionType = NightActionType.SeerCheck,
					TargetIds = [targetPlayerId],
					ActingPlayerId = powerIdentity.ActingPlayerId,
					SourceRole = powerIdentity.SourceRole,
					SourcePowerIdentifier =
						powerIdentity.SourcePowerIdentifier,
					PowerInstanceId = powerIdentity.PowerInstanceId,
					PowerInstanceOrigin = RolePowerInstanceOrigin.Borrowed,
					OneUseResourceId = Guid.NewGuid()
				},
			_ => throw new ArgumentOutOfRangeException(nameof(entryKind))
		};
	}

	public enum PublicBorrowedEntryKind
	{
		Recurring,
		TargetPrivate,
		OneUse
	}

	private sealed record CommittedSeerFixture(
		GameSession Session,
		RolePowerInstanceIdentity PowerIdentity,
		Guid TargetPlayerId);

	private sealed record CommittedDefenderFixture(
		GameSession Session,
		Guid AlternateTargetPlayerId);

	private sealed class RecordingStateChangeObserver : IStateChangeObserver
	{
		internal List<GameLogEntryBase> LogEntries { get; } = [];
		internal int NotificationCount { get; private set; }

		public void OnLogEntryApplied(GameLogEntryBase entry)
		{
			NotificationCount++;
			LogEntries.Add(entry);
		}

		public void OnMainPhaseChanged(GamePhase newPhase)
		{
			NotificationCount++;
		}

		public void OnSubPhaseChanged(string? newSubPhase)
		{
			NotificationCount++;
		}

		public void OnSubPhaseStageChanged(string? newSubPhaseStage)
		{
			NotificationCount++;
		}

		public void OnListenerChanged(
			ListenerIdentifier? listener,
			string? listenerState)
		{
			NotificationCount++;
		}

		public void OnTurnNumberChanged(int newTurnNumber)
		{
			NotificationCount++;
		}

		public void OnPendingInstructionChanged(
			ModeratorInstruction? instruction)
		{
			NotificationCount++;
		}
	}
}
