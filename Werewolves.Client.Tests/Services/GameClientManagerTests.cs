using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class GameClientManagerTests
{
	[Fact]
	public void StartGame_FromValidLobby_CreatesActiveSession()
	{
		var lobby = CreateValidLobby();
		var manager = new GameClientManager(new GameService());
		var stateChangedCount = 0;
		manager.StateChanged += () => stateChangedCount++;

		var instruction = manager.StartGame(lobby);

		instruction.GameGuid.Should().NotBeEmpty();
		manager.ActiveGameId.Should().Be(instruction.GameGuid);
		manager.ActiveSession.Should().NotBeNull();
		manager.ActiveSession!.GetPlayers().Select(p => p.Name)
			.Should().Equal("Ana", "Bruno", "Catarina", "Diana", "Eduardo");
		manager.CurrentInstruction.Should().BeOfType<StartGameConfirmationInstruction>();
		stateChangedCount.Should().Be(1);
	}

	[Fact]
	public void ProcessModeratorResponse_AdvancesCurrentInstructionAndNotifies()
	{
		var lobby = CreateValidLobby();
		var manager = new GameClientManager(new GameService());
		var startInstruction = manager.StartGame(lobby);
		var stateChangedCount = 0;
		manager.StateChanged += () => stateChangedCount++;

		var result = manager.ProcessModeratorResponse(startInstruction.CreateResponse(true));

		result.IsSuccess.Should().BeTrue();
		manager.ActiveSession.Should().NotBeNull();
		manager.ActiveSession!.GetCurrentPhase().Should().Be(GamePhase.Night);
		manager.ActiveSession.TurnNumber.Should().Be(1);
		manager.CurrentInstruction.Should().BeOfType<ConfirmationInstruction>();
		manager.CurrentInstruction.Should().NotBeOfType<StartGameConfirmationInstruction>();
		stateChangedCount.Should().Be(1);
	}

	private static LobbySetupState CreateValidLobby()
	{
		var lobby = new LobbySetupState();
		foreach (var playerName in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			lobby.AddPlayer(playerName);
		}

		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(MainRoleType.Seer);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.SimpleVillager);

		return lobby;
	}
}
