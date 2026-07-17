using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models.Simulation;

public abstract record SimulationRun
{
	public RunSeedMaterial RunSeedMaterial { get; }

	protected SimulationRun(RunSeedMaterial runSeedMaterial)
	{
		RunSeedMaterial = runSeedMaterial
			?? throw new ArgumentNullException(nameof(runSeedMaterial));
	}
}

public sealed record CompletedSimulationRun : SimulationRun
{
	public GameResult GameResult { get; }

	public int EndingTurn { get; }

	public VictoryCheckWindow VictoryCheckWindow { get; }

	public CompletedSimulationRun(
		RunSeedMaterial runSeedMaterial,
		GameResult gameResult,
		int endingTurn,
		VictoryCheckWindow victoryCheckWindow)
		: base(runSeedMaterial)
	{
		ArgumentNullException.ThrowIfNull(gameResult);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(endingTurn);
		if (!Enum.IsDefined(victoryCheckWindow))
		{
			throw new ArgumentOutOfRangeException(nameof(victoryCheckWindow));
		}

		GameResult = gameResult;
		EndingTurn = endingTurn;
		VictoryCheckWindow = victoryCheckWindow;
	}
}

public sealed record IncompleteSimulationRun : SimulationRun
{
	public IncompleteSimulationRun(RunSeedMaterial runSeedMaterial)
		: base(runSeedMaterial)
	{
	}
}
