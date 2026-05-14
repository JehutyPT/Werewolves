using System.Diagnostics;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Strategies;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Services;

public sealed class GameBenchmarkHarness
{
	public const int DefaultGameCount = 1_000;
	public const int DefaultDegreeOfParallelism = 2;

	private readonly HeadlessGameDriver _driver;

	public GameBenchmarkHarness(HeadlessGameDriver driver)
	{
		_driver = driver;
	}

	public static GameBenchmarkHarness CreateDefault()
		=> new(new HeadlessGameDriver(new FirstValidOptionStrategy()));

	public GameBenchmarkResult Run(
		int gameCount = DefaultGameCount,
		int degreeOfParallelism = DefaultDegreeOfParallelism)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameCount);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(degreeOfParallelism);

		var gen0Before = GC.CollectionCount(0);
		var gen1Before = GC.CollectionCount(1);
		var gen2Before = GC.CollectionCount(2);
		var elapsedPerGame = new TimeSpan[gameCount];
		var turnCounts = new int[gameCount];
		var totalStopwatch = Stopwatch.StartNew();

		var tasks = Enumerable.Range(0, degreeOfParallelism)
			.Select(workerIndex => Task.Run(() =>
			{
				for (var gameIndex = workerIndex; gameIndex < gameCount; gameIndex += degreeOfParallelism)
				{
					var gameStopwatch = Stopwatch.StartNew();
					var gameResult = _driver.CompleteGame(CreateSpikeConfig());
					gameStopwatch.Stop();

					elapsedPerGame[gameIndex] = gameStopwatch.Elapsed;
					turnCounts[gameIndex] = gameResult.TurnCount;
				}
			}))
			.ToArray();

		Task.WaitAll(tasks);
		totalStopwatch.Stop();

		return new GameBenchmarkResult(
			GameCount: gameCount,
			DegreeOfParallelism: degreeOfParallelism,
			TotalElapsed: totalStopwatch.Elapsed,
			AverageElapsedPerGame: TimeSpan.FromTicks((long)elapsedPerGame.Average(elapsed => elapsed.Ticks)),
			MinElapsedPerGame: elapsedPerGame.Min(),
			MaxElapsedPerGame: elapsedPerGame.Max(),
			TurnCounts: turnCounts,
			GcCollections: new GcCollectionDelta(
				GC.CollectionCount(0) - gen0Before,
				GC.CollectionCount(1) - gen1Before,
				GC.CollectionCount(2) - gen2Before));
	}

	public static GameSessionConfig CreateSpikeConfig()
	{
		var players = Enumerable.Range(1, 15)
			.Select(index => $"Player {index}")
			.ToList();

		List<MainRoleType> roles =
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			.. Enumerable.Repeat(MainRoleType.SimpleVillager, 10)
		];

		return new GameSessionConfig(players, roles);
	}
}
