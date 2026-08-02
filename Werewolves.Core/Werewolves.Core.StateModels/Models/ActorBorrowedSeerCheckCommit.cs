using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable projection of one Actor-borrowed Seer check.
/// Source, activation, target, and result never enter public Game History.
/// </summary>
internal sealed record ActorBorrowedSeerCheckCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	Guid TargetPlayerId,
	FactionAgentKnowledge TargetAgentKnowledge,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex) : IActorBorrowedRolePowerCommit
{
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
		if (CurrentPhase != GamePhase.Night ||
		    PowerIdentity.SourceRole != MainRoleType.Seer ||
		    TargetPlayerId == Guid.Empty ||
		    TargetPlayerId == PowerIdentity.ActingPlayerId ||
		    TargetAgentKnowledge is not
			    (FactionAgentKnowledge.KnownAgent or
			     FactionAgentKnowledge.KnownNonAgent))
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Seer check is structurally invalid.");
		}
	}
}
