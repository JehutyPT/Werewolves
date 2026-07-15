namespace Werewolves.Core.StateModels.Models.Simulation;

public sealed class SimulatorProfileIdentity : IEquatable<SimulatorProfileIdentity>
{
	private readonly VersionedIdentityParts _parts;

	public string ProfileId => _parts.Identifier;

	public string Version => _parts.Version;

	public SimulatorProfileIdentity(string profileId, string version)
	{
		_parts = VersionedIdentityParts.Create(
			profileId,
			version,
			nameof(profileId),
			nameof(version),
			"Profile identifiers may contain only ordinal letters, digits, '.', '_' and '-'.",
			"Profile versions may contain only ordinal letters, digits, '.', '_' and '-'.");
	}

	private SimulatorProfileIdentity(VersionedIdentityParts parts) => _parts = parts;

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
		if (!VersionedIdentityParts.TryParse(value, out var parts))
		{
			return false;
		}

		identity = new SimulatorProfileIdentity(parts);
		return true;
	}

	public override string ToString() => _parts.ToString();

	public bool Equals(SimulatorProfileIdentity? other) =>
		other is not null && _parts.Equals(other._parts);

	public override bool Equals(object? obj) =>
		obj is SimulatorProfileIdentity other && Equals(other);

	public override int GetHashCode() => _parts.GetHashCode();

	public static bool operator ==(
		SimulatorProfileIdentity? left,
		SimulatorProfileIdentity? right) => Equals(left, right);

	public static bool operator !=(
		SimulatorProfileIdentity? left,
		SimulatorProfileIdentity? right) => !Equals(left, right);
}
