using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
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

public sealed class SeerRolePowerAvailabilityTests : DiagnosticTestBase
{
	public SeerRolePowerAvailabilityTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void ProcessInstruction_AllowedSeerPower_PreservesTargetFeedbackAndSingleDecision()
	{
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var players = builder.GetGameState()!.GetPlayers().ToList();
		var werewolf = players[0];
		var seer = players[1];
		builder.CompleteWerewolfNightAction([werewolf.Id], players[4].Id);
		var identifySeer = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			builder.GetCurrentInstruction());

		var afterIdentification = builder.Process(
			identifySeer.CreateResponse([seer.Id]));

		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				afterIdentification);
		targetSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectSeerTarget);
		targetSelection.SelectablePlayerIds.Should()
			.BeEquivalentTo(players.Where(player => player.Id != seer.Id)
				.Select(player => player.Id));
		policy.ObservedAttempts.Should().ContainSingle();
		var afterTarget = builder.Process(
			targetSelection.CreateResponse([werewolf.Id]));
		var feedback =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterTarget);
		feedback.Semantic.Should().Be(ModeratorInstructionSemantic.RevealSeerResult);
		feedback.PublicAnnouncement.Should().BeNull();
		feedback.PrivateInstruction.Should().Be(
			GameStrings.SeerResultWerewolfTeam.Format(werewolf.Name));
		policy.ObservedAttempts.Should().ContainSingle();
		builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Where(entry => entry.ActionType == NightActionType.SeerCheck)
			.Should().ContainSingle()
			.Which.TargetIds.Should().Equal(werewolf.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void ProcessInstruction_DeniedSeerPower_SleepsWithoutTargetFeedbackOrCheck()
	{
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Denied);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var players = builder.GetGameState()!.GetPlayers().ToList();
		var werewolf = players[0];
		var seer = players[1];
		var victim = players[4];
		builder.CompleteWerewolfNightAction([werewolf.Id], victim.Id);
		var identifySeer = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			builder.GetCurrentInstruction());

		var afterIdentification = builder.Process(
			identifySeer.CreateResponse([seer.Id]));

		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			afterIdentification);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().BeNull();
		policy.ObservedAttempts.Should().ContainSingle();
		var observed = policy.ObservedAttempts.Single();
		observed.ActingPlayer.Id.Should().Be(seer.Id);
		observed.SourceRole.Should().Be(MainRoleType.Seer);
		observed.SourcePower.Identifier.Should().Be(
			new RolePowerIdentifier("seer-werewolf-detection"));
		observed.PowerInstance.Origin.Should().Be(RolePowerInstanceOrigin.Native);
		observed.OneUseResource.Should().BeNull();
		builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().NotContain(entry => entry.ActionType == NightActionType.SeerCheck);
		MarkTestCompleted();
	}

	[Fact]
	public void RehydrateSession_DeniedSeerAfterIdentification_ResumesSleepWithoutRepeatingDecision()
	{
		var originalPolicy = new RecordingPolicy(RolePowerAvailabilityResult.Denied);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(originalPolicy)
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var players = builder.GetGameState()!.GetPlayers().ToList();
		builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);
		var identifySeer = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			builder.GetCurrentInstruction());
		var denied = builder.Process(
			identifySeer.CreateResponse([players[1].Id]));
		var originalSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(denied);
		var serialized = builder.GetGameState()!.Serialize();
		var rehydratedPolicy = new RecordingPolicy(RolePowerAvailabilityResult.Denied);
		var rehydratedService = new GameService(rehydratedPolicy);

		var rehydratedGameId = rehydratedService.RehydrateSession(serialized);

		var rehydratedSleep = InstructionAssert.ExpectType<ConfirmationInstruction>(
			rehydratedService.GetCurrentInstruction(rehydratedGameId));
		rehydratedSleep.InstructionId.Should().Be(originalSleep.InstructionId);
		rehydratedSleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		var afterSleep = rehydratedService.ProcessInstruction(
			rehydratedGameId,
			rehydratedSleep.CreateResponse());
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(afterSleep);
		finishNight.Semantic.Should().Be(ModeratorInstructionSemantic.FinishNightActions);
		rehydratedPolicy.ObservedAttempts.Should().BeEmpty();
		rehydratedService.GetGameStateView(rehydratedGameId)!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().NotContain(entry => entry.ActionType == NightActionType.SeerCheck);
		MarkTestCompleted();
	}

	[Fact]
	public void RehydrateSession_AllowedSeerAfterFeedback_ResumesTheSleepBoundaryWithoutRepeatingTheCheck()
	{
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(
				new RecordingPolicy(RolePowerAvailabilityResult.Allowed))
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var players = builder.GetGameState()!.GetPlayers().ToList();
		var werewolf = players[0];
		var seer = players[1];
		builder.CompleteWerewolfNightAction([werewolf.Id], players[4].Id);
		var identifySeer = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			builder.GetCurrentInstruction());
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(identifySeer.CreateResponse([seer.Id])));
		var feedback =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(targetSelection.CreateResponse([werewolf.Id])));
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(feedback.CreateResponse()));
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		var serialized = builder.GetGameState()!.Serialize();
		var rehydratedPolicy = new RecordingPolicy(
			RolePowerAvailabilityResult.Allowed);
		var rehydratedService = new GameService(rehydratedPolicy);

		var rehydratedGameId = rehydratedService.RehydrateSession(serialized);

		var rehydratedSleep = InstructionAssert.ExpectType<ConfirmationInstruction>(
			rehydratedService.GetCurrentInstruction(rehydratedGameId));
		rehydratedSleep.InstructionId.Should().Be(sleep.InstructionId);
		rehydratedSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		rehydratedSleep.AffectedPlayerIds.Should().BeNull();

		var afterSleep = InstructionAssert.ExpectSuccessWithType<ModeratorInstruction>(
			rehydratedService.ProcessInstruction(
				rehydratedGameId,
				rehydratedSleep.CreateResponse()));

		afterSleep.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.SelectSeerTarget);
		rehydratedPolicy.ObservedAttempts.Should().BeEmpty();
		rehydratedService.GetGameStateView(rehydratedGameId)!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Where(entry => entry.ActionType == NightActionType.SeerCheck)
			.Should().ContainSingle()
			.Which.TargetIds.Should().Equal(werewolf.Id);
		MarkTestCompleted();
	}

	private sealed class RecordingPolicy(RolePowerAvailabilityResult result)
		: IRolePowerAvailabilityPolicy
	{
		public List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return result;
		}
	}
}
