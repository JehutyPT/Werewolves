using System.Globalization;
using FluentAssertions;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
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
		var result = new SharedVictoryGameResult(
			[Faction.Villager, Faction.Werewolf, Faction.WhiteWerewolf]);

		var name = LobbyEvaluationPresentation.GameResultName(result);

		name.Should().Contain(ClientStrings.LobbyEvaluation_GameResultShared);
		name.Should().Contain(ClientStrings.LobbyEvaluation_FactionVillager);
		name.Should().Contain(ClientStrings.LobbyEvaluation_FactionWerewolf);
		name.Should().Contain(ClientStrings.LobbyEvaluation_FactionWhiteWerewolf);
	}

	[Fact]
	public void GameResultName_NoWinnerIsOneStandaloneLocalizedOutcome()
	{
		using var context = new ModeratorComponentTestContext();

		var name = LobbyEvaluationPresentation.GameResultName(new NoWinnerGameResult());

		name.Should().Be(ClientStrings.LobbyEvaluation_GameResultNoWinner);
		name.Should().NotContain(ClientStrings.LobbyEvaluation_FactionVillager);
		name.Should().NotContain(ClientStrings.LobbyEvaluation_FactionWerewolf);
		name.Should().NotContain(ClientStrings.LobbyEvaluation_FactionWhiteWerewolf);
	}

	[Theory]
	[InlineData(Faction.Villager)]
	[InlineData(Faction.Werewolf)]
	[InlineData(Faction.WhiteWerewolf)]
	public void GameResultName_SingleFactionUsesItsLocalizedFactionName(Faction faction)
	{
		using var context = new ModeratorComponentTestContext();
		var expected = faction switch
		{
			Faction.Villager => ClientStrings.LobbyEvaluation_FactionVillager,
			Faction.Werewolf => ClientStrings.LobbyEvaluation_FactionWerewolf,
			Faction.WhiteWerewolf =>
				ClientStrings.LobbyEvaluation_FactionWhiteWerewolf,
			_ => throw new ArgumentOutOfRangeException(nameof(faction))
		};

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
		var probability = new LobbyProbabilityData(
			[
				Outcome(villager, 0, 10_000),
				Outcome(werewolf, 1, 10_000, new LobbyProbabilityTurnData(3, 1, 10_000)),
				Outcome(noWinner, 9_999, 10_000, new LobbyProbabilityTurnData(1, 9_999, 10_000))
			]);

		var presentation = LobbyEvaluationPresentation.Probability(probability);

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
		var probability = new LobbyProbabilityData(
			[
				Outcome(villager, 3_333, 10_000, new LobbyProbabilityTurnData(1, 3_333, 10_000)),
				Outcome(werewolf, 3_333, 10_000, new LobbyProbabilityTurnData(1, 3_333, 10_000)),
				Outcome(noWinner, 3_334, 10_000, new LobbyProbabilityTurnData(1, 3_334, 10_000))
			]);

		var presentation = LobbyEvaluationPresentation.Probability(probability);
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
	public void Probability_RendersNonCumulativeEndingFrequencyByTurnFromClientProjection()
	{
		using var context = new ModeratorComponentTestContext();
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var probability = new LobbyProbabilityData(
			[
				Outcome(
					villager,
					6_000,
					10_000,
					new LobbyProbabilityTurnData(1, 3_000, 10_000),
					new LobbyProbabilityTurnData(2, 3_000, 10_000)),
				Outcome(werewolf, 4_000, 10_000, new LobbyProbabilityTurnData(2, 4_000, 10_000)),
				Outcome(noWinner, 0, 10_000)
			]);

		var presentation = LobbyEvaluationPresentation.Probability(probability);

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
	[InlineData(AlreadyDecidedReason.WhiteWerewolfSoleSurvivor)]
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
			AlreadyDecidedReason.WhiteWerewolfSoleSurvivor =>
				ClientStrings.LobbyEvaluation_ReasonWhiteWerewolfSoleSurvivor,
			_ => throw new ArgumentOutOfRangeException(nameof(reason))
		};

		LobbyEvaluationPresentation.AlreadyDecidedReasonText(reason).Should().Be(expected);
	}

	private static LobbyProbabilityOutcomeData Outcome(
		GameResult gameResult,
		int numerator,
		int denominator,
		params LobbyProbabilityTurnData[] turns) =>
		new(gameResult, numerator, denominator, turns);
}
