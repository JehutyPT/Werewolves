using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public class SimulationRoleLockInTests
{
	[Fact]
	public void Derive_WithFixedOffers_AssignsOnlyDealPoolAndRetainsOrderedOffers()
	{
		MainRoleType[] dealPool =
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(
			playerCount: 5,
			roleCompositionCards: dealPool.Concat(
				[MainRoleType.Cupid, MainRoleType.Defender]),
			dealPoolCards: dealPool,
			offer1Role: MainRoleType.Cupid,
			offer2Role: MainRoleType.Defender);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.FullProbability.Identity),
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 31);

		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.FullProbability);

		CanonicalRoleComposition.Create(
				startState.RoleAssignments.Select(assignment => assignment.Role))
			.Should().Be(CanonicalRoleComposition.Create(dealPool));
		startState.CanonicalScenario.Offer1Role.Should().Be(MainRoleType.Cupid);
		startState.CanonicalScenario.Offer2Role.Should().Be(MainRoleType.Defender);
		startState.RoleAssignments.Should().NotContain(
			assignment => assignment.Role == MainRoleType.Cupid ||
				assignment.Role == MainRoleType.Defender);
	}

	[Fact]
	public void AlreadyDecidedProjection_WithOffers_ClassifiesOnlyDealPool()
	{
		MainRoleType[] dealPool =
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(
			playerCount: 5,
			roleCompositionCards: dealPool.Concat(
				[MainRoleType.SimpleVillager, MainRoleType.SimpleVillager]),
			dealPoolCards: dealPool,
			offer1Role: MainRoleType.SimpleVillager,
			offer2Role: MainRoleType.SimpleVillager);

		var result = AlreadyDecidedRoleCompositionClassifier.Classify(
			scenario.ToCanonical().RoleComposition,
			SimulatorCapability.FullProbability);

		result.IsAlreadyDecided.Should().BeTrue();
		result.Reason.Should().Be(AlreadyDecidedReason.WerewolfControlShortcut);
	}

	[Fact]
	public void CreateGameSessionConfig_WithThiefPartition_RetainsFullInventoryAndOrderedOffers()
	{
		MainRoleType[] dealPool =
		[
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(
			5,
			dealPool.Concat([MainRoleType.Seer, MainRoleType.Cupid]),
			dealPool,
			MainRoleType.Seer,
			MainRoleType.Cupid);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.FullProbability.Identity);
		var assignments = dealPool
			.Select((role, index) => new SimulationPlayerRoleAssignment(index + 1, role))
			.ToArray();
		var factionFacts = assignments.Select(assignment =>
			new SimulationPlayerFactionFacts(
				assignment.SeatNumber,
				FactionBeneficiaryKnowledge.Known(
					assignment.Role == MainRoleType.SimpleWerewolf
						? Faction.Werewolf
						: Faction.Villager),
				Enum.GetValues<Faction>().ToDictionary(
					faction => faction,
					faction => assignment.Role == MainRoleType.SimpleWerewolf &&
						faction == Faction.Werewolf
						? FactionAgentKnowledge.KnownAgent
						: FactionAgentKnowledge.KnownNonAgent)))
			.ToArray();
		var startState = new SimulationStartState(identity, assignments, factionFacts);

		var config = startState.CreateGameSessionConfig();

		CanonicalRoleComposition.Create(config.Roles).Should().Be(
			CanonicalRoleComposition.Create(dealPool.Concat(
				[MainRoleType.Seer, MainRoleType.Cupid])));
		config.RoleLockIn.DealPool.Select(card => card.PrintedRole)
			.Should().BeEquivalentTo(dealPool);
		config.RoleLockIn.Offer1!.PrintedRole.Should().Be(MainRoleType.Seer);
		config.RoleLockIn.Offer2!.PrintedRole.Should().Be(MainRoleType.Cupid);
	}
}
