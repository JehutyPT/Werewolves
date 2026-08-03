using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorBorrowedElderResistanceCommandLogEntry
	: GameLogEntryBase
{
	internal required RolePowerInstanceIdentity PowerIdentity { get; init; }
	internal required Guid ActorSetupCardId { get; init; }
	internal required Guid TargetPlayerId { get; init; }
	internal required int TriggeringNightActionLogIndex { get; init; }
	internal required int? RestoringWitchSaveLogIndex { get; init; }

	internal override void EnforceValidity()
	{
		new ActorBorrowedElderResistanceCommit(
			PowerIdentity,
			ActorSetupCardId,
			TargetPlayerId,
			TriggeringNightActionLogIndex,
			RestoringWitchSaveLogIndex,
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
				"This Session Mutator does not project Actor borrowed Elder resistance.");
		}

		var integrityCommitment =
			actorMutator.ApplyActorBorrowedElderResistance(this);
		return new ActorBorrowedRolePowerCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase,
			IntegrityCommitment = integrityCommitment
		};
	}

	public override string ToString() =>
		"ActorBorrowedElderResistanceCommand";
}
