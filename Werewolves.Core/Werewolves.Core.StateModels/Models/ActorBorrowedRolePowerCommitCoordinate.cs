using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Common non-identifying envelope used to correlate one private Actor-borrowed
/// Role Power projection with its public marker.
/// </summary>
internal readonly record struct ActorBorrowedRolePowerCommitCoordinate(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex)
{
	internal void EnforceValidity()
	{
		PowerIdentity.EnforceValidity();
		if (PowerIdentity.PowerInstanceOrigin !=
				RolePowerInstanceOrigin.Borrowed ||
			ActorSetupCardId == Guid.Empty ||
			Timestamp == default ||
			TurnNumber < 1 ||
			CurrentPhase is not (
				GamePhase.Night or GamePhase.Dawn or GamePhase.Day) ||
			PublicMarkerLogIndex < 0)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Role Power coordinate is structurally invalid.");
		}
	}
}

internal interface IActorBorrowedRolePowerCommit
{
	ActorBorrowedRolePowerCommitCoordinate Coordinate { get; }
}
