using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Services;

public sealed class HeadlessGameDriver
{
	private readonly IModeratorDecisionStrategy _decisionStrategy;
	private readonly int _maxProcessedInstructionCount;

	public HeadlessGameDriver(IModeratorDecisionStrategy decisionStrategy, int maxProcessedInstructionCount = 5_000)
	{
		_decisionStrategy = decisionStrategy;
		_maxProcessedInstructionCount = maxProcessedInstructionCount;
	}

	public HeadlessGameResult CompleteGame(GameSessionConfig config)
	{
		var execution = CompleteGameSession(config, CancellationToken.None);
		var finishedInstruction = (FinishedGameConfirmationInstruction)execution.FinalInstruction;

		return new HeadlessGameResult(
			IsFinished: true,
			TurnCount: execution.Session.TurnNumber,
			ProcessedInstructionCount: execution.ProcessedInstructionCount,
			VictoryDescription: finishedInstruction.VictoryDescription);
	}

	internal HeadlessGameExecution CompleteGameSession(
		GameSessionConfig config,
		CancellationToken cancellationToken,
		Action? betweenInstructions = null) =>
		CompleteGameSession(
			config,
			factionFacts: null,
			cancellationToken,
			betweenInstructions);

	internal HeadlessGameExecution CompleteGameSession(
		SimulationStartState startState,
		CancellationToken cancellationToken,
		Action? betweenInstructions = null)
	{
		ArgumentNullException.ThrowIfNull(startState);
		return CompleteGameSession(
			startState.CreateGameSessionConfig(),
			startState.FactionFacts,
			cancellationToken,
			betweenInstructions);
	}

	private HeadlessGameExecution CompleteGameSession(
		GameSessionConfig config,
		IReadOnlyList<SimulationPlayerFactionFacts>? factionFacts,
		CancellationToken cancellationToken,
		Action? betweenInstructions)
	{
		ArgumentNullException.ThrowIfNull(config);
		cancellationToken.ThrowIfCancellationRequested();
		var gameService = new GameService();
		var startInstruction = factionFacts is null
			? gameService.StartNewGame(config)
			: gameService.StartNewSimulationGame(config, factionFacts);
		ModeratorInstruction instruction = startInstruction;
		var gameId = startInstruction.GameGuid;
		var processedInstructionCount = 0;

		while (instruction is not FinishedGameConfirmationInstruction)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (processedInstructionCount >= _maxProcessedInstructionCount)
			{
				throw new InvalidOperationException(
					$"Headless driver exceeded {_maxProcessedInstructionCount} processed instructions without finishing the game.");
			}

			var session = gameService.GetGameStateView(gameId)
				?? throw new InvalidOperationException($"Game session {gameId} is no longer available.");
			var response = _decisionStrategy.CreateResponse(instruction, session);
			var result = gameService.ProcessInstruction(gameId, response);

			if (!result.IsSuccess || result.ModeratorInstruction is null)
			{
				throw new InvalidOperationException("Headless driver could not process the current moderator instruction.");
			}

			instruction = result.ModeratorInstruction;
			processedInstructionCount++;
			if (instruction is not FinishedGameConfirmationInstruction)
			{
				betweenInstructions?.Invoke();
				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		var finishedSession = gameService.GetGameStateView(gameId)
			?? throw new InvalidOperationException($"Finished game session {gameId} is no longer available.");

		return new HeadlessGameExecution(
			finishedSession,
			instruction,
			processedInstructionCount);
	}
}

internal sealed record HeadlessGameExecution(
	IGameSession Session,
	ModeratorInstruction FinalInstruction,
	int ProcessedInstructionCount);
