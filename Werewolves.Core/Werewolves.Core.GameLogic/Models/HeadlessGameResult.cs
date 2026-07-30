namespace Werewolves.Core.GameLogic.Models;

using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

public sealed record HeadlessGameResult(
	bool IsFinished,
	int TurnCount,
	int ProcessedInstructionCount,
	GameResult GameResult,
	VictoryCheckWindow VictoryCheckWindow);
