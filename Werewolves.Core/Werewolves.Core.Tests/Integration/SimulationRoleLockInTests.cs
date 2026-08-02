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
	public void CreateGameSessionConfig_WithOffersButNoAssignedThief_RejectsRatherThanDroppingOffers()
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

		var act = () => startState.CreateGameSessionConfig();

		act.Should().Throw<InvalidOperationException>();
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
	public void CreateGameSessionConfig_WithCanonicalPublicGroupPartition_MapsSeatsToRunRosterAndRetainsSamePrintedOffers()
	{
		MainRoleType[] dealPool =
		[
			MainRoleType.Thief,
			MainRoleType.PrejudicedManipulator,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var canonicalPartition = CanonicalPublicGroupPartition.Create(
			5,
			[1, 3],
			[2, 4, 5]);
		var scenario = new SimulationScenario(
			5,
			dealPool.Concat([MainRoleType.Seer, MainRoleType.Seer]),
			dealPool,
			MainRoleType.Seer,
			MainRoleType.Seer,
			publicGroupPartition: canonicalPartition);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
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
			CanonicalRoleComposition.Create(dealPool));
		CanonicalRoleComposition.Create(config.RoleLockIn.RoleComposition
			.Select(card => card.PrintedRole)).Should().Be(
			CanonicalRoleComposition.Create(dealPool.Concat(
				[MainRoleType.Seer, MainRoleType.Seer])));
		config.RoleLockIn.DealPool.Select(card => card.PrintedRole)
			.Should().BeEquivalentTo(dealPool);
		config.RoleLockIn.Offer1!.PrintedRole.Should().Be(MainRoleType.Seer);
		config.RoleLockIn.Offer2!.PrintedRole.Should().Be(MainRoleType.Seer);
		config.RoleLockIn.Offer1.Id.Should().NotBe(config.RoleLockIn.Offer2.Id);
		config.PlayerRoster.Select(player => player.Name).Should().Equal(
			Enumerable.Range(1, 5).Select(seatNumber =>
				$"Simulation Player {seatNumber}"));
		config.PlayerRoster.Select(player => player.Id)
			.Should().NotContain(Guid.Empty)
			.And.OnlyHaveUniqueItems();
		config.PublicGroupPartition.Should().NotBeNull();
		config.PublicGroupPartition!.FirstGroupPlayerIds.Should().BeEquivalentTo(
			canonicalPartition.FirstGroupSeatNumbers.Select(seatNumber =>
				config.PlayerRoster[seatNumber - 1].Id));
		config.PublicGroupPartition.SecondGroupPlayerIds.Should().BeEquivalentTo(
			canonicalPartition.SecondGroupSeatNumbers.Select(seatNumber =>
				config.PlayerRoster[seatNumber - 1].Id));
	}
}
