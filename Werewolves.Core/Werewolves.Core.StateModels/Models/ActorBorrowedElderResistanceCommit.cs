using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable projection of Actor's borrowed Elder resistance. The
/// activation and triggering attack or infection remain behind the generic
/// Actor borrowed Role Power marker.
/// </summary>
internal sealed record ActorBorrowedElderResistanceCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	Guid TargetPlayerId,
	int TriggeringNightActionLogIndex,
	int? RestoringWitchSaveLogIndex,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex) : IActorBorrowedRolePowerCommit
{
	internal const string ExpectedSourcePowerIdentifier =
		"elder-werewolf-attack-resistance";

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
			TargetPlayerId == Guid.Empty ||
			TargetPlayerId != PowerIdentity.ActingPlayerId ||
			TriggeringNightActionLogIndex < 0 ||
			RestoringWitchSaveLogIndex is < 0 ||
			CurrentPhase != GamePhase.Dawn)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Elder resistance is structurally invalid.");
		}
	}
}
