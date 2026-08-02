using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Services;

internal sealed record LivingFactionBeneficiarySnapshot(
	Faction Beneficiary,
	bool IsCharmed,
	int DurableVotingPower,
	bool IsCommittedLover = false,
	Guid PlayerId = default);

internal static class FactionVictoryPredicates
{
	internal static IReadOnlyList<Faction> Evaluate(
		IEnumerable<LivingFactionBeneficiarySnapshot> livingPlayers,
		PublicGroupPartition? publicGroupPartition = null)
	{
		ArgumentNullException.ThrowIfNull(livingPlayers);
		var snapshot = livingPlayers.ToArray();
		if (snapshot.Any(player =>
			    !Enum.IsDefined(player.Beneficiary) ||
			    player.DurableVotingPower < 0))
		{
			throw new ArgumentOutOfRangeException(nameof(livingPlayers));
		}

		var satisfiedFactions = Evaluate(
				snapshot.Select(player => player.Beneficiary))
			.ToList();
		satisfiedFactions.Remove(Faction.Werewolf);
		var werewolves = snapshot
			.Where(player => player.Beneficiary == Faction.Werewolf)
			.ToArray();
		var nonWerewolves = snapshot
			.Where(player => player.Beneficiary != Faction.Werewolf)
			.ToArray();
		if (werewolves.Length > 0 &&
		    (nonWerewolves.Length == 0 ||
		     nonWerewolves.All(player =>
			     player.Beneficiary == Faction.Villager) &&
		     werewolves.Sum(player =>
			     (long)player.DurableVotingPower) >=
		     nonWerewolves.Sum(player =>
			     (long)player.DurableVotingPower)))
		{
			satisfiedFactions.Add(Faction.Werewolf);
		}

		if (snapshot.Any(player =>
			    player.Beneficiary == Faction.Piper) &&
		    snapshot.All(player =>
			    player.Beneficiary == Faction.Piper ||
			    player.IsCharmed))
		{
			satisfiedFactions.Add(Faction.Piper);
		}

		if (snapshot is
		    [
			    { Beneficiary: Faction.CrossFactionLovers,
			      IsCommittedLover: true },
			    { Beneficiary: Faction.CrossFactionLovers,
			      IsCommittedLover: true }
		    ])
		{
			satisfiedFactions.Add(Faction.CrossFactionLovers);
		}

		if (publicGroupPartition is not null &&
		    snapshot.Any(player =>
			    player.Beneficiary == Faction.PrejudicedManipulator &&
			    HasNoLivingPlayerInOpposingGroup(
				    player.PlayerId,
				    snapshot,
				    publicGroupPartition)))
		{
			satisfiedFactions.Add(Faction.PrejudicedManipulator);
		}

		return satisfiedFactions;
	}

	private static bool HasNoLivingPlayerInOpposingGroup(
		Guid beneficiaryPlayerId,
		IReadOnlyCollection<LivingFactionBeneficiarySnapshot> livingPlayers,
		PublicGroupPartition publicGroupPartition)
	{
		IReadOnlySet<Guid>? opposingGroup =
			publicGroupPartition.FirstGroupPlayerIds.Contains(beneficiaryPlayerId)
				? publicGroupPartition.SecondGroupPlayerIds
				: publicGroupPartition.SecondGroupPlayerIds.Contains(
					beneficiaryPlayerId)
					? publicGroupPartition.FirstGroupPlayerIds
					: null;
		return opposingGroup is not null &&
		       livingPlayers.All(player =>
			       player.PlayerId != Guid.Empty &&
			       !opposingGroup.Contains(player.PlayerId));
	}

	internal static IReadOnlyList<Faction> Evaluate(
		IEnumerable<Faction> livingBeneficiaries)
	{
		ArgumentNullException.ThrowIfNull(livingBeneficiaries);
		var snapshot = livingBeneficiaries.ToArray();
		if (snapshot.Any(faction => !Enum.IsDefined(faction)))
		{
			throw new ArgumentOutOfRangeException(nameof(livingBeneficiaries));
		}

		var counts = snapshot
			.GroupBy(faction => faction)
			.ToDictionary(group => group.Key, group => group.Count());
		return Evaluate(new FactionBeneficiaryComposition(counts));
	}

	internal static IReadOnlyList<Faction> Evaluate(
		FactionBeneficiaryComposition composition)
	{
		ArgumentNullException.ThrowIfNull(composition);
		var villagers = composition.GetBeneficiaryCount(Faction.Villager);
		var werewolves = composition.GetBeneficiaryCount(Faction.Werewolf);
		var whiteWerewolves =
			composition.GetBeneficiaryCount(Faction.WhiteWerewolf);
		var total = Enum.GetValues<Faction>()
			.Sum(composition.GetBeneficiaryCount);

		return new[]
			{
				(Faction: Faction.Villager,
					IsSatisfied: villagers > 0 && total == villagers),
				(Faction: Faction.Werewolf,
					IsSatisfied:
						werewolves > 0 &&
						(total - werewolves == 0 ||
						 (total - werewolves == villagers &&
						  werewolves >= villagers))),
				(Faction: Faction.WhiteWerewolf,
					IsSatisfied: whiteWerewolves == 1 && total == 1)
			}
			.Where(result => result.IsSatisfied)
			.Select(result => result.Faction)
			.ToArray();
	}
}
