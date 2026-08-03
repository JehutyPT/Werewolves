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
	public void Derive_WithSafetyActorScenario_ProducesVillagerNonAgentFactsAndRematerializesCanonicalSetup()
	{
		MainRoleType[] roles =
		[
			MainRoleType.Actor,
			MainRoleType.BigBadWolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var setupArtifact = new ActorSetupCards(
			version: 8,
			[
				new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Seer),
				new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Cupid),
				new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Elder)
			]);
		var scenario = new SimulationScenario(5, roles, setupArtifact);
		var capability = SimulatorCapabilityRegistry.Production.SafetyScreening;
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				capability.Identity),
			capability.HeadlessResponsePolicy.StrategyIdentity,
			runNumber: 144);

		var startState = SimulationStartStateDeriver.Derive(material, capability);
		var actorAssignment = startState.RoleAssignments.Single(assignment =>
			assignment.Role == MainRoleType.Actor);
		var actorFacts = startState.FactionFacts.Single(facts =>
			facts.SeatNumber == actorAssignment.SeatNumber);

		var config = startState.CreateGameSessionConfig();

		actorFacts.Beneficiary.Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Villager));
		foreach (var faction in Enum.GetValues<Faction>())
		{
			actorFacts.GetAgentKnowledge(faction).Should().Be(
				FactionAgentKnowledge.KnownNonAgent);
		}
		MainRoleType[] canonicalSetup =
		[
			MainRoleType.Cupid,
			MainRoleType.Elder,
			MainRoleType.Seer
		];
		startState.CanonicalScenario.ActorSetupCards.Should().Equal(canonicalSetup);
		setupArtifact.Version.Should().Be(8);
		config.ActorSetupCards.Version.Should().Be(1);
		config.ActorSetupCards.PrintedRoles.Should().Equal(canonicalSetup);
		config.ActorSetupCards.Cards.Select(card => card.PrintedRole).Should().Equal(
			canonicalSetup);
		config.ActorSetupCards.Cards.Select(card => card.Id)
			.Should().NotContain(Guid.Empty)
			.And.OnlyHaveUniqueItems();
		config.ActorSetupCards.Cards.Select(card => card.Id)
			.Should().NotIntersectWith(
				setupArtifact.Cards.Select(card => card.Id));
	}

	[Fact]
	public void CreateGameSessionConfig_WithActorReachableOnlyInOffer_PropagatesSetupAlongsideTheFullRoleLockIn()
	{
		MainRoleType[] dealPool =
		[
			MainRoleType.Thief,
			MainRoleType.BigBadWolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(
			playerCount: 5,
			roleCompositionCards: dealPool.Concat(
				[MainRoleType.Actor, MainRoleType.Seer]),
			dealPoolCards: dealPool,
			offer1Role: MainRoleType.Actor,
			offer2Role: MainRoleType.Seer,
			new ActorSetupCards(
				[MainRoleType.Cupid, MainRoleType.Elder, MainRoleType.Fox]));
		var startState = CreateStartState(scenario, dealPool);

		var config = startState.CreateGameSessionConfig();

		config.RoleLockIn.Offer1!.PrintedRole.Should().Be(MainRoleType.Actor);
		config.RoleLockIn.Offer2!.PrintedRole.Should().Be(MainRoleType.Seer);
		config.ActorSetupCards.PrintedRoles.Should().Equal(
			scenario.ToCanonical().ActorSetupCards);
		config.ActorSetupCards.Cards.Select(card => card.Id)
			.Should().NotContain(Guid.Empty)
			.And.OnlyHaveUniqueItems();
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

	private static SimulationStartState CreateStartState(
		SimulationScenario scenario,
		IReadOnlyList<MainRoleType> assignedRoles)
	{
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var assignments = assignedRoles
			.Select((role, index) =>
				new SimulationPlayerRoleAssignment(index + 1, role))
			.ToArray();
		var factionFacts = assignments.Select(assignment =>
		{
			var isWerewolf = assignment.Role is MainRoleType.SimpleWerewolf
				or MainRoleType.BigBadWolf
				or MainRoleType.AccursedWolfFather
				or MainRoleType.WhiteWerewolf;
			return new SimulationPlayerFactionFacts(
				assignment.SeatNumber,
				FactionBeneficiaryKnowledge.Known(
					isWerewolf ? Faction.Werewolf : Faction.Villager),
				Enum.GetValues<Faction>().ToDictionary(
					faction => faction,
					faction => isWerewolf && faction == Faction.Werewolf
						? FactionAgentKnowledge.KnownAgent
						: FactionAgentKnowledge.KnownNonAgent));
		}).ToArray();

		return new SimulationStartState(identity, assignments, factionFacts);
	}
}
