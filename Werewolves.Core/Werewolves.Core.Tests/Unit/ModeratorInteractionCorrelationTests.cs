using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class ModeratorInteractionCorrelationTests
{
	[Fact]
	public void ContinueResponse_CarriesInstructionIdentity()
	{
		var instruction = new ConfirmationInstruction(
			privateInstruction: nameof(ContinueResponse_CarriesInstructionIdentity));

		var response = instruction.CreateResponse();

		instruction.InstructionId.Should().NotBeEmpty();
		response.InstructionId.Should().Be(instruction.InstructionId);
		response.Type.Should().Be(ExpectedInputType.Continue);
	}

	[Fact]
	public void ProcessInstruction_SupersededSameShapeResponse_IsSideEffectFree()
	{
		var service = new GameService();
		var startInstruction = service.StartNewGame(CreateConfig());
		var staleResponse = startInstruction.CreateResponse();
		var gameId = startInstruction.GameGuid;
		service.ProcessInstruction(gameId, staleResponse);

		var pendingBefore = service.GetCurrentInstruction(gameId);
		var sessionBefore = service.GetGameStateView(gameId)!;
		var phaseBefore = sessionBefore.GetCurrentPhase();
		var logBefore = sessionBefore.GameHistoryLog.ToArray();
		var serializationBefore = service.SerializeSession(gameId);

		var act = () => service.ProcessInstruction(gameId, staleResponse);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*pending Moderator Instruction*");
		service.GetCurrentInstruction(gameId)!.InstructionId
			.Should().Be(pendingBefore!.InstructionId);
		service.GetGameStateView(gameId)!.GetCurrentPhase().Should().Be(phaseBefore);
		service.GetGameStateView(gameId)!.GameHistoryLog.Should().Equal(logBefore);
		service.SerializeSession(gameId).Should().Be(serializationBefore);
	}

	[Fact]
	public void ResponseFactories_AllInstructionKinds_CarryTheirInstructionIdentity()
	{
		var selectedPlayerId = Guid.NewGuid();
		var confirmation = new ConfirmationInstruction(
			privateInstruction: nameof(ResponseFactories_AllInstructionKinds_CarryTheirInstructionIdentity));
		var start = new StartGameConfirmationInstruction(Guid.NewGuid());
		var playerSelection = new SelectPlayersInstruction(
			[selectedPlayerId],
			NumberRangeConstraint.Single,
			privateInstruction: nameof(ResponseFactories_AllInstructionKinds_CarryTheirInstructionIdentity));
		var roleAssignment = new AssignRolesInstruction(
			[selectedPlayerId],
			[MainRoleType.SimpleVillager],
			privateInstruction: nameof(ResponseFactories_AllInstructionKinds_CarryTheirInstructionIdentity));
		var optionSelection = new SelectOptionsInstruction(
			[new ModeratorOption("semantic-id", "Rótulo")],
			NumberRangeConstraint.Single,
			privateInstruction: nameof(ResponseFactories_AllInstructionKinds_CarryTheirInstructionIdentity));

		var interactions = new (ModeratorInstruction Instruction, ModeratorResponse Response)[]
		{
			(confirmation, confirmation.CreateResponse()),
			(start, start.CreateResponse()),
			(playerSelection, playerSelection.CreateResponse([selectedPlayerId])),
			(roleAssignment, roleAssignment.CreateResponse(new Dictionary<Guid, MainRoleType>
			{
				[selectedPlayerId] = MainRoleType.SimpleVillager
			})),
			(optionSelection, optionSelection.CreateResponse("semantic-id"))
		};

		foreach (var (instruction, response) in interactions)
		{
			instruction.InstructionId.Should().NotBeEmpty();
			response.InstructionId.Should().Be(instruction.InstructionId);
		}
	}

	[Fact]
	public void ProcessInstruction_NullResponse_IsSideEffectFree()
	{
		var service = new GameService();
		var instruction = service.StartNewGame(CreateConfig());
		var gameId = instruction.GameGuid;
		var before = service.SerializeSession(gameId);

		var act = () => service.ProcessInstruction(gameId, null);

		act.Should().Throw<ArgumentNullException>();
		service.SerializeSession(gameId).Should().Be(before);
		service.GetCurrentInstruction(gameId)!.InstructionId
			.Should().Be(instruction.InstructionId);
	}

	[Fact]
	public void ProcessInstruction_StaleFinishedGameResponse_DoesNotRemoveSession()
	{
		var finishedInstruction = new FinishedGameConfirmationInstruction(
			new SingleFactionGameResult(Faction.Villager),
			VictoryCheckWindow.Dawn);
		var session = new GameSession(
			Guid.NewGuid(),
			finishedInstruction,
			CreateConfig());
		var service = new GameService();
		var gameId = service.RehydrateSession(session.SerializeRecoverySnapshot());
		var staleResponse = new ConfirmationInstruction(
			privateInstruction: nameof(ProcessInstruction_StaleFinishedGameResponse_DoesNotRemoveSession))
			.CreateResponse();
		var serializedBefore = service.SerializeSession(gameId);

		service.ProcessInstruction(gameId, staleResponse).IsSuccess.Should().BeFalse();
		service.GetGameStateView(gameId).Should().NotBeNull();
		service.GetCurrentInstruction(gameId)!.InstructionId
			.Should().Be(finishedInstruction.InstructionId);

		var malformedMatchingResponse = new ModeratorResponse
		{
			InstructionId = finishedInstruction.InstructionId,
			Type = ExpectedInputType.Continue,
			SelectedPlayerIds = new HashSet<Guid>()
		};
		var malformedAct = () => service.ProcessInstruction(gameId, malformedMatchingResponse);

		malformedAct().IsSuccess.Should().BeFalse();
		service.GetGameStateView(gameId).Should().NotBeNull();
		service.SerializeSession(gameId).Should().Be(serializedBefore);
	}

	private static GameSessionConfig CreateConfig() => new(
		["A", "B", "C", "D", "E"],
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		]);
}
