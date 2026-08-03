using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable fixed candidate/permitted-voter snapshot selected after an
/// Actor borrowed Scapegoat sacrifice. Its generic announcement remains public.
/// </summary>
internal sealed record ActorBorrowedScapegoatVoterRestrictionCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	int TieReplacementPublicMarkerLogIndex,
	string CascadeScopeId,
	IReadOnlyList<Guid> CandidatePlayerIds,
	IReadOnlyList<Guid> PermittedVoterIds,
	int AppliesOnTurnNumber,
	Guid AnnouncementInstructionId,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex) : IActorBorrowedRolePowerCommit
{
	internal const string ExpectedSourcePowerIdentifier =
		ActorBorrowedScapegoatTieReplacementCommit
			.ExpectedSourcePowerIdentifier;

	ActorBorrowedRolePowerCommitCoordinate
		IActorBorrowedRolePowerCommit.Coordinate => new(
			PowerIdentity,
			ActorSetupCardId,
			Timestamp,
			TurnNumber,
			CurrentPhase,
			PublicMarkerLogIndex);

	internal void EnforceValidity()
	{
		((IActorBorrowedRolePowerCommit)this).Coordinate.EnforceValidity();
		if (PowerIdentity.SourceRole != MainRoleType.Scapegoat ||
			!StringComparer.Ordinal.Equals(
				PowerIdentity.SourcePowerIdentifier,
				ExpectedSourcePowerIdentifier) ||
			TieReplacementPublicMarkerLogIndex < 0 ||
			string.IsNullOrWhiteSpace(CascadeScopeId) ||
			CandidatePlayerIds is not { Count: > 0 } ||
			PermittedVoterIds is not { Count: > 0 } ||
			CandidatePlayerIds.Any(playerId => playerId == Guid.Empty) ||
			PermittedVoterIds.Any(playerId => playerId == Guid.Empty) ||
			CandidatePlayerIds.Distinct().Count() != CandidatePlayerIds.Count ||
			PermittedVoterIds.Distinct().Count() != PermittedVoterIds.Count ||
			PermittedVoterIds.Except(CandidatePlayerIds).Any() ||
			AppliesOnTurnNumber != TurnNumber + 1 ||
			AnnouncementInstructionId == Guid.Empty ||
			CurrentPhase != GamePhase.Day)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Scapegoat voter restriction is structurally invalid.");
		}
	}
}
