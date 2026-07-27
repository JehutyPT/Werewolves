using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class SimulationScenarioClassifierTests
{
	[Fact]
	public void Classify_UsesTheExplicitlySelectedCapability()
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
		var safety = new SimulatorCapability(
			new SimulatorProfileIdentity("test-safety", "1"),
			[
				new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
				new(MainRoleType.WildChild, Faction.Villager),
				new(MainRoleType.SimpleVillager, Faction.Villager)
			]);
		var probability = new SimulatorCapability(
			new SimulatorProfileIdentity("test-probability", "1"),
			[
				new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
				new(MainRoleType.SimpleVillager, Faction.Villager)
			]);
		_ = new SimulatorCapabilityRegistry(safety, probability);

		var safetyClassification = SimulationScenarioClassifier.Classify(scenario, safety);
		var probabilityClassification = SimulationScenarioClassifier.Classify(scenario, probability);

		safetyClassification.SimulatorSupport.Should().Match<SimulatorSupportResult>(
			result => result.IsSupported && result.Capability == safety);
		probabilityClassification.SimulatorSupport.Should().Match<SimulatorSupportResult>(
			result => !result.IsSupported
				&& result.Capability == probability);
		probabilityClassification.SimulatorSupport!.UnsupportedRoles.Should()
			.Equal(MainRoleType.WildChild);
	}

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

	[Fact]
	public void Classify_VillagerVillager_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.VillagerVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.SimulatorSupport.Should().Match<SimulatorSupportResult>(
			result =>
				result.IsSupported &&
				result.Capability == SimulatorCapability.SafetyScreening);
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(new SimulatorProfileIdentity("safety-screening", "2"));
		probability.SimulatorSupport.Should().Match<SimulatorSupportResult>(
			result =>
				!result.IsSupported &&
				result.Capability == SimulatorCapability.FullProbability);
		probability.SimulatorSupport!.UnsupportedRoles.Should()
			.Equal(MainRoleType.VillagerVillager);
		probability.Cacheability.Should().BeNull();
	}
}
