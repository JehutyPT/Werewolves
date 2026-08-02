using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable projection of one Actor-borrowed Stuttering Judge signal setup.
/// The signal content and all source lineage remain outside public Game History.
/// </summary>
internal sealed record ActorBorrowedStutteringJudgeSignalSetupCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex) : IActorBorrowedRolePowerCommit
{
	internal const string ExpectedSourcePowerIdentifier =
		"stuttering-judge-consecutive-vote";

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
			PowerIdentity.SourceRole != MainRoleType.StutteringJudge ||
			!StringComparer.Ordinal.Equals(
				PowerIdentity.SourcePowerIdentifier,
				ExpectedSourcePowerIdentifier))
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Stuttering Judge signal setup is structurally invalid.");
		}
	}
}
