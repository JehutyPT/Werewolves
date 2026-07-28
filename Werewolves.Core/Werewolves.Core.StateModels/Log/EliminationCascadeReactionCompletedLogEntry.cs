using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Durable proof that one ordered Elimination Cascade reaction completed for
/// one deterministic trigger batch, including the follow-on eliminations that
/// were admitted by that reaction.
/// </summary>
public sealed record EliminationCascadeReactionCompletedLogEntry
	: GameLogEntryBase
{
	public required string ScopeId { get; init; }
	public required string ReactionId { get; init; }
	public required List<EliminationCascadeElimination> TriggeringEliminations
	{
		get;
		init;
	}
	public required List<EliminationCascadeElimination> AdmittedEliminations
	{
		get;
		init;
	}

	internal override void EnforceValidity()
	{
		if (string.IsNullOrWhiteSpace(ScopeId) ||
			string.IsNullOrWhiteSpace(ReactionId) ||
			TriggeringEliminations is not { Count: > 0 } ||
			AdmittedEliminations is null ||
			TriggeringEliminations.Any(elimination =>
				elimination.PlayerId == Guid.Empty) ||
			AdmittedEliminations.Any(elimination =>
				elimination.PlayerId == Guid.Empty) ||
			TriggeringEliminations
				.GroupBy(elimination => elimination.PlayerId)
				.Any(group => group.Count() > 1) ||
			AdmittedEliminations
				.GroupBy(elimination => elimination.PlayerId)
				.Any(group => group.Count() > 1))
		{
			throw new InvalidOperationException(
				"Elimination Cascade reaction completion is structurally invalid.");
		}
	}

	internal bool HasSameCompletionKey(
		EliminationCascadeReactionCompletedLogEntry other) =>
		ScopeId == other.ScopeId &&
		ReactionId == other.ReactionId &&
		TriggeringEliminations.SequenceEqual(other.TriggeringEliminations);

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
		=> this;

	public override string ToString() =>
		$"EliminationCascadeReactionCompleted: {ScopeId}/{ReactionId}, " +
		$"{TriggeringEliminations.Count} trigger(s), " +
		$"{AdmittedEliminations.Count} admitted";
}

public readonly record struct EliminationCascadeElimination(
	Guid PlayerId,
	EliminationReason Reason);
