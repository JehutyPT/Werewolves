using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class RoleLockInTests
{
	[Fact]
	public void ThiefRoleLockIn_PreservesStableInventoryAndOrderedOfferPartition()
	{
		var thief = Card("10000000-0000-0000-0000-000000000001", MainRoleType.Thief);
		var werewolf = Card("10000000-0000-0000-0000-000000000002", MainRoleType.BigBadWolf);
		var seer = Card("10000000-0000-0000-0000-000000000003", MainRoleType.Seer);
		var witch = Card("10000000-0000-0000-0000-000000000004", MainRoleType.Witch);
		var hunter = Card("10000000-0000-0000-0000-000000000005", MainRoleType.Hunter);
		var firstVillager = Card("10000000-0000-0000-0000-000000000006", MainRoleType.SimpleVillager);
		var secondVillager = Card("10000000-0000-0000-0000-000000000007", MainRoleType.SimpleVillager);
		PhysicalCharacterCard[] inventory =
			[thief, werewolf, seer, witch, hunter, firstVillager, secondVillager];

		var roleLockIn = new RoleLockIn(
			version: 7,
			playerCount: 5,
			inventory,
			dealPoolCardIds: [thief.Id, werewolf.Id, seer.Id, witch.Id, hunter.Id],
			offer1CardId: firstVillager.Id,
			offer2CardId: secondVillager.Id);

		roleLockIn.Version.Should().Be(7);
		roleLockIn.RoleComposition.Should().Equal(inventory);
		roleLockIn.DealPool.Should().Equal(thief, werewolf, seer, witch, hunter);
		roleLockIn.Offer1.Should().Be(firstVillager);
		roleLockIn.Offer2.Should().Be(secondVillager);
		roleLockIn.Offer1!.Id.Should().NotBe(roleLockIn.Offer2!.Id);
		roleLockIn.Offer1.PrintedRole.Should().Be(roleLockIn.Offer2.PrintedRole);
	}

	[Fact]
	public void ThiefRoleLockIn_WithGroupedRoleInOffers_IsRejected()
	{
		var thief = Card("20000000-0000-0000-0000-000000000001", MainRoleType.Thief);
		var werewolf = Card("20000000-0000-0000-0000-000000000002", MainRoleType.BigBadWolf);
		var seer = Card("20000000-0000-0000-0000-000000000003", MainRoleType.Seer);
		var witch = Card("20000000-0000-0000-0000-000000000004", MainRoleType.Witch);
		var hunter = Card("20000000-0000-0000-0000-000000000005", MainRoleType.Hunter);
		var firstSister = Card("20000000-0000-0000-0000-000000000006", MainRoleType.TwoSisters);
		var secondSister = Card("20000000-0000-0000-0000-000000000007", MainRoleType.TwoSisters);

		var act = () => new RoleLockIn(
			version: 1,
			playerCount: 5,
			roleComposition:
				[thief, werewolf, seer, witch, hunter, firstSister, secondSister],
			dealPoolCardIds: [thief.Id, werewolf.Id, seer.Id, witch.Id, hunter.Id],
			offer1CardId: firstSister.Id,
			offer2CardId: secondSister.Id);

		act.Should().Throw<ArgumentException>()
			.WithParameterName("offer1CardId");
	}

	[Fact]
	public void NonThiefRoleLockIn_WithInventoryCardOutsideTheDealPool_IsRejected()
	{
		var werewolf = Card("30000000-0000-0000-0000-000000000001", MainRoleType.BigBadWolf);
		var seer = Card("30000000-0000-0000-0000-000000000002", MainRoleType.Seer);
		var witch = Card("30000000-0000-0000-0000-000000000003", MainRoleType.Witch);
		var hunter = Card("30000000-0000-0000-0000-000000000004", MainRoleType.Hunter);
		var villager = Card("30000000-0000-0000-0000-000000000005", MainRoleType.SimpleVillager);

		var act = () => new RoleLockIn(
			version: 1,
			playerCount: 5,
			roleComposition: [werewolf, seer, witch, hunter, villager],
			dealPoolCardIds: [werewolf.Id, seer.Id, witch.Id, hunter.Id]);

		act.Should().Throw<ArgumentException>()
			.WithParameterName("dealPoolCardIds");
	}

	[Fact]
	public void NonThiefRoleLockIn_WithOfferSlots_IsRejected()
	{
		var werewolf = Card("40000000-0000-0000-0000-000000000001", MainRoleType.BigBadWolf);
		var seer = Card("40000000-0000-0000-0000-000000000002", MainRoleType.Seer);
		var witch = Card("40000000-0000-0000-0000-000000000003", MainRoleType.Witch);
		var hunter = Card("40000000-0000-0000-0000-000000000004", MainRoleType.Hunter);
		var villager = Card("40000000-0000-0000-0000-000000000005", MainRoleType.SimpleVillager);

		var act = () => new RoleLockIn(
			version: 1,
			playerCount: 5,
			roleComposition: [werewolf, seer, witch, hunter, villager],
			dealPoolCardIds: [werewolf.Id, seer.Id, witch.Id],
			offer1CardId: hunter.Id,
			offer2CardId: villager.Id);

		act.Should().Throw<ArgumentException>()
			.WithParameterName("offer1CardId");
	}

	[Fact]
	public void ThiefRoleLockIn_WithSamePrintedEligibleOffers_IsAccepted()
	{
		var thief = Card("50000000-0000-0000-0000-000000000001", MainRoleType.Thief);
		var werewolf = Card("50000000-0000-0000-0000-000000000002", MainRoleType.BigBadWolf);
		var villager = Card("50000000-0000-0000-0000-000000000003", MainRoleType.SimpleVillager);
		var witch = Card("50000000-0000-0000-0000-000000000004", MainRoleType.Witch);
		var hunter = Card("50000000-0000-0000-0000-000000000005", MainRoleType.Hunter);
		var firstSeer = Card("50000000-0000-0000-0000-000000000006", MainRoleType.Seer);
		var secondSeer = Card("50000000-0000-0000-0000-000000000007", MainRoleType.Seer);

		var roleLockIn = new RoleLockIn(
			version: 1,
			playerCount: 5,
			roleComposition:
				[thief, werewolf, villager, witch, hunter, firstSeer, secondSeer],
			dealPoolCardIds: [thief.Id, werewolf.Id, villager.Id, witch.Id, hunter.Id],
			offer1CardId: firstSeer.Id,
			offer2CardId: secondSeer.Id);

		roleLockIn.Offer1.Should().Be(firstSeer);
		roleLockIn.Offer2.Should().Be(secondSeer);
		var config = new GameSessionConfig(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			roleLockIn);
		config.RoleLockIn.Should().BeSameAs(roleLockIn);
	}

	[Fact]
	public void RoleLockIn_WithDuplicatePhysicalInstanceIdentity_IsRejected()
	{
		var duplicateId = Guid.Parse("60000000-0000-0000-0000-000000000001");
		var werewolf = new PhysicalCharacterCard(duplicateId, MainRoleType.BigBadWolf);
		var seer = new PhysicalCharacterCard(duplicateId, MainRoleType.Seer);
		var witch = Card("60000000-0000-0000-0000-000000000003", MainRoleType.Witch);
		var hunter = Card("60000000-0000-0000-0000-000000000004", MainRoleType.Hunter);
		var villager = Card("60000000-0000-0000-0000-000000000005", MainRoleType.SimpleVillager);

		var act = () => new RoleLockIn(
			version: 1,
			playerCount: 5,
			roleComposition: [werewolf, seer, witch, hunter, villager],
			dealPoolCardIds: [werewolf.Id, seer.Id, witch.Id, hunter.Id, villager.Id]);

		act.Should().Throw<ArgumentException>()
			.WithParameterName("roleComposition");
	}

	[Fact]
	public void PhysicalCharacterCard_WithEmptyIdentity_IsRejected()
	{
		var act = () => new PhysicalCharacterCard(
			Guid.Empty,
			MainRoleType.Seer);

		act.Should().Throw<ArgumentException>()
			.WithParameterName("id");
	}

	[Fact]
	public void PhysicalCharacterCard_WithUnknownPrintedRole_IsRejected()
	{
		var act = () => new PhysicalCharacterCard(
			Guid.Parse("80000000-0000-0000-0000-000000000001"),
			(MainRoleType)int.MaxValue);

		act.Should().Throw<ArgumentOutOfRangeException>()
			.WithParameterName("printedRole");
	}

	[Theory]
	[InlineData(MainRoleType.TwoSisters, 2)]
	[InlineData(MainRoleType.ThreeBrothers, 3)]
	public void NonThiefRoleLockIn_WithCompleteGroupedRoleInDealPool_IsAccepted(
		MainRoleType groupedRole,
		int groupedCount)
	{
		var cards = new List<PhysicalCharacterCard>
		{
			Card("70000000-0000-0000-0000-000000000001", MainRoleType.BigBadWolf),
			Card("70000000-0000-0000-0000-000000000002", MainRoleType.Seer)
		};
		cards.AddRange(Enumerable.Range(3, groupedCount)
			.Select(index => Card(
				$"70000000-0000-0000-0000-{index:D12}",
				groupedRole)));
		while (cards.Count < 5)
		{
			cards.Add(Card(
				$"70000000-0000-0000-0001-{cards.Count:D12}",
				MainRoleType.SimpleVillager));
		}

		var roleLockIn = new RoleLockIn(
			version: 1,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Select(card => card.Id));

		roleLockIn.DealPool.Count(card => card.PrintedRole == groupedRole)
			.Should().Be(groupedCount);
		roleLockIn.Offer1.Should().BeNull();
		roleLockIn.Offer2.Should().BeNull();
	}

	[Fact]
	public void GameSessionConfig_WithAcceptedRoleLockIn_UsesItsPhysicalInventory()
	{
		var cards = new[]
		{
			Card("90000000-0000-0000-0000-000000000001", MainRoleType.BigBadWolf),
			Card("90000000-0000-0000-0000-000000000002", MainRoleType.Seer),
			Card("90000000-0000-0000-0000-000000000003", MainRoleType.Witch),
			Card("90000000-0000-0000-0000-000000000004", MainRoleType.Hunter),
			Card("90000000-0000-0000-0000-000000000005", MainRoleType.SimpleVillager)
		};
		var roleLockIn = new RoleLockIn(
			version: 4,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Select(card => card.Id));

		var config = new GameSessionConfig(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			roleLockIn);

		config.RoleLockIn.Should().BeSameAs(roleLockIn);
		config.Roles.Should().Equal(cards.Select(card => card.PrintedRole));
	}

	private static PhysicalCharacterCard Card(string id, MainRoleType printedRole) =>
		new(Guid.Parse(id), printedRole);
}
