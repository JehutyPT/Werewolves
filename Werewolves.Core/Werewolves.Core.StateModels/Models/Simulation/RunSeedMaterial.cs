using System.Globalization;

namespace Werewolves.Core.StateModels.Models.Simulation;

public sealed class RunSeedMaterial : IEquatable<RunSeedMaterial>
{
	private const string StrategyMarker = "|strategy=";
	private const string RunMarker = "|run=";

	public SimulationCompatibilityIdentity CompatibilityIdentity { get; }

	public DecisionStrategyIdentity DecisionStrategyIdentity { get; }

	public long RunNumber { get; }

	public RunSeedMaterial(
		SimulationCompatibilityIdentity compatibilityIdentity,
		DecisionStrategyIdentity decisionStrategyIdentity,
		long runNumber)
	{
		ArgumentNullException.ThrowIfNull(compatibilityIdentity);
		ArgumentNullException.ThrowIfNull(decisionStrategyIdentity);
		ArgumentOutOfRangeException.ThrowIfNegative(runNumber);

		CompatibilityIdentity = compatibilityIdentity;
		DecisionStrategyIdentity = decisionStrategyIdentity;
		RunNumber = runNumber;
	}

	public static RunSeedMaterial Parse(string value)
	{
		if (!TryParse(value, out var material))
		{
			throw new FormatException("The value is not canonical Run Seed Material.");
		}

		return material;
	}

	public static bool TryParse(string? value, out RunSeedMaterial material)
	{
		material = null!;
		if (value is null)
		{
			return false;
		}

		var strategyIndex = value.LastIndexOf(StrategyMarker, StringComparison.Ordinal);
		var runIndex = value.LastIndexOf(RunMarker, StringComparison.Ordinal);
		if (strategyIndex <= 0 || runIndex <= strategyIndex + StrategyMarker.Length)
		{
			return false;
		}

		if (!SimulationCompatibilityIdentity.TryParse(value[..strategyIndex], out var compatibilityIdentity)
			|| !DecisionStrategyIdentity.TryParse(
				value[(strategyIndex + StrategyMarker.Length)..runIndex],
				out var strategyIdentity))
		{
			return false;
		}

		var runText = value[(runIndex + RunMarker.Length)..];
		if (!long.TryParse(
				runText,
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out var runNumber)
			|| runNumber < 0
			|| !string.Equals(
				runText,
				runNumber.ToString(CultureInfo.InvariantCulture),
				StringComparison.Ordinal))
		{
			return false;
		}

		material = new RunSeedMaterial(
			compatibilityIdentity,
			strategyIdentity,
			runNumber);
		return string.Equals(value, material.ToString(), StringComparison.Ordinal);
	}

	public override string ToString() =>
		$"{CompatibilityIdentity}{StrategyMarker}{DecisionStrategyIdentity}{RunMarker}{RunNumber.ToString(CultureInfo.InvariantCulture)}";

	public bool Equals(RunSeedMaterial? other) =>
		other is not null
		&& CompatibilityIdentity.Equals(other.CompatibilityIdentity)
		&& DecisionStrategyIdentity.Equals(other.DecisionStrategyIdentity)
		&& RunNumber == other.RunNumber;

	public override bool Equals(object? obj) =>
		obj is RunSeedMaterial other && Equals(other);

	public override int GetHashCode() =>
		HashCode.Combine(CompatibilityIdentity, DecisionStrategyIdentity, RunNumber);
}
