using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class StutteringJudgeRoleTests : DiagnosticTestBase
{
	public StutteringJudgeRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void FirstNight_UnknownHolder_IdentifiesExactlyOnceThenEstablishesSignal()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();

		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var judge = builder.GetGameState()!.GetPlayers().First();

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(
			MainRoleType.StutteringJudge);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Single);
		identification.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(
				MainRoleType.StutteringJudge.GetPublicName()));

		var setup =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					identification.CreateResponse([judge.Id])));

		setup.Semantic.Should().Be(
			ModeratorInstructionSemantic.EstablishStutteringJudgeSignal);
		setup.PublicAnnouncement.Should().BeNull();
		setup.PrivateInstruction.Should().Be(
			GameStrings.StutteringJudgeSignalSetupInstruction);
		setup.AffectedPlayerIds.Should().Equal(judge.Id);

		var afterSetup = builder.Process(setup.CreateResponse());

		afterSetup.IsSuccess.Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<StutteringJudgeSignalEstablishedLogEntry>()
			.Should().ContainSingle(entry => entry.JudgePlayerId == judge.Id);
		var werewolfObservation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				afterSetup);
		werewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_KnownHolder_SkipsIdentificationAndEstablishesSignal()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var judge = builder.GetGameState()!.GetPlayers().First();
		builder.ArrangeKnownRole(judge.Id, MainRoleType.StutteringJudge);
		builder.ConfirmGameStart();

		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		var setup =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(wake.CreateResponse()));

		setup.Semantic.Should().Be(
			ModeratorInstructionSemantic.EstablishStutteringJudgeSignal);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Where(entry => entry.Role == MainRoleType.StutteringJudge)
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_SignalSetupRecovery_PersistsCompletionWithoutReplayingSetup()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var judge = builder.GetGameState()!.GetPlayers().First();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var setup =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					identification.CreateResponse([judge.Id])));
		var firstService = new GameService();
		var gameId = firstService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredSetup = InstructionAssert.ExpectType<ConfirmationInstruction>(
			firstService.GetCurrentInstruction(gameId));
		recoveredSetup.InstructionId.Should().Be(setup.InstructionId);

		var afterSetup = firstService.ProcessInstruction(
			gameId,
			recoveredSetup.CreateResponse());
		var werewolfObservation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				afterSetup);
		var firstRecovered = firstService.GetGameStateView(gameId)!;
		firstRecovered.GameHistoryLog
			.OfType<StutteringJudgeSignalEstablishedLogEntry>()
			.Should().ContainSingle(entry => entry.JudgePlayerId == judge.Id);

		var secondService = new GameService();
		var secondId = secondService.RehydrateSession(firstRecovered.Serialize());
		var recoveredWerewolfObservation =
			InstructionAssert.ExpectType<SelectPlayersInstruction>(
				secondService.GetCurrentInstruction(secondId));

		recoveredWerewolfObservation.InstructionId.Should().Be(
			werewolfObservation.InstructionId);
		recoveredWerewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		recoveredWerewolfObservation.SelectablePlayerIds.Should().BeEquivalentTo(
			werewolfObservation.SelectablePlayerIds);
		secondService.GetGameStateView(secondId)!.GameHistoryLog
			.OfType<StutteringJudgeSignalEstablishedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_PhysicalVoteCompletesBeforeNegativeSignalObservationAndResultCapture()
	{
		var (builder, judge, _, _) = CreateGameAtFirstDay();
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());

		var conductVote =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(debate.CreateResponse()));

		conductVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.ConductDayVote);
		conductVote.PublicAnnouncement.Should().Be(
			GameStrings.VoteStartsPublicInstruction);
		conductVote.PrivateInstruction.Should().Be(
			GameStrings.DayVoteConductInstruction);
		builder.GetGameState()!.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Should().BeEmpty();

		var signal =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(conductVote.CreateResponse()));

		signal.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveStutteringJudgeSignal);
		signal.PublicAnnouncement.Should().BeNull();
		signal.PrivateInstruction.Should().Be(
			GameStrings.StutteringJudgeSignalObservationInstruction);
		signal.AffectedPlayerIds.Should().Equal(judge.Id);
		signal.SelectionRange.Should().BeEquivalentTo(
			NumberRangeConstraint.Single);
		signal.Options.Select(option => (option.Id, option.Label)).Should().Equal(
			(
				StutteringJudgeSignalOptionIds.Occurred,
				GameStrings.StutteringJudgeSignalOccurredOption),
			(
				StutteringJudgeSignalOptionIds.DidNotOccur,
				GameStrings.StutteringJudgeSignalDidNotOccurOption));

		var voteResult =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(signal.CreateResponse(
					StutteringJudgeSignalOptionIds.DidNotOccur)));

		voteResult.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		voteResult.PublicAnnouncement.Should().BeNull();
		builder.GetGameState()!.GameHistoryLog
			.OfType<StutteringJudgeSignalDidNotOccurLogEntry>()
			.Should().ContainSingle(entry =>
				entry.JudgePlayerId == judge.Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(NativeSignalRecoveryTamper.PublicAnnouncement)]
	[InlineData(NativeSignalRecoveryTamper.PrivateInstruction)]
	[InlineData(NativeSignalRecoveryTamper.AffectedPlayer)]
	public void FirstDay_SignalObservationRecoveryRejectsInvalidNativePresentation(
		NativeSignalRecoveryTamper tamper)
	{
		var (builder, judge, _, _) = CreateGameAtFirstDay();
		var signal = ReachSignalObservation(builder);
		var session = (GameSession)builder.GetGameState()!;
		var otherPlayerId = session.GetPlayers()
			.First(player => player.Id != judge.Id).Id;
		var publicAnnouncement = tamper ==
			NativeSignalRecoveryTamper.PublicAnnouncement
			? "tampered public announcement"
			: signal.PublicAnnouncement;
		var privateInstruction = tamper ==
			NativeSignalRecoveryTamper.PrivateInstruction
			? "tampered private instruction"
			: signal.PrivateInstruction;
		IReadOnlyList<Guid>? affectedPlayerIds = tamper ==
			NativeSignalRecoveryTamper.AffectedPlayer
			? [otherPlayerId]
			: signal.AffectedPlayerIds;
		var tampered = new SelectOptionsInstruction(
			signal.Semantic,
			signal.Options,
			signal.SelectionRange,
			publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			signal.InstructionId);
		var serialized = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(tampered)
			.Serialize();
		var service = new GameService();

		var restore = () => service.RehydrateSession(serialized);

		restore.Should().Throw<InvalidOperationException>()
			.WithMessage("*Stuttering Judge signal instruction*structurally invalid*");
		service.GetGameStateView(session.Id).Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_SignalObservationRecoveryRejectsReportedVoteOutcome()
	{
		var (builder, _, _, _) = CreateGameAtFirstDay();
		var signal = ReachSignalObservation(builder);
		var session = (GameSession)builder.GetGameState()!;
		session.PerformDayVote(null);
		var serialized = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(signal)
			.Serialize();
		var service = new GameService();

		var restore = () => service.RehydrateSession(serialized);

		restore.Should().Throw<InvalidOperationException>()
			.WithMessage("*Stuttering Judge signal instruction*structurally invalid*");
		service.GetGameStateView(session.Id).Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_PositiveSignalAtomicallySpendsPowerAndCommitsConsecutiveVoteBeforeResult()
	{
		var (builder, judge, _, _) = CreateGameAtFirstDay();
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var conductVote =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(debate.CreateResponse()));
		var signal =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(conductVote.CreateResponse()));

		var voteResult =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(signal.CreateResponse(
					StutteringJudgeSignalOptionIds.Occurred)));

		voteResult.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		var commit = builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerDayActionCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		commit.ActionType.Should().Be(DayPowerType.JudgeExtraVote);
		commit.TargetIds.Should().BeNull();
		commit.ResourceIdentity.ActingPlayerId.Should().Be(judge.Id);
		commit.ResourceIdentity.SourceRole.Should().Be(
			MainRoleType.StutteringJudge);
		commit.ResourceIdentity.PowerInstanceId.Should().Be(judge.Id);
		commit.ResourceIdentity.PowerInstanceOrigin.Should().Be(
			RolePowerInstanceOrigin.Native);
		commit.ResourceIdentity.IsValid.Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<StutteringJudgeSignalDidNotOccurLogEntry>()
			.Should().BeEmpty();
		builder.GetGameState()!.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_NoEligibleVoters_CompletesSilentlyBeforePhysicalVote()
	{
		var (builder, _, _, _) = CreateGameAtFirstDay();
		foreach (var player in builder.GetGameState()!.GetPlayers())
		{
			builder.ArrangeVotingRight(player.Id, hasVotingRight: false);
		}
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());

		var nextNight = builder.Process(debate.CreateResponse());

		nextNight.IsSuccess.Should().BeTrue();
		nextNight.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		var session = builder.GetGameState()!;
		session.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Where(entry => entry.ScopeId.StartsWith(
				$"Day:{session.TurnNumber}:Vote:",
				StringComparison.Ordinal))
			.Should().BeEmpty();
		session.GetPlayers().Should().OnlyContain(player =>
			!player.State.HasVotingRight);
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_NoLegalTargets_CompletesSilentlyBeforePhysicalVote()
	{
		var (builder, _, _, _) = CreateGameAtFirstDay();
		foreach (var player in builder.GetGameState()!.GetPlayers()
			         .Where(player => player.State.Health == PlayerHealth.Alive))
		{
			builder.ArrangeEliminatedPlayer(player.Id);
		}
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());

		var terminal = builder.Process(debate.CreateResponse());

		terminal.IsSuccess.Should().BeTrue();
		var finished = terminal.ModeratorInstruction.Should()
			.BeOfType<FinishedGameConfirmationInstruction>().Subject;
		finished.GameResult.Should().BeOfType<NoWinnerGameResult>();
		finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.PreNight);
		builder.GetGameState()!.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_VoteConductRecoveryRejectsReportedVoteOutcome()
	{
		var (builder, _, _, _) = CreateGameAtFirstDay();
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var conductVote =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(debate.CreateResponse()));
		var session = (GameSession)builder.GetGameState()!;
		session.PerformDayVote(null);
		var serialized = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(conductVote)
			.Serialize();
		var service = new GameService();

		var restore = () => service.RehydrateSession(serialized);

		restore.Should().Throw<InvalidOperationException>()
			.WithMessage(
				"*Stuttering Judge vote conduct instruction*structurally invalid*");
		service.GetGameStateView(session.Id).Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_UnansweredSignalRecovery_RestoresExactObservation()
	{
		var (builder, judge, _, _) = CreateGameAtFirstDay();
		var signal = ReachSignalObservation(builder);
		var service = new GameService();
		var gameId = service.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recovered = InstructionAssert.ExpectType<SelectOptionsInstruction>(
			service.GetCurrentInstruction(gameId));

		recovered.InstructionId.Should().Be(signal.InstructionId);
		recovered.Options.Should().Equal(signal.Options);
		recovered.PublicAnnouncement.Should().BeNull();
		recovered.PrivateInstruction.Should().Be(
			GameStrings.StutteringJudgeSignalObservationInstruction);
		recovered.AffectedPlayerIds.Should().Equal(judge.Id);
		var result = service.ProcessInstruction(
			gameId,
			recovered.CreateResponse(
				StutteringJudgeSignalOptionIds.DidNotOccur));

		InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(result);
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<StutteringJudgeSignalDidNotOccurLogEntry>()
			.Should().ContainSingle(entry => entry.JudgePlayerId == judge.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_AcceptedNegativeRecovery_ResumesExactResultWithoutReobservation()
	{
		var (builder, _, _, _) = CreateGameAtFirstDay();
		var signal = ReachSignalObservation(builder);
		var result =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(signal.CreateResponse(
					StutteringJudgeSignalOptionIds.DidNotOccur)));
		var service = new GameService();
		var gameId = service.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recovered = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			service.GetCurrentInstruction(gameId));

		recovered.InstructionId.Should().Be(result.InstructionId);
		var afterVote = service.ProcessInstruction(
			gameId,
			recovered.CreateResponse([]));

		afterVote.IsSuccess.Should().BeTrue();
		afterVote.ModeratorInstruction!.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.ObserveStutteringJudgeSignal);
		var session = service.GetGameStateView(gameId)!;
		session.GameHistoryLog
			.OfType<StutteringJudgeSignalDidNotOccurLogEntry>()
			.Should().ContainSingle();
		session.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_AcceptedPositiveRecovery_ResumesResultAndSchedulesOneLegacyRepeat()
	{
		var (builder, _, _, _) = CreateGameAtFirstDay();
		var signal = ReachSignalObservation(builder);
		var result =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(signal.CreateResponse(
					StutteringJudgeSignalOptionIds.Occurred)));
		var service = new GameService();
		var gameId = service.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recovered = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			service.GetCurrentInstruction(gameId));

		recovered.InstructionId.Should().Be(result.InstructionId);
		var repeatedVote =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					gameId,
					recovered.CreateResponse([])));

		repeatedVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		repeatedVote.PublicAnnouncement.Should().Be(
			GameStrings.VoteStartsPublicInstruction);
		var session = service.GetGameStateView(gameId)!;
		session.GameHistoryLog
			.OfType<OneUseRolePowerDayActionCommittedLogEntry>()
			.Should().ContainSingle();
		session.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_InvalidSignalResponses_AreSideEffectFree()
	{
		var (builder, _, _, _) = CreateGameAtFirstDay();
		var signal = ReachSignalObservation(builder);
		var cases = new (string Name, ModeratorResponse Response)[]
		{
			("empty", new ModeratorResponse
			{
				InstructionId = signal.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds = []
			}),
			("multiple", new ModeratorResponse
			{
				InstructionId = signal.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds =
				[
					StutteringJudgeSignalOptionIds.Occurred,
					StutteringJudgeSignalOptionIds.DidNotOccur
				]
			}),
			("unknown", new ModeratorResponse
			{
				InstructionId = signal.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds = ["unknown-signal"]
			}),
			("wrong-type", new ModeratorResponse
			{
				InstructionId = signal.InstructionId,
				Type = ExpectedInputType.Continue
			}),
			("wrong-correlation", new ModeratorResponse
			{
				InstructionId = Guid.NewGuid(),
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds =
					[StutteringJudgeSignalOptionIds.Occurred]
			})
		};

		foreach (var invalidCase in cases)
		{
			var before = builder.GetGameState()!.Serialize();
			var beforeLogs = builder.GetGameState()!.GameHistoryLog.ToArray();

			var act = () => builder.Process(invalidCase.Response);

			act.Should().Throw<InvalidOperationException>(invalidCase.Name);
			builder.GetCurrentInstruction()!.InstructionId.Should().Be(
				signal.InstructionId,
				invalidCase.Name);
			builder.GetGameState()!.Serialize().Should().Be(
				before,
				invalidCase.Name);
			builder.GetGameState()!.GameHistoryLog.Should().Equal(
				beforeLogs,
				invalidCase.Name);
		}
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_SpentStaleSignalResponseAfterAcceptance_IsSideEffectFree()
	{
		var (builder, _, _, _) = CreateGameAtFirstDay();
		var signal = ReachSignalObservation(builder);
		var staleResponse = signal.CreateResponse(
			StutteringJudgeSignalOptionIds.Occurred);
		var result =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(staleResponse));
		var before = builder.GetGameState()!.Serialize();
		var beforeLogs = builder.GetGameState()!.GameHistoryLog.ToArray();

		var act = () => builder.Process(staleResponse);

		act.Should().Throw<InvalidOperationException>();
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			result.InstructionId);
		builder.GetGameState()!.Serialize().Should().Be(before);
		builder.GetGameState()!.GameHistoryLog.Should().Equal(beforeLogs);
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerDayActionCommittedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_AvailabilityRevokedAfterPrompt_RejectsWithoutSideEffects()
	{
		var policy = new SequenceAvailabilityPolicy(true, false);
		var (builder, _, _, _) = CreateGameAtFirstDay(policy);
		var signal = ReachSignalObservation(builder);
		var response = signal.CreateResponse(
			StutteringJudgeSignalOptionIds.Occurred);
		var before = builder.GetGameState()!.Serialize();
		var beforeLogs = builder.GetGameState()!.GameHistoryLog.ToArray();

		var act = () => builder.Process(response);

		act.Should().Throw<InvalidOperationException>();
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			signal.InstructionId);
		builder.GetGameState()!.Serialize().Should().Be(before);
		builder.GetGameState()!.GameHistoryLog.Should().Equal(beforeLogs);
		MarkTestCompleted();
	}

	[Fact]
	public void FirstDay_UnavailableOpportunity_UsesLegacyCombinedVoteWithoutSignalPrompt()
	{
		var policy = new SequenceAvailabilityPolicy(false);
		var (builder, _, _, _) = CreateGameAtFirstDay(policy);
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());

		var vote =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(debate.CreateResponse()));

		vote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		vote.PublicAnnouncement.Should().Be(
			GameStrings.VoteStartsPublicInstruction);
		builder.GetGameState()!.GameHistoryLog
			.OfType<StutteringJudgeSignalDidNotOccurLogEntry>()
			.Should().BeEmpty();
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerDayActionCommittedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void LegacyDayWithoutJudge_UsesCombinedRecordInstructionAndNeverConductSemantic()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.CompleteNightPhase([players[0].Id], players[^1].Id);
		builder.CompleteDawnPhase(new()
		{
			[players[^1].Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());

		var vote =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(debate.CreateResponse()));

		vote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		vote.PublicAnnouncement.Should().Be(
			GameStrings.VoteStartsPublicInstruction);
		vote.PrivateInstruction.Should().Be(
			GameStrings.VoteStartsModeratorInstruction);
		builder.GetGameState()!.GameHistoryLog.Should().NotContain(entry =>
				entry is OneUseRolePowerDayActionCommittedLogEntry);
		MarkTestCompleted();
	}

	[Fact]
	public void DayTwo_AcquiredJudge_DoesNotReusePredecessorsNightOneSignal()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleWerewolf,
				MainRoleType.DevotedServant,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var originalJudge = players[0];
		var werewolf = players[1];
		var servant = players[2];
		var firstNightVictim = players[6];
		var secondNightVictim = players[5];
		builder.ArrangeKnownRole(
			originalJudge.Id,
			MainRoleType.StutteringJudge);
		builder.ConfirmGameStart();

		var judgeWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var signalSetup =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(judgeWake.CreateResponse()));
		_ = builder.Process(signalSetup.CreateResponse());
		var firstNightEnd =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					firstNightVictim.Id));
		_ = builder.Process(firstNightEnd.CreateResponse());
		builder.CompleteDawnPhase(new()
		{
			[firstNightVictim.Id] = MainRoleType.SimpleVillager
		});

		var firstDayDebate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var conductFirstVote =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(firstDayDebate.CreateResponse()));
		var signalObservation =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(conductFirstVote.CreateResponse()));
		var firstVote =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(signalObservation.CreateResponse(
					StutteringJudgeSignalOptionIds.DidNotOccur)));
		var servantWindow =
			InstructionAssert.ExpectSuccessWithType<
				DevotedServantVoteWindowInstruction>(
				builder.Process(firstVote.CreateResponse([originalJudge.Id])));
		var acquiredCard =
			InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
				builder.Process(servantWindow.CreatePublicSelfRevealResponse(
					servant.Id)));
		var firstEliminationAnnouncement =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(acquiredCard.CreateResponse(new()
				{
					[originalJudge.Id] = MainRoleType.StutteringJudge
				})));

		var transfer = builder.GetGameState()!.GameHistoryLog
			.OfType<DevotedServantRoleTakenCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		transfer.ActingPlayerId.Should().Be(servant.Id);
		transfer.VoteTargetId.Should().Be(originalJudge.Id);
		transfer.NewCurrentRole.Should().Be(MainRoleType.StutteringJudge);
		transfer.PowerInstanceOrigin.Should().Be(RolePowerInstanceOrigin.Swapped);
		transfer.NewPowerInstanceId.Should().NotBe(servant.Id);
		transfer.NewPowerInstanceId.Should().NotBe(originalJudge.Id);
		servant.State.CurrentRole.Should().Be(MainRoleType.StutteringJudge);
		originalJudge.State.Health.Should().Be(PlayerHealth.Dead);

		var secondNightStart = builder.Process(
			firstEliminationAnnouncement.CreateResponse());
		secondNightStart.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		builder.ConfirmNightStart();
		_ = builder.CompleteWerewolfNightActionSubsequentNight(
			secondNightVictim.Id);
		var secondNightEnd = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		_ = builder.Process(secondNightEnd.CreateResponse());
		builder.CompleteDawnPhase(new()
		{
			[secondNightVictim.Id] = MainRoleType.SimpleVillager
		});

		var secondDayDebate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var directSecondDayVote =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(secondDayDebate.CreateResponse()));

		directSecondDayVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		var session = builder.GetGameState()!;
		session.GameHistoryLog
			.OfType<StutteringJudgeSignalEstablishedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.JudgePlayerId == originalJudge.Id);
		session.GameHistoryLog
			.OfType<StutteringJudgeSignalDidNotOccurLogEntry>()
			.Should().NotContain(entry => entry.JudgePlayerId == servant.Id);
		session.GameHistoryLog
			.OfType<OneUseRolePowerDayActionCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.ResourceIdentity.ActingPlayerId == servant.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void PositiveSignal_AfterFirstCascade_UsesFreshScopeRosterAndVotingRightsEvenIfJudgeDies()
	{
		var (builder, judge, werewolf, _) = CreateGameAtFirstDay();
		var livingNonJudge = builder.GetGameState()!.GetPlayers()
			.Where(player =>
				player.Id != judge.Id &&
				player.State.Health == PlayerHealth.Alive)
			.ToArray();
		var soleEligibleSurvivor = livingNonJudge[0];
		foreach (var player in livingNonJudge)
		{
			builder.ArrangeVotingRight(
				player.Id,
				player.Id == soleEligibleSurvivor.Id);
		}
		var signal = ReachSignalObservation(builder);
		var firstVote =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(signal.CreateResponse(
					StutteringJudgeSignalOptionIds.Occurred)));
		var next = builder.Process(firstVote.CreateResponse([judge.Id]));
		var betweenVoteSemantics = new List<ModeratorInstructionSemantic>();
		SelectPlayersInstruction? secondVote = null;

		for (var step = 0; step < 20 && secondVote == null; step++)
		{
			var instruction = next.ModeratorInstruction!;
			if (instruction is SelectPlayersInstruction candidate &&
			    candidate.Semantic ==
			    ModeratorInstructionSemantic.RecordDayVote)
			{
				secondVote = candidate;
				break;
			}

			betweenVoteSemantics.Add(instruction.Semantic);
			instruction.Semantic.Should().NotBe(
				ModeratorInstructionSemantic.StartDayDebate);
			instruction.Semantic.Should().NotBe(
				ModeratorInstructionSemantic.FinishedGame);
			instruction.Semantic.Should().NotBe(
				ModeratorInstructionSemantic.ConductDayVote);
			instruction.Semantic.Should().NotBe(
				ModeratorInstructionSemantic.ObserveStutteringJudgeSignal);
			next = instruction switch
			{
				AssignRolesInstruction assign => builder.Process(
					assign.CreateResponse(
						assign.PlayersForAssignment.ToDictionary(
							id => id,
							id => id == judge.Id
								? MainRoleType.StutteringJudge
								: MainRoleType.SimpleVillager))),
				ConfirmationInstruction confirmation =>
					builder.Process(confirmation.CreateResponse()),
				_ => throw new InvalidOperationException(
					$"Unexpected instruction before the repeated vote: {instruction.Semantic}.")
			};
		}

		secondVote.Should().NotBeNull();
		var session = builder.GetGameState()!;
		session.GetPlayerState(judge.Id).Health.Should().Be(PlayerHealth.Dead);
		session.GetPlayers()
			.Where(player => player.State.Health == PlayerHealth.Alive)
			.Should().ContainSingle(player => player.State.HasVotingRight);
		secondVote!.SelectablePlayerIds.Should().BeEquivalentTo(
			session.GetPlayers()
				.Where(player => player.State.Health == PlayerHealth.Alive)
				.Select(player => player.Id));
		secondVote.PublicAnnouncement.Should().Be(
			GameStrings.VoteStartsPublicInstruction);
		session.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Where(entry => entry.ScopeId.StartsWith(
				"Day:1:Vote:",
				StringComparison.Ordinal))
			.Select(entry => entry.ScopeId)
			.Should().Equal("Day:1:Vote:1");

		var secondTarget = session.GetPlayers().First(player =>
			player.State.Health == PlayerHealth.Alive &&
			player.Id != werewolf.Id);
		next = builder.Process(
			secondVote.CreateResponse([secondTarget.Id]));
		for (var step = 0;
		     step < 20 &&
		     builder.GetGameState()!.GetCurrentPhase() == GamePhase.Day;
		     step++)
		{
			var instruction = next.ModeratorInstruction!;
			next = instruction switch
			{
				AssignRolesInstruction assign => builder.Process(
					assign.CreateResponse(
						assign.PlayersForAssignment.ToDictionary(
							id => id,
							_ => MainRoleType.SimpleVillager))),
				ConfirmationInstruction confirmation =>
					builder.Process(confirmation.CreateResponse()),
				_ => throw new InvalidOperationException(
					$"Unexpected instruction after the repeated vote: {instruction.Semantic}.")
			};
		}

		session.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Where(entry => entry.ScopeId.StartsWith(
				"Day:1:Vote:",
				StringComparison.Ordinal))
			.Select(entry => entry.ScopeId)
			.Should().Equal(
				"Day:1:Vote:1",
				"Day:1:Vote:2");
		session.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Should().HaveCount(2);
		builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Night);
		MarkTestCompleted();
	}

	[Fact]
	public void PositiveSignal_PostCascadeWithoutEligibleVoters_RehydratesAndSkipsConsecutiveVote()
	{
		var (builder, judge, _, _) = CreateGameAtFirstDay();
		foreach (var player in builder.GetGameState()!.GetPlayers()
			         .Where(player =>
				         player.Id != judge.Id &&
				         player.State.Health == PlayerHealth.Alive))
		{
			builder.ArrangeVotingRight(player.Id, hasVotingRight: false);
		}
		var signal = ReachSignalObservation(builder);
		var firstVote =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(signal.CreateResponse(
					StutteringJudgeSignalOptionIds.Occurred)));
		var next = builder.Process(firstVote.CreateResponse([judge.Id]));
		for (var step = 0;
		     step < 10 &&
		     next.ModeratorInstruction?.Semantic !=
			     ModeratorInstructionSemantic.AnnounceDayElimination;
		     step++)
		{
			next = next.ModeratorInstruction switch
			{
				AssignRolesInstruction assign => builder.Process(
					assign.CreateResponse(
						assign.PlayersForAssignment.ToDictionary(
							id => id,
							id => id == judge.Id
								? MainRoleType.StutteringJudge
								: MainRoleType.SimpleVillager))),
				ConfirmationInstruction confirmation => builder.Process(
					confirmation.CreateResponse()),
				_ => throw new InvalidOperationException(
					$"Unexpected instruction while settling the first vote: {next.ModeratorInstruction?.Semantic}.")
			};
		}
		var firstAnnouncement =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				next);

		firstAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		var settledSession = builder.GetGameState()!;
		settledSession.GetPlayerState(judge.Id).Health.Should().Be(
			PlayerHealth.Dead);
		settledSession.GetPlayers()
			.Where(player => player.State.Health == PlayerHealth.Alive)
			.Should().OnlyContain(player => !player.State.HasVotingRight);

		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			settledSession.Serialize());
		var recoveredAnnouncement =
			InstructionAssert.ExpectType<ConfirmationInstruction>(
				recoveredService.GetCurrentInstruction(recoveredGameId));

		recoveredAnnouncement.InstructionId.Should().Be(
			firstAnnouncement.InstructionId);
		recoveredService.GetGameStateView(recoveredGameId)!.GetPlayers()
			.Where(player => player.State.Health == PlayerHealth.Alive)
			.Should().OnlyContain(player => !player.State.HasVotingRight);

		var nextNight = recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredAnnouncement.CreateResponse());

		nextNight.IsSuccess.Should().BeTrue();
		nextNight.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		var recoveredSession =
			recoveredService.GetGameStateView(recoveredGameId)!;
		recoveredSession.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Should().ContainSingle();
		recoveredSession.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Where(entry => entry.ScopeId.StartsWith(
				"Day:1:Vote:",
				StringComparison.Ordinal))
			.Select(entry => entry.ScopeId)
			.Should().Equal("Day:1:Vote:1");
		recoveredSession.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Where(entry =>
				entry.TurnNumber == 1 &&
				entry.CurrentPhase == GamePhase.Day)
			.Should().ContainSingle(entry => entry.PlayerId == judge.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void Rehydrate_RejectsStructurallyInvalidStutteringJudgeCommit()
	{
		var (builder, _, _, _) = CreateGameAtFirstDay();
		CommitConsecutiveVoteBeforeResult(builder);
		var payload = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.RewriteLatestStutteringJudgeAction(DayPowerType.Unknown)
			.Serialize();
		var service = new GameService();

		var act = () => service.RehydrateSession(payload);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*structurally invalid*");
		MarkTestCompleted();
	}

	[Fact]
	public void Rehydrate_RejectsTargetedStutteringJudgeCommit()
	{
		var (builder, _, _, _) = CreateGameAtFirstDay();
		CommitConsecutiveVoteBeforeResult(builder);
		var payload = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.TargetLatestStutteringJudgeAction()
			.Serialize();
		var service = new GameService();

		var act = () => service.RehydrateSession(payload);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*structurally invalid*");
		MarkTestCompleted();
	}

	[Fact]
	public void Rehydrate_RejectsCrossTypeDuplicateOneUseResourceIdentity()
	{
		var (builder, _, _, _) = CreateGameAtFirstDay();
		CommitConsecutiveVoteBeforeResult(builder);
		var payload = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.AddCrossTypeDuplicateOfStutteringJudgeResource()
			.Serialize();
		var service = new GameService();

		var act = () => service.RehydrateSession(payload);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*already spent*");
		MarkTestCompleted();
	}

	private static SelectPlayersInstruction CommitConsecutiveVoteBeforeResult(
		GameTestBuilder builder)
	{
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var conductVote =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(debate.CreateResponse()));
		var signal =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(conductVote.CreateResponse()));
		return InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(signal.CreateResponse(
				StutteringJudgeSignalOptionIds.Occurred)));
	}

	private static SelectOptionsInstruction ReachSignalObservation(
		GameTestBuilder builder)
	{
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var conductVote =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(debate.CreateResponse()));
		return InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
			builder.Process(conductVote.CreateResponse()));
	}

	private (GameTestBuilder Builder, IPlayer Judge, IPlayer Werewolf, IPlayer Victim)
		CreateGameAtFirstDay(
			IRolePowerAvailabilityPolicy? availabilityPolicy = null)
	{
		var builder = CreateBuilder();
		if (availabilityPolicy != null)
		{
			builder.WithRolePowerAvailabilityPolicy(availabilityPolicy);
		}
		builder
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var judge = players[0];
		var werewolf = players[1];
		var victim = players[^1];
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var setup =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					identification.CreateResponse([judge.Id])));
		builder.Process(setup.CreateResponse());
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					victim.Id));
		builder.Process(finishNight.CreateResponse());
		builder.CompleteDawnPhase(new()
		{
			[victim.Id] = MainRoleType.SimpleVillager
		});
		return (builder, judge, werewolf, victim);
	}

	private sealed class SequenceAvailabilityPolicy(params bool[] decisions)
		: IRolePowerAvailabilityPolicy
	{
		private readonly Queue<bool> _decisions = new(decisions);

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt) =>
			_decisions.Count > 0 && _decisions.Dequeue()
				? RolePowerAvailabilityResult.Allowed
					: RolePowerAvailabilityResult.Denied;
	}

	public enum NativeSignalRecoveryTamper
	{
		PublicAnnouncement,
		PrivateInstruction,
		AffectedPlayer
	}
}
