using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable projection of Actor's borrowed Village Idiot pardon.
/// Source, activation, setup-card, and Resource lineage remain outside public
/// Game History while the voting consequence stays publicly observable.
/// </summary>
internal sealed record ActorBorrowedVillageIdiotPardonCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	OneUseRolePowerResourceIdentity SpentResourceIdentity,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex) : IActorBorrowedRolePowerCommit
{
	internal const string ExpectedSourcePowerIdentifier =
		"village-idiot-pardon";
	internal static readonly Guid ExpectedResourceId =
		Guid.Parse("4f86b827-47c4-48f8-9ba4-29028d5c75a0");

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
		if (CurrentPhase != GamePhase.Day ||
			PowerIdentity.SourceRole != MainRoleType.VillageIdiot ||
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
			SpentResourceIdentity.OneUseResourceId != ExpectedResourceId)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Village Idiot pardon is structurally invalid.");
		}
	}
}
