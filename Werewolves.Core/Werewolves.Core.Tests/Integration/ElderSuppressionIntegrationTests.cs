using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class ElderSuppressionIntegrationTests(ITestOutputHelper output)
	: DiagnosticTestBase(output)
{
	[Fact]
	public void PendingAnnouncement_RecoversExactIdentity_RejectsInvalidResponses_AndAcknowledgesExactlyOnce()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var elder = players[1];
		builder.ArrangeKnownPhysicalRole(elder.Id, MainRoleType.Elder);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase([werewolf.Id], elder.Id);
		builder.CompleteDawnPhase();
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var reveal = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(vote.CreateResponse([elder.Id])));
		var elimination =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(reveal.CreateResponse()));
		var announcement =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(elimination.CreateResponse()));

		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillagerRolePowerSuppression);
		announcement.InstructionId.Should().NotBeEmpty();
		var committed = builder.GetGameState()!.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		committed.AnnouncementInstructionId.Should().Be(
			announcement.InstructionId);
		builder.GetGameState()!.GameHistoryLog
			.OfType<
				VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry>()
			.Should().BeEmpty();

		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredAnnouncement = recoveredService
			.GetCurrentInstruction(recoveredId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredAnnouncement.InstructionId.Should().Be(announcement.InstructionId);
		recoveredAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillagerRolePowerSuppression);

		var invalidResponses = new ModeratorResponse[]
		{
			new()
			{
				InstructionId = Guid.NewGuid(),
				Type = ExpectedInputType.Continue
			},
			new()
			{
				InstructionId = recoveredAnnouncement.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds = new HashSet<Guid> { players[2].Id }
			},
			new()
			{
				InstructionId = recoveredAnnouncement.InstructionId,
				Type = ExpectedInputType.Continue,
				SelectedPlayerIds = new HashSet<Guid> { players[2].Id }
			}
		};
		foreach (var invalidResponse in invalidResponses)
		{
			var before = PublicGameSessionSnapshot.Capture(
				recoveredService,
				recoveredId);
			Action process = () => recoveredService.ProcessInstruction(
				recoveredId,
				invalidResponse);

			process.Should().Throw<InvalidOperationException>();
			PublicGameSessionSnapshot.Capture(recoveredService, recoveredId)
				.Should().BeEquivalentTo(
					before,
					options => options.WithStrictOrdering());
		}

		var afterAcknowledgment = recoveredService.ProcessInstruction(
			recoveredId,
			recoveredAnnouncement.CreateResponse());
		afterAcknowledgment.IsSuccess.Should().BeTrue();
		afterAcknowledgment.ModeratorInstruction.Should().NotBeNull();
		var nextInstruction = afterAcknowledgment.ModeratorInstruction!;
		nextInstruction.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.AnnounceVillagerRolePowerSuppression);
		var acknowledged = recoveredService.GetGameStateView(recoveredId)!;
		acknowledged.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().ContainSingle()
			.Which.AnnouncementInstructionId.Should().Be(
				recoveredAnnouncement.InstructionId);
		acknowledged.GameHistoryLog
			.OfType<
				VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry>()
			.Should().ContainSingle()
			.Which.AnnouncementInstructionId.Should().Be(
				recoveredAnnouncement.InstructionId);

		var afterAckService = new GameService();
		var afterAckId = afterAckService.RehydrateSession(
			acknowledged.Serialize());
		var restoredNext = afterAckService.GetCurrentInstruction(afterAckId)!;
		restoredNext.InstructionId.Should().Be(
			nextInstruction.InstructionId);
		restoredNext.Semantic.Should().Be(
			nextInstruction.Semantic);
		var restoredState = afterAckService.GetGameStateView(afterAckId)!;
		restoredState.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().ContainSingle();
		restoredState.GameHistoryLog
			.OfType<
				VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void DevotedServantAcquiredElder_IsDormantDuringSameDayConsecutiveVote()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleWerewolf,
				MainRoleType.DevotedServant,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var judge = players[0];
		var werewolf = players[1];
		var servant = players[2];
		var elderTarget = players[3];
		var nightVictim = players[6];
		builder.ArrangeKnownRole(judge.Id, MainRoleType.StutteringJudge);
		builder.ArrangeKnownPhysicalRole(elderTarget.Id, MainRoleType.Elder);
		builder.ConfirmGameStart();
		var judgeWake = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var judgeSetup = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(judgeWake.CreateResponse()));
		builder.Process(judgeSetup.CreateResponse()).IsSuccess.Should().BeTrue();
		var finishNight = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					nightVictim.Id));
		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new()
		{
			[nightVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var conductVote = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(debate.CreateResponse()));
		var signal = InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
			builder.Process(conductVote.CreateResponse()));
		var firstVote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(signal.CreateResponse(
				StutteringJudgeSignalOptionIds.Occurred)));
		var window = builder.Process(firstVote.CreateResponse([elderTarget.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<DevotedServantVoteWindowInstruction>().Subject;
		var acquiredCard = builder.Process(
				window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var firstAnnouncement = builder.Process(
				acquiredCard.CreateResponse(new()
				{
					[elderTarget.Id] = MainRoleType.Elder
				}))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var consecutiveVote = builder.Process(firstAnnouncement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		servant.State.CurrentRole.Should().Be(MainRoleType.Elder);
		builder.GetGameState()!.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().BeEmpty();
		var secondAnnouncement = builder.Process(
				consecutiveVote.CreateResponse([servant.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		secondAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);

		var nextNight = builder.Process(secondAnnouncement.CreateResponse());

		nextNight.IsSuccess.Should().BeTrue();
		nextNight.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		servant.State.Health.Should().Be(PlayerHealth.Dead);
		var session = builder.GetGameState()!;
		session.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog
			.OfType<
				VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void VoteCascade_CompletesHunterReactionBeforeSuppression_ThenResumesCommittedConsecutiveVote()
	{
		var builder = CreateBuilder()
			.WithPlayers(8)
			.WithRoles(
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.Elder,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var judge = players[0];
		var werewolf = players[1];
		var cupid = players[2];
		var elder = players[3];
		var hunter = players[4];
		var shotTarget = players[5];
		var nightVictim = players[7];
		builder.ArrangeKnownRole(judge.Id, MainRoleType.StutteringJudge);
		builder.ArrangeKnownRole(cupid.Id, MainRoleType.Cupid);
		builder.ArrangeKnownPhysicalRole(elder.Id, MainRoleType.Elder);
		builder.ArrangeKnownPhysicalRole(hunter.Id, MainRoleType.Hunter);
		builder.ConfirmGameStart();
		var cupidWake = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var loverSelection = InstructionAssert
			.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(cupidWake.CreateResponse()));
		var recognition = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(loverSelection.CreateResponse(
					[elder.Id, hunter.Id])));
		var loversSleep = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(recognition.CreateResponse()));
		var judgeWake = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(loversSleep.CreateResponse()));
		var judgeSetup = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(judgeWake.CreateResponse()));
		builder.Process(judgeSetup.CreateResponse()).IsSuccess.Should().BeTrue();
		var finishNight = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					nightVictim.Id));
		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new()
		{
			[nightVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var conductVote = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(debate.CreateResponse()));
		var signal = InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
			builder.Process(conductVote.CreateResponse()));
		var firstVote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(signal.CreateResponse(
				StutteringJudgeSignalOptionIds.Occurred)));
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerDayActionCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == DayPowerType.JudgeExtraVote);

		var firstCascade = builder.Process(firstVote.CreateResponse([elder.Id]));
		SelectPlayersInstruction? finalShot = null;
		for (var step = 0; step < 12 && finalShot == null; step++)
		{
			if (firstCascade.ModeratorInstruction is SelectPlayersInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.SelectHunterFinalShotTarget
				} shot)
			{
				finalShot = shot;
				break;
			}

			builder.GetGameState()!.GameHistoryLog
				.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
				.Should().BeEmpty();
			firstCascade = firstCascade.ModeratorInstruction switch
			{
				AssignRolesInstruction assignment => builder.Process(
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							id => id,
							id => id == elder.Id
								? MainRoleType.Elder
								: id == hunter.Id
									? MainRoleType.Hunter
									: MainRoleType.SimpleVillager))),
				ConfirmationInstruction confirmation =>
					builder.Process(confirmation.CreateResponse()),
				var instruction => throw new InvalidOperationException(
					$"Unexpected first-cascade instruction: {instruction?.Semantic}.")
			};
		}
		finalShot.Should().NotBeNull();
		var pendingShot = finalShot!;
		var beforeShot = builder.GetGameState()!;
		beforeShot.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().BeEmpty();
		beforeShot.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == elder.Id &&
				entry.Reason == EliminationReason.DayVote);
		beforeShot.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == hunter.Id &&
				entry.Reason == EliminationReason.LoversHeartbreak);

		var afterShot = builder.Process(
			pendingShot.CreateResponse([shotTarget.Id]));
		ConfirmationInstruction? suppression = null;
		for (var step = 0; step < 12 && suppression == null; step++)
		{
			if (afterShot.ModeratorInstruction is ConfirmationInstruction
				{
					Semantic:
						ModeratorInstructionSemantic
							.AnnounceVillagerRolePowerSuppression
				} announcement)
			{
				suppression = announcement;
				break;
			}

			afterShot = afterShot.ModeratorInstruction switch
			{
				AssignRolesInstruction assignment => builder.Process(
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							id => id,
							_ => MainRoleType.SimpleVillager))),
				ConfirmationInstruction confirmation =>
					builder.Process(confirmation.CreateResponse()),
				var instruction => throw new InvalidOperationException(
					$"Unexpected post-shot instruction: {instruction?.Semantic}.")
			};
		}
		suppression.Should().NotBeNull();
		var suppressionAnnouncement = suppression!;
		var suppressed = builder.GetGameState()!;
		suppressed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == shotTarget.Id &&
				entry.Reason == EliminationReason.HunterShot);
		suppressed.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ScopeId == "Day:1:Vote:1");
		suppressed.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().ContainSingle()
			.Which.AnnouncementInstructionId.Should().Be(
				suppressionAnnouncement.InstructionId);
		suppressed.GameHistoryLog
			.OfType<
				VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry>()
			.Should().BeEmpty();
		suppressed.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Should().ContainSingle();

		var consecutiveVote = builder.Process(
				suppressionAnnouncement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		consecutiveVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		var resumed = builder.GetGameState()!;
		resumed.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().ContainSingle();
		resumed.GameHistoryLog
			.OfType<
				VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry>()
			.Should().ContainSingle()
			.Which.AnnouncementInstructionId.Should().Be(
				suppressionAnnouncement.InstructionId);
		MarkTestCompleted();
	}

	[Fact]
	public void PendingSuppressionAnnouncement_WithForgedAnnouncementCorrelation_IsRejectedBeforeAUsableSession()
	{
		var builder = ArrangePendingSuppressionAnnouncement(out _);

		var forged = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.RewritePendingConfirmationInstructionId(Guid.NewGuid())
			.Serialize();
		var recoveredService = new GameService();
		Action rehydrate = () => recoveredService.RehydrateSession(forged);

		rehydrate.Should().Throw<InvalidOperationException>();
		MarkTestCompleted();
	}

	[Fact]
	public void PendingSuppressionAnnouncement_WithForgedAnnouncementShape_IsRejectedBeforeAUsableSession()
	{
		var builder = ArrangePendingSuppressionAnnouncement(out _);
		var bystander = builder.GetGameState()!.GetPlayers()
			.First(player => player.State.Health == PlayerHealth.Alive);

		var forged = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.RewritePendingConfirmationAffectedPlayer(bystander.Id)
			.Serialize();
		var recoveredService = new GameService();
		Action rehydrate = () => recoveredService.RehydrateSession(forged);

		rehydrate.Should().Throw<InvalidOperationException>();
		MarkTestCompleted();
	}

	private GameTestBuilder ArrangePendingSuppressionAnnouncement(
		out ConfirmationInstruction announcement)
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var elder = players[1];
		builder.ArrangeKnownPhysicalRole(elder.Id, MainRoleType.Elder);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase([werewolf.Id], elder.Id);
		builder.CompleteDawnPhase();
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var reveal = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(vote.CreateResponse([elder.Id])));
		var elimination =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(reveal.CreateResponse()));
		announcement =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(elimination.CreateResponse()));
		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillagerRolePowerSuppression);
		return builder;
	}
}
