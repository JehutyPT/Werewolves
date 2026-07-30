using System.Globalization;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.Components.Pages;

public enum LobbyEvaluationFrequencyKind
{
	NotObserved,
	LessThanOnePercent,
	WholePercent
}

public sealed record LobbyEvaluationFrequency(
	LobbyEvaluationFrequencyKind Kind,
	int? WholePercent = null);

public sealed record LobbyEvaluationTurnFrequency(
	int EndingTurn,
	LobbyEvaluationFrequency Frequency);

public sealed record LobbyEvaluationOutcome(
	GameResult GameResult,
	string Name,
	LobbyEvaluationFrequency Frequency,
	IReadOnlyList<LobbyEvaluationTurnFrequency> Turns);

public sealed record LobbyProbabilityPresentation(
	IReadOnlyList<LobbyEvaluationOutcome> Outcomes);

public static class LobbyEvaluationPresentation
{
	public static string GameResultName(GameResult result)
	{
		ArgumentNullException.ThrowIfNull(result);

		return result switch
		{
			NoWinnerGameResult => ClientStrings.LobbyEvaluation_GameResultNoWinner,
			SingleFactionGameResult single => FactionName(single.Faction),
			SharedVictoryGameResult shared => string.Format(
				CultureInfo.CurrentCulture,
				ClientStrings.LobbyEvaluation_GameResultSharedFormat,
				ClientStrings.LobbyEvaluation_GameResultShared,
				string.Join(
					ClientStrings.LobbyEvaluation_FactionSeparator,
					shared.Factions.Select(FactionName))),
			_ => throw new ArgumentOutOfRangeException(nameof(result))
		};
	}

	public static LobbyProbabilityPresentation Probability(
		LobbyProbabilityData probability)
	{
		ArgumentNullException.ThrowIfNull(probability);
		return new(probability.Outcomes
			.Select(row => new LobbyEvaluationOutcome(
				row.GameResult,
				GameResultName(row.GameResult),
				Frequency(row.Numerator, row.Denominator),
				row.Turns.Select(turn => new LobbyEvaluationTurnFrequency(
					turn.EndingTurn,
					Frequency(turn.Numerator, turn.Denominator))).ToArray()))
			.ToArray());
	}

	public static string FrequencyText(LobbyEvaluationFrequency frequency) =>
		frequency.Kind switch
		{
			LobbyEvaluationFrequencyKind.NotObserved => ClientStrings.LobbyEvaluation_NotObserved,
			LobbyEvaluationFrequencyKind.LessThanOnePercent => ClientStrings.LobbyEvaluation_LessThanOnePercent,
			LobbyEvaluationFrequencyKind.WholePercent when frequency.WholePercent is { } percentage =>
				string.Format(
					CultureInfo.CurrentCulture,
					ClientStrings.LobbyEvaluation_WholePercentFormat,
					percentage),
			_ => throw new ArgumentOutOfRangeException(nameof(frequency))
		};

	public static string AlreadyDecidedReasonText(AlreadyDecidedReason reason) => reason switch
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

	private static LobbyEvaluationFrequency Frequency(int numerator, int denominator)
	{
		if (numerator == 0)
		{
			return new(LobbyEvaluationFrequencyKind.NotObserved);
		}

		if ((long)numerator * 100 < denominator)
		{
			return new(LobbyEvaluationFrequencyKind.LessThanOnePercent);
		}

		var percentage = (int)Math.Round(
			(decimal)numerator * 100 / denominator,
			MidpointRounding.AwayFromZero);
		return new(LobbyEvaluationFrequencyKind.WholePercent, percentage);
	}

	private static string FactionName(Faction faction) => faction switch
	{
		Faction.Villager => ClientStrings.LobbyEvaluation_FactionVillager,
		Faction.Werewolf => ClientStrings.LobbyEvaluation_FactionWerewolf,
		Faction.WhiteWerewolf =>
			ClientStrings.LobbyEvaluation_FactionWhiteWerewolf,
		_ => throw new ArgumentOutOfRangeException(nameof(faction))
	};
}
