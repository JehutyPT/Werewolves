using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable projection of Actor's borrowed Rusty Sword schedule. The
/// borrowed source, triggering elimination, and bound target remain behind the
/// generic Actor borrowed Role Power marker.
/// </summary>
internal sealed record ActorBorrowedKnightRustySwordScheduleCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	Guid TargetPlayerId,
	int WerewolfAttackEliminationLogIndex,
	string CascadeScopeId,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex) : IActorBorrowedRolePowerCommit
{
	internal const string ExpectedSourcePowerIdentifier =
		"knight-rusty-sword-disease";

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
		if (PowerIdentity.SourceRole !=
				MainRoleType.KnightWithRustySword ||
			!StringComparer.Ordinal.Equals(
				PowerIdentity.SourcePowerIdentifier,
				ExpectedSourcePowerIdentifier) ||
			TargetPlayerId == Guid.Empty ||
			TargetPlayerId == PowerIdentity.ActingPlayerId ||
			WerewolfAttackEliminationLogIndex < 0 ||
			string.IsNullOrWhiteSpace(CascadeScopeId) ||
			CurrentPhase != GamePhase.Dawn)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Rusty Sword schedule is structurally invalid.");
		}
	}
}
