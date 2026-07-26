using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public static class SimulatorFactionBeneficiaryBridge
{
	public static FactionBeneficiaryComposition Map(
		CanonicalRoleComposition composition,
		SimulatorCapability capability)
	{
		ArgumentNullException.ThrowIfNull(capability);
		return Map(composition, (SimulatorProfile)capability);
	}

	internal static FactionBeneficiaryComposition Map(
		CanonicalRoleComposition composition,
		SimulatorProfile profile)
	{
		ArgumentNullException.ThrowIfNull(composition);
		ArgumentNullException.ThrowIfNull(profile);
		var counts = new Dictionary<Faction, int>();
		foreach (var entry in composition.Entries)
		{
			if (!profile.TryGetBeneficiaryFaction(entry.Role, out var faction))
			{
				throw new ArgumentException(
					$"Role {entry.Role} is not supported by the selected simulator producer.",
					nameof(composition));
			}

			counts[faction] = counts.GetValueOrDefault(faction) + entry.Count;
		}

		return new FactionBeneficiaryComposition(counts);
	}
}

public static class AlreadyDecidedRoleCompositionClassifier
{
	public static AlreadyDecidedRoleCompositionResult Classify(
		CanonicalRoleComposition composition,
		SimulatorCapability capability)
	{
		ArgumentNullException.ThrowIfNull(capability);
		return Classify(composition, (SimulatorProfile)capability);
	}

	internal static AlreadyDecidedRoleCompositionResult Classify(
		CanonicalRoleComposition composition,
		SimulatorProfile profile)
	{
		ArgumentNullException.ThrowIfNull(composition);
		ArgumentNullException.ThrowIfNull(profile);
		var evidence = SimulatorFactionBeneficiaryBridge.Map(composition, profile);
		var werewolves = evidence.GetBeneficiaryCount(Faction.Werewolf);
		var villagers = evidence.GetBeneficiaryCount(Faction.Villager);

		return Resolve(
		[
			new FactionVictoryPredicateResult(
				Faction.Villager,
				villagers > 0 && werewolves == 0,
				AlreadyDecidedReason.NoWerewolfFactionBeneficiariesAtLobbyExit),
			new FactionVictoryPredicateResult(
				Faction.Werewolf,
				werewolves > 0 && werewolves >= villagers,
				AlreadyDecidedReason.WerewolfControlShortcut)
		]);
	}

	public static AlreadyDecidedRoleCompositionResult Resolve(
		IEnumerable<FactionVictoryPredicateResult> predicateResults)
	{
		ArgumentNullException.ThrowIfNull(predicateResults);
		var snapshot = predicateResults.ToArray();
		if (snapshot.Any(result => !Enum.IsDefined(result.Faction)))
		{
			throw new ArgumentOutOfRangeException(nameof(predicateResults));
		}
		if (snapshot.GroupBy(result => result.Faction).Any(group => group.Count() > 1))
		{
			throw new ArgumentException(
				"Each Faction can have only one lobby-exit victory predicate result.",
				nameof(predicateResults));
		}

		var satisfied = snapshot
			.Where(result => result.IsSatisfied)
			.OrderBy(result => result.Faction)
			.ToArray();
		if (satisfied.Length == 0)
		{
			return new(null, AlreadyDecidedReason.NoLobbyExitVictoryPredicateSatisfied);
		}

		if (satisfied.Length == 1)
		{
			return new(
				new SingleFactionGameResult(satisfied[0].Faction),
				satisfied[0].Reason);
		}

		return new(
			new SharedVictoryGameResult(satisfied.Select(result => result.Faction)),
			AlreadyDecidedReason.MultipleLobbyExitVictoryPredicatesSatisfied);
	}
}
