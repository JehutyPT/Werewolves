using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class CanonicalSimulationScenarioTests
{
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
	public void TryParse_WithMalformedOrNoncanonicalValue_ReturnsFalse(string value)
	{
		var parsed = CanonicalSimulationScenario.TryParse(value, out var scenario);

		parsed.Should().BeFalse();
		scenario.Should().BeNull();
	}
}
