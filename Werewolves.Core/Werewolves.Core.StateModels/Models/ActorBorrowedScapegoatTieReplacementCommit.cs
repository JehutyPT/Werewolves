using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable lineage for Actor replacing a tied Day vote with the
/// borrowed Scapegoat power. Only its opaque integrity marker is public.
/// </summary>
internal sealed record ActorBorrowedScapegoatTieReplacementCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	int TriggeringVoteOutcomeLogIndex,
	int VoteOrdinal,
	string CascadeScopeId,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex) : IActorBorrowedRolePowerCommit
{
	internal const string ExpectedSourcePowerIdentifier =
		"scapegoat-tie-replacement";

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
			TriggeringVoteOutcomeLogIndex < 0 ||
			VoteOrdinal <= 0 ||
			string.IsNullOrWhiteSpace(CascadeScopeId) ||
			CurrentPhase != GamePhase.Day)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Scapegoat tie replacement is structurally invalid.");
		}
	}
}
