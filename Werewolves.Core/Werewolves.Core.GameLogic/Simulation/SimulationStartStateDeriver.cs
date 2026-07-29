using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public static class SimulationStartStateDeriver
{
	internal static SimulationStartState Derive(RunSeedMaterial material) =>
		Derive(material, new DeterministicRandomSource(material));

	public static SimulationStartState Derive(
		RunSeedMaterial material,
		SimulatorCapability capability)
	{
		ArgumentNullException.ThrowIfNull(material);
		ArgumentNullException.ThrowIfNull(capability);
		EnsureMaterialMatchesCapability(material, capability);

		return Derive(
			material,
			capability,
			new DeterministicRandomSource(material));
	}

	internal static SimulationStartState Derive(
		RunSeedMaterial material,
		DeterministicRandomSource random)
	{
		ArgumentNullException.ThrowIfNull(material);
		var capability = ResolveCurrentCapability(material);
		EnsureMaterialMatchesCapability(material, capability);
		return Derive(material, capability, random);
	}

	internal static SimulationStartState Derive(
		RunSeedMaterial material,
		SimulatorCapability capability,
		DeterministicRandomSource random)
	{
		ArgumentNullException.ThrowIfNull(material);
		ArgumentNullException.ThrowIfNull(capability);
		ArgumentNullException.ThrowIfNull(random);
		if (!material.DecisionStrategyIdentity.Equals(
				BaselineRandomDecisionStrategy.Identity) &&
		    !material.DecisionStrategyIdentity.Equals(
			    BaselineRandomDecisionStrategy.SafetyScreeningIdentity))
		{
			throw new ArgumentException(
				"Run Seed Material does not identify the active baseline decision strategy.",
				nameof(material));
		}
		if (!random.Material.Equals(material))
		{
			throw new ArgumentException(
				"The deterministic random source must be derived from the same Run Seed Material.",
				nameof(random));
		}

		var scenario = material.CompatibilityIdentity.Scenario;
		var roles = scenario.RoleComposition.Entries
			.SelectMany(entry => Enumerable.Repeat(entry.Role, entry.Count))
			.ToList();
		if (roles.Count != scenario.PlayerCount)
		{
			throw new InvalidOperationException(
				"The active simulator profile requires exactly one Role Composition card per Player.");
		}

		random.Shuffle(roles);
		var assignments = roles
			.Select((role, index) => new SimulationPlayerRoleAssignment(index + 1, role))
			.ToArray();
		var factionFacts = assignments
			.Select(assignment => CreateFactionFacts(assignment, capability))
			.ToArray();

		return new SimulationStartState(
			material.CompatibilityIdentity,
			assignments,
			factionFacts);
	}

	private static SimulationPlayerFactionFacts CreateFactionFacts(
		SimulationPlayerRoleAssignment assignment,
		SimulatorCapability capability)
	{
		if (!capability.TryGetBeneficiaryFaction(
			    assignment.Role,
			    out var beneficiaryFaction))
		{
			throw new InvalidOperationException(
				"The active Simulator Capability does not declare complete Faction facts.");
		}

		return new SimulationPlayerFactionFacts(
			assignment.SeatNumber,
			FactionBeneficiaryKnowledge.Known(beneficiaryFaction),
			Enum.GetValues<Faction>().ToDictionary(
				faction => faction,
				faction => capability.IsFactionAgent(assignment.Role, faction)
					? FactionAgentKnowledge.KnownAgent
					: FactionAgentKnowledge.KnownNonAgent));
	}

	private static SimulatorCapability ResolveCurrentCapability(
		RunSeedMaterial material)
	{
		var profile = material.CompatibilityIdentity.Profile;
		var registry = SimulatorCapabilityRegistry.Production;
		if (profile.Equals(registry.SafetyScreening.Identity))
		{
			return registry.SafetyScreening;
		}
		if (profile.Equals(registry.FullProbability.Identity))
		{
			return registry.FullProbability;
		}

		throw new ArgumentException(
			"Run Seed Material does not identify a current Simulator Capability.",
			nameof(material));
	}

	private static void EnsureMaterialMatchesCapability(
		RunSeedMaterial material,
		SimulatorCapability capability)
	{
		if (!material.CompatibilityIdentity.Profile.Equals(capability.Identity))
		{
			throw new ArgumentException(
				"Run Seed Material does not identify the selected Simulator Capability.",
				nameof(capability));
		}
		if (!material.DecisionStrategyIdentity.Equals(
			    capability.HeadlessResponsePolicy.StrategyIdentity))
		{
			throw new ArgumentException(
				"Run Seed Material does not identify the selected Simulator Capability response policy.",
				nameof(material));
		}
	}

	internal static GameSessionConfig CreateGameSessionConfig(this SimulationStartState startState)
	{
		ArgumentNullException.ThrowIfNull(startState);
		return new GameSessionConfig(
			Enumerable.Range(1, startState.PlayerCount)
				.Select(seatNumber => $"Simulation Player {seatNumber}")
				.ToList(),
			startState.RoleAssignments.Select(assignment => assignment.Role).ToList());
	}
}
