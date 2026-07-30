using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class SimulatorProfileTests
{
	[Fact]
	public void ProductionCapabilities_ExposeIndependentFrozenDeclarations()
	{
		ModeratorInstructionSemantic[] expectedProbabilitySemantics =
		[
			ModeratorInstructionSemantic.StartGame,
			ModeratorInstructionSemantic.FinishedGame,
			ModeratorInstructionSemantic.StartNight,
			ModeratorInstructionSemantic.FinishNightActions,
			ModeratorInstructionSemantic.WakeRole,
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup,
			ModeratorInstructionSemantic.PutRoleToSleep,
			ModeratorInstructionSemantic.SelectWerewolfVictim,
			ModeratorInstructionSemantic.SelectSeerTarget,
			ModeratorInstructionSemantic.RevealSeerResult,
			ModeratorInstructionSemantic.SelectWildChildModel,
			ModeratorInstructionSemantic.AnnounceDawnVictims,
			ModeratorInstructionSemantic.AssignDawnVictimRoles,
			ModeratorInstructionSemantic.StartDayDebate,
			ModeratorInstructionSemantic.RecordDayVote,
			ModeratorInstructionSemantic.AssignDayVoteTargetRole,
			ModeratorInstructionSemantic.AnnounceLynchingImmunity,
			ModeratorInstructionSemantic.AnnounceDayElimination
		];
		var expectedSafetySemantics = expectedProbabilitySemantics
			.Append(ModeratorInstructionSemantic.ConductDayVote)
			.Append(ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal)
			.Append(ModeratorInstructionSemantic.RecognizeRoleHolders)
			.Append(ModeratorInstructionSemantic.CommunicateAsRoleHolders)
			.Append(ModeratorInstructionSemantic.SelectWitchHealingTarget)
			.Append(ModeratorInstructionSemantic.SelectWitchPoisonTarget)
			.Append(ModeratorInstructionSemantic.AnnounceEliminationCascadeVictims)
			.Append(ModeratorInstructionSemantic.AssignEliminationCascadeRoles)
			.Append(ModeratorInstructionSemantic.SelectHunterFinalShotTarget)
			.Append(ModeratorInstructionSemantic.EstablishStutteringJudgeSignal)
			.Append(ModeratorInstructionSemantic.ObserveStutteringJudgeSignal)
			.Append(ModeratorInstructionSemantic.ObserveScapegoatHolderForTie)
			.Append(ModeratorInstructionSemantic.RevealScapegoatForTie)
			.Append(ModeratorInstructionSemantic.SelectScapegoatPermittedVoters)
			.Append(ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters)
			.Append(ModeratorInstructionSemantic.ChooseWolfHoundAlignment)
			.Append(ModeratorInstructionSemantic
				.ChooseAccursedWolfFatherInfection)
			.Append(ModeratorInstructionSemantic.SelectBigBadWolfTarget);
		var safety = SimulatorCapability.SafetyScreening;
		var probability = SimulatorCapability.FullProbability;

		safety.Identity.Should().Be(new SimulatorProfileIdentity("safety-screening", "13"));
		probability.Identity.Should().Be(new SimulatorProfileIdentity("full-probability", "3"));
		BaselineRandomDecisionStrategy.Identity.Should()
			.Be(new DecisionStrategyIdentity("baseline-random", "3-splitmix64"));
		BaselineRandomDecisionStrategy.SafetyScreeningIdentity.Should()
			.Be(new DecisionStrategyIdentity("baseline-random", "7-splitmix64"));
		safety.SupportedRoles.Should().Equal(
			MainRoleType.SimpleWerewolf,
			MainRoleType.BigBadWolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager,
			MainRoleType.VillagerVillager,
			MainRoleType.TwoSisters,
			MainRoleType.ThreeBrothers,
			MainRoleType.Witch,
			MainRoleType.Hunter,
			MainRoleType.StutteringJudge,
			MainRoleType.Scapegoat,
			MainRoleType.WolfHound,
			MainRoleType.AccursedWolfFather);
		probability.SupportedRoles.Should().Equal(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager);
		probability.SupportedRoles.Should().NotBeSameAs(safety.SupportedRoles);
		safety.TryGetBeneficiaryFaction(
				MainRoleType.AccursedWolfFather,
				out var accursedBeneficiary)
			.Should().BeTrue();
		accursedBeneficiary.Should().Be(Faction.Werewolf);
		safety.IsFactionAgent(
				MainRoleType.AccursedWolfFather,
				Faction.Werewolf)
			.Should().BeTrue();
		safety.TryGetBeneficiaryFaction(
				MainRoleType.BigBadWolf,
				out var bigBadWolfBeneficiary)
			.Should().BeTrue();
		bigBadWolfBeneficiary.Should().Be(Faction.Werewolf);
		safety.IsFactionAgent(
				MainRoleType.BigBadWolf,
				Faction.Werewolf)
			.Should().BeTrue();
		safety.SupportsActorSetupCards.Should().BeFalse();
		probability.SupportsActorSetupCards.Should().BeFalse();
		safety.SupportsRuleState(SimulationRuleState.Default).Should().BeTrue();
		probability.SupportsRuleState(SimulationRuleState.Default).Should().BeTrue();
		probability.SupportedRuleStates.Should().NotBeSameAs(safety.SupportedRuleStates);
		safety.HeadlessResponsePolicy.Should().NotBeSameAs(probability.HeadlessResponsePolicy);
		safety.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.NotBeSameAs(BaselineRandomDecisionStrategy.Policy.AdmittedSemantics);
		probability.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.NotBeSameAs(BaselineRandomDecisionStrategy.Policy.AdmittedSemantics);
		safety.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.NotBeSameAs(probability.HeadlessResponsePolicy.AdmittedSemantics);
		safety.HeadlessResponsePolicy.StrategyIdentity.Should()
			.Be(BaselineRandomDecisionStrategy.SafetyScreeningIdentity);
		probability.HeadlessResponsePolicy.StrategyIdentity.Should().Be(BaselineRandomDecisionStrategy.Identity);
		safety.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.BeEquivalentTo(expectedSafetySemantics);
		probability.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.BeEquivalentTo(expectedProbabilitySemantics);
		SimulatorCapabilityRegistry.Production.SafetyScreening.Should().BeSameAs(safety);
		SimulatorCapabilityRegistry.Production.FullProbability.Should().BeSameAs(probability);
	}

	[Fact]
	public void Registry_RejectsFullProbabilityRolesOutsideSafetyScreening()
	{
		var safety = new SimulatorCapability(
			new SimulatorProfileIdentity("test-safety", "1"),
			[
				new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
				new(MainRoleType.SimpleVillager, Faction.Villager)
			]);
		var probability = new SimulatorCapability(
			new SimulatorProfileIdentity("test-probability", "1"),
			[
				new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
				new(MainRoleType.Seer, Faction.Villager),
				new(MainRoleType.SimpleVillager, Faction.Villager)
			]);

		var act = () => new SimulatorCapabilityRegistry(safety, probability);

		act.Should().Throw<ArgumentException>()
			.WithParameterName("fullProbability");
	}

	[Fact]
	public void CompatibilityIdentity_RoundTripsAndDistinguishesProfileIdAndVersion()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.WildChild,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]).ToCanonical();
		var current = new SimulationCompatibilityIdentity(
			scenario,
			SimulatorCapability.FullProbability.Identity);
		var differentProfile = new SimulationCompatibilityIdentity(
			scenario,
			new SimulatorProfileIdentity("alternate-simulator", "1"));
		var differentVersion = new SimulationCompatibilityIdentity(
			scenario,
			new SimulatorProfileIdentity("full-probability", "4"));

		var serialized = current.ToString();
		var parsed = SimulationCompatibilityIdentity.Parse(serialized);

		serialized.Should().Be(
			"profile=full-probability@3|players=5|roles=[Seer=1,SimpleVillager=2,SimpleWerewolf=1,WildChild=1]|actor=[]|rules=[]");
		parsed.Should().Be(current);
		differentProfile.Should().NotBe(current);
		differentVersion.Should().NotBe(current);
	}

	[Fact]
	public void PossibleGameResults_UsesOnlyDeclaredApplicableSharedVictoryCapabilities()
	{
		var shared = new SharedVictoryGameResult([Faction.Villager, Faction.Werewolf]);
		var profile = new SimulatorProfile(
			new SimulatorProfileIdentity("shared-capable", "1"),
			[
				new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
				new(MainRoleType.SimpleVillager, Faction.Villager)
			],
			[shared]);

		profile.CreatePossibleGameResults([Faction.Villager, Faction.Werewolf]).Should().Equal(
			new SingleFactionGameResult(Faction.Villager),
			new SingleFactionGameResult(Faction.Werewolf),
			shared,
			new NoWinnerGameResult());
		profile.CreatePossibleGameResults([Faction.Villager]).Should().Equal(
			new SingleFactionGameResult(Faction.Villager),
			new NoWinnerGameResult());
		SimulatorCapability.FullProbability.SharedVictoryCapabilities.Should().BeEmpty();
	}

}
