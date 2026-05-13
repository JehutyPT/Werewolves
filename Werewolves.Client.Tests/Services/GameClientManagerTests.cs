using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class GameClientManagerTests
{
	[Fact]
	public void StartGame_FromLobbyConfiguration_CreatesCoreSessionAndExposesInstruction()
	{
		var manager = new GameClientManager();
		var players = new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" };
		var roles = new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};

		var instruction = manager.StartGame(players, roles);

		instruction.Should().BeOfType<StartGameConfirmationInstruction>();
		manager.HasActiveSession.Should().BeTrue();
		manager.ActiveGameId.Should().Be(instruction.GameGuid);
		manager.CurrentInstruction.Should().Be(instruction);
		manager.CurrentSession.Should().NotBeNull();
		manager.CurrentSession!.GetPlayers().Select(p => p.Name).Should().Equal(players);
		manager.CurrentSession.RoleInPlayCount(MainRoleType.SimpleWerewolf).Should().Be(1);
		manager.CurrentSession.RoleInPlayCount(MainRoleType.Seer).Should().Be(1);
		manager.CurrentSession.RoleInPlayCount(MainRoleType.SimpleVillager).Should().Be(3);
	}

	[Fact]
	public void ProcessInput_ForCurrentInstruction_AdvancesCurrentInstruction()
	{
		var manager = new GameClientManager();
		var startInstruction = StartSimpleGame(manager);

		var result = manager.ProcessInput(startInstruction.CreateResponse(true));

		result.IsSuccess.Should().BeTrue();
		result.ModeratorInstruction.Should().NotBeNull();
		manager.CurrentInstruction.Should().Be(result.ModeratorInstruction);
		manager.CurrentInstruction.Should().NotBe(startInstruction);
		manager.CurrentPhase.Should().Be(GamePhase.Night);
		manager.TurnNumber.Should().Be(1);
	}

	[Fact]
	public void StartGame_RaisesStateChangedOnceAfterSessionCreation()
	{
		var manager = new GameClientManager();
		var eventCount = 0;
		manager.StateChanged += (_, _) => eventCount++;

		StartSimpleGame(manager);

		eventCount.Should().Be(1);
	}

	[Fact]
	public void ProcessInput_RaisesStateChangedAfterSuccessfulProcessing()
	{
		var manager = new GameClientManager();
		var startInstruction = StartSimpleGame(manager);
		var eventCount = 0;
		manager.StateChanged += (_, _) => eventCount++;

		manager.ProcessInput(startInstruction.CreateResponse(true));

		eventCount.Should().Be(1);
	}

	[Fact]
	public void ProcessInput_WithoutActiveSession_ThrowsInvalidOperationException()
	{
		var manager = new GameClientManager();
		var response = StartSimpleGame(new GameClientManager()).CreateResponse(true);

		var act = () => manager.ProcessInput(response);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Cannot process moderator response without an active game session.");
	}

	private static StartGameConfirmationInstruction StartSimpleGame(GameClientManager manager)
	{
		var players = new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" };
		var roles = new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};

		return manager.StartGame(players, roles);
	}
}
