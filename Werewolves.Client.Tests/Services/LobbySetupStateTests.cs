using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class LobbySetupStateTests
{
	[Fact]
	public void AddPlayer_AppendsNamesInSeatingOrder()
	{
		var state = new LobbySetupState();

		state.AddPlayer("Ana").Should().Be(AddPlayerResult.Success);
		state.AddPlayer("Bruno").Should().Be(AddPlayerResult.Success);
		state.AddPlayer("Catarina").Should().Be(AddPlayerResult.Success);

		state.PlayerNames.Should().Equal("Ana", "Bruno", "Catarina");
	}

	[Fact]
	public void RemovePlayerAt_RemovesNameFromSeatingOrder()
	{
		var state = new LobbySetupState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");

		state.RemovePlayerAt(1).Should().BeTrue();

		state.PlayerNames.Should().Equal("Ana", "Catarina");
	}

	[Fact]
	public void MovePlayerUp_SwapsPlayerWithPreviousSeat()
	{
		var state = new LobbySetupState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");

		state.MovePlayerUp(2).Should().BeTrue();

		state.PlayerNames.Should().Equal("Ana", "Catarina", "Bruno");
	}

	[Fact]
	public void MovePlayerDown_SwapsPlayerWithNextSeat()
	{
		var state = new LobbySetupState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");

		state.MovePlayerDown(0).Should().BeTrue();

		state.PlayerNames.Should().Equal("Bruno", "Ana", "Catarina");
	}

	[Fact]
	public void CanMovePlayer_ReportsDisabledAtSeatingOrderBoundaries()
	{
		var state = new LobbySetupState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");

		state.CanMovePlayerUp(0).Should().BeFalse();
		state.CanMovePlayerDown(2).Should().BeFalse();
		state.CanMovePlayerUp(1).Should().BeTrue();
		state.CanMovePlayerDown(1).Should().BeTrue();
	}

	[Fact]
	public void HasPlayerConfigIssues_ReturnsTooFewPlayers_WhenUnderMinimum()
	{
		var state = new LobbySetupState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");

		state.HasPlayerConfigIssues(out var issues).Should().BeTrue();
		issues.Should().ContainSingle(e => e.Type == GameConfigValidationErrorType.TooFewPlayers);
	}

	[Fact]
	public void AddPlayer_RejectsDuplicateNameCaseInsensitive()
	{
		var state = new LobbySetupState();
		state.AddPlayer("Ana").Should().Be(AddPlayerResult.Success);

		state.AddPlayer("ana").Should().Be(AddPlayerResult.DuplicateName);
		state.AddPlayer("ANA").Should().Be(AddPlayerResult.DuplicateName);

		state.PlayerNames.Should().Equal("Ana");
	}

	[Fact]
	public void HasPlayerConfigIssues_ReturnsNoIssues_WhenRosterIsValid()
	{
		var state = new LobbySetupState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");
		state.AddPlayer("Diana");
		state.AddPlayer("Eduardo");

		state.HasPlayerConfigIssues(out var issues).Should().BeFalse();
		issues.Should().BeEmpty();
	}
}
