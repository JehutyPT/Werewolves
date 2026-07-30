using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Services;

internal sealed record LivingFactionBeneficiarySnapshot(
	Faction Beneficiary,
	bool IsCharmed);

internal static class FactionVictoryPredicates
{
	internal static IReadOnlyList<Faction> Evaluate(
		IEnumerable<LivingFactionBeneficiarySnapshot> livingPlayers)
	{
		ArgumentNullException.ThrowIfNull(livingPlayers);
		var snapshot = livingPlayers.ToArray();
		if (snapshot.Any(player =>
			    !Enum.IsDefined(player.Beneficiary)))
		{
			throw new ArgumentOutOfRangeException(nameof(livingPlayers));
		}

		var satisfiedFactions = Evaluate(
				snapshot.Select(player => player.Beneficiary))
			.ToList();
		if (snapshot.Any(player =>
			    player.Beneficiary == Faction.Piper) &&
		    snapshot.All(player =>
			    player.Beneficiary == Faction.Piper ||
			    player.IsCharmed))
		{
			satisfiedFactions.Add(Faction.Piper);
		}

		return satisfiedFactions;
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
