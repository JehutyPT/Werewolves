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
				SimulatorCapability.FullProbability.Identity),
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 7);

		var serialized = material.ToString();
		var parsed = RunSeedMaterial.Parse(serialized);

		serialized.Should().Be(
			"profile=full-probability@4|players=5|roles=[Seer=1,SimpleVillager=3,SimpleWerewolf=1]|actor=[]|rules=[]|strategy=baseline-random@3-splitmix64|run=7");
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
			SimulatorCapability.FullProbability.Identity);
		var material = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 19);

		var first = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.FullProbability);
		var replay = SimulationStartStateDeriver.Derive(
			RunSeedMaterial.Parse(material.ToString()),
			SimulatorCapability.FullProbability);

		first.PlayerCount.Should().Be(5);
		first.CanonicalScenario.Should().Be(scenario.ToCanonical());
		first.CompatibilityIdentity.Should().Be(identity);
		first.Should().Be(replay);
		first.GetHashCode().Should().Be(replay.GetHashCode());
		first.RoleAssignments.Should().Equal(replay.RoleAssignments);
		first.FactionFacts.Should().Equal(replay.FactionFacts);
		first.RoleAssignments.Select(assignment => assignment.SeatNumber)
			.Should().Equal(1, 2, 3, 4, 5);
		first.FactionFacts.Select(facts => facts.SeatNumber)
			.Should().Equal(1, 2, 3, 4, 5);
		first.RoleAssignments.Select(assignment => assignment.Role)
			.Should().BeEquivalentTo(scenario.RoleCompositionCards);
		first.FactionFacts.Should().OnlyContain(facts =>
			facts.Beneficiary.IsKnown &&
			Enum.GetValues<Faction>().All(faction =>
				facts.GetAgentKnowledge(faction) !=
					FactionAgentKnowledge.Unknown));

		var factsBySeat = first.FactionFacts.ToDictionary(facts => facts.SeatNumber);
		foreach (var assignment in first.RoleAssignments)
		{
			var facts = factsBySeat[assignment.SeatNumber];
			var isWerewolf = assignment.Role == MainRoleType.SimpleWerewolf;
			facts.Beneficiary.Faction.Should().Be(
				isWerewolf ? Faction.Werewolf : Faction.Villager);
			facts.GetAgentKnowledge(Faction.Werewolf).Should().Be(
				isWerewolf
					? FactionAgentKnowledge.KnownAgent
					: FactionAgentKnowledge.KnownNonAgent);
			facts.GetAgentKnowledge(Faction.Villager).Should().Be(
				FactionAgentKnowledge.KnownNonAgent);
		}
	}

	[Fact]
	public void Derive_WithWhiteWerewolf_SeparatesBeneficiaryFromOperationalAgent()
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
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity),
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 17);

		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening);
		var whiteWerewolfSeat = startState.RoleAssignments
			.Single(assignment => assignment.Role == MainRoleType.WhiteWerewolf)
			.SeatNumber;
		var facts = startState.FactionFacts
			.Single(candidate => candidate.SeatNumber == whiteWerewolfSeat);

		facts.Beneficiary.IsKnown.Should().BeTrue();
		facts.Beneficiary.Faction.Should().Be(Faction.WhiteWerewolf);
		facts.GetAgentKnowledge(Faction.Werewolf).Should().Be(
			FactionAgentKnowledge.KnownAgent);
		facts.GetAgentKnowledge(Faction.WhiteWerewolf).Should().Be(
			FactionAgentKnowledge.KnownNonAgent);
	}

	[Fact]
	public void SimulationPlayerFactionFacts_RequiresCompleteKnownFactsAndSnapshotsAgents()
	{
		var agents = Enum.GetValues<Faction>().ToDictionary(
			faction => faction,
			_ => FactionAgentKnowledge.KnownNonAgent);
		var facts = new SimulationPlayerFactionFacts(
			seatNumber: 1,
			FactionBeneficiaryKnowledge.Known(Faction.Villager),
			agents);

		agents[Faction.Werewolf] = FactionAgentKnowledge.Unknown;

		facts.GetAgentKnowledge(Faction.Werewolf).Should().Be(
			FactionAgentKnowledge.KnownNonAgent);
		Action unknownBeneficiary = () => new SimulationPlayerFactionFacts(
			1,
			FactionBeneficiaryKnowledge.Unknown,
			Enum.GetValues<Faction>().ToDictionary(
				faction => faction,
				_ => FactionAgentKnowledge.KnownNonAgent));
		Action incompleteAgents = () => new SimulationPlayerFactionFacts(
			1,
			FactionBeneficiaryKnowledge.Known(Faction.Villager),
			new Dictionary<Faction, FactionAgentKnowledge>
			{
				[Faction.Werewolf] = FactionAgentKnowledge.KnownNonAgent
			});
		Action unknownAgent = () => new SimulationPlayerFactionFacts(
			1,
			FactionBeneficiaryKnowledge.Known(Faction.Villager),
			Enum.GetValues<Faction>().ToDictionary(
				faction => faction,
				_ => FactionAgentKnowledge.Unknown));

		unknownBeneficiary.Should().Throw<ArgumentException>()
			.WithParameterName("beneficiary");
		incompleteAgents.Should().Throw<ArgumentException>()
			.WithParameterName("agents");
		unknownAgent.Should().Throw<ArgumentException>()
			.WithParameterName("agents");
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
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
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
			SimulatorCapability.FullProbability.Identity);
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
					SimulatorCapability.FullProbability.Identity),
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
					SimulatorCapability.FullProbability.Identity),
				BaselineRandomDecisionStrategy.Identity,
				7),
			new RunSeedMaterial(
				new SimulationCompatibilityIdentity(
					new SimulationScenario(
						5,
						scenario.RoleCompositionCards,
						new ActorSetupCards(
							[MainRoleType.Cupid, MainRoleType.Defender, MainRoleType.Elder])).ToCanonical(),
					SimulatorCapability.FullProbability.Identity),
				BaselineRandomDecisionStrategy.Identity,
				7),
			new RunSeedMaterial(
				new SimulationCompatibilityIdentity(
					new SimulationScenario(
						5,
						scenario.RoleCompositionCards,
						ruleState: new SimulationRuleState(NewMoonEnabled: true)).ToCanonical(),
					SimulatorCapability.FullProbability.Identity),
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
					new SimulatorProfileIdentity("full-probability", "3")),
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
			.Should().Be(15_958_056_341_016_561_059UL);
		materials.Select(DeterministicRandomSource.DeriveNumericSeed)
			.Should().OnlyHaveUniqueItems();
	}
}
