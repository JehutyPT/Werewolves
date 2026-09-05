using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
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
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class ThreeBrothersNightRoleTests : DiagnosticTestBase
{
	public ThreeBrothersNightRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void FirstNight_UnknownTrio_IdentifiesExactlyThreeThenRecognizesAndSleeps()
	{
		var policy = new RecordingPolicy();
		var builder = CreateThreeBrothersBuilder(policy);
		builder.StartGame();
		builder.ConfirmGameStart();

		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var brothers = builder.GetGameState()!.GetPlayers().Take(3).ToArray();

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.ThreeBrothers);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Exact(3));
		identification.AffectedPlayerIds.Should().BeNull();
		identification.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersWakeUp.Format(
				MainRoleType.ThreeBrothers.GetPublicName()));
		identification.PublicAnnouncement.Should().NotContainAny(
			brothers.Select(brother => brother.Name).ToArray());

		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(identification.CreateResponse(
					brothers.Select(brother => brother.Id).ToHashSet())));

		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeRoleHolders);
		recognition.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersRecognitionPrompt.Format(
				MainRoleType.ThreeBrothers.GetPublicName()));
		recognition.PrivateInstruction.Should().BeNull();
		recognition.AffectedPlayerIds.Should().Equal(
			brothers.Select(brother => brother.Id).Order());
		policy.ObservedAttempts.Select(attempt => attempt.ActingPlayer.Id).Should()
			.Equal(brothers.Select(brother => brother.Id).Order());
		policy.ObservedAttempts.Should().OnlyContain(attempt =>
			attempt.SourceRole == MainRoleType.ThreeBrothers &&
			attempt.SourcePower.Category == RolePowerCategory.Recognition &&
			attempt.PowerInstance.Origin == RolePowerInstanceOrigin.Native);

		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(recognition.CreateResponse()));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersGoToSleep.Format(
				MainRoleType.ThreeBrothers.GetPublicName()));
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(
			brothers.Select(brother => brother.Id).Order());
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_PartiallyKnownTrio_RejectsOmittingCommittedHolderWithoutStateAdvance()
	{
		var builder = CreateThreeBrothersBuilder(new RecordingPolicy());
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var committedBrother = players[0];
		var brotherIds = players.Take(3).Select(player => player.Id).ToHashSet();
		builder.ArrangePartiallyKnownThreeBrothers(committedBrother.Id);
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var stateBeforeInvalidResponse = builder.SerializeSession();
		var logBeforeInvalidResponse = session.GameHistoryLog.ToArray();
		var invalidSelection = players
			.Skip(1)
			.Take(3)
			.Select(player => player.Id)
			.ToHashSet();

		var invalidAct = () => builder.Process(
			identification.CreateResponse(invalidSelection));

		invalidAct.Should().Throw<InvalidOperationException>()
			.WithMessage("*cannot replace a committed Living Role Holder*");
		builder.GetCurrentInstruction()!.InstructionId.Should()
			.Be(identification.InstructionId);
		session.GameHistoryLog.Should().Equal(logBeforeInvalidResponse);
		builder.SerializeSession().Should().Be(stateBeforeInvalidResponse);

		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(identification.CreateResponse(brotherIds)));

		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeRoleHolders);
		recognition.AffectedPlayerIds.Should().Equal(brotherIds.Order());
		session.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Last().PlayerIds.Should().BeEquivalentTo(brotherIds);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void FirstNight_KnownTrio_RecognitionAvailabilityControlsTheWholeSlot(
		bool recognitionAllowed)
	{
		var policy = new RecordingPolicy(
			recognitionAllowed
				? RolePowerAvailabilityResult.Allowed
				: RolePowerAvailabilityResult.Denied);
		var builder = CreateThreeBrothersBuilder(policy);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var brothers = session.GetPlayers().Take(3).ToArray();
		var brotherIds = brothers.Select(player => player.Id).ToHashSet();
		builder.ArrangeKnownThreeBrothers(brotherIds);
		builder.ConfirmGameStart();

		var afterNightStart = builder.ConfirmNightStart();

		if (recognitionAllowed)
		{
			var recognition =
				InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
					afterNightStart);
			recognition.Semantic.Should().Be(
				ModeratorInstructionSemantic.RecognizeRoleHolders);
			recognition.AffectedPlayerIds.Should().Equal(brotherIds.Order());
		}
		else
		{
			var nextSlot =
				InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
					afterNightStart);
			nextSlot.Semantic.Should().Be(
				ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
			nextSlot.RoleIdentification.Should().BeNull();
		}

		policy.ObservedAttempts.Select(attempt => attempt.ActingPlayer.Id).Should()
			.Equal(brotherIds.Order());
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_UnknownTrio_WhenOneRecognitionIsDenied_EvaluatesAllThenSleeps()
	{
		var policy = new RecordingPolicy(
			RolePowerAvailabilityResult.Denied,
			RolePowerAvailabilityResult.Allowed,
			RolePowerAvailabilityResult.Allowed);
		var builder = CreateThreeBrothersBuilder(policy);
		builder.StartGame();
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var brothers = builder.GetGameState()!.GetPlayers().Take(3).ToArray();

		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(identification.CreateResponse(
				brothers.Select(brother => brother.Id).ToHashSet())));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(
			brothers.Select(brother => brother.Id).Order());
		policy.ObservedAttempts.Select(attempt => attempt.ActingPlayer.Id).Should()
			.Equal(brothers.Select(brother => brother.Id).Order());
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(true, ModeratorInstructionSemantic.RecognizeRoleHolders)]
	[InlineData(false, ModeratorInstructionSemantic.PutRoleToSleep)]
	public void AcceptedIdentification_RehydrateRestoresPinnedContinuationWithoutReevaluation(
		bool recognitionAllowed,
		ModeratorInstructionSemantic expectedSemantic)
	{
		var originalPolicy = new RecordingPolicy(
			recognitionAllowed
				? RolePowerAvailabilityResult.Allowed
				: RolePowerAvailabilityResult.Denied);
		var builder = CreateThreeBrothersBuilder(originalPolicy);
		builder.StartGame();
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var brotherIds = builder.GetGameState()!.GetPlayers()
			.Take(3)
			.Select(player => player.Id)
			.ToHashSet();
		var expectedContinuation =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(identification.CreateResponse(brotherIds)));
		var rehydratedPolicy = new RecordingPolicy(
			recognitionAllowed
				? RolePowerAvailabilityResult.Denied
				: RolePowerAvailabilityResult.Allowed);
		var service = new GameService(rehydratedPolicy);

		var gameId = service.RehydrateSession(builder.SerializeSession());
		var recoveredContinuation =
			InstructionAssert.ExpectType<ConfirmationInstruction>(
				service.GetCurrentInstruction(gameId));

		recoveredContinuation.InstructionId.Should()
			.Be(expectedContinuation.InstructionId);
		recoveredContinuation.Semantic.Should().Be(expectedSemantic);
		recoveredContinuation.PublicAnnouncement.Should()
			.Be(expectedContinuation.PublicAnnouncement);
		recoveredContinuation.PrivateInstruction.Should()
			.Be(expectedContinuation.PrivateInstruction);
		recoveredContinuation.AffectedPlayerIds.Should()
			.Equal(expectedContinuation.AffectedPlayerIds);
		rehydratedPolicy.ObservedAttempts.Should().BeEmpty();
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Count(entry => entry.Role == MainRoleType.ThreeBrothers)
			.Should().Be(1);

		if (recognitionAllowed)
		{
			var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					gameId,
					recoveredContinuation.CreateResponse()));
			sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
			rehydratedPolicy.ObservedAttempts.Should().BeEmpty();
		}

		MarkTestCompleted();
	}

	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	public void OddNightFromThree_LivingQuorumCommunicatesThenSleeps(
		int livingCurrentRoleHolderCount)
	{
		var policy = new RecordingPolicy();
		var fixture = StartThreeBrothersGame(policy);
		var nightThreeStart = AdvanceUntil(
			fixture,
			(session, instruction) =>
				session.TurnNumber == 3 &&
				instruction.Semantic == ModeratorInstructionSemantic.StartNight)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var session = fixture.Builder.GetGameState()!;
		foreach (var brotherId in fixture.BrotherIds
			         .Order()
			         .Take(3 - livingCurrentRoleHolderCount))
		{
			fixture.Builder.ArrangeThreeBrotherLeavesCurrentRole(brotherId);
		}

		var communication =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				fixture.Builder.Process(nightThreeStart.CreateResponse()));
		var currentBrotherIds = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.State.CurrentRole == MainRoleType.ThreeBrothers)
			.Select(player => player.Id)
			.Order()
			.ToArray();

		communication.Semantic.Should().Be(
			ModeratorInstructionSemantic.CommunicateAsRoleHolders);
		communication.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersCommunicationPrompt.Format(
				MainRoleType.ThreeBrothers.GetPublicName()));
		communication.PrivateInstruction.Should().BeNull();
		communication.AffectedPlayerIds.Should().Equal(currentBrotherIds);
		policy.ObservedAttempts.Take(3).Should().OnlyContain(attempt =>
			attempt.SourcePower.Category == RolePowerCategory.Recognition);
		policy.ObservedAttempts.Skip(3).Should().HaveCount(
			livingCurrentRoleHolderCount);
		policy.ObservedAttempts.Skip(3).Should().OnlyContain(attempt =>
			attempt.SourceRole == MainRoleType.ThreeBrothers &&
			attempt.SourcePower.Category == RolePowerCategory.Communication &&
			attempt.PowerInstance.Origin == RolePowerInstanceOrigin.Native);
		policy.ObservedAttempts.Skip(3)
			.Select(attempt => attempt.ActingPlayer.Id).Should()
			.Equal(currentBrotherIds);

		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			fixture.Builder.Process(communication.CreateResponse()));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(currentBrotherIds);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	public void LaterCadence_EvenNightAndOddSubquorumOmitWithoutPowerAttempts(
		int livingCurrentRoleHolderCount)
	{
		var policy = new RecordingPolicy();
		var fixture = StartThreeBrothersGame(policy);
		var nightTwoStart = AdvanceUntil(
			fixture,
			(session, instruction) =>
				session.TurnNumber == 2 &&
				instruction.Semantic == ModeratorInstructionSemantic.StartNight)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		var afterNightTwoStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				fixture.Builder.Process(nightTwoStart.CreateResponse()));

		afterNightTwoStart.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		policy.ObservedAttempts.Should().HaveCount(3);

		var nightThreeStart = AdvanceUntil(
			fixture,
			(session, instruction) =>
				session.TurnNumber == 3 &&
				instruction.Semantic == ModeratorInstructionSemantic.StartNight)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var session = fixture.Builder.GetGameState()!;
		foreach (var brotherId in fixture.BrotherIds
			         .Order()
			         .Take(3 - livingCurrentRoleHolderCount))
		{
			fixture.Builder.ArrangeThreeBrotherLeavesCurrentRole(brotherId);
		}

		var afterNightThreeStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				fixture.Builder.Process(nightThreeStart.CreateResponse()));

		afterNightThreeStart.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		policy.ObservedAttempts.Should().HaveCount(3);
		MarkTestCompleted();
	}

	[Fact]
	public void OddNightFromThree_WhenOneCommunicationIsDenied_EvaluatesAllAndOmitsTheWholeSlot()
	{
		var policy = new RecordingPolicy(
			RolePowerAvailabilityResult.Allowed,
			RolePowerAvailabilityResult.Allowed,
			RolePowerAvailabilityResult.Allowed,
			RolePowerAvailabilityResult.Denied,
			RolePowerAvailabilityResult.Allowed,
			RolePowerAvailabilityResult.Allowed);
		var fixture = StartThreeBrothersGame(policy);
		var nightThreeStart = AdvanceUntil(
			fixture,
			(session, instruction) =>
				session.TurnNumber == 3 &&
				instruction.Semantic == ModeratorInstructionSemantic.StartNight)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		var nextRole =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				fixture.Builder.Process(nightThreeStart.CreateResponse()));

		nextRole.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		policy.ObservedAttempts.Should().HaveCount(6);
		policy.ObservedAttempts.Skip(3)
			.Select(attempt => attempt.ActingPlayer.Id).Should()
			.Equal(fixture.BrotherIds.Order());
		policy.ObservedAttempts.Skip(3).Should().OnlyContain(attempt =>
			attempt.SourcePower.Category == RolePowerCategory.Communication);
		MarkTestCompleted();
	}

	[Fact]
	public void Recognition_ReplayedResponseAtSleep_IsRejectedWithoutStateAdvance()
	{
		var builder = CreateThreeBrothersBuilder(new RecordingPolicy());
		builder.StartGame();
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var brotherIds = builder.GetGameState()!.GetPlayers()
			.Take(3)
			.Select(player => player.Id)
			.ToHashSet();
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(identification.CreateResponse(brotherIds)));
		var completedRecognitionResponse = recognition.CreateResponse();
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(completedRecognitionResponse));

		AssertCorrelationFailureIsSideEffectFree(
			builder,
			sleep,
			completedRecognitionResponse);

		builder.Process(sleep.CreateResponse()).IsSuccess.Should().BeTrue();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_OrderRunsTwoSistersThenThreeBrothersThenWildChild()
	{
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(new RecordingPolicy())
			.WithPlayers(
				"Sister A",
				"Sister B",
				"Brother A",
				"Brother B",
				"Brother C",
				"Wild Child",
				"Werewolf",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.TwoSisters,
				MainRoleType.TwoSisters,
				MainRoleType.ThreeBrothers,
				MainRoleType.ThreeBrothers,
				MainRoleType.ThreeBrothers,
				MainRoleType.WildChild,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ConfirmGameStart();

		var sistersIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		sistersIdentification.RoleIdentification.Should().Be(
			MainRoleType.TwoSisters);
		var sistersRecognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sistersIdentification.CreateResponse(
					players.Take(2).Select(player => player.Id).ToHashSet())));
		var sistersSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sistersRecognition.CreateResponse()));
		var brothersIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(sistersSleep.CreateResponse()));

		brothersIdentification.RoleIdentification.Should().Be(
			MainRoleType.ThreeBrothers);
		var brothersRecognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(brothersIdentification.CreateResponse(
					players.Skip(2).Take(3).Select(player => player.Id).ToHashSet())));
		var brothersSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(brothersRecognition.CreateResponse()));
		var wildChildIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(brothersSleep.CreateResponse()));

		wildChildIdentification.RoleIdentification.Should().Be(
			MainRoleType.WildChild);
		MarkTestCompleted();
	}

	[Fact]
	public void OddNightFromThree_OrderCompletesTwoSistersThenThreeBrothersBeforeWerewolves()
	{
		var fixture = StartSistersAndThreeBrothersGame(new RecordingPolicy());
		var sistersCommunication = AdvanceUntil(
				fixture,
				(session, instruction) =>
					session.TurnNumber == 3 &&
					instruction.Semantic ==
					ModeratorInstructionSemantic.CommunicateAsRoleHolders)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sistersCommunication.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersCommunicationPrompt.Format(
				MainRoleType.TwoSisters.GetPublicName()));
		sistersCommunication.AffectedPlayerIds.Should()
			.Equal(fixture.SisterIds.Order());

		var sistersSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				fixture.Builder.Process(sistersCommunication.CreateResponse()));

		sistersSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sistersSleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersGoToSleep.Format(
				MainRoleType.TwoSisters.GetPublicName()));
		sistersSleep.AffectedPlayerIds.Should().Equal(fixture.SisterIds.Order());

		var brothersCommunication =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				fixture.Builder.Process(sistersSleep.CreateResponse()));

		brothersCommunication.Semantic.Should().Be(
			ModeratorInstructionSemantic.CommunicateAsRoleHolders);
		brothersCommunication.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersCommunicationPrompt.Format(
				MainRoleType.ThreeBrothers.GetPublicName()));
		brothersCommunication.AffectedPlayerIds.Should()
			.Equal(fixture.BrotherIds.Order());

		var brothersSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				fixture.Builder.Process(brothersCommunication.CreateResponse()));

		brothersSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		brothersSleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersGoToSleep.Format(
				MainRoleType.ThreeBrothers.GetPublicName()));
		brothersSleep.AffectedPlayerIds.Should().Equal(fixture.BrotherIds.Order());

		var nextSlot =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				fixture.Builder.Process(brothersSleep.CreateResponse()));

		nextSlot.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		nextSlot.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersWakeUp.Format(
				GameStrings.WerewolvesGroupName));
		nextSlot.AffectedPlayerIds.Should().Equal(fixture.WerewolfId);
		MarkTestCompleted();
	}

	private GameTestBuilder CreateThreeBrothersBuilder(
		IRolePowerAvailabilityPolicy policy) =>
		CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(
				"Brother A",
				"Brother B",
				"Brother C",
				"Werewolf",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.ThreeBrothers,
				MainRoleType.ThreeBrothers,
				MainRoleType.ThreeBrothers,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);

	private ThreeBrothersFixture StartThreeBrothersGame(
		IRolePowerAvailabilityPolicy policy)
	{
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(
				"Brother A",
				"Brother B",
				"Brother C",
				"Werewolf",
				"Villager A",
				"Villager B",
				"Villager C",
				"Villager D",
				"Villager E")
			.WithRoles(
				MainRoleType.ThreeBrothers,
				MainRoleType.ThreeBrothers,
				MainRoleType.ThreeBrothers,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var fixture = new ThreeBrothersFixture(
			builder,
			players.Take(3).Select(player => player.Id).ToHashSet(),
			players[3].Id,
			[]);
		builder.ConfirmGameStart();
		return fixture;
	}

	private ThreeBrothersFixture StartSistersAndThreeBrothersGame(
		IRolePowerAvailabilityPolicy policy)
	{
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(
				"Sister A",
				"Sister B",
				"Brother A",
				"Brother B",
				"Brother C",
				"Werewolf",
				"Villager A",
				"Villager B",
				"Villager C")
			.WithRoles(
				MainRoleType.TwoSisters,
				MainRoleType.TwoSisters,
				MainRoleType.ThreeBrothers,
				MainRoleType.ThreeBrothers,
				MainRoleType.ThreeBrothers,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var fixture = new ThreeBrothersFixture(
			builder,
			players.Skip(2).Take(3).Select(player => player.Id).ToHashSet(),
			players[5].Id,
			players.Take(2).Select(player => player.Id).ToHashSet());
		builder.ConfirmGameStart();
		return fixture;
	}

	private static ModeratorInstruction AdvanceUntil(
		ThreeBrothersFixture fixture,
		Func<IGameSession, ModeratorInstruction, bool> stop)
	{
		for (var step = 0; step < 200; step++)
		{
			var session = fixture.Builder.GetGameState()!;
			var instruction = fixture.Builder.GetCurrentInstruction()
				?? throw new InvalidOperationException(
					"The game has no pending instruction.");
			if (stop(session, instruction))
			{
				return instruction;
			}

			var response = instruction switch
			{
				ConfirmationInstruction confirmation =>
					confirmation.CreateResponse(),
				SelectPlayersInstruction
				{
					Semantic:
						ModeratorInstructionSemantic
							.ObserveWerewolfFactionAgentGroup
				} observation =>
					observation.CreateResponse([fixture.WerewolfId]),
				SelectPlayersInstruction
				{
					RoleIdentification: MainRoleType.TwoSisters
				} identification =>
					identification.CreateResponse(fixture.SisterIds),
				SelectPlayersInstruction
				{
					RoleIdentification: MainRoleType.ThreeBrothers
				} identification =>
					identification.CreateResponse(fixture.BrotherIds),
				SelectPlayersInstruction
				{
					RoleIdentification: MainRoleType.SimpleWerewolf
				} identification =>
					identification.CreateResponse([fixture.WerewolfId]),
				SelectPlayersInstruction
				{
					Semantic: ModeratorInstructionSemantic.SelectWerewolfVictim
				} victim => victim.CreateResponse(
					[
						session.GetPlayers()
							.First(player =>
								player.State.Health == PlayerHealth.Alive &&
								player.Id != fixture.WerewolfId &&
								!fixture.SisterIds.Contains(player.Id) &&
								!fixture.BrotherIds.Contains(player.Id))
							.Id
					]),
				SelectPlayersInstruction
				{
					Semantic: ModeratorInstructionSemantic.RecordDayVote
				} vote => vote.CreateResponse([]),
				AssignRolesInstruction assignment =>
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							_ => MainRoleType.SimpleVillager)),
				_ => throw new InvalidOperationException(
					$"Unexpected instruction {instruction.Semantic}.")
			};

			fixture.Builder.Process(response).IsSuccess.Should().BeTrue();
		}

		throw new InvalidOperationException(
			"The requested Three Brothers instruction was not reached.");
	}

	private static void AssertCorrelationFailureIsSideEffectFree(
		GameTestBuilder builder,
		ModeratorInstruction pendingInstruction,
		ModeratorResponse mismatchedResponse)
	{
		var session = builder.GetGameState()!;
		var serializedBefore = builder.SerializeSession();
		var logBefore = session.GameHistoryLog.ToArray();

		var act = () => builder.Process(mismatchedResponse);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*pending Moderator Instruction*");
		builder.GetCurrentInstruction()!.InstructionId.Should()
			.Be(pendingInstruction.InstructionId);
		session.GameHistoryLog.Should().Equal(logBefore);
		builder.SerializeSession().Should().Be(serializedBefore);
	}

	private sealed record ThreeBrothersFixture(
		GameTestBuilder Builder,
		HashSet<Guid> BrotherIds,
		Guid WerewolfId,
		HashSet<Guid> SisterIds);

	private sealed class RecordingPolicy : IRolePowerAvailabilityPolicy
	{
		private readonly IReadOnlyList<RolePowerAvailabilityResult> _results;

		public RecordingPolicy(params RolePowerAvailabilityResult[] results)
		{
			_results = results.Length == 0
				? [RolePowerAvailabilityResult.Allowed]
				: results;
		}

		public List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return _results[Math.Min(ObservedAttempts.Count - 1, _results.Count - 1)];
		}
	}
}
