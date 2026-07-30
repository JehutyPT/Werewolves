using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class DefenderRoleAvailabilityTests : DiagnosticTestBase
{
	public DefenderRoleAvailabilityTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void AvailabilityDenied_EvaluatesOnceAndSleepsWithoutCommittingProtection()
	{
		var policy = new SequenceAvailabilityPolicy(false);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var defender = builder.GetGameState()!.GetPlayers().First();
		builder.ArrangeKnownRole(defender.Id, MainRoleType.Defender);
		builder.ConfirmGameStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(wake.CreateResponse()));

		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().NotBeNullOrWhiteSpace();
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(defender.Id);
		policy.Attempts.Should().ContainSingle();
		policy.Attempts.Single().OneUseResource.Should().BeNull();
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void KnownEmptyHolder_OmitsEntireCallWithoutAvailabilityEvaluation()
	{
		var policy = new SequenceAvailabilityPolicy();
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var defender = players[0];
		builder
			.ArrangeKnownRole(defender.Id, MainRoleType.Defender)
			.ArrangeEliminatedPlayer(defender.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[1].Id],
					players[4].Id));

		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		policy.Attempts.Should().BeEmpty();
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void NoEligibleTarget_SleepsWithoutCommittingAnotherProtection()
	{
		var policy = new SequenceAvailabilityPolicy(true, true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var defender = players[0];
		var werewolf = players[1];
		builder.ArrangeKnownRole(defender.Id, MainRoleType.Defender);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		var firstSleep = SelectDefenderTarget(builder, defender.Id);
		FinishNightAndDay(builder, firstSleep, players[2].Id);
		foreach (var player in players.Skip(1))
		{
			builder.ArrangeCurrentRole(player.Id, MainRoleType.LittleGirl);
		}
		var secondWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(secondWake.CreateResponse()));

		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(defender.Id);
		policy.Attempts.Should().HaveCount(2);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void ExcludedTargetResponse_IsRejectedWithoutMutationAndAcceptedResponseCannotReplay()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var defender = players[0];
		var littleGirl = players[1];
		var target = players[3];
		builder.ArrangeKnownRole(defender.Id, MainRoleType.Defender);
		builder.ArrangeKnownRole(littleGirl.Id, MainRoleType.LittleGirl);
		builder.ConfirmGameStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var beforeInvalid = builder.GetGameState()!.Serialize();

		Action selectLittleGirl = () =>
			builder.Process(targetSelection.CreateResponse([littleGirl.Id]));

		selectLittleGirl.Should().Throw<ArgumentException>();
		builder.GetGameState()!.Serialize().Should().Be(beforeInvalid);

		var acceptedResponse = targetSelection.CreateResponse([target.Id]);
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(acceptedResponse));

		Action replayAcceptedResponse = () => builder.Process(acceptedResponse);

		replayAcceptedResponse.Should().Throw<InvalidOperationException>();
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		MarkTestCompleted();
	}

	private static ConfirmationInstruction SelectDefenderTarget(
		GameTestBuilder builder,
		Guid targetId)
	{
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		return InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(selection.CreateResponse([targetId])));
	}

	private static void FinishNightAndDay(
		GameTestBuilder builder,
		ConfirmationInstruction defenderSleep,
		Guid victimId)
	{
		var werewolfWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(defenderSleep.CreateResponse()));
		werewolfWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightActionSubsequentNight(victimId));
		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(
			new Dictionary<Guid, MainRoleType>
			{
				[victimId] = MainRoleType.SimpleVillager
			}).IsSuccess.Should().BeTrue();
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();
	}

	private sealed class SequenceAvailabilityPolicy(params bool[] decisions)
		: IRolePowerAvailabilityPolicy
	{
		private int _nextDecision;

		internal List<RolePowerAttempt> Attempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			Attempts.Add(attempt);
			if (_nextDecision >= decisions.Length)
			{
				throw new InvalidOperationException(
					"The availability policy was evaluated more often than expected.");
			}

			return decisions[_nextDecision++]
				? RolePowerAvailabilityResult.Allowed
				: RolePowerAvailabilityResult.Denied;
		}
	}
}
