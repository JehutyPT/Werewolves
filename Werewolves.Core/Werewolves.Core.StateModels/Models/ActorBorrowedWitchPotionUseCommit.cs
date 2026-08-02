using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable projection of one Actor-borrowed Witch potion use.
/// Resource lineage and target remain outside public Game History.
/// </summary>
internal sealed record ActorBorrowedWitchPotionUseCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	OneUseRolePowerResourceIdentity SpentResourceIdentity,
	Guid TargetPlayerId,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex) : IActorBorrowedRolePowerCommit
{
	internal const string ExpectedSourcePowerIdentifier = "witch-potions";
	internal static readonly Guid HealingResourceId =
		Guid.Parse("a9b9d885-3edc-4671-bec8-1ddabbe4de3e");
	internal static readonly Guid PoisonResourceId =
		Guid.Parse("da29bd31-bbe8-4abc-bb12-87b15df6df38");

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
		SpentResourceIdentity.EnforceValidity();
		if (CurrentPhase != GamePhase.Night ||
			PowerIdentity.SourceRole != MainRoleType.Witch ||
			!StringComparer.Ordinal.Equals(
				PowerIdentity.SourcePowerIdentifier,
				ExpectedSourcePowerIdentifier) ||
			SpentResourceIdentity.ActingPlayerId !=
				PowerIdentity.ActingPlayerId ||
			SpentResourceIdentity.SourceRole != PowerIdentity.SourceRole ||
			!StringComparer.Ordinal.Equals(
				SpentResourceIdentity.SourcePowerIdentifier,
				PowerIdentity.SourcePowerIdentifier) ||
			SpentResourceIdentity.PowerInstanceId !=
				PowerIdentity.PowerInstanceId ||
			SpentResourceIdentity.PowerInstanceOrigin !=
				PowerIdentity.PowerInstanceOrigin ||
			SpentResourceIdentity.OneUseResourceId != HealingResourceId &&
			SpentResourceIdentity.OneUseResourceId != PoisonResourceId ||
			TargetPlayerId == Guid.Empty)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Witch potion use is structurally invalid.");
		}
	}

	internal static NightActionType GetActionType(
		OneUseRolePowerResourceIdentity resourceIdentity) =>
		resourceIdentity.OneUseResourceId == HealingResourceId
			? NightActionType.WitchSave
			: resourceIdentity.OneUseResourceId == PoisonResourceId
				? NightActionType.WitchKill
				: NightActionType.Unknown;
}
