using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
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

	[Theory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public void Select_WithPrejudicedManipulatorAndSimultaneousEligibleFactions_UsesOneLivingSnapshot(
		bool angelVictoryEligible,
		bool piperVictoryEligible)
	{
		var prejudicedManipulatorId = Guid.NewGuid();
		var piperId = Guid.NewGuid();
		var eliminatedOpposingPlayerId = Guid.NewGuid();
		var livingSnapshot = new List<LivingFactionBeneficiarySnapshot>
		{
			new(
				Faction.PrejudicedManipulator,
				IsCharmed: piperVictoryEligible,
				DurableVotingPower: 1,
				PlayerId: prejudicedManipulatorId)
		};
		if (piperVictoryEligible)
		{
			livingSnapshot.Add(new LivingFactionBeneficiarySnapshot(
				Faction.Piper,
				IsCharmed: false,
				DurableVotingPower: 1,
				PlayerId: piperId));
		}

		var partition = PublicGroupPartition.Create(
			[prejudicedManipulatorId, piperId, eliminatedOpposingPlayerId],
			[prejudicedManipulatorId, piperId],
			[eliminatedOpposingPlayerId]);
		var satisfiedFactions = FactionVictoryPredicates
			.Evaluate(livingSnapshot, partition)
			.Concat(angelVictoryEligible ? [Faction.Angel] : []);

		GameResultSelection.Select(
				satisfiedFactions,
				allPlayersEliminated: false)
			.Should().Be(new SharedVictoryGameResult(
				new[]
				{
					piperVictoryEligible ? Faction.Piper : (Faction?)null,
					angelVictoryEligible ? Faction.Angel : null,
					Faction.PrejudicedManipulator
				}
				.Where(faction => faction.HasValue)
				.Select(faction => faction!.Value)
				.ToArray()));
	}
}
