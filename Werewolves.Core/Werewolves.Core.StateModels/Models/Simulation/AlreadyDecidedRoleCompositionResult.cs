using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models.Simulation;

public abstract class GameResult;

public sealed class NoWinnerGameResult : GameResult, IEquatable<NoWinnerGameResult>
{
	public bool Equals(NoWinnerGameResult? other) => other is not null;

	public override bool Equals(object? obj) => obj is NoWinnerGameResult;

	public override int GetHashCode() => typeof(NoWinnerGameResult).GetHashCode();
}

public sealed class SingleFactionGameResult : GameResult, IEquatable<SingleFactionGameResult>
{
	public Faction Faction { get; }

	public SingleFactionGameResult(Faction faction)
	{
		if (!Enum.IsDefined(faction))
		{
			throw new ArgumentOutOfRangeException(nameof(faction));
		}

		Faction = faction;
	}

	public bool Equals(SingleFactionGameResult? other) =>
		other is not null && Faction == other.Faction;

	public override bool Equals(object? obj) => Equals(obj as SingleFactionGameResult);

	public override int GetHashCode() => Faction.GetHashCode();
}

public sealed class SharedVictoryGameResult : GameResult, IEquatable<SharedVictoryGameResult>
{
	private readonly Faction[] _factions;

	public IReadOnlyList<Faction> Factions { get; }

	public SharedVictoryGameResult(IEnumerable<Faction> factions)
	{
		ArgumentNullException.ThrowIfNull(factions);
		var snapshot = factions.ToArray();
		if (snapshot.Any(faction => !Enum.IsDefined(faction)))
		{
			throw new ArgumentOutOfRangeException(nameof(factions));
		}

		_factions = snapshot.Distinct().Order().ToArray();
		if (_factions.Length < 2)
		{
			throw new ArgumentException(
				"A Shared Victory Outcome requires at least two distinct Factions.",
				nameof(factions));
		}

		Factions = Array.AsReadOnly(_factions);
	}

	public bool Equals(SharedVictoryGameResult? other) =>
		other is not null && _factions.SequenceEqual(other._factions);

	public override bool Equals(object? obj) => Equals(obj as SharedVictoryGameResult);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach (var faction in _factions)
		{
			hash.Add(faction);
		}

		return hash.ToHashCode();
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

public sealed record AlreadyDecidedRoleCompositionResult(
	GameResult? GameResult,
	AlreadyDecidedReason Reason)
{
	public bool IsAlreadyDecided => GameResult is not null;
}
