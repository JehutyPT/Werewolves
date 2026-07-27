using System.Collections.Immutable;
using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Atomically records the Scapegoat's fixed candidate snapshot, selected
/// permitted voters, following-Day identity, and pending announcement identity.
/// </summary>
public sealed record ScapegoatVoterRestrictionCommittedLogEntry
	: GameLogEntryBase
{
	public required string ScopeId { get; init; }
	public required Guid ScapegoatPlayerId { get; init; }
	public required ImmutableArray<Guid> CandidatePlayerIds { get; init; }
	public required ImmutableArray<Guid> PermittedVoterIds { get; init; }
	public required int AppliesOnTurnNumber { get; init; }
	public required Guid AnnouncementInstructionId { get; init; }

	internal void EnforceValidity()
	{
		if (string.IsNullOrWhiteSpace(ScopeId) ||
		    ScapegoatPlayerId == Guid.Empty ||
		    CandidatePlayerIds.IsDefaultOrEmpty ||
		    PermittedVoterIds.IsDefaultOrEmpty ||
		    CandidatePlayerIds.Any(playerId => playerId == Guid.Empty) ||
		    PermittedVoterIds.Any(playerId => playerId == Guid.Empty) ||
		    CandidatePlayerIds.Contains(ScapegoatPlayerId) ||
		    CandidatePlayerIds.Length != CandidatePlayerIds.Distinct().Count() ||
		    PermittedVoterIds.Length != PermittedVoterIds.Distinct().Count() ||
		    !PermittedVoterIds.ToHashSet().IsSubsetOf(CandidatePlayerIds) ||
		    AppliesOnTurnNumber != TurnNumber + 1 ||
		    AnnouncementInstructionId == Guid.Empty)
		{
			throw new InvalidOperationException(
				"The Scapegoat voter restriction is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;
}
