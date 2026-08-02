using System.Globalization;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Models.Simulation;

public sealed class CanonicalPublicGroupPartition
	: IEquatable<CanonicalPublicGroupPartition>
{
	private const string Prefix = "partition=[[";
	private const string Suffix = "]]";
	private const string GroupSeparator = "],[";
	private readonly int[] _firstGroupSeatNumbers;
	private readonly int[] _secondGroupSeatNumbers;

	public int PlayerCount =>
		_firstGroupSeatNumbers.Length + _secondGroupSeatNumbers.Length;

	public IReadOnlyList<int> FirstGroupSeatNumbers { get; }
	public IReadOnlyList<int> SecondGroupSeatNumbers { get; }

	private CanonicalPublicGroupPartition(
		int[] firstGroupSeatNumbers,
		int[] secondGroupSeatNumbers)
	{
		_firstGroupSeatNumbers = firstGroupSeatNumbers;
		_secondGroupSeatNumbers = secondGroupSeatNumbers;
		FirstGroupSeatNumbers = Array.AsReadOnly(_firstGroupSeatNumbers);
		SecondGroupSeatNumbers = Array.AsReadOnly(_secondGroupSeatNumbers);
	}

	public static CanonicalPublicGroupPartition Create(
		int playerCount,
		IEnumerable<int> firstGroupSeatNumbers,
		IEnumerable<int> secondGroupSeatNumbers)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerCount);
		ArgumentNullException.ThrowIfNull(firstGroupSeatNumbers);
		ArgumentNullException.ThrowIfNull(secondGroupSeatNumbers);

		var firstGroup = firstGroupSeatNumbers.Order().ToArray();
		var secondGroup = secondGroupSeatNumbers.Order().ToArray();
		if (firstGroup.Length == 0 || secondGroup.Length == 0 ||
			firstGroup.Any(seatNumber => seatNumber > playerCount) ||
			secondGroup.Any(seatNumber => seatNumber > playerCount) ||
			firstGroup.Distinct().Count() != firstGroup.Length ||
			secondGroup.Distinct().Count() != secondGroup.Length ||
			firstGroup.Intersect(secondGroup).Any() ||
			!firstGroup.Concat(secondGroup)
				.Order()
				.SequenceEqual(Enumerable.Range(1, playerCount)))
		{
			throw new ArgumentException(
				"A canonical Public Group Partition must contain every normalized Player seat exactly once in two non-empty groups.");
		}

		if (CompareLexicographically(firstGroup, secondGroup) > 0)
		{
			(firstGroup, secondGroup) = (secondGroup, firstGroup);
		}

		return new CanonicalPublicGroupPartition(firstGroup, secondGroup);
	}

	public static CanonicalPublicGroupPartition Project(
		IReadOnlyList<Guid> orderedPlayerIds,
		PublicGroupPartition publicGroupPartition)
	{
		ArgumentNullException.ThrowIfNull(orderedPlayerIds);
		ArgumentNullException.ThrowIfNull(publicGroupPartition);

		var roster = orderedPlayerIds.ToArray();
		if (roster.Any(playerId => playerId == Guid.Empty) ||
			roster.Distinct().Count() != roster.Length)
		{
			throw new ArgumentException(
				"Canonical projection requires distinct stable Player identities in Seating Order.",
				nameof(orderedPlayerIds));
		}

		var seatByPlayerId = roster
			.Select((playerId, index) => new { playerId, SeatNumber = index + 1 })
			.ToDictionary(entry => entry.playerId, entry => entry.SeatNumber);
		var partitionPlayerIds = publicGroupPartition.FirstGroupPlayerIds
			.Concat(publicGroupPartition.SecondGroupPlayerIds)
			.ToHashSet();
		if (partitionPlayerIds.Count != roster.Length ||
			partitionPlayerIds.Any(playerId => !seatByPlayerId.ContainsKey(playerId)))
		{
			throw new ArgumentException(
				"The Public Group Partition must cover the ordered Lobby roster exactly.",
				nameof(publicGroupPartition));
		}

		return Create(
			roster.Length,
			publicGroupPartition.FirstGroupPlayerIds.Select(playerId =>
				seatByPlayerId[playerId]),
			publicGroupPartition.SecondGroupPlayerIds.Select(playerId =>
				seatByPlayerId[playerId]));
	}

	public static CanonicalPublicGroupPartition Parse(
		string value,
		int playerCount)
	{
		if (!TryParse(value, playerCount, out var partition))
		{
			throw new FormatException(
				"The value is not a canonical Public Group Partition.");
		}

		return partition;
	}

	public static bool TryParse(
		string? value,
		int playerCount,
		out CanonicalPublicGroupPartition partition)
	{
		partition = null!;
		if (value is null || !HasCanonicalEnvelope(value))
		{
			return false;
		}

		var serializedGroups = value[Prefix.Length..^Suffix.Length]
			.Split(GroupSeparator, StringSplitOptions.None);
		if (serializedGroups.Length != 2 ||
			!TryParseGroup(serializedGroups[0], out var firstGroup) ||
			!TryParseGroup(serializedGroups[1], out var secondGroup))
		{
			return false;
		}

		try
		{
			partition = Create(playerCount, firstGroup, secondGroup);
		}
		catch (ArgumentException)
		{
			return false;
		}

		return string.Equals(value, partition.ToString(), StringComparison.Ordinal);
	}

	public override string ToString() =>
		$"{Prefix}{string.Join(',', _firstGroupSeatNumbers)}{GroupSeparator}{string.Join(',', _secondGroupSeatNumbers)}{Suffix}";

	internal static bool HasCanonicalEnvelope(string value) =>
		value.StartsWith(Prefix, StringComparison.Ordinal) &&
		value.EndsWith(Suffix, StringComparison.Ordinal);

	public bool Equals(CanonicalPublicGroupPartition? other) =>
		other is not null &&
		_firstGroupSeatNumbers.SequenceEqual(other._firstGroupSeatNumbers) &&
		_secondGroupSeatNumbers.SequenceEqual(other._secondGroupSeatNumbers);

	public override bool Equals(object? obj) =>
		obj is CanonicalPublicGroupPartition other && Equals(other);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach (var seatNumber in _firstGroupSeatNumbers)
		{
			hash.Add(seatNumber);
		}
		hash.Add(0);
		foreach (var seatNumber in _secondGroupSeatNumbers)
		{
			hash.Add(seatNumber);
		}
		return hash.ToHashCode();
	}

	public static bool operator ==(
		CanonicalPublicGroupPartition? left,
		CanonicalPublicGroupPartition? right) => Equals(left, right);

	public static bool operator !=(
		CanonicalPublicGroupPartition? left,
		CanonicalPublicGroupPartition? right) => !Equals(left, right);

	private static bool TryParseGroup(string value, out int[] seatNumbers)
	{
		seatNumbers = [];
		if (value.Length == 0)
		{
			return false;
		}

		var values = value.Split(',');
		var parsed = new int[values.Length];
		for (var index = 0; index < values.Length; index++)
		{
			if (!int.TryParse(
					values[index],
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out parsed[index]) ||
				!string.Equals(
					values[index],
					parsed[index].ToString(CultureInfo.InvariantCulture),
					StringComparison.Ordinal))
			{
				return false;
			}
		}

		seatNumbers = parsed;
		return true;
	}

	private static int CompareLexicographically(
		IReadOnlyList<int> left,
		IReadOnlyList<int> right)
	{
		for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
		{
			var comparison = left[index].CompareTo(right[index]);
			if (comparison != 0)
			{
				return comparison;
			}
		}

		return left.Count.CompareTo(right.Count);
	}
}
