using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;

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
		var gameService = new GameService();
		var startInstruction = gameService.StartNewGame(config);
		ModeratorInstruction instruction = startInstruction;
		var gameId = startInstruction.GameGuid;
		var processedInstructionCount = 0;

		while (instruction is not FinishedGameConfirmationInstruction)
		{
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
		}

		var finishedSession = gameService.GetGameStateView(gameId)
			?? throw new InvalidOperationException($"Finished game session {gameId} is no longer available.");
		var finishedInstruction = (FinishedGameConfirmationInstruction)instruction;

		return new HeadlessGameResult(
			IsFinished: true,
			TurnCount: finishedSession.TurnNumber,
			ProcessedInstructionCount: processedInstructionCount,
			VictoryDescription: finishedInstruction.VictoryDescription);
	}
}
