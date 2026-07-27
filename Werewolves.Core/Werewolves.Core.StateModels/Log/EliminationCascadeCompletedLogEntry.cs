using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Durable proof that one resolution-scoped Elimination Cascade drained all
/// admitted batches and reactions.
/// </summary>
public sealed record EliminationCascadeCompletedLogEntry
	: GameLogEntryBase
{
	public required string ScopeId { get; init; }

	internal void EnforceValidity()
	{
		if (string.IsNullOrWhiteSpace(ScopeId))
		{
			throw new InvalidOperationException(
				"Elimination Cascade completion is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
		=> this;

	public override string ToString() =>
		$"EliminationCascadeCompleted: {ScopeId}";
}
