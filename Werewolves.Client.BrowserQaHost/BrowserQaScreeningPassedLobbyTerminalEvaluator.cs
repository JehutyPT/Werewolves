using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.BrowserQaHost;

public sealed class BrowserQaScreeningPassedLobbyTerminalEvaluator : ILobbyTerminalEvaluator
{
	public Task<LobbyEvaluationResult> EvaluateAsync(
		SimulationScenario scenario,
		SimulatorCapability capability,
		LobbyEvaluationDepth depth,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(capability);
		if (!Enum.IsDefined(depth))
		{
			throw new ArgumentOutOfRangeException(nameof(depth));
		}

		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult<LobbyEvaluationResult>(new ScreeningPassedLobbyEvaluation());
	}
}
