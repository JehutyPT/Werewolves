using Werewolves.Core.StateModels.Enums;
using System.Globalization;

namespace Werewolves.Core.StateModels.Models.Simulation;

public readonly record struct CanonicalRoleCount(MainRoleType Role, int Count);

public sealed class CanonicalRoleComposition : IEquatable<CanonicalRoleComposition>
{
	private readonly CanonicalRoleCount[] _entries;

	public IReadOnlyList<CanonicalRoleCount> Entries { get; }

	public int CardCount { get; }

	private CanonicalRoleComposition(CanonicalRoleCount[] entries)
	{
		_entries = entries;
		Entries = Array.AsReadOnly(_entries);
		CardCount = entries.Sum(entry => entry.Count);
	}

	public static CanonicalRoleComposition Create(IEnumerable<MainRoleType> roleCards)
	{
		ArgumentNullException.ThrowIfNull(roleCards);

		var snapshot = roleCards.ToArray();
		if (snapshot.Any(role => !Enum.IsDefined(role)))
		{
			throw new ArgumentOutOfRangeException(
				nameof(roleCards),
				"Role Composition contains an unknown Role identifier.");
		}

		var entries = snapshot
			.GroupBy(role => role)
			.Select(group => new CanonicalRoleCount(group.Key, group.Count()))
			.OrderBy(entry => entry.Role.ToString(), StringComparer.Ordinal)
			.ToArray();

		return new CanonicalRoleComposition(entries);
	}

	public int GetCount(MainRoleType role) =>
		_entries.FirstOrDefault(entry => entry.Role == role).Count;

	public static CanonicalRoleComposition Parse(string value)
	{
		if (!TryParse(value, out var composition))
		{
			throw new FormatException("The value is not a canonical Role Composition.");
		}

		return composition;
	}

	public static bool TryParse(
		string? value,
		out CanonicalRoleComposition composition)
	{
		composition = null!;
		const string prefix = "roles=[";
		if (value is null
			|| !value.StartsWith(prefix, StringComparison.Ordinal)
			|| !value.EndsWith(']'))
		{
			return false;
		}

		var body = value[prefix.Length..^1];
		if (body.Length == 0)
		{
			composition = new CanonicalRoleComposition([]);
			return value == composition.ToString();
		}

		var entries = new List<CanonicalRoleCount>();
		var seenRoles = new HashSet<MainRoleType>();
		foreach (var serializedEntry in body.Split(','))
		{
			var separatorIndex = serializedEntry.IndexOf('=');
			if (separatorIndex <= 0
				|| separatorIndex != serializedEntry.LastIndexOf('='))
			{
				return false;
			}

			var roleIdentifier = serializedEntry[..separatorIndex];
			var countText = serializedEntry[(separatorIndex + 1)..];
			if (!Enum.TryParse<MainRoleType>(roleIdentifier, out var role)
				|| !Enum.IsDefined(role)
				|| !string.Equals(roleIdentifier, role.ToString(), StringComparison.Ordinal)
				|| !int.TryParse(
					countText,
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out var count)
				|| count <= 0
				|| !string.Equals(
					countText,
					count.ToString(CultureInfo.InvariantCulture),
					StringComparison.Ordinal)
				|| !seenRoles.Add(role))
			{
				return false;
			}

			entries.Add(new CanonicalRoleCount(role, count));
		}

		var parsedEntries = entries.ToArray();
		var sortedEntries = parsedEntries
			.OrderBy(entry => entry.Role.ToString(), StringComparer.Ordinal)
			.ToArray();
		if (!parsedEntries.SequenceEqual(sortedEntries))
		{
			return false;
		}

		try
		{
			composition = new CanonicalRoleComposition(parsedEntries);
		}
		catch (OverflowException)
		{
			return false;
		}

		return string.Equals(value, composition.ToString(), StringComparison.Ordinal);
	}

	public override string ToString() =>
		$"roles=[{string.Join(',', _entries.Select(entry =>
			$"{entry.Role}={entry.Count.ToString(CultureInfo.InvariantCulture)}"))}]";

	public bool Equals(CanonicalRoleComposition? other) =>
		other is not null && _entries.SequenceEqual(other._entries);

	public override bool Equals(object? obj) =>
		obj is CanonicalRoleComposition other && Equals(other);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach (var entry in _entries)
		{
			hash.Add(entry.Role);
			hash.Add(entry.Count);
		}

		return hash.ToHashCode();
	}

	public static bool operator ==(
		CanonicalRoleComposition? left,
		CanonicalRoleComposition? right) => Equals(left, right);

	public static bool operator !=(
		CanonicalRoleComposition? left,
		CanonicalRoleComposition? right) => !Equals(left, right);
}
