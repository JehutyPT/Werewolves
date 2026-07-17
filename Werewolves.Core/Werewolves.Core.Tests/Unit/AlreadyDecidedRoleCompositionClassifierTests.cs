using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class AlreadyDecidedRoleCompositionClassifierTests
{
	[Fact]
	public void Map_WithSupportedCurrentProfileRoles_ReturnsFactionBeneficiaryEvidence()
	{
		var composition = CanonicalRoleComposition.Create(
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager
		]);

		var evidence = CurrentProfileFactionBridge.Map(composition);

		evidence.GetBeneficiaryCount(Faction.Werewolf).Should().Be(1);
		evidence.GetBeneficiaryCount(Faction.Villager).Should().Be(3);
	}

	[Fact]
	public void Map_WithUnsupportedRole_RejectsInsteadOfInferringLegacyTeam()
	{
		var composition = CanonicalRoleComposition.Create([MainRoleType.BigBadWolf]);

		var action = () => CurrentProfileFactionBridge.Map(composition);

		action.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Map_WithProfileOwnedBeneficiaryDescriptor_ReturnsFactionBeneficiaryEvidence()
	{
		var profile = new SimulatorProfile(
			new SimulatorProfileIdentity("descriptor-test", "1"),
			[new(MainRoleType.Seer, Faction.Villager)]);
		var composition = CanonicalRoleComposition.Create([MainRoleType.Seer]);

		var evidence = CurrentProfileFactionBridge.Map(composition, profile);

		evidence.GetBeneficiaryCount(Faction.Villager).Should().Be(1);
		evidence.GetBeneficiaryCount(Faction.Werewolf).Should().Be(0);
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
