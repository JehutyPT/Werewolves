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
		ModeratorInstructionSemantic[] expectedLegacyAndProbabilitySemantics =
		[
			ModeratorInstructionSemantic.StartGame,
			ModeratorInstructionSemantic.FinishedGame,
			ModeratorInstructionSemantic.StartNight,
			ModeratorInstructionSemantic.FinishNightActions,
			ModeratorInstructionSemantic.WakeRole,
			ModeratorInstructionSemantic.IdentifyRoleHolders,
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
		var expectedSafetySemantics = expectedLegacyAndProbabilitySemantics
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
			.Append(ModeratorInstructionSemantic.ObserveStutteringJudgeSignal);
		var legacy = SimulatorProfile.LegacyCore;
		var safety = SimulatorCapability.SafetyScreening;
		var probability = SimulatorCapability.FullProbability;

		safety.Identity.Should().Be(new SimulatorProfileIdentity("safety-screening", "7"));
		probability.Identity.Should().Be(new SimulatorProfileIdentity("full-probability", "1"));
		legacy.Identity.Should().Be(new SimulatorProfileIdentity("core-simulator", "1"));
		BaselineRandomDecisionStrategy.Identity.Should()
			.Be(new DecisionStrategyIdentity("baseline-random", "1-splitmix64"));
		safety.SupportedRoles.Should().Equal(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager,
			MainRoleType.VillagerVillager,
			MainRoleType.TwoSisters,
			MainRoleType.ThreeBrothers,
			MainRoleType.Witch,
			MainRoleType.Hunter,
			MainRoleType.StutteringJudge);
		probability.SupportedRoles.Should().Equal(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager);
		probability.SupportedRoles.Should().NotBeSameAs(safety.SupportedRoles);
		safety.SupportsActorSetupCards.Should().BeFalse();
		probability.SupportsActorSetupCards.Should().BeFalse();
		safety.SupportsRuleState(SimulationRuleState.Default).Should().BeTrue();
		probability.SupportsRuleState(SimulationRuleState.Default).Should().BeTrue();
		probability.SupportedRuleStates.Should().NotBeSameAs(safety.SupportedRuleStates);
		safety.HeadlessResponsePolicy.Should().NotBeSameAs(probability.HeadlessResponsePolicy);
		legacy.HeadlessResponsePolicy.Should().NotBeSameAs(safety.HeadlessResponsePolicy);
		legacy.HeadlessResponsePolicy.Should().NotBeSameAs(probability.HeadlessResponsePolicy);
		legacy.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.NotBeSameAs(BaselineRandomDecisionStrategy.Policy.AdmittedSemantics);
		safety.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.NotBeSameAs(BaselineRandomDecisionStrategy.Policy.AdmittedSemantics);
		probability.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.NotBeSameAs(BaselineRandomDecisionStrategy.Policy.AdmittedSemantics);
		legacy.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.NotBeSameAs(safety.HeadlessResponsePolicy.AdmittedSemantics);
		legacy.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.NotBeSameAs(probability.HeadlessResponsePolicy.AdmittedSemantics);
		safety.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.NotBeSameAs(probability.HeadlessResponsePolicy.AdmittedSemantics);
		safety.HeadlessResponsePolicy.StrategyIdentity.Should().Be(BaselineRandomDecisionStrategy.Identity);
		probability.HeadlessResponsePolicy.StrategyIdentity.Should().Be(BaselineRandomDecisionStrategy.Identity);
		legacy.HeadlessResponsePolicy.StrategyIdentity.Should().Be(BaselineRandomDecisionStrategy.Identity);
		legacy.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.BeEquivalentTo(expectedLegacyAndProbabilitySemantics);
		safety.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.BeEquivalentTo(expectedSafetySemantics);
		probability.HeadlessResponsePolicy.AdmittedSemantics.Should()
			.BeEquivalentTo(expectedLegacyAndProbabilitySemantics);
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
	public void CompatibilitySemantics_CompareEveryExecutionAndOutcomeDeclaration()
	{
		var baseline = CompatibilityProfile();
		var equivalent = CompatibilityProfile(
			roleDescriptors:
			[
				new(MainRoleType.SimpleVillager, Faction.Villager),
				new(MainRoleType.SimpleWerewolf, Faction.Werewolf)
			],
			supportedRuleStates:
			[
				new SimulationRuleState(NewMoonEnabled: true),
				SimulationRuleState.Default
			],
			admittedSemantics:
			[
				ModeratorInstructionSemantic.FinishedGame,
				ModeratorInstructionSemantic.StartGame
			]);
		var changedBeneficiary = CompatibilityProfile(
			roleDescriptors:
			[
				new(MainRoleType.SimpleWerewolf, Faction.Villager),
				new(MainRoleType.SimpleVillager, Faction.Villager)
			]);
		var changedActorSetup = CompatibilityProfile(supportsActorSetupCards: true);
		var changedRuleStates = CompatibilityProfile(
			supportedRuleStates: [SimulationRuleState.Default]);
		var changedSharedVictory = CompatibilityProfile(
			sharedVictoryCapabilities:
			[
				new SharedVictoryGameResult([Faction.Villager, Faction.Werewolf])
			]);
		var changedStrategy = CompatibilityProfile(
			strategyIdentity: new DecisionStrategyIdentity("other-strategy", "1"));
		var changedSemantics = CompatibilityProfile(
			admittedSemantics: [ModeratorInstructionSemantic.StartGame]);

		baseline.HasSameCompatibilitySemanticsAs(equivalent).Should().BeTrue();
		baseline.HasSameCompatibilitySemanticsAs(changedBeneficiary).Should().BeFalse();
		baseline.HasSameCompatibilitySemanticsAs(changedActorSetup).Should().BeFalse();
		baseline.HasSameCompatibilitySemanticsAs(changedRuleStates).Should().BeFalse();
		baseline.HasSameCompatibilitySemanticsAs(changedSharedVictory).Should().BeFalse();
		baseline.HasSameCompatibilitySemanticsAs(changedStrategy).Should().BeFalse();
		baseline.HasSameCompatibilitySemanticsAs(changedSemantics).Should().BeFalse();
	}

	[Fact]
	public void LegacyCore_ExposesStableIdentityAndExactlyFrozenFirstDeliveryRoles()
	{
		var profile = SimulatorProfile.LegacyCore;

		profile.Identity.Should().Be(new SimulatorProfileIdentity("core-simulator", "1"));
		profile.SupportedRoles.Should().Equal(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager);
		profile.SupportedRoles.Should().OnlyContain(role => profile.SupportsRole(role));
		profile.SupportsActorSetupCards.Should().BeFalse();
		profile.SupportsRuleState(SimulationRuleState.Default).Should().BeTrue();
		profile.SupportsRuleState(new SimulationRuleState(NewMoonEnabled: true)).Should().BeFalse();
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
			SimulatorProfile.LegacyCore.Identity);
		var differentProfile = new SimulationCompatibilityIdentity(
			scenario,
			new SimulatorProfileIdentity("alternate-simulator", "1"));
		var differentVersion = new SimulationCompatibilityIdentity(
			scenario,
			new SimulatorProfileIdentity("core-simulator", "2"));

		var serialized = current.ToString();
		var parsed = SimulationCompatibilityIdentity.Parse(serialized);

		serialized.Should().Be(
			"profile=core-simulator@1|players=5|roles=[Seer=1,SimpleVillager=2,SimpleWerewolf=1,WildChild=1]|actor=[]|rules=[]");
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
		SimulatorProfile.LegacyCore.SharedVictoryCapabilities.Should().BeEmpty();
	}

	private static SimulatorProfile CompatibilityProfile(
		IEnumerable<SimulatorProfileRoleDescriptor>? roleDescriptors = null,
		IEnumerable<SharedVictoryGameResult>? sharedVictoryCapabilities = null,
		bool supportsActorSetupCards = false,
		IEnumerable<SimulationRuleState>? supportedRuleStates = null,
		DecisionStrategyIdentity? strategyIdentity = null,
		IEnumerable<ModeratorInstructionSemantic>? admittedSemantics = null) =>
		new(
			new SimulatorProfileIdentity("compatibility-test", "1"),
			roleDescriptors ??
			[
				new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
				new(MainRoleType.SimpleVillager, Faction.Villager)
			],
			sharedVictoryCapabilities ?? [],
			new HeadlessResponsePolicy(
				strategyIdentity ?? BaselineRandomDecisionStrategy.Identity,
				admittedSemantics ??
				[
					ModeratorInstructionSemantic.StartGame,
					ModeratorInstructionSemantic.FinishedGame
				]),
			supportsActorSetupCards,
			supportedRuleStates ??
			[
				SimulationRuleState.Default,
				new SimulationRuleState(NewMoonEnabled: true)
			]);
}
