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

public sealed class DefenderRoleRestrictionTests : DiagnosticTestBase
{
	public DefenderRoleRestrictionTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void NightWithoutCommit_ResetsImmediatelyPreviousTargetRestriction()
	{
		var policy = new SequenceAvailabilityPolicy(true, false, true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(8)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
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

		var secondWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var deniedSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(secondWake.CreateResponse()));
		FinishNightAndDay(builder, deniedSleep, players[3].Id);

		var thirdWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var thirdTargetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(thirdWake.CreateResponse()));

		thirdTargetSelection.SelectablePlayerIds.Should()
			.Contain(defender.Id);
		policy.Attempts.Should().HaveCount(3);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void ChangedHolder_DoesNotInheritPreviousNativePowerTargetRestriction()
	{
		var builder = CreateBuilder()
			.WithPlayers(8)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var originalHolder = players[0];
		var werewolf = players[1];
		var protectedTarget = players[4];
		var replacementHolder = players[5];
		builder.ArrangeKnownRole(
			originalHolder.Id,
			MainRoleType.Defender);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();

		var firstSleep = SelectDefenderTarget(
			builder,
			protectedTarget.Id);
		FinishNightAndDay(builder, firstSleep, players[2].Id);
		builder.ArrangeCurrentRole(
			originalHolder.Id,
			MainRoleType.SimpleVillager);
		builder.ArrangeKnownRole(
			replacementHolder.Id,
			MainRoleType.Defender);
		builder.ArrangeCurrentRole(
			replacementHolder.Id,
			MainRoleType.Defender);

		var secondWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var secondTargetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(secondWake.CreateResponse()));

		secondWake.AffectedPlayerIds.Should().Equal(replacementHolder.Id);
		secondTargetSelection.SelectablePlayerIds.Should()
			.Contain(protectedTarget.Id);
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
