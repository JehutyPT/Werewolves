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

public sealed class TwoSistersNightRoleTests : DiagnosticTestBase
{
	public TwoSistersNightRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void FirstNight_UnknownPair_IdentifiesBothThenRecognizesAndSleepsAsRoleHolders()
	{
		var policy = new RecordingPolicy();
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers("Sister A", "Sister B", "Werewolf", "Villager A", "Villager B")
			.WithRoles(
				MainRoleType.TwoSisters,
				MainRoleType.TwoSisters,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();

		var afterNightStart = builder.ConfirmNightStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(afterNightStart);
		var sisters = builder.GetGameState()!.GetPlayers().Take(2).ToArray();

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.TwoSisters);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Exact(2));
		identification.AffectedPlayerIds.Should().BeNull();
		identification.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersWakeUp.Format(
				MainRoleType.TwoSisters.GetPublicName()));
		identification.PublicAnnouncement.Should().NotContain(sisters[0].Name);
		identification.PublicAnnouncement.Should().NotContain(sisters[1].Name);

		var afterIdentification = builder.Process(
			identification.CreateResponse(sisters.Select(player => player.Id).ToHashSet()));
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterIdentification);

		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeRoleHolders);
		recognition.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersRecognitionPrompt.Format(
				MainRoleType.TwoSisters.GetPublicName()));
		recognition.PrivateInstruction.Should().BeNull();
		recognition.AffectedPlayerIds.Should().Equal(
			sisters.Select(player => player.Id).Order());
		policy.ObservedAttempts.Select(attempt => attempt.ActingPlayer.Id).Should()
			.Equal(sisters.Select(player => player.Id).Order());
		policy.ObservedAttempts.Should().OnlyContain(attempt =>
			attempt.SourceRole == MainRoleType.TwoSisters &&
			attempt.SourcePower.Category == RolePowerCategory.Recognition &&
			attempt.PowerInstance.Origin == RolePowerInstanceOrigin.Native);

		var afterRecognition = builder.Process(recognition.CreateResponse());
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			afterRecognition);

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersGoToSleep.Format(
				MainRoleType.TwoSisters.GetPublicName()));
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(
			sisters.Select(player => player.Id).Order());
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_PartiallyKnownPair_RejectsIdentificationThatOmitsCommittedHolderWithoutStateAdvance()
	{
		var builder = CreateTwoSistersBuilder(new RecordingPolicy());
		builder.StartGame();
		var session = (GameSession)builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var committedSister = players[0];
		var otherSister = players[1];
		session.AssignRole(committedSister.Id, MainRoleType.TwoSisters);
		session.IdentifyRole([committedSister.Id], MainRoleType.TwoSisters);
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var stateBeforeInvalidResponse = session.Serialize();
		var logBeforeInvalidResponse = session.GameHistoryLog.ToArray();
		var invalidSelection = players
			.Skip(1)
			.Take(2)
			.Select(player => player.Id)
			.ToHashSet();

		var invalidAct = () => builder.Process(
			identification.CreateResponse(invalidSelection));

		invalidAct.Should().Throw<InvalidOperationException>()
			.WithMessage("*cannot replace a committed Living Role Holder*");
		builder.GetCurrentInstruction()!.InstructionId.Should()
			.Be(identification.InstructionId);
		session.GameHistoryLog.Should().Equal(logBeforeInvalidResponse);
		session.Serialize().Should().Be(stateBeforeInvalidResponse);

		var completeSelection = new HashSet<Guid>
		{
			committedSister.Id,
			otherSister.Id
		};
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(identification.CreateResponse(completeSelection)));

		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeRoleHolders);
		recognition.AffectedPlayerIds.Should().Equal(completeSelection.Order());
		session.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Last().PlayerIds.Should().BeEquivalentTo(completeSelection);
		MarkTestCompleted();
	}

	[Fact]
	public void Recognition_StaleConfirmationFromPriorStep_IsRejectedWithoutStateAdvance()
	{
		var builder = CreateTwoSistersBuilder(new RecordingPolicy());
		builder.StartGame();
		builder.ConfirmGameStart();
		var nightStart =
			InstructionAssert.ExpectType<ConfirmationInstruction>(
				builder.GetCurrentInstruction());
		var staleNightStartResponse = nightStart.CreateResponse();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(staleNightStartResponse));
		var sisterIds = builder.GetGameState()!.GetPlayers()
			.Take(2)
			.Select(player => player.Id)
			.ToHashSet();
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(identification.CreateResponse(sisterIds)));

		AssertCorrelationFailureIsSideEffectFree(
			builder,
			recognition,
			staleNightStartResponse);

		InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(recognition.CreateResponse()))
			.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedIdentification_WithDeniedRecognition_RehydrateRestoresPinnedSleepWithoutReevaluating()
	{
		var originalPolicy = new RecordingPolicy(
			RolePowerAvailabilityResult.Denied,
			RolePowerAvailabilityResult.Allowed);
		var builder = CreateTwoSistersBuilder(originalPolicy);
		builder.StartGame();
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var sisterIds = builder.GetGameState()!.GetPlayers()
			.Take(2)
			.Select(player => player.Id)
			.ToHashSet();
		var expectedSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(identification.CreateResponse(sisterIds)));
		var rehydratedPolicy = new RecordingPolicy();
		var service = new GameService(rehydratedPolicy);

		var gameId = service.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredSleep =
			InstructionAssert.ExpectType<ConfirmationInstruction>(
				service.GetCurrentInstruction(gameId));

		recoveredSleep.InstructionId.Should().Be(expectedSleep.InstructionId);
		recoveredSleep.Semantic.Should()
			.Be(ModeratorInstructionSemantic.PutRoleToSleep);
		recoveredSleep.AffectedPlayerIds.Should().Equal(sisterIds.Order());
		rehydratedPolicy.ObservedAttempts.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedIdentification_RehydrateRestoresPinnedRecognitionWithoutReidentifyingOrReevaluating()
	{
		var originalPolicy = new RecordingPolicy();
		var builder = CreateTwoSistersBuilder(originalPolicy);
		builder.StartGame();
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var sisterIds = builder.GetGameState()!.GetPlayers()
			.Take(2)
			.Select(player => player.Id)
			.ToHashSet();
		var expectedRecognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(identification.CreateResponse(sisterIds)));
		var rehydratedPolicy = new RecordingPolicy(
			RolePowerAvailabilityResult.Denied);
		var service = new GameService(rehydratedPolicy);

		var gameId = service.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredRecognition =
			InstructionAssert.ExpectType<ConfirmationInstruction>(
				service.GetCurrentInstruction(gameId));

		recoveredRecognition.InstructionId.Should()
			.Be(expectedRecognition.InstructionId);
		recoveredRecognition.Semantic.Should()
			.Be(ModeratorInstructionSemantic.RecognizeRoleHolders);
		recoveredRecognition.PublicAnnouncement.Should()
			.Be(expectedRecognition.PublicAnnouncement);
		recoveredRecognition.PrivateInstruction.Should()
			.Be(expectedRecognition.PrivateInstruction);
		recoveredRecognition.AffectedPlayerIds.Should()
			.Equal(expectedRecognition.AffectedPlayerIds);
		rehydratedPolicy.ObservedAttempts.Should().BeEmpty();

		var afterRecognition = service.ProcessInstruction(
			gameId,
			recoveredRecognition.CreateResponse());
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			afterRecognition);

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		rehydratedPolicy.ObservedAttempts.Should().BeEmpty();
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Count(entry => entry.Role == MainRoleType.TwoSisters)
			.Should().Be(1);
		MarkTestCompleted();
	}

	[Fact]
	public void OddNightFromThree_LivingPair_CommunicatesThenSleepsWithoutASeparateTimer()
	{
		var policy = new RecordingPolicy();
		var fixture = StartTwoSistersGame(policy);

		var communication = AdvanceUntil(
			fixture,
			(session, instruction) =>
				session.TurnNumber == 3 &&
				instruction.Semantic ==
				ModeratorInstructionSemantic.CommunicateAsRoleHolders)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		communication.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersCommunicationPrompt.Format(
				MainRoleType.TwoSisters.GetPublicName()));
		communication.PrivateInstruction.Should().BeNull();
		communication.AffectedPlayerIds.Should().Equal(fixture.SisterIds.Order());
		policy.ObservedAttempts.Should().HaveCount(4);
		policy.ObservedAttempts.Take(2).Should().OnlyContain(attempt =>
			attempt.SourcePower.Category == RolePowerCategory.Recognition);
		policy.ObservedAttempts.Skip(2).Should().OnlyContain(attempt =>
			attempt.SourcePower.Category == RolePowerCategory.Communication);

		var afterCommunication = fixture.Builder.Process(
			communication.CreateResponse());
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			afterCommunication);

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersGoToSleep.Format(
				MainRoleType.TwoSisters.GetPublicName()));
		sleep.AffectedPlayerIds.Should().Equal(fixture.SisterIds.Order());
		MarkTestCompleted();
	}

	[Fact]
	public void Communication_ReplayedResponseAtSleep_IsRejectedWithoutStateAdvance()
	{
		var fixture = StartTwoSistersGame(new RecordingPolicy());
		var communication = AdvanceUntil(
			fixture,
			(session, instruction) =>
				session.TurnNumber == 3 &&
				instruction.Semantic ==
					ModeratorInstructionSemantic.CommunicateAsRoleHolders)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var completedCommunicationResponse = communication.CreateResponse();
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				fixture.Builder.Process(completedCommunicationResponse));

		AssertCorrelationFailureIsSideEffectFree(
			fixture.Builder,
			sleep,
			completedCommunicationResponse);

		fixture.Builder.Process(sleep.CreateResponse()).IsSuccess.Should().BeTrue();
		MarkTestCompleted();
	}

	[Fact]
	public void OddNightFromThree_WhenOneCommunicationIsDenied_EvaluatesBothAndKeepsTheWholeIntervalSilent()
	{
		var policy = new RecordingPolicy(
			RolePowerAvailabilityResult.Allowed,
			RolePowerAvailabilityResult.Allowed,
			RolePowerAvailabilityResult.Denied,
			RolePowerAvailabilityResult.Allowed);
		var fixture = StartTwoSistersGame(policy);

		var nextRole = AdvanceUntil(
			fixture,
			(session, instruction) =>
				session.TurnNumber == 3 &&
				instruction.Semantic == ModeratorInstructionSemantic.WakeRole);

		nextRole.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersWakeUp.Format(
				GameStrings.WerewolvesGroupName));
		nextRole.AffectedPlayerIds.Should().Equal(fixture.WerewolfId);
		policy.ObservedAttempts.Should().HaveCount(4);
		policy.ObservedAttempts.Skip(2)
			.Select(attempt => attempt.ActingPlayer.Id).Should()
			.Equal(fixture.SisterIds.Order());
		policy.ObservedAttempts.Skip(2).Should().OnlyContain(attempt =>
			attempt.SourcePower.Category == RolePowerCategory.Communication);
		MarkTestCompleted();
	}

	[Fact]
	public void OddNightFromThree_RecalculatesCurrentLivingHoldersAndSkipsWhenFewerThanTwoRemain()
	{
		var policy = new RecordingPolicy();
		var fixture = StartTwoSistersGame(policy);
		var nightThreeStart = AdvanceUntil(
			fixture,
			(session, instruction) =>
				session.TurnNumber == 3 &&
				instruction.Semantic == ModeratorInstructionSemantic.StartNight)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var session = (GameSession)fixture.Builder.GetGameState()!;
		session.AssignRole(
			fixture.SisterIds.Order().First(),
			MainRoleType.SimpleVillager);

		var afterNightStart = fixture.Builder.Process(
			nightThreeStart.CreateResponse());
		var nextRole =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterNightStart);

		nextRole.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		nextRole.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersWakeUp.Format(
				GameStrings.WerewolvesGroupName));
		nextRole.AffectedPlayerIds.Should().Equal(fixture.WerewolfId);
		policy.ObservedAttempts.Should().HaveCount(2);
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_KnownPair_WhenRecognitionIsDenied_EmitsNothingForTheRoleHolders()
	{
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Denied);
		var builder = CreateTwoSistersBuilder(policy);
		builder.StartGame();
		var session = (GameSession)builder.GetGameState()!;
		var sisters = session.GetPlayers().Take(2).ToArray();
		var sisterIds = sisters.Select(player => player.Id).ToHashSet();
		session.AssignRole(sisterIds, MainRoleType.TwoSisters);
		session.IdentifyRole(sisterIds, MainRoleType.TwoSisters);
		builder.ConfirmGameStart();

		var afterNightStart = builder.ConfirmNightStart();
		var nextSlot =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				afterNightStart);

		nextSlot.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		nextSlot.RoleIdentification.Should().BeNull();
		policy.ObservedAttempts.Select(attempt => attempt.ActingPlayer.Id).Should()
			.Equal(sisters.Select(player => player.Id).Order());
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_KnownPair_WhenRecognitionIsAllowed_WakesAndRecognizesWithoutSeparateWakeStep()
	{
		var policy = new RecordingPolicy();
		var builder = CreateTwoSistersBuilder(policy);
		builder.StartGame();
		var session = (GameSession)builder.GetGameState()!;
		var sisters = session.GetPlayers().Take(2).ToArray();
		var sisterIds = sisters.Select(player => player.Id).ToHashSet();
		session.AssignRole(sisterIds, MainRoleType.TwoSisters);
		session.IdentifyRole(sisterIds, MainRoleType.TwoSisters);
		builder.ConfirmGameStart();

		var afterNightStart = builder.ConfirmNightStart();
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterNightStart);

		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeRoleHolders);
		recognition.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersRecognitionPrompt.Format(
				GameStrings.TwoSistersRoleName));
		recognition.AffectedPlayerIds.Should().Equal(
			sisters.Select(player => player.Id).Order());
		policy.ObservedAttempts.Should().HaveCount(2);
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_UnknownPair_WhenOneRecognitionIsDenied_EvaluatesBothThenSleeps()
	{
		var policy = new RecordingPolicy(
			RolePowerAvailabilityResult.Denied,
			RolePowerAvailabilityResult.Allowed);
		var builder = CreateTwoSistersBuilder(policy);
		builder.StartGame();
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var sisters = builder.GetGameState()!.GetPlayers().Take(2).ToArray();

		var afterIdentification = builder.Process(
			identification.CreateResponse(sisters.Select(player => player.Id).ToHashSet()));
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			afterIdentification);

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(
			sisters.Select(player => player.Id).Order());
		policy.ObservedAttempts.Select(attempt => attempt.ActingPlayer.Id).Should()
			.Equal(sisters.Select(player => player.Id).Order());
		MarkTestCompleted();
	}

	private GameTestBuilder CreateTwoSistersBuilder(IRolePowerAvailabilityPolicy policy) =>
		CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers("Sister A", "Sister B", "Werewolf", "Villager A", "Villager B")
			.WithRoles(
				MainRoleType.TwoSisters,
				MainRoleType.TwoSisters,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);

	private TwoSistersFixture StartTwoSistersGame(
		IRolePowerAvailabilityPolicy policy)
	{
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(
				"Sister A",
				"Sister B",
				"Werewolf",
				"Villager A",
				"Villager B",
				"Villager C",
				"Villager D",
				"Villager E")
			.WithRoles(
				MainRoleType.TwoSisters,
				MainRoleType.TwoSisters,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var fixture = new TwoSistersFixture(
			builder,
			players.Take(2).Select(player => player.Id).ToHashSet(),
			players[2].Id);
		builder.ConfirmGameStart();
		return fixture;
	}

	private static ModeratorInstruction AdvanceUntil(
		TwoSistersFixture fixture,
		Func<IGameSession, ModeratorInstruction, bool> stop)
	{
		for (var step = 0; step < 200; step++)
		{
			var session = fixture.Builder.GetGameState()!;
			var instruction = fixture.Builder.GetCurrentInstruction()
				?? throw new InvalidOperationException("The game has no pending instruction.");
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
					RoleIdentification: MainRoleType.SimpleWerewolf
				} identification =>
					identification.CreateResponse([fixture.WerewolfId]),
				SelectPlayersInstruction
				{
					Semantic: ModeratorInstructionSemantic.SelectWerewolfVictim
				} victim =>
					victim.CreateResponse(
					[
						session.GetPlayers()
							.First(player =>
								player.State.Health == PlayerHealth.Alive &&
								player.Id != fixture.WerewolfId &&
								!fixture.SisterIds.Contains(player.Id))
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
			"The requested Two Sisters instruction was not reached.");
	}

	private static void AssertCorrelationFailureIsSideEffectFree(
		GameTestBuilder builder,
		ModeratorInstruction pendingInstruction,
		ModeratorResponse mismatchedResponse)
	{
		var session = builder.GetGameState()!;
		var serializedBefore = session.Serialize();
		var logBefore = session.GameHistoryLog.ToArray();

		var act = () => builder.Process(mismatchedResponse);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*pending Moderator Instruction*");
		builder.GetCurrentInstruction()!.InstructionId.Should()
			.Be(pendingInstruction.InstructionId);
		session.GameHistoryLog.Should().Equal(logBefore);
		session.Serialize().Should().Be(serializedBefore);
	}

	private sealed record TwoSistersFixture(
		GameTestBuilder Builder,
		HashSet<Guid> SisterIds,
		Guid WerewolfId);

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
