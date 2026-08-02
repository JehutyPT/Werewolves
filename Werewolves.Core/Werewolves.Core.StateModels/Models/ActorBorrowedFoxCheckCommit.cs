using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable projection of one Actor-borrowed Fox neighborhood check.
/// Center, result, and conditional Resource spend never enter public Game History.
/// </summary>
internal sealed record ActorBorrowedFoxCheckCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	Guid CenterPlayerId,
	FactionAgentKnowledge NeighborhoodAgentKnowledge,
	OneUseRolePowerResourceIdentity? SpentResourceIdentity,
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
		var isAffirmative = NeighborhoodAgentKnowledge ==
			FactionAgentKnowledge.KnownAgent;
		var isNegative = NeighborhoodAgentKnowledge ==
			FactionAgentKnowledge.KnownNonAgent;
		if (CurrentPhase != GamePhase.Night ||
			PowerIdentity.SourceRole != MainRoleType.Fox ||
			CenterPlayerId == Guid.Empty ||
			!isAffirmative && !isNegative ||
			isAffirmative && SpentResourceIdentity is not null ||
			isNegative && SpentResourceIdentity is null)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Fox check is structurally invalid.");
		}

		if (SpentResourceIdentity is not { } spentResource)
		{
			return;
		}

		spentResource.EnforceValidity();
		if (spentResource.ActingPlayerId != PowerIdentity.ActingPlayerId ||
			spentResource.SourceRole != PowerIdentity.SourceRole ||
			!StringComparer.Ordinal.Equals(
				spentResource.SourcePowerIdentifier,
				PowerIdentity.SourcePowerIdentifier) ||
			spentResource.PowerInstanceId != PowerIdentity.PowerInstanceId ||
			spentResource.PowerInstanceOrigin !=
				PowerIdentity.PowerInstanceOrigin)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Fox spent Resource must belong to the committed concrete Role Power.");
		}
	}
}
