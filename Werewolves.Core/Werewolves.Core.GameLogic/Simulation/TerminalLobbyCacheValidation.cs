using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public static partial class TerminalLobbyCache
{
	private const int MaximumPhysicalRoleCardCount =
		GameSessionConfig.MaximumPlayerCount + 2;

	internal static GameResult ValidateGameResult(GameResult gameResult)
	{
		ArgumentNullException.ThrowIfNull(gameResult);
		if (gameResult.GetType() != typeof(SingleFactionGameResult)
			&& gameResult.GetType() != typeof(SharedVictoryGameResult)
			&& gameResult.GetType() != typeof(NoWinnerGameResult))
		{
			throw new ArgumentException(
				"Unknown Game Result.",
				nameof(gameResult));
		}

		return gameResult;
	}

	internal static string ResultKey(GameResult result) => result switch
	{
		SingleFactionGameResult value => $"0:{(int)value.Faction:D10}",
		SharedVictoryGameResult value =>
			$"1:{string.Join(',', value.Factions.Select(faction => ((int)faction).ToString("D10")))}",
		NoWinnerGameResult => "2:",
		_ => throw new ArgumentException("Unknown Game Result.", nameof(result))
	};

	internal static void ValidateFrequency(int numerator, int denominator)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(numerator);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
		if (numerator > denominator)
		{
			throw new ArgumentOutOfRangeException(nameof(numerator));
		}
	}

	internal static void ValidateAlreadyDecided(
		AlreadyDecidedTerminalCacheRecord record,
		SimulatorCapability capability)
	{
		ArgumentNullException.ThrowIfNull(capability);
		var classification = ClassifyProducer(
			record.CompatibilityIdentity,
			capability,
			LobbyEvaluationDepth.DegenerateScreeningOnly);
		var expected = classification.AlreadyDecided;
		if (expected is not { IsAlreadyDecided: true, GameResult: not null }
			|| !expected.GameResult.Equals(record.GameResult)
			|| expected.Reason != record.Reason)
		{
			throw new ArgumentException(
				"The record does not match its cache producer's already-decided classification.");
		}
	}

	internal static void ValidateAggregate(
		SimulationCompatibilityIdentity identity,
		SimulatorCapability capability,
		int policy,
		TerminalCacheGameResultFrequency[] rows,
		TerminalCacheTurnWindowFrequency[] cells,
		bool turnOneOnly)
	{
		ArgumentNullException.ThrowIfNull(capability);
		if (rows.Length == 0
			|| rows.Any(row => row.Denominator != policy)
			|| cells.Any(cell => cell.Denominator != policy)
			|| rows.Select(row => row.GameResult).Distinct().Count() != rows.Length
			|| rows.Sum(row => row.Numerator) != policy)
		{
			throw new ArgumentException("Invalid complete Game Result distribution.");
		}

		var depth = turnOneOnly
			? LobbyEvaluationDepth.DegenerateScreeningOnly
			: LobbyEvaluationDepth.FullProbability;
		var classification = ClassifyProducer(identity, capability, depth);
		if (classification.AlreadyDecided is not { IsAlreadyDecided: false }
			|| classification.Cacheability?.CompatibilityIdentity != identity)
		{
			throw new ArgumentException(
				"Aggregate records require a cacheable Simulation Scenario for their named producer.");
		}

		if (!PossibleGameResultInventory.TryCreate(
			classification.Scenario,
			capability,
			out var derivedInventory))
		{
			throw new ArgumentException(
				"The Simulation Scenario does not have a Game Result inventory for its named producer.");
		}

		var expectedInventory = derivedInventory.GameResults
			.OrderBy(ResultKey, StringComparer.Ordinal)
			.ToArray();
		if (!rows
			.Select(row => row.GameResult)
			.SequenceEqual(expectedInventory))
		{
			throw new ArgumentException(
				"The Game Result distribution must exactly match the named producer's inventory.");
		}

		var inventory = rows.Select(row => row.GameResult).ToArray();
		if (cells.Any(cell => !inventory.Contains(cell.GameResult))
			|| cells
				.GroupBy(cell => new
				{
					cell.GameResult,
					cell.EndingTurn,
					cell.VictoryCheckWindow
				})
				.Any(group => group.Count() != 1)
			|| (turnOneOnly && cells.Any(cell => cell.EndingTurn > 1)))
		{
			throw new ArgumentException("Invalid Turn/window cells.");
		}

		foreach (var row in rows)
		{
			if (cells
				.Where(cell => cell.GameResult.Equals(row.GameResult))
				.Sum(cell => cell.Numerator) != row.Numerator)
			{
				throw new ArgumentException(
					"Turn/window cells do not reproduce the distribution.");
			}
		}

		if (cells.Sum(cell => cell.Numerator) != policy)
		{
			throw new ArgumentException(
				"Turn/window cells do not reproduce the distribution.");
		}
	}

	private static SimulationScenarioClassification ClassifyProducer(
		SimulationCompatibilityIdentity identity,
		SimulatorCapability capability,
		LobbyEvaluationDepth depth)
	{
		ArgumentNullException.ThrowIfNull(identity);
		ArgumentNullException.ThrowIfNull(capability);
		if (!capability.SupportsEvaluationDepth(depth))
		{
			throw new ArgumentException(
				"The cache record kind is not supported by its Simulator Capability.",
				nameof(identity));
		}

		var canonical = identity.Scenario;
		ValidateMaterializationBounds(canonical);
		var scenario = SimulationScenario.FromCanonical(canonical);
		if (!scenario.ToCanonical().Equals(canonical))
		{
			throw new ArgumentException(
				"The canonical Simulation Scenario cannot be reconstructed exactly.",
				nameof(identity));
		}

		var classification = SimulationScenarioClassifier.Classify(scenario, capability);
		if (classification.SimulatorSupport is not
			{ IsSupported: true }
			|| !capability.CreateCompatibilityIdentity(scenario).Equals(identity))
		{
			throw new ArgumentException(
				"The canonical Simulation Scenario is not supported by its cache producer.",
				nameof(identity));
		}

		return classification;
	}

	private static void ValidateMaterializationBounds(
		CanonicalSimulationScenario scenario)
	{
		if (scenario.PlayerCount < GameSessionConfig.MinimumPlayerCount
			|| scenario.PlayerCount > GameSessionConfig.MaximumPlayerCount)
		{
			throw new ArgumentException(
				"The canonical Simulation Scenario has an unsupported Player Count.",
				nameof(scenario));
		}

		var cardCount = 0;
		foreach (var entry in scenario.RoleComposition.Entries)
		{
			try
			{
				cardCount = checked(cardCount + entry.Count);
			}
			catch (OverflowException exception)
			{
				throw new ArgumentException(
					"The canonical Role Composition card count is unbounded.",
					nameof(scenario),
					exception);
			}

			if (cardCount > MaximumPhysicalRoleCardCount)
			{
				throw new ArgumentException(
					"The canonical Role Composition exceeds the physical card maximum.",
					nameof(scenario));
			}
		}

		try
		{
			cardCount = checked(cardCount +
				(scenario.Offer1Role is null ? 0 : 1) +
				(scenario.Offer2Role is null ? 0 : 1));
		}
		catch (OverflowException exception)
		{
			throw new ArgumentException(
				"The canonical Role Composition card count is unbounded.",
				nameof(scenario),
				exception);
		}
		if (cardCount > MaximumPhysicalRoleCardCount)
		{
			throw new ArgumentException(
				"The canonical Role Composition exceeds the physical card maximum.",
				nameof(scenario));
		}
	}
}
