using FluentAssertions;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class PublicGroupPartitionTests
{
	public static TheoryData<string, Guid[], Guid[], Guid[]> InvalidPartitions => new()
	{
		{
			"empty roster",
			[],
			[PlayerId(1)],
			[PlayerId(2)]
		},
		{
			"empty first group",
			[PlayerId(1), PlayerId(2)],
			[],
			[PlayerId(1), PlayerId(2)]
		},
		{
			"empty second group",
			[PlayerId(1), PlayerId(2)],
			[PlayerId(1), PlayerId(2)],
			[]
		},
		{
			"empty roster identity",
			[Guid.Empty, PlayerId(2)],
			[Guid.Empty],
			[PlayerId(2)]
		},
		{
			"empty first-group identity",
			[PlayerId(1), PlayerId(2)],
			[Guid.Empty],
			[PlayerId(1), PlayerId(2)]
		},
		{
			"empty second-group identity",
			[PlayerId(1), PlayerId(2)],
			[PlayerId(1), PlayerId(2)],
			[Guid.Empty]
		},
		{
			"duplicate roster identity",
			[PlayerId(1), PlayerId(1), PlayerId(2)],
			[PlayerId(1)],
			[PlayerId(2)]
		},
		{
			"duplicate first-group identity",
			[PlayerId(1), PlayerId(2)],
			[PlayerId(1), PlayerId(1)],
			[PlayerId(2)]
		},
		{
			"duplicate second-group identity",
			[PlayerId(1), PlayerId(2)],
			[PlayerId(1)],
			[PlayerId(2), PlayerId(2)]
		},
		{
			"overlapping groups",
			[PlayerId(1), PlayerId(2), PlayerId(3)],
			[PlayerId(1), PlayerId(2)],
			[PlayerId(2), PlayerId(3)]
		},
		{
			"omitted roster identity",
			[PlayerId(1), PlayerId(2), PlayerId(3)],
			[PlayerId(1)],
			[PlayerId(2)]
		},
		{
			"foreign group identity",
			[PlayerId(1), PlayerId(2)],
			[PlayerId(1)],
			[PlayerId(2), PlayerId(3)]
		}
	};

	[Fact]
	public void Create_WithUnequalCompleteGroups_PreservesExactMembership()
	{
		Guid[] roster = [PlayerId(1), PlayerId(2), PlayerId(3), PlayerId(4)];

		var partition = PublicGroupPartition.Create(
			roster,
			firstGroupPlayerIds: [PlayerId(1)],
			secondGroupPlayerIds: [PlayerId(2), PlayerId(3), PlayerId(4)]);

		partition.FirstGroupPlayerIds.Should().BeEquivalentTo([PlayerId(1)]);
		partition.SecondGroupPlayerIds.Should().BeEquivalentTo(
			[PlayerId(2), PlayerId(3), PlayerId(4)]);
	}

	[Theory]
	[MemberData(nameof(InvalidPartitions))]
	public void Create_WithoutAnExactTwoGroupRosterPartition_IsRejected(
		string reason,
		Guid[] rosterPlayerIds,
		Guid[] firstGroupPlayerIds,
		Guid[] secondGroupPlayerIds)
	{
		var act = () => PublicGroupPartition.Create(
			rosterPlayerIds,
			firstGroupPlayerIds,
			secondGroupPlayerIds);

		act.Should().Throw<ArgumentException>(reason);
	}

	[Fact]
	public void Create_WithNullIdentityCollection_IsRejectedAtThePublicBoundary()
	{
		var missingRoster = () => PublicGroupPartition.Create(
			null!,
			[PlayerId(1)],
			[PlayerId(2)]);
		var missingFirstGroup = () => PublicGroupPartition.Create(
			[PlayerId(1), PlayerId(2)],
			null!,
			[PlayerId(2)]);
		var missingSecondGroup = () => PublicGroupPartition.Create(
			[PlayerId(1), PlayerId(2)],
			[PlayerId(1)],
			null!);

		missingRoster.Should().Throw<ArgumentNullException>()
			.WithParameterName("rosterPlayerIds");
		missingFirstGroup.Should().Throw<ArgumentNullException>()
			.WithParameterName("firstGroupPlayerIds");
		missingSecondGroup.Should().Throw<ArgumentNullException>()
			.WithParameterName("secondGroupPlayerIds");
	}

	[Fact]
	public void Equality_IgnoresMemberOrderAndExchangeOfTheUnlabeledGroups()
	{
		Guid[] roster = [PlayerId(1), PlayerId(2), PlayerId(3), PlayerId(4)];
		var partition = PublicGroupPartition.Create(
			roster,
			[PlayerId(1), PlayerId(2)],
			[PlayerId(3), PlayerId(4)]);
		var reorderedAndExchanged = PublicGroupPartition.Create(
			roster.Reverse(),
			[PlayerId(4), PlayerId(3)],
			[PlayerId(2), PlayerId(1)]);
		var changedMembership = PublicGroupPartition.Create(
			roster,
			[PlayerId(1), PlayerId(3)],
			[PlayerId(2), PlayerId(4)]);

		reorderedAndExchanged.Should().Be(partition);
		reorderedAndExchanged.GetHashCode().Should().Be(partition.GetHashCode());
		changedMembership.Should().NotBe(partition);
	}

	[Fact]
	public void Create_SnapshotsMembershipDefensively()
	{
		var roster = new List<Guid> { PlayerId(1), PlayerId(2), PlayerId(3) };
		var firstGroup = new List<Guid> { PlayerId(1) };
		var secondGroup = new List<Guid> { PlayerId(2), PlayerId(3) };
		var partition = PublicGroupPartition.Create(
			roster,
			firstGroup,
			secondGroup);

		roster.Clear();
		firstGroup.Add(PlayerId(2));
		secondGroup.Clear();

		partition.FirstGroupPlayerIds.Should().BeEquivalentTo([PlayerId(1)]);
		partition.SecondGroupPlayerIds.Should().BeEquivalentTo(
			[PlayerId(2), PlayerId(3)]);
	}

	private static Guid PlayerId(int value) =>
		Guid.Parse($"10000000-0000-0000-0000-{value:D12}");
}
