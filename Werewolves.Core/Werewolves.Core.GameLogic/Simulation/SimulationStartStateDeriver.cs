using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public static class SimulationStartStateDeriver
{
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
		SimulatorCapability capability,
		DeterministicRandomSource random)
	{
		ArgumentNullException.ThrowIfNull(material);
		ArgumentNullException.ThrowIfNull(capability);
		ArgumentNullException.ThrowIfNull(random);
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
				"The selected Simulator Capability requires exactly one Deal Pool card per Player.");
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

		var scenario = SimulationScenario.FromCanonical(
			material.CompatibilityIdentity.Scenario);
		if (SimulationScenarioClassifier.ClassifyAdmission(
				scenario,
				capability,
				material.CompatibilityIdentity)
			!= SimulationScenarioAdmission.Admitted)
		{
			throw new ArgumentException(
				"Run Seed Material does not identify a scenario supported by the selected Simulator Capability.",
				nameof(material));
		}
	}

	internal static GameSessionConfig CreateGameSessionConfig(this SimulationStartState startState)
	{
		ArgumentNullException.ThrowIfNull(startState);
		var playerRoster = Enumerable.Range(1, startState.PlayerCount)
			.Select(seatNumber => new GameSessionPlayerConfig(
				Guid.NewGuid(),
				$"Simulation Player {seatNumber}"))
			.ToArray();
		var publicGroupPartition =
			startState.CanonicalScenario.PublicGroupPartition is { } canonicalPartition
				? PublicGroupPartition.Create(
					playerRoster.Select(player => player.Id),
					canonicalPartition.FirstGroupSeatNumbers.Select(seatNumber =>
						playerRoster[seatNumber - 1].Id),
					canonicalPartition.SecondGroupSeatNumbers.Select(seatNumber =>
						playerRoster[seatNumber - 1].Id))
				: null;
		var assignedRoles = startState.RoleAssignments
			.Select(assignment => assignment.Role)
			.ToList();
		var actorSetupCards = startState.CanonicalScenario.ActorSetupCards.Count == 0
			? ActorSetupCards.None
			: ActorSetupCards.CreateFromPrintedRoles(
				version: 1,
				startState.CanonicalScenario.ActorSetupCards);
		if (startState.CanonicalScenario.Offer1Role is not { } offer1Role ||
			startState.CanonicalScenario.Offer2Role is not { } offer2Role)
		{
			return new GameSessionConfig(
				playerRoster,
				assignedRoles,
				actorSetupCards,
				publicGroupPartition: publicGroupPartition);
		}
		if (!assignedRoles.Contains(MainRoleType.Thief))
		{
			throw new InvalidOperationException(
				"An offer-bearing Simulation Start State requires an assigned Thief.");
		}

		var dealPoolCards = startState.RoleAssignments
			.Select(assignment => new PhysicalCharacterCard(Guid.NewGuid(), assignment.Role))
			.ToArray();
		var offer1 = new PhysicalCharacterCard(Guid.NewGuid(), offer1Role);
		var offer2 = new PhysicalCharacterCard(Guid.NewGuid(), offer2Role);
		return new GameSessionConfig(
			playerRoster,
			new RoleLockIn(
				version: 1,
				playerCount: startState.PlayerCount,
				roleComposition: dealPoolCards.Concat([offer1, offer2]),
				dealPoolCardIds: dealPoolCards.Select(card => card.Id),
				offer1CardId: offer1.Id,
				offer2CardId: offer2.Id),
			actorSetupCards,
			publicGroupPartition: publicGroupPartition);
	}
}
