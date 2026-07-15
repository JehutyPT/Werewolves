using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class SimulationResultEvidenceTests
{
	[Fact]
	public void NoWinner_IsAnImmutableMutuallyExclusiveGameResultValue()
	{
		new NoWinnerGameResult().Should().Be(new NoWinnerGameResult());
		new NoWinnerGameResult().Should().NotBe(new SingleFactionGameResult(Faction.Villager));
		new NoWinnerGameResult().GetHashCode().Should().Be(new NoWinnerGameResult().GetHashCode());
	}

	[Fact]
	public void Evidence_PreservesDeclaredInventoryAndExactZeroFrequencyRows()
	{
		var identity = CreateIdentity();
		var source = new SimulationBatchSourceEvidence(
			identity.Scenario,
			identity.Profile,
			new DecisionStrategyIdentity("baseline-random", "1-splitmix64"),
			[
				Completed(identity, 0, new SingleFactionGameResult(Faction.Villager), 1, VictoryCheckWindow.Dawn),
				Completed(identity, 1, new NoWinnerGameResult(), 2, VictoryCheckWindow.PreNight),
				Completed(identity, 2, new SingleFactionGameResult(Faction.Villager), 2, VictoryCheckWindow.Dawn)
			]);
		var shared = new SharedVictoryGameResult([Faction.Villager, Faction.Werewolf]);
		var evidence = new SimulationResultEvidence(
			source,
			[Faction.Villager, Faction.Werewolf],
			[
				new SingleFactionGameResult(Faction.Villager),
				new SingleFactionGameResult(Faction.Werewolf),
				shared,
				new NoWinnerGameResult()
			]);

		evidence.AttemptedRunCount.Should().Be(3);
		evidence.CompletedRunCount.Should().Be(3);
		evidence.IncompleteRunCount.Should().Be(0);
		evidence.PossibleFactions.Should().Equal(Faction.Villager, Faction.Werewolf);
		evidence.GameResultFrequencies.Should().ContainEquivalentOf(
			new GameResultFrequency(new SingleFactionGameResult(Faction.Werewolf), 0, 3));
		evidence.GameResultFrequencies.Sum(row => row.Numerator).Should().Be(3);
		evidence.GameResultFrequencyByTurn.Sum(cell => cell.Numerator).Should().Be(3);
		evidence.GetEndedByTurnFrequency(1).Should().Be(new ExactFrequency(1, 3));
		evidence.GetEndedByTurnFrequency(2, new SingleFactionGameResult(Faction.Villager))
			.Should().Be(new ExactFrequency(2, 3));
	}

	[Fact]
	public void Evidence_RejectsDuplicatesUndefinedFactionsAndOutOfInventoryResults()
	{
		var identity = CreateIdentity();
		var source = new SimulationBatchSourceEvidence(
			identity.Scenario,
			identity.Profile,
			new DecisionStrategyIdentity("baseline-random", "1-splitmix64"),
			[Completed(identity, 0, new NoWinnerGameResult(), 1, VictoryCheckWindow.Dawn)]);

		Action duplicate = () => new SimulationResultEvidence(
			source,
			[Faction.Villager, Faction.Villager],
			[new NoWinnerGameResult()]);
		Action undefined = () => new SimulationResultEvidence(
			source,
			[(Faction)99],
			[new NoWinnerGameResult()]);
		Action missingObserved = () => new SimulationResultEvidence(
			source,
			[Faction.Villager],
			[new SingleFactionGameResult(Faction.Villager)]);

		duplicate.Should().Throw<ArgumentException>();
		undefined.Should().Throw<ArgumentOutOfRangeException>();
		missingObserved.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Evidence_WithNoCompletedRuns_RetainsEvidenceWithoutUndefinedFrequencyRows()
	{
		var identity = CreateIdentity();
		var source = new SimulationBatchSourceEvidence(
			identity.Scenario,
			identity.Profile,
			new DecisionStrategyIdentity("baseline-random", "1-splitmix64"),
			[]);

		var evidence = new SimulationResultEvidence(
			source,
			[Faction.Villager, Faction.Werewolf],
			[
				new SingleFactionGameResult(Faction.Villager),
				new SingleFactionGameResult(Faction.Werewolf),
				new NoWinnerGameResult()
			]);

		evidence.GameResultFrequencies.Should().BeEmpty();
		evidence.GameResultFrequencyByTurn.Should().BeEmpty();
		Action endedByTurn = () => evidence.GetEndedByTurnFrequency(1);
		endedByTurn.Should().Throw<InvalidOperationException>();
		Action zeroDenominator = () => new ExactFrequency(0, 0);
		zeroDenominator.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void Evidence_WithMixedAttempts_PreservesEveryRecordAndUsesCompletedOnlyDenominator()
	{
		var identity = CreateIdentity();
		var source = new SimulationBatchSourceEvidence(
			identity.Scenario,
			identity.Profile,
			new DecisionStrategyIdentity("baseline-random", "1-splitmix64"),
			[
				Completed(identity, 0, new SingleFactionGameResult(Faction.Villager), 1, VictoryCheckWindow.Dawn),
				new IncompleteSimulationRun(new RunSeedMaterial(
					identity,
					new DecisionStrategyIdentity("baseline-random", "1-splitmix64"),
					1))
			]);

		var evidence = new SimulationResultEvidence(
			source,
			[Faction.Villager, Faction.Werewolf],
			[
				new SingleFactionGameResult(Faction.Villager),
				new SingleFactionGameResult(Faction.Werewolf),
				new NoWinnerGameResult()
			]);

		evidence.Records.Should().Equal(source.Records);
		evidence.AttemptedRunCount.Should().Be(2);
		evidence.CompletedRunCount.Should().Be(1);
		evidence.IncompleteRunCount.Should().Be(1);
		evidence.GameResultFrequencies.Should().HaveCount(3)
			.And.OnlyContain(row => row.Denominator == 1);
		evidence.GameResultFrequencies.Sum(row => row.Numerator).Should().Be(1);
	}

	private static SimulationCompatibilityIdentity CreateIdentity()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]).ToCanonical();
		return new SimulationCompatibilityIdentity(
			scenario,
			new SimulatorProfileIdentity("core-simulator", "1"));
	}

	private static CompletedSimulationRun Completed(
		SimulationCompatibilityIdentity identity,
		long run,
		GameResult result,
		int turn,
		VictoryCheckWindow window) => new(
		new RunSeedMaterial(
			identity,
			new DecisionStrategyIdentity("baseline-random", "1-splitmix64"),
			run),
		result,
		turn,
		window);
}
