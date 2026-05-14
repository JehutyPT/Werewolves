using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Services;

namespace Werewolves.Client.Services;

public sealed class BenchmarkClientManager
{
	private readonly GameBenchmarkHarness _benchmarkHarness;

	public BenchmarkClientManager()
		: this(GameBenchmarkHarness.CreateDefault())
	{
	}

	public BenchmarkClientManager(GameBenchmarkHarness benchmarkHarness)
	{
		_benchmarkHarness = benchmarkHarness;
	}

	public bool IsRunning { get; private set; }
	public GameBenchmarkResult? LastResult { get; private set; }

	public async Task<GameBenchmarkResult> RunAsync(
		int gameCount = GameBenchmarkHarness.DefaultGameCount,
		int degreeOfParallelism = GameBenchmarkHarness.DefaultDegreeOfParallelism)
	{
		if (IsRunning)
		{
			throw new InvalidOperationException("Benchmark is already running.");
		}

		IsRunning = true;
		try
		{
			var result = await Task.Run(() => _benchmarkHarness.Run(gameCount, degreeOfParallelism));
			LastResult = result;
			return result;
		}
		finally
		{
			IsRunning = false;
		}
	}
}
