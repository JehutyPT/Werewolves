using System.Collections.Immutable;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Atomically records a Role-owned fixed voter-eligibility snapshot, the Day
/// on which it applies, and the pending announcement identity.
/// </summary>
public sealed record VoterEligibilityRestrictionCommittedLogEntry
	: GameLogEntryBase,
		IGameFactLogEntry
{
	public required string ScopeId { get; init; }
	public required MainRoleType SourceRole { get; init; }
	public required ImmutableArray<Guid> CandidatePlayerIds { get; init; }
	public required ImmutableArray<Guid> PermittedVoterIds { get; init; }
	public required int AppliesOnTurnNumber { get; init; }
	public required Guid AnnouncementInstructionId { get; init; }

	internal override void EnforceValidity()
	{
		if (string.IsNullOrWhiteSpace(ScopeId) ||
		    !Enum.IsDefined(SourceRole) ||
		    CandidatePlayerIds.IsDefaultOrEmpty ||
		    PermittedVoterIds.IsDefaultOrEmpty ||
		    CandidatePlayerIds.Any(playerId => playerId == Guid.Empty) ||
		    PermittedVoterIds.Any(playerId => playerId == Guid.Empty) ||
		    CandidatePlayerIds.Length != CandidatePlayerIds.Distinct().Count() ||
		    PermittedVoterIds.Length != PermittedVoterIds.Distinct().Count() ||
		    !PermittedVoterIds.ToHashSet().IsSubsetOf(CandidatePlayerIds) ||
		    AppliesOnTurnNumber != TurnNumber + 1 ||
		    AnnouncementInstructionId == Guid.Empty ||
		    CurrentPhase != GamePhase.Day)
		{
			throw new InvalidOperationException(
				"The voter-eligibility restriction is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

	public override string ToString() =>
		$"VoterEligibilityRestriction: {SourceRole}, scope {ScopeId}, " +
		$"turn {AppliesOnTurnNumber}";
}
