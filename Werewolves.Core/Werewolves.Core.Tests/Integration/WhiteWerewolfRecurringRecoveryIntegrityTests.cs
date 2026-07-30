using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class WhiteWerewolfRecurringRecoveryIntegrityTests
{
	[Fact]
	public void CommittedAttack_CorrelatedNonSleepContinuationIsRejected()
	{
		var tampered = RecoveryPayloadTestDriver
			.Parse(CreateCommittedAttack())
			.RewriteRecurringNextSemantic(
				ModeratorInstructionSemantic.WakeRole)
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void CommittedAttack_CoherentlyMovedToOddNightIsRejected()
	{
		var tampered = RecoveryPayloadTestDriver
			.Parse(CreateCommittedAttack())
			.RewriteSessionTurnNumber(3)
			.RewriteRecurringTurnNumber(3)
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	private static string CreateCommittedAttack()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var whiteWerewolf = players[1];
		builder.ArrangeKnownRole(
			whiteWerewolf.Id,
			MainRoleType.WhiteWerewolf);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			players[0].Id,
			whiteWerewolf.Id);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [players[0].Id, whiteWerewolf.Id],
			WerewolfVictimId = players[4].Id
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[players[4].Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();
		builder.ConfirmNightStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id, whiteWerewolf.Id],
					players[5].Id));
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					targetSelection.CreateResponse([players[0].Id])));
		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		return builder.GetGameState()!.Serialize();
	}
}
