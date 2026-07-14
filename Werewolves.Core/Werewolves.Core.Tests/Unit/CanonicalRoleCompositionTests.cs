using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class CanonicalRoleCompositionTests
{
	[Fact]
	public void Create_WithEquivalentCardMultisets_ReturnsEqualSortedSnapshots()
	{
		var firstCards = new List<MainRoleType>
		{
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager
		};
		var secondCards = firstCards.AsEnumerable().Reverse().ToArray();

		var first = CanonicalRoleComposition.Create(firstCards);
		var second = CanonicalRoleComposition.Create(secondCards);
		firstCards.Clear();

		first.Should().Be(second);
		first.Entries.Select(entry => entry.Role).Should().Equal(
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleWerewolf,
			MainRoleType.WildChild);
		first.GetCount(MainRoleType.SimpleVillager).Should().Be(2);
		first.CardCount.Should().Be(5);
	}

	[Fact]
	public void Parse_WithFivePlayerThiefComposition_RoundTripsAllSevenPhysicalCards()
	{
		var composition = CanonicalRoleComposition.Create(
		[
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		]);

		var serialized = composition.ToString();
		var parsed = CanonicalRoleComposition.Parse(serialized);

		serialized.Should().Be("roles=[Seer=1,SimpleVillager=4,SimpleWerewolf=1,Thief=1]");
		parsed.Should().Be(composition);
		parsed.CardCount.Should().Be(7);
	}

	[Theory]
	[InlineData("roles=[SimpleVillager=0]")]
	[InlineData("roles=[SimpleVillager=1,Seer=1]")]
	[InlineData("roles=[1=1]")]
	[InlineData("roles=[seer=1]")]
	[InlineData("roles=[UnknownRole=1]")]
	[InlineData("roles=[Seer=1,Seer=1]")]
	[InlineData("roles=[Seer=01]")]
	[InlineData("Seer=1")]
	public void TryParse_WithMalformedOrNoncanonicalValue_ReturnsFalse(string value)
	{
		var parsed = CanonicalRoleComposition.TryParse(value, out var composition);

		parsed.Should().BeFalse();
		composition.Should().BeNull();
	}
}
