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
	public void Classify_TwoSisters_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.TwoSisters,
				MainRoleType.TwoSisters,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.TwoSisters);
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
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport.Should().Match<SimulatorSupportResult>(
			result =>
				!result.IsSupported &&
				result.Capability == SimulatorCapability.FullProbability);
		probability.SimulatorSupport!.UnsupportedRoles.Should()
			.Equal(MainRoleType.VillagerVillager);
		probability.Cacheability.Should().BeNull();
	}

	[Fact]
	public void Classify_ThreeBrothers_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			6,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.ThreeBrothers,
				MainRoleType.ThreeBrothers,
				MainRoleType.ThreeBrothers,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.ThreeBrothers);
	}

	[Fact]
	public void Classify_Witch_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.Witch);
	}

	[Fact]
	public void Classify_Hunter_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.Hunter);
	}

	[Fact]
	public void Classify_StutteringJudge_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.StutteringJudge);
	}

	[Fact]
	public void Classify_Scapegoat_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.Scapegoat);
	}

	[Fact]
	public void Classify_WolfHound_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.WolfHound,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.WolfHound);
	}

	[Fact]
	public void Classify_AccursedWolfFather_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.AccursedWolfFather);
	}

	[Fact]
	public void Classify_BigBadWolf_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.BigBadWolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.BigBadWolf);
	}

	[Fact]
	public void Classify_LittleGirl_IsSafetyScreeningOnly()
	{
	    var scenario = new SimulationScenario(
	        5,
	        [
	            MainRoleType.SimpleWerewolf,
	            MainRoleType.LittleGirl,
	            MainRoleType.SimpleVillager,
	            MainRoleType.SimpleVillager,
	            MainRoleType.SimpleVillager
	        ]);

	    var safety = SimulationScenarioClassifier.Classify(
	        scenario,
	        SimulatorCapability.SafetyScreening);
	    var probability = SimulationScenarioClassifier.Classify(
	        scenario,
	        SimulatorCapability.FullProbability);

	    safety.AppSupport!.IsSupported.Should().BeTrue();
	    safety.SimulatorSupport!.IsSupported.Should().BeTrue();
	    safety.Cacheability!.CompatibilityIdentity.Profile.Should()
	        .Be(SimulatorCapability.SafetyScreening.Identity);
	    probability.SimulatorSupport!.IsSupported.Should().BeFalse();
	    probability.SimulatorSupport.UnsupportedRoles.Should()
	        .Equal(MainRoleType.LittleGirl);
	}

	[Fact]
	public void Classify_Defender_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Defender,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.Defender);
	}

	[Fact]
	public void Classify_WhiteWerewolf_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.WhiteWerewolf);
		probability.Cacheability.Should().BeNull();
	}

	[Fact]
	public void Classify_Angel_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.Angel,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.AppSupport!.IsSupported.Should().BeTrue();
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.Angel);
		probability.Cacheability.Should().BeNull();
	}

	[Fact]
	public void Classify_KnightWithRustySword_IsSafetyScreeningOnly()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.KnightWithRustySword,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		safety.AppSupport!.IsSupported.Should().BeTrue();
		safety.SimulatorSupport!.IsSupported.Should().BeTrue();
		safety.Cacheability!.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorSupport!.IsSupported.Should().BeFalse();
		probability.SimulatorSupport.UnsupportedRoles.Should()
			.Equal(MainRoleType.KnightWithRustySword);
		probability.Cacheability.Should().BeNull();
	}
}
