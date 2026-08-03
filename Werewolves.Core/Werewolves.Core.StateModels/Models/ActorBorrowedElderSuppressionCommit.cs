using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable projection of Actor's borrowed Elder village-vote
/// suppression. The activation and triggering vote/cascade lineage remain
/// behind the generic Actor borrowed Role Power marker.
/// </summary>
internal sealed record ActorBorrowedElderSuppressionCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	int TriggeringVoteOutcomeLogIndex,
	string CascadeScopeId,
	Guid AnnouncementInstructionId,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex) : IActorBorrowedRolePowerCommit
{
	internal const string ExpectedSourcePowerIdentifier =
		"elder-village-vote-suppression";

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
		if (PowerIdentity.SourceRole != MainRoleType.Elder ||
			!StringComparer.Ordinal.Equals(
				PowerIdentity.SourcePowerIdentifier,
				ExpectedSourcePowerIdentifier) ||
			TriggeringVoteOutcomeLogIndex < 0 ||
			string.IsNullOrWhiteSpace(CascadeScopeId) ||
			AnnouncementInstructionId == Guid.Empty ||
			CurrentPhase != GamePhase.Day)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Elder suppression is structurally invalid.");
		}
	}
}
