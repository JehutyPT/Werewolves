using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class WhiteWerewolfRecoveryTests
{
	[Fact]
	public void Decline_SerializeRehydrateReplaysStableNightStartWithoutCommit()
	{
		var (builder, stableNightStart, targetSelection) =
			CreateNightTwoTargetSelection();
		builder.Process(
				targetSelection.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Which.Semantic.Should().Be(
				ModeratorInstructionSemantic.PutRoleToSleep);
		var freshService = new GameService();

		var recoveredGameId = freshService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredSession =
			freshService.GetGameStateView(recoveredGameId)!;
		var recoveredNightStart = freshService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredNightStart.Should().BeEquivalentTo(stableNightStart);
		recoveredSession.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType ==
				NightActionType.WhiteWerewolfVictimSelection);

		Action replayDecline = () => freshService.ProcessInstruction(
			recoveredGameId,
			targetSelection.CreateResponse([]));

		replayDecline.Should().Throw<InvalidOperationException>();
		freshService.GetCurrentInstruction(recoveredGameId)
			.Should().BeEquivalentTo(recoveredNightStart);
		recoveredSession.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType ==
				NightActionType.WhiteWerewolfVictimSelection);
	}

	[Fact]
	public void AcceptedIdentification_MissingWhiteBeneficiaryClosureFactIsRejected()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.BigBadWolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var whiteWerewolf = players[1];
		var werewolfAgentIds = new HashSet<Guid>
		{
			players[0].Id,
			whiteWerewolf.Id,
			players[2].Id
		};
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			[.. werewolfAgentIds]);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteWerewolfNightAction(
					werewolfAgentIds,
					players[4].Id));

		var nextInstruction =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(
					identification.CreateResponse([whiteWerewolf.Id])));

		nextInstruction.RoleIdentification.Should().Be(
			MainRoleType.BigBadWolf);
		var tampered = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.RemoveInitialBeneficiaryClosureFact(whiteWerewolf.Id)
			.Serialize();
		var freshService = new GameService();

		Action rehydrate = () => freshService.RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	private static (
			GameTestBuilder Builder,
			ConfirmationInstruction StableNightStart,
			SelectPlayersInstruction TargetSelection)
		CreateNightTwoTargetSelection()
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
		var stableNightStart = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		stableNightStart.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		builder.Process(stableNightStart.CreateResponse())
			.IsSuccess.Should().BeTrue();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id, whiteWerewolf.Id],
					players[5].Id));
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		return (builder, stableNightStart, targetSelection);
	}

}
