using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorBorrowedHunterFinalShotCommandLogEntry
	: GameLogEntryBase
{
	internal required RolePowerInstanceIdentity PowerIdentity { get; init; }
	internal required Guid ActorSetupCardId { get; init; }
	internal required string CascadeScopeId { get; init; }
	internal required IReadOnlyList<Guid> TriggeringPlayerIds { get; init; }
	internal required Guid TargetPlayerId { get; init; }

	internal override void EnforceValidity()
	{
		new ActorBorrowedHunterFinalShotCommit(
			PowerIdentity,
			ActorSetupCardId,
			CascadeScopeId,
			TriggeringPlayerIds,
			TargetPlayerId,
			Timestamp,
			TurnNumber,
			CurrentPhase,
			PublicMarkerLogIndex: 0).EnforceValidity();
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		if (mutator is not IActorSessionMutator actorMutator)
		{
			throw new NotSupportedException(
				"This Session Mutator does not project Actor borrowed Hunter final shots.");
		}

		var integrityCommitment =
			actorMutator.ApplyActorBorrowedHunterFinalShot(this);
		return new ActorBorrowedRolePowerCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase,
			IntegrityCommitment = integrityCommitment
		};
	}

	public override string ToString() =>
		"ActorBorrowedHunterFinalShotCommand";
}
