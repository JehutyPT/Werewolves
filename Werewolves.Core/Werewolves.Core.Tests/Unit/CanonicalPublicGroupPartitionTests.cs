using FluentAssertions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class CanonicalPublicGroupPartitionTests
{
	[Fact]
	public void Create_WithEquivalentUnevenGroups_NormalizesAndDefensivelySnapshots()
	{
		var singleton = new List<int> { 5 };
		var remainingSeats = new List<int> { 3, 1, 4, 2 };

		var first = CanonicalPublicGroupPartition.Create(
			playerCount: 5,
			singleton,
			remainingSeats);
		var swapped = CanonicalPublicGroupPartition.Create(
			playerCount: 5,
			remainingSeats.AsEnumerable().Reverse(),
			singleton);
		singleton.Clear();
		remainingSeats.Clear();

		first.Should().Be(swapped);
		first.GetHashCode().Should().Be(swapped.GetHashCode());
		first.FirstGroupSeatNumbers.Should().Equal(1, 2, 3, 4);
		first.SecondGroupSeatNumbers.Should().Equal(5);
		first.ToString().Should().Be("partition=[[1,2,3,4],[5]]");
		CanonicalPublicGroupPartition.Parse(first.ToString(), playerCount: 5)
			.Should().Be(first);
	}

	[Fact]
	public void Project_WithStablePlayerIds_UsesSeatOrderWithoutLeakingIdentities()
	{
		var roster = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
		var sameShapeRoster = Enumerable.Range(0, 5)
			.Select(_ => Guid.NewGuid())
			.ToArray();
		var partition = PublicGroupPartition.Create(
			roster,
			[roster[4], roster[0]],
			[roster[3], roster[1], roster[2]]);
		var sameShapePartition = PublicGroupPartition.Create(
			sameShapeRoster,
			[sameShapeRoster[0], sameShapeRoster[4]],
			[sameShapeRoster[2], sameShapeRoster[3], sameShapeRoster[1]]);
		var reorderedRoster = new[]
		{
			roster[1], roster[0], roster[2], roster[3], roster[4]
		};

		var projected = CanonicalPublicGroupPartition.Project(roster, partition);
		var sameShape = CanonicalPublicGroupPartition.Project(
			sameShapeRoster,
			sameShapePartition);
		var reordered = CanonicalPublicGroupPartition.Project(
			reorderedRoster,
			partition);

		projected.Should().Be(sameShape);
		projected.ToString().Should().Be("partition=[[1,5],[2,3,4]]");
		reordered.Should().NotBe(projected);
		reordered.ToString().Should().Be("partition=[[1,3,4],[2,5]]");
	}

	[Theory]
	[MemberData(nameof(InvalidSeatPartitions))]
	public void Create_WithInvalidSeatPartition_RejectsBeforeCanonicalization(
		int[] firstGroup,
		int[] secondGroup)
	{
		var act = () => CanonicalPublicGroupPartition.Create(
			playerCount: 5,
			firstGroup,
			secondGroup);

		act.Should().Throw<ArgumentException>();
	}

	public static IEnumerable<object[]> InvalidSeatPartitions()
	{
		yield return [Array.Empty<int>(), new[] { 1, 2, 3, 4, 5 }];
		yield return [new[] { 1, 2 }, new[] { 2, 3, 4, 5 }];
		yield return [new[] { 1 }, new[] { 2, 3, 4 }];
		yield return [new[] { 1, 1 }, new[] { 2, 3, 4, 5 }];
		yield return [new[] { 0, 1 }, new[] { 2, 3, 4, 5 }];
		yield return [new[] { 1 }, new[] { 2, 3, 4, 6 }];
	}
}
