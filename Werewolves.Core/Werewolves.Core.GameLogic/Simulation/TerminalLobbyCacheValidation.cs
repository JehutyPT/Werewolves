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
		AlreadyDecidedTerminalCacheRecord record)
	{
		var classification = ClassifyProducer(
			record.CompatibilityIdentity,
			probabilityRecord: false);
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
		int policy,
		TerminalCacheGameResultFrequency[] rows,
		TerminalCacheTurnWindowFrequency[] cells,
		bool turnOneOnly)
	{
		if (rows.Length == 0
			|| rows.Any(row => row.Denominator != policy)
			|| cells.Any(cell => cell.Denominator != policy)
			|| rows.Select(row => row.GameResult).Distinct().Count() != rows.Length
			|| rows.Sum(row => row.Numerator) != policy)
		{
			throw new ArgumentException("Invalid complete Game Result distribution.");
		}

		var probabilityRecord = !turnOneOnly;
		var profile = ResolveProducerProfile(identity.Profile, probabilityRecord);
		var classification = ClassifyProducer(identity, probabilityRecord);
		if (classification.AlreadyDecided is not { IsAlreadyDecided: false }
			|| classification.Cacheability?.CompatibilityIdentity != identity)
		{
			throw new ArgumentException(
				"Aggregate records require a cacheable Simulation Scenario for their named producer.");
		}

		if (!PossibleGameResultInventory.TryCreate(
			classification.Scenario,
			profile,
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
		bool probabilityRecord)
	{
		ArgumentNullException.ThrowIfNull(identity);
		var producer = ResolveProducerProfile(identity.Profile, probabilityRecord);

		var canonical = identity.Scenario;
		ValidateMaterializationBounds(canonical);
		var dealPool = canonical.RoleComposition.Entries.SelectMany(entry =>
			Enumerable.Repeat(entry.Role, entry.Count)).ToArray();
		var fullComposition = dealPool
			.Concat(canonical.Offer1Role is { } offer1 ? [offer1] : [])
			.Concat(canonical.Offer2Role is { } offer2 ? [offer2] : [])
			.ToArray();
		var scenario = new SimulationScenario(
			canonical.PlayerCount,
			fullComposition,
			dealPool,
			canonical.Offer1Role,
			canonical.Offer2Role,
			new ActorSetupCards(canonical.ActorSetupCards),
			canonical.RuleState);
		if (!scenario.ToCanonical().Equals(canonical))
		{
			throw new ArgumentException(
				"The canonical Simulation Scenario cannot be reconstructed exactly.",
				nameof(identity));
		}

		var classification = SimulationScenarioClassifier.Classify(scenario, producer);
		if (classification.SimulatorSupport is not
			{
				IsSupported: true,
				Profile: var profile
			}
			|| !profile.Identity.Equals(identity.Profile))
		{
			throw new ArgumentException(
				"The canonical Simulation Scenario is not supported by its cache producer.",
				nameof(identity));
		}

		return classification;
	}

	private static SimulatorProfile ResolveProducerProfile(
		SimulatorProfileIdentity identity,
		bool probabilityRecord)
	{
		ArgumentNullException.ThrowIfNull(identity);
		SimulatorProfile producer;
		if (identity.Equals(SimulatorCapability.SafetyScreening.Identity))
		{
			if (probabilityRecord)
			{
				throw new ArgumentException(
					"Safety screening cannot produce probability cache records.",
					nameof(identity));
			}

			producer = SimulatorCapability.SafetyScreening;
		}
		else if (identity.Equals(SimulatorCapability.FullProbability.Identity))
		{
			producer = SimulatorCapability.FullProbability;
		}
		else
		{
			throw new ArgumentException(
				"The cache identity does not name a known producer.",
				nameof(identity));
		}

		return producer;
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
