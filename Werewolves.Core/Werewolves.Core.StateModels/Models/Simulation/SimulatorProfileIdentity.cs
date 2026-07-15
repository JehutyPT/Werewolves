namespace Werewolves.Core.StateModels.Models.Simulation;

public sealed class SimulatorProfileIdentity : IEquatable<SimulatorProfileIdentity>
{
	public string ProfileId { get; }

	public string Version { get; }

	public SimulatorProfileIdentity(string profileId, string version)
	{
		if (!IsValidPart(profileId))
		{
			throw new ArgumentException(
				"Profile identifiers may contain only ordinal letters, digits, '.', '_' and '-'.",
				nameof(profileId));
		}

		if (!IsValidPart(version))
		{
			throw new ArgumentException(
				"Profile versions may contain only ordinal letters, digits, '.', '_' and '-'.",
				nameof(version));
		}

		ProfileId = profileId;
		Version = version;
	}

	public static SimulatorProfileIdentity Parse(string value)
	{
		if (!TryParse(value, out var identity))
		{
			throw new FormatException("The value is not a simulator profile identity.");
		}

		return identity;
	}

	public static bool TryParse(string? value, out SimulatorProfileIdentity identity)
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
			identity = new SimulatorProfileIdentity(
				value[..separatorIndex],
				value[(separatorIndex + 1)..]);
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	public override string ToString() => $"{ProfileId}@{Version}";

	public bool Equals(SimulatorProfileIdentity? other) =>
		other is not null
		&& string.Equals(ProfileId, other.ProfileId, StringComparison.Ordinal)
		&& string.Equals(Version, other.Version, StringComparison.Ordinal);

	public override bool Equals(object? obj) =>
		obj is SimulatorProfileIdentity other && Equals(other);

	public override int GetHashCode() =>
		HashCode.Combine(
			StringComparer.Ordinal.GetHashCode(ProfileId),
			StringComparer.Ordinal.GetHashCode(Version));

	public static bool operator ==(
		SimulatorProfileIdentity? left,
		SimulatorProfileIdentity? right) => Equals(left, right);

	public static bool operator !=(
		SimulatorProfileIdentity? left,
		SimulatorProfileIdentity? right) => !Equals(left, right);

	private static bool IsValidPart(string? value) =>
		!string.IsNullOrEmpty(value)
		&& value.All(character =>
			character is >= 'A' and <= 'Z'
			or >= 'a' and <= 'z'
			or >= '0' and <= '9'
			or '.' or '_' or '-');
}
