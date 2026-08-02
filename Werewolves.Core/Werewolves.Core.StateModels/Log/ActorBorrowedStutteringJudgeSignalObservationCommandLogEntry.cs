using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorBorrowedStutteringJudgeSignalObservationCommandLogEntry
	: GameLogEntryBase
{
	internal required RolePowerInstanceIdentity PowerIdentity { get; init; }
	internal required Guid ActorSetupCardId { get; init; }
	internal required bool SignalOccurred { get; init; }
	internal OneUseRolePowerResourceIdentity? SpentResourceIdentity { get; init; }

	internal override void EnforceValidity()
	{
		new ActorBorrowedStutteringJudgeSignalObservationCommit(
			PowerIdentity,
			ActorSetupCardId,
			SignalOccurred,
			SpentResourceIdentity,
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
				"This Session Mutator does not project Actor borrowed Stuttering Judge signal observations.");
		}

		var integrityCommitment = actorMutator
			.ApplyActorBorrowedStutteringJudgeSignalObservation(this);
		return new ActorBorrowedRolePowerCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase,
			IntegrityCommitment = integrityCommitment
		};
	}

	public override string ToString() =>
		"ActorBorrowedStutteringJudgeSignalObservationCommand";
}
