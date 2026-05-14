namespace Werewolves.Core.GameLogic.Models;

public sealed record GcCollectionDelta(int Gen0, int Gen1, int Gen2);

public sealed record GameBenchmarkResult(
	int GameCount,
	int DegreeOfParallelism,
	TimeSpan TotalElapsed,
	TimeSpan AverageElapsedPerGame,
	TimeSpan MinElapsedPerGame,
	TimeSpan MaxElapsedPerGame,
	IReadOnlyList<int> TurnCounts,
	GcCollectionDelta GcCollections);
