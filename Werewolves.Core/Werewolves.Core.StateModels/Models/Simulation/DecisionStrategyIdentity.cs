namespace Werewolves.Core.StateModels.Models.Simulation;

public sealed class DecisionStrategyIdentity : IEquatable<DecisionStrategyIdentity>
{
	private readonly VersionedIdentityParts _parts;

	public string StrategyId => _parts.Identifier;

	public string Version => _parts.Version;

	public DecisionStrategyIdentity(string strategyId, string version)
	{
		_parts = VersionedIdentityParts.Create(
			strategyId,
			version,
			nameof(strategyId),
			nameof(version),
			"Strategy identifiers may contain only ordinal letters, digits, '.', '_' and '-'.",
			"Strategy versions may contain only ordinal letters, digits, '.', '_' and '-'.");
	}

	private DecisionStrategyIdentity(VersionedIdentityParts parts) => _parts = parts;

	public static DecisionStrategyIdentity Parse(string value)
	{
		if (!TryParse(value, out var identity))
		{
			throw new FormatException("The value is not a decision strategy identity.");
		}

		return identity;
	}

	public static bool TryParse(string? value, out DecisionStrategyIdentity identity)
	{
		identity = null!;
		if (!VersionedIdentityParts.TryParse(value, out var parts))
		{
			return false;
		}

		identity = new DecisionStrategyIdentity(parts);
		return true;
	}

	public override string ToString() => _parts.ToString();

	public bool Equals(DecisionStrategyIdentity? other) =>
		other is not null && _parts.Equals(other._parts);

	public override bool Equals(object? obj) =>
		obj is DecisionStrategyIdentity other && Equals(other);

	public override int GetHashCode() => _parts.GetHashCode();
}
