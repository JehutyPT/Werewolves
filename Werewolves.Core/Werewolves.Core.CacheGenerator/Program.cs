using Werewolves.Core.GameLogic.Simulation;

return Run(args);

static int Run(string[] args)
{
	if (!TryParse(args, out var outputPath, out var diagnosticsPath, out var degree))
	{
		Console.Error.WriteLine(
			"Usage: --output <terminal-lobby-cache.json> --diagnostics <report.json> [--degree-of-parallelism <positive integer>]");
		return 64;
	}

	using var cancellation = new CancellationTokenSource();
	ConsoleCancelEventHandler handler = (_, eventArgs) =>
	{
		eventArgs.Cancel = true;
		cancellation.Cancel();
	};
	Console.CancelKeyPress += handler;
	try
	{
		var generator = new BuildTimeTerminalLobbyCacheGenerator(
			degree,
			progress =>
			{
				if (progress.CompletedScenarioCount == progress.TotalScenarioCount
					|| progress.CompletedScenarioCount % 10 == 0)
				{
					Console.Error.WriteLine(
						$"[{progress.CompletedScenarioCount}/{progress.TotalScenarioCount}] {progress.CanonicalIdentity}");
				}
			});
		var result = generator.GenerateToFiles(
			outputPath,
			diagnosticsPath,
			cancellationToken: cancellation.Token);
		Console.Error.WriteLine(
			$"status={result.StatusCode} records={result.Diagnostics.Artifact?.RecordCount ?? 0} "
			+ $"sha256={result.Diagnostics.Artifact?.Sha256 ?? "none"} "
			+ $"bytes={result.Diagnostics.Artifact?.ByteLength ?? 0}");
		return result.StatusCode switch
		{
			"completed" => 0,
			"cancelled" => 2,
			_ => 1
		};
	}
	finally
	{
		Console.CancelKeyPress -= handler;
	}
}

static bool TryParse(
	string[] args,
	out string outputPath,
	out string diagnosticsPath,
	out int degreeOfParallelism)
{
	outputPath = string.Empty;
	diagnosticsPath = string.Empty;
	degreeOfParallelism = Math.Max(1, Environment.ProcessorCount);
	for (var index = 0; index < args.Length; index++)
	{
		if (index + 1 >= args.Length)
		{
			return false;
		}

		switch (args[index])
		{
			case "--output":
				outputPath = args[++index];
				break;
			case "--diagnostics":
				diagnosticsPath = args[++index];
				break;
			case "--degree-of-parallelism":
				if (!int.TryParse(args[++index], out degreeOfParallelism)
					|| degreeOfParallelism <= 0)
				{
					return false;
				}
				break;
			default:
				return false;
		}
	}

	return outputPath.Length > 0 && diagnosticsPath.Length > 0;
}
