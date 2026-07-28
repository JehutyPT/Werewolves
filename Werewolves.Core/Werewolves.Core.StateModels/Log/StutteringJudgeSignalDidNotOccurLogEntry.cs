using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records an accepted negative observation without spending the Judge's power.
/// </summary>
public sealed record StutteringJudgeSignalDidNotOccurLogEntry
	: GameLogEntryBase,
		IGameFactLogEntry
{
	public required Guid JudgePlayerId { get; init; }

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) =>
		this;

	public override string ToString() =>
		$"StutteringJudgeSignalDidNotOccur: {JudgePlayerId}";
}
