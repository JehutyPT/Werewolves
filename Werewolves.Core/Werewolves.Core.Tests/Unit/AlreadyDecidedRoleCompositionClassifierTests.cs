using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class AlreadyDecidedRoleCompositionClassifierTests
{
	[Fact]
	public void CurrentProfileBridge_MapsEachSupportedRoleToFactionBeneficiaryEvidence()
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
	public void CurrentProfileBridge_WithUnsupportedRole_RejectsInsteadOfInferringLegacyTeam()
	{
		var composition = CanonicalRoleComposition.Create([MainRoleType.BigBadWolf]);

		var action = () => CurrentProfileFactionBridge.Map(composition);

		action.Should().Throw<ArgumentException>();
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
			new(Faction.Villager, true, AlreadyDecidedReason.WerewolfControlShortcut)
		]);
		var reverse = AlreadyDecidedRoleCompositionClassifier.Resolve(
		[
			new(Faction.Villager, true, AlreadyDecidedReason.WerewolfControlShortcut),
			new(Faction.Werewolf, true, AlreadyDecidedReason.WerewolfControlShortcut)
		]);

		forward.Reason.Should().Be(AlreadyDecidedReason.MultipleLobbyExitVictoryPredicatesSatisfied);
		forward.GameResult.Should().BeOfType<SharedVictoryGameResult>()
			.Which.Factions.Should().Equal(Faction.Villager, Faction.Werewolf);
		reverse.GameResult.Should().BeOfType<SharedVictoryGameResult>()
			.Which.Factions.Should().Equal(Faction.Villager, Faction.Werewolf);
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
