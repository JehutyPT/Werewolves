using System.Globalization;
using FluentAssertions;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class LobbyEvaluationPresentationTests
{
	[Fact]
	public void GameResultName_SharedVictoryComposesEveryLocalizedFactionAsOneOutcome()
	{
		using var context = new ModeratorComponentTestContext();
		var result = new SharedVictoryGameResult([Faction.Villager, Faction.Werewolf]);

		var name = LobbyEvaluationPresentation.GameResultName(result);

		name.Should().Contain(ClientStrings.LobbyEvaluation_GameResultShared);
		name.Should().Contain(ClientStrings.LobbyEvaluation_FactionVillager);
		name.Should().Contain(ClientStrings.LobbyEvaluation_FactionWerewolf);
	}

	[Fact]
	public void GameResultName_NoWinnerIsOneStandaloneLocalizedOutcome()
	{
		using var context = new ModeratorComponentTestContext();

		var name = LobbyEvaluationPresentation.GameResultName(new NoWinnerGameResult());

		name.Should().Be(ClientStrings.LobbyEvaluation_GameResultNoWinner);
		name.Should().NotContain(ClientStrings.LobbyEvaluation_FactionVillager);
		name.Should().NotContain(ClientStrings.LobbyEvaluation_FactionWerewolf);
	}

	[Theory]
	[InlineData(Faction.Villager)]
	[InlineData(Faction.Werewolf)]
	public void GameResultName_SingleFactionUsesItsLocalizedFactionName(Faction faction)
	{
		using var context = new ModeratorComponentTestContext();
		var expected = faction == Faction.Villager
			? ClientStrings.LobbyEvaluation_FactionVillager
			: ClientStrings.LobbyEvaluation_FactionWerewolf;

		LobbyEvaluationPresentation.GameResultName(new SingleFactionGameResult(faction))
			.Should().Be(expected);
	}

	[Fact]
	public void Probability_ClassifiesZeroAndPositiveBelowOnePercentBeforeWholeRounding()
	{
		using var context = new ModeratorComponentTestContext();
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var record = CreateProbabilityRecord(
			[
				new(villager, 0, 10_000),
				new(werewolf, 1, 10_000),
				new(noWinner, 9_999, 10_000)
			],
			[
				new(werewolf, 3, VictoryCheckWindow.Dawn, 1, 10_000),
				new(noWinner, 1, VictoryCheckWindow.PreNight, 9_999, 10_000)
			]);

		var presentation = LobbyEvaluationPresentation.Probability(record);

		presentation.Outcomes.Single(outcome => outcome.Name == ClientStrings.LobbyEvaluation_FactionVillager)
			.Frequency.Kind.Should().Be(LobbyEvaluationFrequencyKind.NotObserved);
		presentation.Outcomes.Single(outcome => outcome.Name == ClientStrings.LobbyEvaluation_FactionWerewolf)
			.Frequency.Kind.Should().Be(LobbyEvaluationFrequencyKind.LessThanOnePercent);
		presentation.Outcomes.Single(outcome => outcome.Name == ClientStrings.LobbyEvaluation_GameResultNoWinner)
			.Frequency.Should().Be(new LobbyEvaluationFrequency(LobbyEvaluationFrequencyKind.WholePercent, 100));
	}

	[Fact]
	public void Probability_IndependentlyRoundsWholePercentsWithoutNormalizingTheirTotal()
	{
		using var context = new ModeratorComponentTestContext();
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var record = CreateProbabilityRecord(
			[
				new(villager, 3_333, 10_000),
				new(werewolf, 3_333, 10_000),
				new(noWinner, 3_334, 10_000)
			],
			[
				new(villager, 1, VictoryCheckWindow.Dawn, 3_333, 10_000),
				new(werewolf, 1, VictoryCheckWindow.Dawn, 3_333, 10_000),
				new(noWinner, 1, VictoryCheckWindow.Dawn, 3_334, 10_000)
			]);

		var presentation = LobbyEvaluationPresentation.Probability(record);
		var wholePercents = presentation.Outcomes
			.Select(outcome => outcome.Frequency.WholePercent!.Value)
			.ToArray();

		wholePercents.Should().Equal(33, 33, 33);
		wholePercents.Sum().Should().Be(99);
		presentation.Outcomes.Select(outcome => LobbyEvaluationPresentation.FrequencyText(outcome.Frequency))
			.Should().OnlyContain(text => text == string.Format(
				CultureInfo.CurrentCulture,
				ClientStrings.LobbyEvaluation_WholePercentFormat,
				33));
	}

	[Fact]
	public void Probability_AggregatesWindowsIntoNonCumulativeEndingFrequencyByTurn()
	{
		using var context = new ModeratorComponentTestContext();
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var record = CreateProbabilityRecord(
			[
				new(villager, 6_000, 10_000),
				new(werewolf, 4_000, 10_000),
				new(noWinner, 0, 10_000)
			],
			[
				new(villager, 1, VictoryCheckWindow.Dawn, 1_000, 10_000),
				new(villager, 1, VictoryCheckWindow.PreNight, 2_000, 10_000),
				new(villager, 2, VictoryCheckWindow.Dawn, 3_000, 10_000),
				new(werewolf, 2, VictoryCheckWindow.Dawn, 1_000, 10_000),
				new(werewolf, 2, VictoryCheckWindow.PreNight, 3_000, 10_000)
			]);

		var presentation = LobbyEvaluationPresentation.Probability(record);

		presentation.Outcomes.Single(outcome => outcome.GameResult.Equals(villager)).Turns.Should().Equal(
			new LobbyEvaluationTurnFrequency(
				1,
				new LobbyEvaluationFrequency(LobbyEvaluationFrequencyKind.WholePercent, 30)),
			new LobbyEvaluationTurnFrequency(
				2,
				new LobbyEvaluationFrequency(LobbyEvaluationFrequencyKind.WholePercent, 30)));
		presentation.Outcomes.Single(outcome => outcome.GameResult.Equals(werewolf)).Turns.Should().Equal(
			new LobbyEvaluationTurnFrequency(
				2,
				new LobbyEvaluationFrequency(LobbyEvaluationFrequencyKind.WholePercent, 40)));
		presentation.Outcomes.Single(outcome => outcome.GameResult.Equals(noWinner)).Turns.Should().BeEmpty();
	}

	[Theory]
	[InlineData(AlreadyDecidedReason.NoWerewolfFactionBeneficiariesAtLobbyExit)]
	[InlineData(AlreadyDecidedReason.WerewolfControlShortcut)]
	[InlineData(AlreadyDecidedReason.MultipleLobbyExitVictoryPredicatesSatisfied)]
	public void AlreadyDecidedReasonText_UsesDistinctLocalizedCopyForEveryValidTerminalReason(
		AlreadyDecidedReason reason)
	{
		using var context = new ModeratorComponentTestContext();
		var expected = reason switch
		{
			AlreadyDecidedReason.NoWerewolfFactionBeneficiariesAtLobbyExit =>
				ClientStrings.LobbyEvaluation_ReasonNoWerewolfBeneficiaries,
			AlreadyDecidedReason.WerewolfControlShortcut =>
				ClientStrings.LobbyEvaluation_ReasonWerewolfControl,
			AlreadyDecidedReason.MultipleLobbyExitVictoryPredicatesSatisfied =>
				ClientStrings.LobbyEvaluation_ReasonMultipleVictories,
			_ => throw new ArgumentOutOfRangeException(nameof(reason))
		};

		LobbyEvaluationPresentation.AlreadyDecidedReasonText(reason).Should().Be(expected);
	}

	private static ProbabilityTerminalCacheRecord CreateProbabilityRecord(
		IEnumerable<TerminalCacheGameResultFrequency> frequencies,
		IEnumerable<TerminalCacheTurnWindowFrequency> cells)
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorProfile.Active.Identity);
		return new(identity, frequencies, cells);
	}
}
