using Werewolves.Core.GameLogic.Services;
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
		return Map(
			composition,
			role => capability.TryGetBeneficiaryFaction(role, out var faction)
				? faction
				: null);
	}

	internal static FactionBeneficiaryComposition Map(
		CanonicalRoleComposition composition,
		SimulatorProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		return Map(
			composition,
			role => profile.TryGetBeneficiaryFaction(role, out var faction)
				? faction
				: null);
	}

	private static FactionBeneficiaryComposition Map(
		CanonicalRoleComposition composition,
		Func<MainRoleType, Faction?> getBeneficiaryFaction)
	{
		ArgumentNullException.ThrowIfNull(composition);
		var counts = new Dictionary<Faction, int>();
		foreach (var entry in composition.Entries)
		{
			if (getBeneficiaryFaction(entry.Role) is not { } faction)
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
		return Classify(SimulatorFactionBeneficiaryBridge.Map(composition, capability));
	}

	internal static AlreadyDecidedRoleCompositionResult Classify(
		CanonicalRoleComposition composition,
		SimulatorProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		return Classify(SimulatorFactionBeneficiaryBridge.Map(composition, profile));
	}

	private static AlreadyDecidedRoleCompositionResult Classify(
		FactionBeneficiaryComposition evidence)
	{
		var satisfiedFactions = FactionVictoryPredicates
			.Evaluate(evidence)
			.ToHashSet();

		return Resolve(
		[
			new FactionVictoryPredicateResult(
				Faction.Villager,
				satisfiedFactions.Contains(Faction.Villager),
				AlreadyDecidedReason.NoWerewolfFactionBeneficiariesAtLobbyExit),
			new FactionVictoryPredicateResult(
				Faction.Werewolf,
				satisfiedFactions.Contains(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut),
			new FactionVictoryPredicateResult(
				Faction.WhiteWerewolf,
				satisfiedFactions.Contains(Faction.WhiteWerewolf),
				AlreadyDecidedReason.WhiteWerewolfSoleSurvivor)
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
		var gameResult = GameResultSelection.Select(
			satisfied.Select(result => result.Faction),
			allPlayersEliminated: false);
		var reason = satisfied.Length switch
		{
			0 => AlreadyDecidedReason.NoLobbyExitVictoryPredicateSatisfied,
			1 => satisfied[0].Reason,
			_ => AlreadyDecidedReason.MultipleLobbyExitVictoryPredicatesSatisfied
		};
		return new(gameResult, reason);
	}
}
