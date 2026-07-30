using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class GameResultSelectionTests
{
	[Fact]
	public void Select_WithMultipleSatisfiedFactions_IsOrderIndependentSharedVictory()
	{
		var forward = GameResultSelection.Select(
			[Faction.Villager, Faction.Werewolf],
			allPlayersEliminated: false);
		var reverse = GameResultSelection.Select(
			[Faction.Werewolf, Faction.Villager],
			allPlayersEliminated: false);

		forward.Should().Be(new SharedVictoryGameResult(
			[Faction.Villager, Faction.Werewolf]));
		reverse.Should().Be(forward);
	}

	[Fact]
	public void Select_WithNoSatisfiedFaction_DistinguishesNoWinnerFromNonterminal()
	{
		GameResultSelection.Select([], allPlayersEliminated: true)
			.Should().Be(new NoWinnerGameResult());
		GameResultSelection.Select([], allPlayersEliminated: false)
			.Should().BeNull();
	}

	[Fact]
	public void Select_WithPiperAndAnotherSatisfiedFaction_UsesSharedResultModel()
	{
		var forward = GameResultSelection.Select(
			[Faction.Piper, Faction.Villager],
			allPlayersEliminated: false);
		var reverse = GameResultSelection.Select(
			[Faction.Villager, Faction.Piper],
			allPlayersEliminated: false);

		forward.Should().Be(new SharedVictoryGameResult(
			[Faction.Piper, Faction.Villager]));
		reverse.Should().Be(forward);
	}
}
