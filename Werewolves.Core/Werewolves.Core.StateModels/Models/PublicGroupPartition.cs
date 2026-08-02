using System.Collections.Immutable;

namespace Werewolves.Core.StateModels.Models;

public sealed class PublicGroupPartition : IEquatable<PublicGroupPartition>
{
	private readonly ImmutableHashSet<Guid> _firstGroupPlayerIds;
	private readonly ImmutableHashSet<Guid> _secondGroupPlayerIds;

	public IReadOnlySet<Guid> FirstGroupPlayerIds => _firstGroupPlayerIds;
	public IReadOnlySet<Guid> SecondGroupPlayerIds => _secondGroupPlayerIds;

	private PublicGroupPartition(
		ImmutableHashSet<Guid> firstGroupPlayerIds,
		ImmutableHashSet<Guid> secondGroupPlayerIds)
	{
		_firstGroupPlayerIds = firstGroupPlayerIds;
		_secondGroupPlayerIds = secondGroupPlayerIds;
	}

	public static PublicGroupPartition Create(
		IEnumerable<Guid> rosterPlayerIds,
		IEnumerable<Guid> firstGroupPlayerIds,
		IEnumerable<Guid> secondGroupPlayerIds)
	{
		ArgumentNullException.ThrowIfNull(rosterPlayerIds);
		ArgumentNullException.ThrowIfNull(firstGroupPlayerIds);
		ArgumentNullException.ThrowIfNull(secondGroupPlayerIds);

		var roster = SnapshotExactSet(rosterPlayerIds, nameof(rosterPlayerIds));
		var firstGroup = SnapshotExactSet(
			firstGroupPlayerIds,
			nameof(firstGroupPlayerIds));
		var secondGroup = SnapshotExactSet(
			secondGroupPlayerIds,
			nameof(secondGroupPlayerIds));
		if (firstGroup.Overlaps(secondGroup))
		{
			throw new ArgumentException(
				"Public Group Partition groups must be disjoint.",
				nameof(secondGroupPlayerIds));
		}
		if (!roster.SetEquals(firstGroup.Union(secondGroup)))
		{
			throw new ArgumentException(
				"Public Group Partition groups must contain every current Player identity exactly once.",
				nameof(rosterPlayerIds));
		}

		return new PublicGroupPartition(firstGroup, secondGroup);
	}

	public bool Equals(PublicGroupPartition? other)
	{
		if (other is null)
		{
			return false;
		}

		return
			(_firstGroupPlayerIds.SetEquals(other._firstGroupPlayerIds) &&
				_secondGroupPlayerIds.SetEquals(other._secondGroupPlayerIds)) ||
			(_firstGroupPlayerIds.SetEquals(other._secondGroupPlayerIds) &&
				_secondGroupPlayerIds.SetEquals(other._firstGroupPlayerIds));
	}

	public override bool Equals(object? obj) =>
		obj is PublicGroupPartition other && Equals(other);

	public override int GetHashCode()
	{
		var firstGroupHash = GetGroupHashCode(_firstGroupPlayerIds);
		var secondGroupHash = GetGroupHashCode(_secondGroupPlayerIds);
		return firstGroupHash <= secondGroupHash
			? HashCode.Combine(firstGroupHash, secondGroupHash)
			: HashCode.Combine(secondGroupHash, firstGroupHash);
	}

	private static ImmutableHashSet<Guid> SnapshotExactSet(
		IEnumerable<Guid> playerIds,
		string parameterName)
	{
		var values = playerIds.ToArray();
		if (values.Length == 0 ||
			values.Any(playerId => playerId == Guid.Empty) ||
			values.Distinct().Count() != values.Length)
		{
			throw new ArgumentException(
				"Player identity sets must be non-empty and contain distinct stable identities.",
				parameterName);
		}

		return values.ToImmutableHashSet();
	}

	private static int GetGroupHashCode(IEnumerable<Guid> playerIds)
	{
		var hash = new HashCode();
		foreach (var playerId in playerIds.Order())
		{
			hash.Add(playerId);
		}
		return hash.ToHashCode();
	}
}
