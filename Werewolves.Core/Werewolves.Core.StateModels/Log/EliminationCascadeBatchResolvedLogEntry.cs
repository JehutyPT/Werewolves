using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Durable proof of one settled Elimination Cascade batch, including its
/// requested Eliminations and the exact subset that committed.
/// </summary>
public sealed record EliminationCascadeBatchResolvedLogEntry
	: GameLogEntryBase
{
	public required string ScopeId { get; init; }
	public required List<EliminationCascadeElimination> RequestedEliminations
	{
		get;
		init;
	}
	public required List<EliminationCascadeElimination> CommittedEliminations
	{
		get;
		init;
	}

	internal void EnforceValidity()
	{
		if (string.IsNullOrWhiteSpace(ScopeId) ||
			RequestedEliminations is not { Count: > 0 } ||
			CommittedEliminations is null ||
			RequestedEliminations.Any(elimination =>
				elimination.PlayerId == Guid.Empty) ||
			CommittedEliminations.Any(elimination =>
				elimination.PlayerId == Guid.Empty) ||
			RequestedEliminations
				.GroupBy(elimination => elimination.PlayerId)
				.Any(group => group.Count() > 1) ||
			CommittedEliminations
				.GroupBy(elimination => elimination.PlayerId)
				.Any(group => group.Count() > 1) ||
			CommittedEliminations.Any(committed =>
				!RequestedEliminations.Contains(committed)))
		{
			throw new InvalidOperationException(
				"Elimination Cascade batch resolution is structurally invalid.");
		}
	}

	internal bool HasSameResolutionKey(
		EliminationCascadeBatchResolvedLogEntry other) =>
		ScopeId == other.ScopeId &&
		RequestedEliminations.SequenceEqual(other.RequestedEliminations);

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) =>
		this;

	public override string ToString() =>
		$"EliminationCascadeBatchResolved: {ScopeId}, " +
		$"{RequestedEliminations.Count} requested, " +
		$"{CommittedEliminations.Count} committed";
}
