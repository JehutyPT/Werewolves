using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class CanonicalSimulationScenarioTests
{
	[Fact]
	public void ToCanonical_WithFixedPartition_UsesDealPoolAndOrderedOffersButNotInstanceIds()
	{
		var first = CreatePartitionedRoleLockIn();
		var samePrintedPartitionWithFreshIds = CreatePartitionedRoleLockIn();
		var swappedOffers = new RoleLockIn(
			version: 7,
			playerCount: first.PlayerCount,
			roleComposition: first.RoleComposition,
			dealPoolCardIds: first.DealPool.Select(card => card.Id),
			offer1CardId: first.Offer2!.Id,
			offer2CardId: first.Offer1!.Id);
		var firstCanonical = new SimulationScenario(first).ToCanonical();
		var freshIdsCanonical = new SimulationScenario(samePrintedPartitionWithFreshIds).ToCanonical();
		var swappedOffersCanonical = new SimulationScenario(swappedOffers).ToCanonical();

		firstCanonical.Should().Be(freshIdsCanonical);
		firstCanonical.Should().NotBe(swappedOffersCanonical);
		firstCanonical.RoleComposition.Should().Be(
			CanonicalRoleComposition.Create(first.DealPool.Select(card => card.PrintedRole)));
		firstCanonical.Offer1Role.Should().Be(MainRoleType.Seer);
		firstCanonical.Offer2Role.Should().Be(MainRoleType.Cupid);
		firstCanonical.ToString().Should().Be(
			"players=5|roles=[SimpleVillager=3,SimpleWerewolf=1,Thief=1]|offers=[Seer,Cupid]|thief=[Offer1,Offer2,Decline]|actor=[]|rules=[]");
		CanonicalSimulationScenario.Parse(firstCanonical.ToString()).Should().Be(firstCanonical);
	}

	[Fact]
	public void ToCanonical_WithSamePrintedOffers_PreservesSlotsAndDeduplicatesBehavioralBranches()
	{
		var canonical = new SimulationScenario(
			CreatePartitionedRoleLockIn(MainRoleType.Seer, MainRoleType.Seer))
			.ToCanonical();

		canonical.Offer1Role.Should().Be(MainRoleType.Seer);
		canonical.Offer2Role.Should().Be(MainRoleType.Seer);
		canonical.ThiefOfferBranchPolicy.Should().NotBeNull();
		canonical.ThiefOfferBranchPolicy!.Branches.Should().Equal(
			ThiefOfferBranch.Offer1,
			ThiefOfferBranch.Decline);
		canonical.ToString().Should().Contain(
			"|offers=[Seer,Seer]|thief=[Offer1,Decline]|");
		CanonicalSimulationScenario.Parse(canonical.ToString()).Should().Be(canonical);
	}

	[Fact]
	public void ToCanonical_WithPublicGroupPartition_PreservesOffersAndRoundTripsMembershipIdentity()
	{
		var roleLockIn = CreatePartitionedRoleLockIn(
			MainRoleType.PrejudicedManipulator,
			MainRoleType.Cupid);
		var partition = CanonicalPublicGroupPartition.Create(
			playerCount: 5,
			[3, 1],
			[5, 2, 4]);
		var changedMembership = CanonicalPublicGroupPartition.Create(
			playerCount: 5,
			[1, 2],
			[3, 4, 5]);

		var canonical = new SimulationScenario(
			roleLockIn,
			publicGroupPartition: partition).ToCanonical();
		var different = new SimulationScenario(
			roleLockIn,
			publicGroupPartition: changedMembership).ToCanonical();

		canonical.PublicGroupPartition.Should().Be(partition);
		canonical.RoleComposition.GetCount(MainRoleType.PrejudicedManipulator)
			.Should().Be(0);
		canonical.Offer1Role.Should().Be(MainRoleType.PrejudicedManipulator);
		canonical.Offer2Role.Should().Be(MainRoleType.Cupid);
		canonical.ToString().Should().Be(
			"players=5|roles=[SimpleVillager=3,SimpleWerewolf=1,Thief=1]|offers=[PrejudicedManipulator,Cupid]|thief=[Offer1,Offer2,Decline]|partition=[[1,3],[2,4,5]]|actor=[]|rules=[]");
		CanonicalSimulationScenario.Parse(canonical.ToString()).Should().Be(canonical);
		different.Should().NotBe(canonical);
	}

	[Fact]
	public void ToCanonical_WithArtifactsAndNonDefaultRuleState_SnapshotsAndRoundTripsAllInputs()
	{
		var roleCards = new List<MainRoleType>
		{
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.WildChild
		};
		var actorCards = new List<MainRoleType>
		{
			MainRoleType.Seer,
			MainRoleType.Cupid,
			MainRoleType.Cupid
		};
		var scenario = new SimulationScenario(
			5,
			roleCards,
			new ActorSetupCards(actorCards),
			new SimulationRuleState(NewMoonEnabled: true));

		roleCards.Clear();
		actorCards.Clear();
		var canonical = scenario.ToCanonical();
		var parsed = CanonicalSimulationScenario.Parse(canonical.ToString());

		canonical.PlayerCount.Should().Be(5);
		canonical.RoleComposition.CardCount.Should().Be(5);
		canonical.ActorSetupCards.Should().Equal(
			MainRoleType.Cupid,
			MainRoleType.Cupid,
			MainRoleType.Seer);
		canonical.RuleState.NewMoonEnabled.Should().BeTrue();
		canonical.ToString().Should().Be(
			"players=5|roles=[Seer=1,SimpleVillager=2,SimpleWerewolf=1,WildChild=1]|actor=[Cupid,Cupid,Seer]|rules=[NewMoonEnabled]");
		parsed.Should().Be(canonical);
	}

	[Fact]
	public void ToCanonical_WithEquivalentOrderAndBehaviorDifferences_UsesBehavioralIdentity()
	{
		var roles = new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};
		var baseline = new SimulationScenario(5, roles).ToCanonical();
		var reordered = new SimulationScenario(5, roles.Reverse()).ToCanonical();
		var differentPlayers = new SimulationScenario(6, roles).ToCanonical();
		var differentArtifact = new SimulationScenario(
			5,
			roles,
			new ActorSetupCards([MainRoleType.Seer])).ToCanonical();
		var differentRuleState = new SimulationScenario(
			5,
			roles,
			ruleState: new SimulationRuleState(NewMoonEnabled: true)).ToCanonical();

		reordered.Should().Be(baseline);
		differentPlayers.Should().NotBe(baseline);
		differentArtifact.Should().NotBe(baseline);
		differentRuleState.Should().NotBe(baseline);
	}

	[Fact]
	public void Equals_WithEquivalentInputCollectionOrder_UsesCanonicalValueSemantics()
	{
		var roles = new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};
		var actorCards = new[]
		{
			MainRoleType.Cupid,
			MainRoleType.Defender,
			MainRoleType.Cupid
		};
		var first = new SimulationScenario(
			5,
			roles,
			new ActorSetupCards(actorCards));
		var second = new SimulationScenario(
			5,
			roles.Reverse(),
			new ActorSetupCards(actorCards.Reverse()));

		second.Should().Be(first);
		second.GetHashCode().Should().Be(first.GetHashCode());
	}

	[Theory]
	[InlineData("players=05|roles=[Seer=1]|actor=[]|rules=[]")]
	[InlineData("players=5|roles=[Seer=1]|actor=[Seer,Cupid]|rules=[]")]
	[InlineData("players=5|roles=[Seer=1]|actor=[1]|rules=[]")]
	[InlineData("players=5|roles=[Seer=1]|actor=[UnknownRole]|rules=[]")]
	[InlineData("players=5|roles=[Seer=1]|actor=[]|rules=[newMoonEnabled]")]
	[InlineData("players=5|roles=[Seer=1]|actor=[]")]
	[InlineData("players=5|roles=[SimpleVillager=3,SimpleWerewolf=1,Thief=1]|offers=[Seer,Cupid]|actor=[]|rules=[]")]
	public void TryParse_WithMalformedOrNoncanonicalValue_ReturnsFalse(string value)
	{
		var parsed = CanonicalSimulationScenario.TryParse(value, out var scenario);

		parsed.Should().BeFalse();
		scenario.Should().BeNull();
	}

	private static RoleLockIn CreatePartitionedRoleLockIn(
		MainRoleType offer1Role = MainRoleType.Seer,
		MainRoleType offer2Role = MainRoleType.Cupid)
	{
		var cards = new[]
		{
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Thief),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), offer1Role),
			new PhysicalCharacterCard(Guid.NewGuid(), offer2Role)
		};
		return new RoleLockIn(
			version: 7,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Take(5).Select(card => card.Id),
			offer1CardId: cards[5].Id,
			offer2CardId: cards[6].Id);
	}
}
