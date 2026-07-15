namespace Werewolves.Core.StateModels.Models.Simulation;

internal readonly struct VersionedIdentityParts : IEquatable<VersionedIdentityParts>
{
	private const char Separator = '@';

	public string Identifier { get; }

	public string Version { get; }

	private VersionedIdentityParts(string identifier, string version)
	{
		Identifier = identifier;
		Version = version;
	}

	public static VersionedIdentityParts Create(
		string? identifier,
		string? version,
		string identifierParameterName,
		string versionParameterName,
		string identifierErrorMessage,
		string versionErrorMessage)
	{
		ValidatePart(identifier, identifierParameterName, identifierErrorMessage);
		ValidatePart(version, versionParameterName, versionErrorMessage);
		return new VersionedIdentityParts(identifier!, version!);
	}

	public static bool TryParse(string? value, out VersionedIdentityParts parts)
	{
		parts = default;
		if (value is null)
		{
			return false;
		}

		var separatorIndex = value.IndexOf(Separator);
		if (separatorIndex <= 0 || separatorIndex != value.LastIndexOf(Separator))
		{
			return false;
		}

		var identifier = value[..separatorIndex];
		var version = value[(separatorIndex + 1)..];
		if (!IsValidPart(identifier) || !IsValidPart(version))
		{
			return false;
		}

		parts = new VersionedIdentityParts(identifier, version);
		return true;
	}

	public override string ToString() => $"{Identifier}{Separator}{Version}";

	public bool Equals(VersionedIdentityParts other) =>
		string.Equals(Identifier, other.Identifier, StringComparison.Ordinal)
		&& string.Equals(Version, other.Version, StringComparison.Ordinal);

	public override bool Equals(object? obj) =>
		obj is VersionedIdentityParts other && Equals(other);

	public override int GetHashCode() =>
		HashCode.Combine(
			StringComparer.Ordinal.GetHashCode(Identifier),
			StringComparer.Ordinal.GetHashCode(Version));

	private static void ValidatePart(
		string? value,
		string parameterName,
		string errorMessage)
	{
		if (!IsValidPart(value))
		{
			throw new ArgumentException(errorMessage, parameterName);
		}
	}

	private static bool IsValidPart(string? value) =>
		!string.IsNullOrEmpty(value)
		&& value.All(character =>
			character is >= 'A' and <= 'Z'
			or >= 'a' and <= 'z'
			or >= '0' and <= '9'
			or '.' or '_' or '-');
}
