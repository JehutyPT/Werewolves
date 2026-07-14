using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class SimulatorProfileTests
{
	[Fact]
	public void Active_ExposesStableIdentityAndExactlyFrozenFirstDeliveryRoles()
	{
		var profile = SimulatorProfile.Active;

		profile.Identity.Should().Be(new SimulatorProfileIdentity("core-simulator", "1"));
		profile.SupportedRoles.Should().Equal(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager);
		profile.SupportedRoles.Should().OnlyContain(role => profile.SupportsRole(role));
		profile.SupportsActorSetupCards.Should().BeFalse();
		profile.SupportsRuleState(SimulationRuleState.Default).Should().BeTrue();
		profile.SupportsRuleState(new SimulationRuleState(NewMoonEnabled: true)).Should().BeFalse();
	}

	[Fact]
	public void CompatibilityIdentity_RoundTripsAndDistinguishesProfileIdAndVersion()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.WildChild,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]).ToCanonical();
		var current = new SimulationCompatibilityIdentity(
			scenario,
			SimulatorProfile.Active.Identity);
		var differentProfile = new SimulationCompatibilityIdentity(
			scenario,
			new SimulatorProfileIdentity("alternate-simulator", "1"));
		var differentVersion = new SimulationCompatibilityIdentity(
			scenario,
			new SimulatorProfileIdentity("core-simulator", "2"));

		var serialized = current.ToString();
		var parsed = SimulationCompatibilityIdentity.Parse(serialized);

		serialized.Should().Be(
			"profile=core-simulator@1|players=5|roles=[Seer=1,SimpleVillager=2,SimpleWerewolf=1,WildChild=1]|actor=[]|rules=[]");
		parsed.Should().Be(current);
		differentProfile.Should().NotBe(current);
		differentVersion.Should().NotBe(current);
	}
}
