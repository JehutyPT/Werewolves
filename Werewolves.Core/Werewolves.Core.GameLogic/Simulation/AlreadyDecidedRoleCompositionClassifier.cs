using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public enum Faction
{
	Villager,
	Werewolf
}

public enum AlreadyDecidedReason
{
	NoLobbyExitVictoryPredicateSatisfied,
	NoWerewolfFactionBeneficiariesAtLobbyExit,
	WerewolfControlShortcut,
	MultipleLobbyExitVictoryPredicatesSatisfied
}

public abstract record GameResult;

public sealed record SingleFactionGameResult(Faction Faction) : GameResult;

public sealed record SharedVictoryGameResult : GameResult
{
	public IReadOnlyList<Faction> Factions { get; }

	public SharedVictoryGameResult(IEnumerable<Faction> factions)
	{
		ArgumentNullException.ThrowIfNull(factions);
		var snapshot = factions.ToArray();
		if (snapshot.Any(faction => !Enum.IsDefined(faction)))
		{
			throw new ArgumentOutOfRangeException(nameof(factions));
		}

		Factions = Array.AsReadOnly(snapshot.Distinct().Order().ToArray());
	}
}

public sealed record FactionVictoryPredicateResult(
	Faction Faction,
	bool IsSatisfied,
	AlreadyDecidedReason Reason);

public sealed class FactionBeneficiaryComposition
{
	private readonly IReadOnlyDictionary<Faction, int> _counts;

	internal FactionBeneficiaryComposition(IReadOnlyDictionary<Faction, int> counts) =>
		_counts = counts;

	public int GetBeneficiaryCount(Faction faction) =>
		_counts.TryGetValue(faction, out var count) ? count : 0;
}

public static class CurrentProfileFactionBridge
{
	public static FactionBeneficiaryComposition Map(CanonicalRoleComposition composition)
	{
		ArgumentNullException.ThrowIfNull(composition);
		var counts = new Dictionary<Faction, int>();
		foreach (var entry in composition.Entries)
		{
			var faction = entry.Role switch
			{
				MainRoleType.SimpleWerewolf => Faction.Werewolf,
				MainRoleType.Seer or MainRoleType.WildChild or MainRoleType.SimpleVillager =>
					Faction.Villager,
				_ => throw new ArgumentException(
					$"Role {entry.Role} is not supported by the current simulator profile.",
					nameof(composition))
			};

			counts[faction] = counts.GetValueOrDefault(faction) + entry.Count;
		}

		return new FactionBeneficiaryComposition(counts);
	}
}

public sealed record AlreadyDecidedRoleCompositionResult(
	GameResult? GameResult,
	AlreadyDecidedReason Reason)
{
	public bool IsAlreadyDecided => GameResult is not null;
}

public static class AlreadyDecidedRoleCompositionClassifier
{
	public static AlreadyDecidedRoleCompositionResult Classify(
		CanonicalRoleComposition composition)
	{
		ArgumentNullException.ThrowIfNull(composition);
		var evidence = CurrentProfileFactionBridge.Map(composition);
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

		var satisfied = snapshot.Where(result => result.IsSatisfied).ToArray();
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
