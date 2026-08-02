using FluentAssertions;
using FluentAssertions.Execution;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class SimulatorProfileTests
{
	[Fact]
	public void SafetyScreeningCapability_UsesIssue140Identity()
	{
		SimulatorCapability.SafetyScreening.Identity.Should().Be(
			new SimulatorProfileIdentity("safety-screening", "28"));
	}

	[Fact]
	public void SafetyScreeningCapability_AdmitsPrejudicedManipulatorWithoutExpandingFullProbability()
	{
		var safety = SimulatorCapability.SafetyScreening;
		var probability = SimulatorCapability.FullProbability;
		var hasBeneficiary = safety.TryGetBeneficiaryFaction(
			MainRoleType.PrejudicedManipulator,
			out var beneficiary);

		using (new AssertionScope())
		{
			safety.SupportsRole(MainRoleType.PrejudicedManipulator).Should().BeTrue();
			hasBeneficiary.Should().BeTrue();
			beneficiary.Should().Be(Faction.PrejudicedManipulator);
			safety.SharedVictoryCapabilities.Should().Contain(
				new SharedVictoryGameResult(
					[Faction.Angel, Faction.PrejudicedManipulator]));
			safety.SharedVictoryCapabilities.Should().Contain(
				new SharedVictoryGameResult(
					[Faction.Piper, Faction.PrejudicedManipulator]));
			safety.SharedVictoryCapabilities.Should().Contain(
				new SharedVictoryGameResult(
					[
						Faction.Angel,
						Faction.Piper,
						Faction.PrejudicedManipulator
					]));
			probability.SupportsRole(MainRoleType.PrejudicedManipulator)
				.Should().BeFalse();
			safety.HeadlessResponsePolicy.StrategyIdentity.Should().Be(
				new DecisionStrategyIdentity("baseline-random", "13-splitmix64"));
			probability.Identity.Should().Be(
				new SimulatorProfileIdentity("full-probability", "4"));
			probability.HeadlessResponsePolicy.StrategyIdentity.Should().Be(
				new DecisionStrategyIdentity("baseline-random", "3-splitmix64"));
		}
	}

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
	    ModeratorInstructionSemantic.AnnounceVillageIdiotPardon,
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
			.Append(ModeratorInstructionSemantic.SelectBigBadWolfTarget)
			.Append(ModeratorInstructionSemantic.SelectDefenderTarget)
			.Append(ModeratorInstructionSemantic.SelectWhiteWerewolfTarget)
			.Append(ModeratorInstructionSemantic.SelectPiperTargets)
			.Append(ModeratorInstructionSemantic.RecognizeCharmedPlayers)
			.Append(ModeratorInstructionSemantic.AnnounceBearTamerGrowl)
			.Append(ModeratorInstructionSemantic.SelectFoxCenter)
			.Append(ModeratorInstructionSemantic.RevealFoxResult)
			.Append(ModeratorInstructionSemantic.SelectCupidLovers)
			.Append(ModeratorInstructionSemantic.RecognizeLovers)
			.Append(ModeratorInstructionSemantic.ChooseThiefOffer)
			.Append(ModeratorInstructionSemantic.ResolveDevotedServantVoteWindow)
			.Append(ModeratorInstructionSemantic.RecordDevotedServantAcquiredCard)
			.Append(ModeratorInstructionSemantic
				.AnnounceVillagerRolePowerSuppression);
		var safety = SimulatorCapability.SafetyScreening;
		var probability = SimulatorCapability.FullProbability;

		safety.Identity.Should().Be(new SimulatorProfileIdentity("safety-screening", "28"));
		probability.Identity.Should().Be(new SimulatorProfileIdentity("full-probability", "4"));
		BaselineRandomDecisionStrategy.Identity.Should()
			.Be(new DecisionStrategyIdentity("baseline-random", "3-splitmix64"));
		BaselineRandomDecisionStrategy.SafetyScreeningIdentity.Should()
			.Be(new DecisionStrategyIdentity("baseline-random", "13-splitmix64"));
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
			MainRoleType.LittleGirl,
			MainRoleType.Defender,
			MainRoleType.Elder,
			MainRoleType.StutteringJudge,
			MainRoleType.Scapegoat,
			MainRoleType.VillageIdiot,
			MainRoleType.WolfHound,
			MainRoleType.AccursedWolfFather,
			MainRoleType.WhiteWerewolf,
			MainRoleType.Piper,
			MainRoleType.BearTamer,
			MainRoleType.Fox,
			MainRoleType.KnightWithRustySword,
			MainRoleType.Cupid,
			MainRoleType.Thief,
			MainRoleType.DevotedServant,
			MainRoleType.Angel,
			MainRoleType.PrejudicedManipulator);
		probability.SupportedRoles.Should().Equal(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager);
		safety.SupportsRole(MainRoleType.DevotedServant).Should().BeTrue();
		probability.SupportsRole(MainRoleType.DevotedServant).Should().BeFalse();
		probability.SupportedRoles.Should().NotBeSameAs(safety.SupportedRoles);
		safety.TryGetBeneficiaryFaction(
				MainRoleType.Angel,
				out var angelBeneficiary)
			.Should().BeTrue();
		angelBeneficiary.Should().Be(Faction.Villager);
		foreach (var faction in Enum.GetValues<Faction>())
		{
			safety.IsFactionAgent(MainRoleType.Angel, faction).Should().BeFalse();
		}
		safety.SharedVictoryCapabilities.Should().BeEquivalentTo(
		[
			new SharedVictoryGameResult([Faction.Angel, Faction.Villager]),
			new SharedVictoryGameResult([Faction.Angel, Faction.Werewolf]),
			new SharedVictoryGameResult([Faction.Angel, Faction.WhiteWerewolf]),
			new SharedVictoryGameResult([Faction.Angel, Faction.Piper]),
			new SharedVictoryGameResult([Faction.Angel, Faction.CrossFactionLovers]),
			new SharedVictoryGameResult(
				[Faction.Angel, Faction.PrejudicedManipulator]),
			new SharedVictoryGameResult(
				[Faction.Piper, Faction.PrejudicedManipulator]),
			new SharedVictoryGameResult(
				[
					Faction.Angel,
					Faction.Piper,
					Faction.PrejudicedManipulator
				])
		]);
		probability.SharedVictoryCapabilities.Should().BeEmpty();
		safety.TryGetBeneficiaryFaction(
				MainRoleType.KnightWithRustySword,
				out var knightBeneficiary)
			.Should().BeTrue();
		knightBeneficiary.Should().Be(Faction.Villager);
		safety.IsFactionAgent(
				MainRoleType.KnightWithRustySword,
				Faction.Werewolf)
			.Should().BeFalse();
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
		safety.TryGetBeneficiaryFaction(
				MainRoleType.WhiteWerewolf,
				out var whiteWerewolfBeneficiary)
			.Should().BeTrue();
		whiteWerewolfBeneficiary.Should().Be(Faction.WhiteWerewolf);
		safety.IsFactionAgent(
				MainRoleType.WhiteWerewolf,
				Faction.Werewolf)
			.Should().BeTrue();
		safety.IsFactionAgent(
				MainRoleType.WhiteWerewolf,
				Faction.WhiteWerewolf)
			.Should().BeFalse();
		safety.TryGetBeneficiaryFaction(
				MainRoleType.Piper,
				out var piperBeneficiary)
			.Should().BeTrue();
		piperBeneficiary.Should().Be(Faction.Piper);
		safety.IsFactionAgent(
				MainRoleType.Piper,
				Faction.Piper)
			.Should().BeFalse();
		safety.TryGetBeneficiaryFaction(
				MainRoleType.BearTamer,
				out var bearTamerBeneficiary)
			.Should().BeTrue();
		bearTamerBeneficiary.Should().Be(Faction.Villager);
		safety.IsFactionAgent(
				MainRoleType.BearTamer,
				Faction.Werewolf)
			.Should().BeFalse();
		safety.TryGetBeneficiaryFaction(
				MainRoleType.Fox,
				out var foxBeneficiary)
			.Should().BeTrue();
		foxBeneficiary.Should().Be(Faction.Villager);
		safety.IsFactionAgent(
				MainRoleType.Fox,
				Faction.Werewolf)
			.Should().BeFalse();
		safety.TryGetBeneficiaryFaction(
				MainRoleType.Cupid,
				out var cupidBeneficiary)
			.Should().BeTrue();
		cupidBeneficiary.Should().Be(Faction.Villager);
		safety.IsFactionAgent(
				MainRoleType.Cupid,
				Faction.Werewolf)
			.Should().BeFalse();
	    safety.TryGetBeneficiaryFaction(
	            MainRoleType.LittleGirl,
	            out var littleGirlBeneficiary)
	        .Should().BeTrue();
	    littleGirlBeneficiary.Should().Be(Faction.Villager);
	    safety.IsFactionAgent(
	            MainRoleType.LittleGirl,
	            Faction.Werewolf)
	        .Should().BeFalse();
		safety.TryGetBeneficiaryFaction(
				MainRoleType.Defender,
				out var defenderBeneficiary)
			.Should().BeTrue();
		defenderBeneficiary.Should().Be(Faction.Villager);
		safety.IsFactionAgent(
				MainRoleType.Defender,
				Faction.Werewolf)
			.Should().BeFalse();
		safety.TryGetBeneficiaryFaction(
				MainRoleType.Elder,
				out var elderBeneficiary)
			.Should().BeTrue();
		elderBeneficiary.Should().Be(Faction.Villager);
		safety.IsFactionAgent(
				MainRoleType.Elder,
				Faction.Werewolf)
			.Should().BeFalse();
		safety.TryGetBeneficiaryFaction(
				MainRoleType.VillageIdiot,
				out var villageIdiotBeneficiary)
			.Should().BeTrue();
		villageIdiotBeneficiary.Should().Be(Faction.Villager);
		safety.IsFactionAgent(
				MainRoleType.VillageIdiot,
				Faction.Werewolf)
			.Should().BeFalse();
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
			new SimulatorProfileIdentity("full-probability", "3"));

		var serialized = current.ToString();
		var parsed = SimulationCompatibilityIdentity.Parse(serialized);

		serialized.Should().Be(
			"profile=full-probability@4|players=5|roles=[Seer=1,SimpleVillager=2,SimpleWerewolf=1,WildChild=1]|actor=[]|rules=[]");
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

	[Fact]
	public void PossibleGameResultInventory_WithCupid_IncludesDynamicLoversOutcome()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.Cupid,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		PossibleGameResultInventory.TryCreate(
				scenario,
				SimulatorCapability.SafetyScreening,
				out var inventory)
			.Should().BeTrue();

		inventory.Factions.Should().Equal(
			Faction.Villager,
			Faction.Werewolf,
			Faction.CrossFactionLovers);
		inventory.GameResults.Should().Contain(
			new SingleFactionGameResult(Faction.CrossFactionLovers));
	}

	[Fact]
	public void PossibleGameResultInventory_WithOfferedCupid_IncludesReachableLoversOutcome()
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
			5,
			dealPool.Concat([MainRoleType.Cupid, MainRoleType.Defender]),
			dealPool,
			MainRoleType.Cupid,
			MainRoleType.Defender);

		PossibleGameResultInventory.TryCreate(
				scenario,
				SimulatorCapability.SafetyScreening,
				out var inventory)
			.Should().BeTrue();

		inventory.Factions.Should().Contain(Faction.CrossFactionLovers);
		inventory.GameResults.Should().Contain(
			new SingleFactionGameResult(Faction.CrossFactionLovers));
	}

	[Fact]
	public void PossibleGameResultInventory_WithAngel_IncludesApplicableAngelOutcomes()
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

		PossibleGameResultInventory.TryCreate(
				scenario,
				SimulatorCapability.SafetyScreening,
				out var inventory)
			.Should().BeTrue();

		inventory.Factions.Should().Contain(
			[
				Faction.Villager,
				Faction.Werewolf,
				Faction.Angel
			]);
		inventory.GameResults.Should().Contain(
			new SingleFactionGameResult(Faction.Angel));
		inventory.GameResults.Should().Contain(
			new SharedVictoryGameResult([Faction.Angel, Faction.Villager]));
		inventory.GameResults.Should().Contain(
			new SharedVictoryGameResult([Faction.Angel, Faction.Werewolf]));
		inventory.GameResults.Should().NotContain(
			new SharedVictoryGameResult([Faction.Angel, Faction.Piper]));
	}

	[Fact]
	public void PossibleGameResultInventory_WithOfferedAngel_IncludesReachableAngelOutcomes()
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
			dealPool.Concat([MainRoleType.Angel, MainRoleType.Seer]),
			dealPool,
			MainRoleType.Angel,
			MainRoleType.Seer);

		PossibleGameResultInventory.TryCreate(
				scenario,
				SimulatorCapability.SafetyScreening,
				out var inventory)
			.Should().BeTrue();

		inventory.Factions.Should().Contain(Faction.Angel);
		inventory.GameResults.Should().Contain(
			new SingleFactionGameResult(Faction.Angel));
		inventory.GameResults.Should().Contain(
			new SharedVictoryGameResult([Faction.Angel, Faction.Villager]));
		inventory.GameResults.Should().Contain(
			new SharedVictoryGameResult([Faction.Angel, Faction.Werewolf]));
	}

}
