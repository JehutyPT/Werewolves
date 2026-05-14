namespace Werewolves.Core.GameLogic.Models;

public sealed record HeadlessGameResult(
	bool IsFinished,
	int TurnCount,
	int ProcessedInstructionCount,
	string? VictoryDescription);
