using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Private durable projection of Actor's borrowed Hunter final shot. The
/// source, activation, cascade, and target correlation remain outside public
/// Game History behind the generic Actor borrowed Role Power marker.
/// </summary>
internal sealed record ActorBorrowedHunterFinalShotCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	string CascadeScopeId,
	IReadOnlyList<Guid> TriggeringPlayerIds,
	Guid TargetPlayerId,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex) : IActorBorrowedRolePowerCommit
{
	internal const string ExpectedSourcePowerIdentifier = "hunter-final-shot";

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
		if (PowerIdentity.SourceRole != MainRoleType.Hunter ||
			!StringComparer.Ordinal.Equals(
				PowerIdentity.SourcePowerIdentifier,
				ExpectedSourcePowerIdentifier) ||
			string.IsNullOrWhiteSpace(CascadeScopeId) ||
			TriggeringPlayerIds is not { Count: > 0 } ||
			TriggeringPlayerIds.Any(playerId => playerId == Guid.Empty) ||
			TriggeringPlayerIds.Distinct().Count() != TriggeringPlayerIds.Count ||
			!TriggeringPlayerIds.Contains(PowerIdentity.ActingPlayerId) ||
			TargetPlayerId == Guid.Empty ||
			TargetPlayerId == PowerIdentity.ActingPlayerId ||
			TriggeringPlayerIds.Contains(TargetPlayerId))
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Hunter final shot is structurally invalid.");
		}
	}
}
