using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public sealed class FactionVictoryPredicatesTests
{
	[Fact]
	public void WerewolfControl_UsesSummedDurableVotingPower()
	{
		var result = FactionVictoryPredicates.Evaluate(
			[
				new LivingFactionBeneficiarySnapshot(
					Faction.Werewolf,
					IsCharmed: false,
					DurableVotingPower: 1),
				new LivingFactionBeneficiarySnapshot(
					Faction.Villager,
					IsCharmed: false,
					DurableVotingPower: 1),
				new LivingFactionBeneficiarySnapshot(
					Faction.Villager,
					IsCharmed: false,
					DurableVotingPower: 0)
			]);

		result.Should().Contain(Faction.Werewolf);
	}

	[Fact]
	public void WerewolfControl_DoesNotTriggerWhenVillagerPowerIsGreater()
	{
		var result = FactionVictoryPredicates.Evaluate(
			[
				new LivingFactionBeneficiarySnapshot(
					Faction.Werewolf,
					IsCharmed: false,
					DurableVotingPower: 1),
				new LivingFactionBeneficiarySnapshot(
					Faction.Villager,
					IsCharmed: false,
					DurableVotingPower: 1),
				new LivingFactionBeneficiarySnapshot(
					Faction.Villager,
					IsCharmed: false,
					DurableVotingPower: 1)
			]);

		result.Should().NotContain(Faction.Werewolf);
	}

	[Fact]
	public void DurableVotingPower_TwoIsRepresentableAndAffectsControl()
	{
		var result = FactionVictoryPredicates.Evaluate(
			[
				new LivingFactionBeneficiarySnapshot(
					Faction.Werewolf,
					IsCharmed: false,
					DurableVotingPower: 2),
				new LivingFactionBeneficiarySnapshot(
					Faction.Villager,
					IsCharmed: false,
					DurableVotingPower: 1),
				new LivingFactionBeneficiarySnapshot(
					Faction.Villager,
					IsCharmed: false,
					DurableVotingPower: 1)
			]);

		result.Should().Contain(Faction.Werewolf);
	}

	[Fact]
	public void NegativeDurableVotingPower_IsRejected()
	{
		var act = () => FactionVictoryPredicates.Evaluate(
			[
				new LivingFactionBeneficiarySnapshot(
					Faction.Villager,
					IsCharmed: false,
					DurableVotingPower: -1)
			]);

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void NonWerewolfPredicates_KeepTheirExistingCompositionSemantics()
	{
		FactionVictoryPredicates.Evaluate(
				[
					new LivingFactionBeneficiarySnapshot(
						Faction.Villager,
						IsCharmed: false,
						DurableVotingPower: 0)
				])
			.Should().Equal(Faction.Villager);
		FactionVictoryPredicates.Evaluate(
				[
					new LivingFactionBeneficiarySnapshot(
						Faction.WhiteWerewolf,
						IsCharmed: false,
						DurableVotingPower: 0)
				])
			.Should().Equal(Faction.WhiteWerewolf);
		FactionVictoryPredicates.Evaluate(
				[
					new LivingFactionBeneficiarySnapshot(
						Faction.Piper,
						IsCharmed: false,
						DurableVotingPower: 1),
					new LivingFactionBeneficiarySnapshot(
						Faction.Villager,
						IsCharmed: true,
						DurableVotingPower: 1)
				])
			.Should().Contain(Faction.Piper);
	}
}
