using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable projection of one Actor-borrowed Bear Tamer growl.
/// The borrowed source lineage remains outside public Game History.
/// </summary>
internal sealed record ActorBorrowedBearTamerGrowlCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex) : IActorBorrowedRolePowerCommit
{
	internal const string ExpectedSourcePowerIdentifier = "bear-tamer-growl";

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
		if (CurrentPhase != GamePhase.Dawn ||
			PowerIdentity.SourceRole != MainRoleType.BearTamer ||
			!StringComparer.Ordinal.Equals(
				PowerIdentity.SourcePowerIdentifier,
				ExpectedSourcePowerIdentifier))
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Bear Tamer growl is structurally invalid.");
		}
	}
}
