using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class SimulationScenarioClassifierTests
{
	[Fact]
	public void Classify_WithAppSupportedButProfileUnsupportedRole_StopsBeforeAlreadyDecidedAndPreservesInput()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.WildChild,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var profile = new SimulatorProfile(
			new SimulatorProfileIdentity("restricted-simulator", "1"),
			[
				new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
				new(MainRoleType.Seer, Faction.Villager),
				new(MainRoleType.SimpleVillager, Faction.Villager)
			]);

		var classification = SimulationScenarioClassifier.Classify(scenario, profile);

		classification.AppSupport!.IsSupported.Should().BeTrue();
		classification.SimulatorSupport!.IsSupported.Should().BeFalse();
		classification.SimulatorSupport.Scenario.Should().BeSameAs(scenario);
		classification.SimulatorSupport.UnsupportedRoles.Should().Equal(MainRoleType.WildChild);
		classification.AlreadyDecided.Should().BeNull();
		classification.Cacheability.Should().BeNull();
	}
}
