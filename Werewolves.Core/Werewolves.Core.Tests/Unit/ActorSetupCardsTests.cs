using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class ActorSetupCardsTests
{
	[Fact]
	public void None_IsTheOnlyEmptyUnversionedArtifact()
	{
		ActorSetupCards.None.Version.Should().Be(0);
		ActorSetupCards.None.Cards.Should().BeEmpty();
		ActorSetupCards.None.PrintedRoles.Should().BeEmpty();
		new ActorSetupCards(Array.Empty<MainRoleType>())
			.Should().Be(ActorSetupCards.None);
	}

	[Fact]
	public void PrintedRoleConstructor_BindsFreshOpaqueCardIdentitiesWithoutChangingTheChoices()
	{
		MainRoleType[] printedRoles =
		[
			MainRoleType.Seer,
			MainRoleType.Cupid,
			MainRoleType.Elder
		];

		var artifact = new ActorSetupCards(printedRoles);

		artifact.Version.Should().Be(1);
		artifact.PrintedRoles.Should().Equal(printedRoles);
		artifact.Cards.Select(card => card.PrintedRole).Should().Equal(printedRoles);
		artifact.Cards.Select(card => card.Id)
			.Should().NotContain(Guid.Empty)
			.And.OnlyHaveUniqueItems();
	}

	[Fact]
	public void PhysicalCardConstructor_SnapshotsCardsAndRetainsPresentationOrder()
	{
		var cards = new List<PhysicalCharacterCard>
		{
			new(Guid.NewGuid(), MainRoleType.Elder),
			new(Guid.NewGuid(), MainRoleType.Seer),
			new(Guid.NewGuid(), MainRoleType.Cupid)
		};

		var artifact = new ActorSetupCards(version: 4, cards);
		cards.Clear();

		artifact.Version.Should().Be(4);
		artifact.PrintedRoles.Should().Equal(
			MainRoleType.Elder,
			MainRoleType.Seer,
			MainRoleType.Cupid);
		artifact.Cards.Should().HaveCount(3);
	}

	[Fact]
	public void Equals_UsesVersionAndUnorderedPhysicalCardIdentity()
	{
		PhysicalCharacterCard[] cards =
		[
			new(Guid.NewGuid(), MainRoleType.Seer),
			new(Guid.NewGuid(), MainRoleType.Cupid),
			new(Guid.NewGuid(), MainRoleType.Elder)
		];
		var baseline = new ActorSetupCards(version: 3, cards);
		var reordered = new ActorSetupCards(version: 3, cards.Reverse());
		var changedVersion = new ActorSetupCards(version: 4, cards);
		var rebound = ActorSetupCards.CreateFromPrintedRoles(
			version: 3,
			cards.Select(card => card.PrintedRole));

		reordered.Should().Be(baseline);
		reordered.GetHashCode().Should().Be(baseline.GetHashCode());
		changedVersion.Should().NotBe(baseline);
		rebound.Should().NotBe(baseline);
	}

	[Theory]
	[InlineData(0, true)]
	[InlineData(-1, true)]
	[InlineData(1, false)]
	public void Constructor_RejectsVersionThatDoesNotMatchArtifactPresence(
		long version,
		bool hasCards)
	{
		PhysicalCharacterCard[] cards = hasCards
			? [new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Seer)]
			: [];

		var act = () => new ActorSetupCards(version, cards);

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void Constructor_WithDuplicatePhysicalCardIdentity_RejectsArtifact()
	{
		var id = Guid.NewGuid();
		PhysicalCharacterCard[] cards =
		[
			new(id, MainRoleType.Seer),
			new(id, MainRoleType.Cupid)
		];

		var act = () => new ActorSetupCards(version: 1, cards);

		act.Should().Throw<ArgumentException>();
	}
}
