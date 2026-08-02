using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable projection of one Day observation of the signal configured
/// by an Actor-borrowed Stuttering Judge power.
/// </summary>
internal sealed record ActorBorrowedStutteringJudgeSignalObservationCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	bool SignalOccurred,
	OneUseRolePowerResourceIdentity? SpentResourceIdentity,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex) : IActorBorrowedRolePowerCommit
{
	internal static readonly Guid ExpectedOneUseResourceId =
		Guid.Parse("85ff5eb7-61cf-4b33-894c-b9c37d58bace");

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
		if (CurrentPhase != GamePhase.Day ||
			PowerIdentity.SourceRole != MainRoleType.StutteringJudge ||
			!StringComparer.Ordinal.Equals(
				PowerIdentity.SourcePowerIdentifier,
				ActorBorrowedStutteringJudgeSignalSetupCommit
					.ExpectedSourcePowerIdentifier) ||
			SignalOccurred != (SpentResourceIdentity is not null))
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Stuttering Judge signal observation is structurally invalid.");
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
				PowerIdentity.PowerInstanceOrigin ||
			spentResource.OneUseResourceId != ExpectedOneUseResourceId)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Stuttering Judge spent Resource must be its fixed borrowed one-use Resource.");
		}
	}
}
