using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class AcceptedObservationRecoveryShapeTests : DiagnosticTestBase
{
	public AcceptedObservationRecoveryShapeTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void RehydrateSession_TargetSemanticWithConfirmationInstruction_RejectsTamperedAcceptedIdentification()
	{
		var builder = CreateBuilder()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var werewolf = builder.GetGameState()!.GetPlayers().First();
		var identification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		builder.Process(identification.CreateResponse([werewolf.Id]));
		var payload = RecoveryPayloadTestDriver
			.Parse(builder.SerializeSession())
			.ReplacePendingInstructionWithConfirmation()
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(payload);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*Pending Instruction*");
		MarkTestCompleted();
	}

	[Fact]
	public void RehydrateSession_SleepSemanticWithPlayerSelectionInstruction_RejectsTamperedAcceptedIdentification()
	{
		var builder = CreateBuilder()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var players = builder.GetGameState()!.GetPlayers().ToList();
		builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);
		var seerIdentification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		builder.Process(seerIdentification.CreateResponse([players[1].Id]));
		var payload = RecoveryPayloadTestDriver
			.Parse(builder.SerializeSession())
			.RewritePendingInstructionSemanticCheckpoint(
				ModeratorInstructionSemantic.PutRoleToSleep)
			.RewriteAcceptedObservationCursorNextSemantic(
				ModeratorInstructionSemantic.PutRoleToSleep)
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(payload);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*Pending Instruction*");
		MarkTestCompleted();
	}
}
