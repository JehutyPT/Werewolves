using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class AlreadyDecidedRoleCompositionClassifierTests
{
	[Fact]
	public void Map_WithCapabilitySupportedRoles_ReturnsFactionBeneficiaryEvidence()
	{
		var composition = CanonicalRoleComposition.Create(
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager
		]);

		var evidence = SimulatorFactionBeneficiaryBridge.Map(
			composition,
			SimulatorCapability.FullProbability);

		evidence.GetBeneficiaryCount(Faction.Werewolf).Should().Be(1);
		evidence.GetBeneficiaryCount(Faction.Villager).Should().Be(3);
	}

	[Fact]
	public void Map_WithUnsupportedRole_RejectsInsteadOfInferringLegacyFaction()
	{
		var composition = CanonicalRoleComposition.Create([MainRoleType.BigBadWolf]);

		var action = () => SimulatorFactionBeneficiaryBridge.Map(
			composition,
			SimulatorCapability.FullProbability);

		action.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Map_WithCapabilityOwnedBeneficiaryFact_ReturnsFactionBeneficiaryEvidence()
	{
		var capability = new SimulatorCapability(
			new SimulatorProfileIdentity("beneficiary-test", "1"),
			[(MainRoleType.Seer, Faction.Villager, [])]);
		var composition = CanonicalRoleComposition.Create([MainRoleType.Seer]);

		var evidence = SimulatorFactionBeneficiaryBridge.Map(composition, capability);

		evidence.GetBeneficiaryCount(Faction.Villager).Should().Be(1);
		evidence.GetBeneficiaryCount(Faction.Werewolf).Should().Be(0);
	}

	[Fact]
	public void Classify_WithResolvedWhiteBeneficiaries_AppliesTheSharedThreeFactionRules()
	{
		var capability = new SimulatorCapability(
			new SimulatorProfileIdentity("three-faction-test", "1"),
			[
				(MainRoleType.SimpleVillager, Faction.Villager, []),
				(MainRoleType.SimpleWerewolf, Faction.Werewolf, []),
				(MainRoleType.WhiteWerewolf, Faction.WhiteWerewolf, [])
			]);

		var soleWhite = AlreadyDecidedRoleCompositionClassifier.Classify(
			CanonicalRoleComposition.Create([MainRoleType.WhiteWerewolf]),
			capability);
		var werewolfControlBlocked = AlreadyDecidedRoleCompositionClassifier.Classify(
			CanonicalRoleComposition.Create(
				[
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleVillager,
					MainRoleType.WhiteWerewolf
				]),
			capability);
		var werewolfEliminationBlocked = AlreadyDecidedRoleCompositionClassifier.Classify(
			CanonicalRoleComposition.Create(
				[
					MainRoleType.SimpleWerewolf,
					MainRoleType.WhiteWerewolf
				]),
			capability);
		var villagerVictoryBlocked = AlreadyDecidedRoleCompositionClassifier.Classify(
			CanonicalRoleComposition.Create(
				[
					MainRoleType.SimpleVillager,
					MainRoleType.WhiteWerewolf
				]),
			capability);
		var soleWerewolf = AlreadyDecidedRoleCompositionClassifier.Classify(
			CanonicalRoleComposition.Create([MainRoleType.SimpleWerewolf]),
			capability);
		var soleVillager = AlreadyDecidedRoleCompositionClassifier.Classify(
			CanonicalRoleComposition.Create([MainRoleType.SimpleVillager]),
			capability);

		soleWhite.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.WhiteWerewolf));
		soleWhite.Reason.Should().Be(
			AlreadyDecidedReason.WhiteWerewolfSoleSurvivor);
		werewolfControlBlocked.GameResult.Should().BeNull();
		werewolfEliminationBlocked.GameResult.Should().BeNull();
		villagerVictoryBlocked.GameResult.Should().BeNull();
		soleWerewolf.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.Werewolf));
		soleVillager.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.Villager));
	}

	[Fact]
	public void Classify_WithPiperComposition_DoesNotInferDynamicCharmVictory()
	{
		var result = AlreadyDecidedRoleCompositionClassifier.Classify(
			CanonicalRoleComposition.Create([MainRoleType.Piper]),
			SimulatorCapability.SafetyScreening);

		result.IsAlreadyDecided.Should().BeFalse();
		result.GameResult.Should().BeNull();
		result.Reason.Should().Be(
			AlreadyDecidedReason.NoLobbyExitVictoryPredicateSatisfied);
	}

	[Fact]
	public void Classify_WithOrdinaryVillagerAndFreshVillageIdiot_DoesNotInferWerewolfControl()
	{
		var result = AlreadyDecidedRoleCompositionClassifier.Classify(
			CanonicalRoleComposition.Create(
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.VillageIdiot
			]),
			SimulatorCapability.SafetyScreening);

		result.IsAlreadyDecided.Should().BeFalse();
		result.GameResult.Should().BeNull();
		result.Reason.Should().Be(
			AlreadyDecidedReason.NoLobbyExitVictoryPredicateSatisfied);
	}

	[Fact]
	public void Resolve_WithNoSatisfiedPredicates_ReturnsExplicitNotAlreadyDecided()
	{
		var result = AlreadyDecidedRoleCompositionClassifier.Resolve(
		[
			new(Faction.Werewolf, false, AlreadyDecidedReason.WerewolfControlShortcut)
		]);

		result.IsAlreadyDecided.Should().BeFalse();
		result.GameResult.Should().BeNull();
		result.Reason.Should().Be(AlreadyDecidedReason.NoLobbyExitVictoryPredicateSatisfied);
	}

	[Fact]
	public void Resolve_WithOneSatisfiedPredicate_ReturnsItsSingleFactionResultAndReason()
	{
		var result = AlreadyDecidedRoleCompositionClassifier.Resolve(
		[
			new(Faction.Villager, true, AlreadyDecidedReason.NoWerewolfFactionBeneficiariesAtLobbyExit),
			new(Faction.Werewolf, false, AlreadyDecidedReason.WerewolfControlShortcut)
		]);

		result.GameResult.Should().Be(new SingleFactionGameResult(Faction.Villager));
		result.Reason.Should().Be(AlreadyDecidedReason.NoWerewolfFactionBeneficiariesAtLobbyExit);
	}

	[Fact]
	public void Resolve_WithMultipleSatisfiedPredicates_ReturnsDeterministicSharedVictory()
	{
		var forward = AlreadyDecidedRoleCompositionClassifier.Resolve(
		[
			new(Faction.Werewolf, true, AlreadyDecidedReason.WerewolfControlShortcut),
			new(Faction.Villager, true, AlreadyDecidedReason.NoWerewolfFactionBeneficiariesAtLobbyExit)
		]);
		var reverse = AlreadyDecidedRoleCompositionClassifier.Resolve(
		[
			new(Faction.Villager, true, AlreadyDecidedReason.NoWerewolfFactionBeneficiariesAtLobbyExit),
			new(Faction.Werewolf, true, AlreadyDecidedReason.WerewolfControlShortcut)
		]);

		forward.Should().Be(reverse);
		forward.Should().Be(
			new AlreadyDecidedRoleCompositionResult(
				new SharedVictoryGameResult([Faction.Villager, Faction.Werewolf]),
				AlreadyDecidedReason.MultipleLobbyExitVictoryPredicatesSatisfied));
	}

	[Fact]
	public void Resolve_WithMultipleSatisfiedPredicatesForOneFaction_RejectsInvalidInput()
	{
		var action = () => AlreadyDecidedRoleCompositionClassifier.Resolve(
			[
				new(Faction.Werewolf, true, AlreadyDecidedReason.WerewolfControlShortcut),
				new(Faction.Werewolf, true, AlreadyDecidedReason.WerewolfControlShortcut)
			]);

		action.Should().Throw<ArgumentException>()
			.Where(exception => exception.ParamName == "predicateResults");
	}

	[Fact]
	public void SharedVictoryGameResult_WithFewerThanTwoDistinctFactions_IsRejected()
	{
		var empty = () => new SharedVictoryGameResult([]);
		var duplicate = () => new SharedVictoryGameResult(
			[Faction.Werewolf, Faction.Werewolf]);

		empty.Should().Throw<ArgumentException>();
		duplicate.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Resolve_WithUnsupportedFaction_RejectsInsteadOfCollapsingToLegacyTeam()
	{
		var action = () => AlreadyDecidedRoleCompositionClassifier.Resolve(
		[
			new((Faction)999, true, AlreadyDecidedReason.WerewolfControlShortcut)
		]);

		action.Should().Throw<ArgumentOutOfRangeException>();
	}
}
