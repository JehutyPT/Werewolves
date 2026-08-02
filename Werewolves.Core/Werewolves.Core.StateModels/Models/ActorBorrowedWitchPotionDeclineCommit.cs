using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable projection of one explicitly declined Actor-borrowed Witch
/// potion offer. The offered resource lineage remains outside public Game
/// History and is not a One-Use Resource spend.
/// </summary>
internal sealed record ActorBorrowedWitchPotionDeclineCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	OneUseRolePowerResourceIdentity OfferedResourceIdentity,
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
		OfferedResourceIdentity.EnforceValidity();
		if (CurrentPhase != GamePhase.Night ||
			PowerIdentity.SourceRole != MainRoleType.Witch ||
			!StringComparer.Ordinal.Equals(
				PowerIdentity.SourcePowerIdentifier,
				ActorBorrowedWitchPotionUseCommit
					.ExpectedSourcePowerIdentifier) ||
			OfferedResourceIdentity.ActingPlayerId !=
				PowerIdentity.ActingPlayerId ||
			OfferedResourceIdentity.SourceRole != PowerIdentity.SourceRole ||
			!StringComparer.Ordinal.Equals(
				OfferedResourceIdentity.SourcePowerIdentifier,
				PowerIdentity.SourcePowerIdentifier) ||
			OfferedResourceIdentity.PowerInstanceId !=
				PowerIdentity.PowerInstanceId ||
			OfferedResourceIdentity.PowerInstanceOrigin !=
				PowerIdentity.PowerInstanceOrigin ||
			OfferedResourceIdentity.OneUseResourceId !=
				ActorBorrowedWitchPotionUseCommit.HealingResourceId &&
			OfferedResourceIdentity.OneUseResourceId !=
				ActorBorrowedWitchPotionUseCommit.PoisonResourceId)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Witch potion decline is structurally invalid.");
		}
	}

	internal static NightActionType GetOfferedActionType(
		OneUseRolePowerResourceIdentity resourceIdentity) =>
		ActorBorrowedWitchPotionUseCommit.GetActionType(resourceIdentity);
}
