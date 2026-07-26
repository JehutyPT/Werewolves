using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class RunSeedMaterialTests
{
	[Fact]
	public void Parse_WithCanonicalCompleteMaterial_RoundTripsStructuralValue()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorProfile.LegacyCore.Identity),
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 7);

		var serialized = material.ToString();
		var parsed = RunSeedMaterial.Parse(serialized);

		serialized.Should().Be(
			"profile=core-simulator@1|players=5|roles=[Seer=1,SimpleVillager=3,SimpleWerewolf=1]|actor=[]|rules=[]|strategy=baseline-random@1-splitmix64|run=7");
		parsed.Should().Be(material);
		parsed.CompatibilityIdentity.Should().Be(material.CompatibilityIdentity);
		parsed.DecisionStrategyIdentity.Should().Be(material.DecisionStrategyIdentity);
		parsed.RunNumber.Should().Be(7);
	}

	[Fact]
	public void Derive_WithSameRunSeedMaterial_PreservesScenarioCardsAndReplayAssignment()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.WildChild,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorProfile.LegacyCore.Identity);
		var material = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 19);

		var first = SimulationStartStateDeriver.Derive(material);
		var replay = SimulationStartStateDeriver.Derive(RunSeedMaterial.Parse(material.ToString()));

		first.PlayerCount.Should().Be(5);
		first.CanonicalScenario.Should().Be(scenario.ToCanonical());
		first.CompatibilityIdentity.Should().Be(identity);
		first.RoleAssignments.Should().Equal(replay.RoleAssignments);
		first.RoleAssignments.Select(assignment => assignment.SeatNumber)
			.Should().Equal(1, 2, 3, 4, 5);
		first.RoleAssignments.Select(assignment => assignment.Role)
			.Should().BeEquivalentTo(scenario.RoleCompositionCards);
	}

	[Fact]
	public void Derive_RequiresTheCapabilityNamedByRunSeedMaterial()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity),
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 3);

		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening);
		var mismatch = () => SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.FullProbability);

		startState.CompatibilityIdentity.Profile.Should().Be(
			SimulatorCapability.SafetyScreening.Identity);
		mismatch.Should().Throw<ArgumentException>().WithParameterName("capability");
	}

	[Fact]
	public void DeriveNumericSeed_WithCompleteUtf8Material_UsesStableBoundaryAndChangesWithEveryPart()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorProfile.LegacyCore.Identity);
		var material = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 7);
		var materials = new[]
		{
			material,
			new RunSeedMaterial(
				new SimulationCompatibilityIdentity(
					new SimulationScenario(
						6,
						[
							MainRoleType.SimpleWerewolf,
							MainRoleType.Seer,
							MainRoleType.SimpleVillager,
							MainRoleType.SimpleVillager,
							MainRoleType.SimpleVillager,
							MainRoleType.SimpleVillager
						]).ToCanonical(),
					SimulatorProfile.LegacyCore.Identity),
				BaselineRandomDecisionStrategy.Identity,
				7),
			new RunSeedMaterial(
				new SimulationCompatibilityIdentity(
					new SimulationScenario(
						5,
						[
							MainRoleType.SimpleWerewolf,
							MainRoleType.WildChild,
							MainRoleType.SimpleVillager,
							MainRoleType.SimpleVillager,
							MainRoleType.SimpleVillager
						]).ToCanonical(),
					SimulatorProfile.LegacyCore.Identity),
				BaselineRandomDecisionStrategy.Identity,
				7),
			new RunSeedMaterial(
				new SimulationCompatibilityIdentity(
					new SimulationScenario(
						5,
						scenario.RoleCompositionCards,
						new ActorSetupCards(
							[MainRoleType.Cupid, MainRoleType.Defender, MainRoleType.Elder])).ToCanonical(),
					SimulatorProfile.LegacyCore.Identity),
				BaselineRandomDecisionStrategy.Identity,
				7),
			new RunSeedMaterial(
				new SimulationCompatibilityIdentity(
					new SimulationScenario(
						5,
						scenario.RoleCompositionCards,
						ruleState: new SimulationRuleState(NewMoonEnabled: true)).ToCanonical(),
					SimulatorProfile.LegacyCore.Identity),
				BaselineRandomDecisionStrategy.Identity,
				7),
			new RunSeedMaterial(
				new SimulationCompatibilityIdentity(
					scenario.ToCanonical(),
					new SimulatorProfileIdentity("alternate-simulator", "1")),
				BaselineRandomDecisionStrategy.Identity,
				7),
			new RunSeedMaterial(
				new SimulationCompatibilityIdentity(
					scenario.ToCanonical(),
					new SimulatorProfileIdentity("core-simulator", "2")),
				BaselineRandomDecisionStrategy.Identity,
				7),
			new RunSeedMaterial(
				identity,
				new DecisionStrategyIdentity("alternate-random", "1-splitmix64"),
				7),
			new RunSeedMaterial(
				identity,
				new DecisionStrategyIdentity("baseline-random", "2-splitmix64"),
				7),
			new RunSeedMaterial(identity, BaselineRandomDecisionStrategy.Identity, 8)
		};

		DeterministicRandomSource.DeriveNumericSeed(material)
			.Should().Be(6_703_387_641_252_472_950UL);
		materials.Select(DeterministicRandomSource.DeriveNumericSeed)
			.Should().OnlyHaveUniqueItems();
	}
}
