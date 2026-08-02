using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public sealed class FactionVictoryPredicatesTests
{
	[Fact]
	public void PrejudicedManipulator_OpposingGroupHasNoLivingPlayers_Wins()
	{
		var manipulatorId = Guid.NewGuid();
		var allyId = Guid.NewGuid();
		var eliminatedOpponentId = Guid.NewGuid();
		var partition = PublicGroupPartition.Create(
			[manipulatorId, allyId, eliminatedOpponentId],
			[manipulatorId, allyId],
			[eliminatedOpponentId]);

		var result = FactionVictoryPredicates.Evaluate(
			[
				new LivingFactionBeneficiarySnapshot(
					Faction.PrejudicedManipulator,
					IsCharmed: false,
					DurableVotingPower: 1,
					PlayerId: manipulatorId),
				new LivingFactionBeneficiarySnapshot(
					Faction.Villager,
					IsCharmed: false,
					DurableVotingPower: 1,
					PlayerId: allyId)
			],
			partition);

		result.Should().Contain(Faction.PrejudicedManipulator);
	}

	[Fact]
	public void PrejudicedManipulator_LivingOpposingGroupPlayer_BlocksVictory()
	{
		var manipulatorId = Guid.NewGuid();
		var opponentId = Guid.NewGuid();
		var partition = PublicGroupPartition.Create(
			[manipulatorId, opponentId],
			[manipulatorId],
			[opponentId]);

		var result = FactionVictoryPredicates.Evaluate(
			[
				new LivingFactionBeneficiarySnapshot(
					Faction.PrejudicedManipulator,
					IsCharmed: false,
					DurableVotingPower: 1,
					PlayerId: manipulatorId),
				new LivingFactionBeneficiarySnapshot(
					Faction.Villager,
					IsCharmed: false,
					DurableVotingPower: 1,
					PlayerId: opponentId)
			],
			partition);

		result.Should().NotContain(Faction.PrejudicedManipulator);
	}

	[Fact]
	public void PrejudicedManipulator_PosthumousOrChangedBeneficiary_DoesNotWin()
	{
		var formerManipulatorId = Guid.NewGuid();
		var livingPlayerId = Guid.NewGuid();
		var partition = PublicGroupPartition.Create(
			[formerManipulatorId, livingPlayerId],
			[formerManipulatorId],
			[livingPlayerId]);

		var posthumousResult = FactionVictoryPredicates.Evaluate(
			[
				new LivingFactionBeneficiarySnapshot(
					Faction.Villager,
					IsCharmed: false,
					DurableVotingPower: 1,
					PlayerId: livingPlayerId)
			],
			partition);
		var changedBeneficiaryResult = FactionVictoryPredicates.Evaluate(
			[
				new LivingFactionBeneficiarySnapshot(
					Faction.Werewolf,
					IsCharmed: false,
					DurableVotingPower: 1,
					PlayerId: formerManipulatorId)
			],
			partition);

		posthumousResult.Should().NotContain(Faction.PrejudicedManipulator);
		changedBeneficiaryResult.Should().NotContain(
			Faction.PrejudicedManipulator);
	}

	[Fact]
	public void PrejudicedManipulator_AndPiperSatisfiedTogether_ResolveSharedVictory()
	{
		var manipulatorId = Guid.NewGuid();
		var piperId = Guid.NewGuid();
		var eliminatedOpponentId = Guid.NewGuid();
		var partition = PublicGroupPartition.Create(
			[manipulatorId, piperId, eliminatedOpponentId],
			[manipulatorId, piperId],
			[eliminatedOpponentId]);
		var satisfied = FactionVictoryPredicates.Evaluate(
			[
				new LivingFactionBeneficiarySnapshot(
					Faction.PrejudicedManipulator,
					IsCharmed: true,
					DurableVotingPower: 1,
					PlayerId: manipulatorId),
				new LivingFactionBeneficiarySnapshot(
					Faction.Piper,
					IsCharmed: false,
					DurableVotingPower: 1,
					PlayerId: piperId)
			],
			partition);

		var result = GameResultSelection.Select(
			satisfied,
			allPlayersEliminated: false);

		result.Should().Be(new SharedVictoryGameResult(
			[Faction.Piper, Faction.PrejudicedManipulator]));
	}

	[Fact]
	public void PrejudicedManipulator_VictoryIsInvariantWhenPartitionGroupsAreExchanged()
	{
		var manipulatorId = Guid.NewGuid();
		var allyId = Guid.NewGuid();
		var eliminatedOpponentId = Guid.NewGuid();
		var roster = new[] { manipulatorId, allyId, eliminatedOpponentId };
		var living = new[]
		{
			new LivingFactionBeneficiarySnapshot(
				Faction.PrejudicedManipulator,
				IsCharmed: false,
				DurableVotingPower: 1,
				PlayerId: manipulatorId),
			new LivingFactionBeneficiarySnapshot(
				Faction.Villager,
				IsCharmed: false,
				DurableVotingPower: 1,
				PlayerId: allyId)
		};
		var original = PublicGroupPartition.Create(
			roster,
			[manipulatorId, allyId],
			[eliminatedOpponentId]);
		var exchanged = PublicGroupPartition.Create(
			roster,
			[eliminatedOpponentId],
			[manipulatorId, allyId]);

		var originalResult = FactionVictoryPredicates.Evaluate(
			living,
			original);
		var exchangedResult = FactionVictoryPredicates.Evaluate(
			living,
			exchanged);

		exchangedResult.Should().Equal(originalResult);
		originalResult.Should().Contain(Faction.PrejudicedManipulator);
	}

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

	[Fact]
	public void CrossFactionLovers_RequiresExactlyTheCommittedLivingPair()
	{
		var exactPair = new[]
		{
			new LivingFactionBeneficiarySnapshot(
				Faction.CrossFactionLovers,
				IsCharmed: false,
				DurableVotingPower: 1,
				IsCommittedLover: true),
			new LivingFactionBeneficiarySnapshot(
				Faction.CrossFactionLovers,
				IsCharmed: false,
				DurableVotingPower: 1,
				IsCommittedLover: true)
		};

		FactionVictoryPredicates.Evaluate(exactPair)
			.Should().ContainSingle()
			.Which.Should().Be(Faction.CrossFactionLovers);
		FactionVictoryPredicates.Evaluate(
				exactPair.Append(new LivingFactionBeneficiarySnapshot(
					Faction.Villager,
					IsCharmed: false,
					DurableVotingPower: 1)))
			.Should().NotContain(Faction.CrossFactionLovers);
		FactionVictoryPredicates.Evaluate(
				exactPair.Select((player, index) =>
					index == 0
						? player with { IsCommittedLover = false }
						: player))
			.Should().NotContain(Faction.CrossFactionLovers);
		FactionVictoryPredicates.Evaluate([exactPair[0]])
			.Should().NotContain(Faction.CrossFactionLovers);
	}

	[Fact]
	public void CrossFactionLovers_AndAnotherSatisfiedPredicate_ResolveAsSharedVictory()
	{
		var result = GameResultSelection.Select(
			[Faction.CrossFactionLovers, Faction.Piper],
			allPlayersEliminated: false);

		result.Should().Be(
			new SharedVictoryGameResult(
				[Faction.CrossFactionLovers, Faction.Piper]));
	}
}
