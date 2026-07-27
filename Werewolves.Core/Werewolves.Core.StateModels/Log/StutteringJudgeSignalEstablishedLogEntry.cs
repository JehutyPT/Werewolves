using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records only that the private Stuttering Judge signal was established.
/// The signal itself is a physical-table fact and is never persisted.
/// </summary>
public sealed record StutteringJudgeSignalEstablishedLogEntry : GameLogEntryBase
{
	public required Guid JudgePlayerId { get; init; }

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) =>
		this;

	public override string ToString() =>
		$"StutteringJudgeSignalEstablished: {JudgePlayerId}";
}
