namespace Werewolves.Core.StateModels.Models.Simulation;

public sealed class DecisionStrategyIdentity : IEquatable<DecisionStrategyIdentity>
{
	public string StrategyId { get; }

	public string Version { get; }

	public DecisionStrategyIdentity(string strategyId, string version)
	{
		if (!IsValidPart(strategyId))
		{
			throw new ArgumentException(
				"Strategy identifiers may contain only ordinal letters, digits, '.', '_' and '-'.",
				nameof(strategyId));
		}

		if (!IsValidPart(version))
		{
			throw new ArgumentException(
				"Strategy versions may contain only ordinal letters, digits, '.', '_' and '-'.",
				nameof(version));
		}

		StrategyId = strategyId;
		Version = version;
	}

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
		if (value is null)
		{
			return false;
		}

		var separatorIndex = value.IndexOf('@');
		if (separatorIndex <= 0 || separatorIndex != value.LastIndexOf('@'))
		{
			return false;
		}

		try
		{
			identity = new DecisionStrategyIdentity(
				value[..separatorIndex],
				value[(separatorIndex + 1)..]);
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	public override string ToString() => $"{StrategyId}@{Version}";

	public bool Equals(DecisionStrategyIdentity? other) =>
		other is not null
		&& string.Equals(StrategyId, other.StrategyId, StringComparison.Ordinal)
		&& string.Equals(Version, other.Version, StringComparison.Ordinal);

	public override bool Equals(object? obj) =>
		obj is DecisionStrategyIdentity other && Equals(other);

	public override int GetHashCode() =>
		HashCode.Combine(
			StringComparer.Ordinal.GetHashCode(StrategyId),
			StringComparer.Ordinal.GetHashCode(Version));

	private static bool IsValidPart(string? value) =>
		!string.IsNullOrEmpty(value)
		&& value.All(character =>
			character is >= 'A' and <= 'Z'
			or >= 'a' and <= 'z'
			or >= '0' and <= '9'
			or '.' or '_' or '-');
}
