using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorBorrowedVillageIdiotPardonCommandLogEntry
	: GameLogEntryBase
{
	internal required RolePowerInstanceIdentity PowerIdentity { get; init; }
	internal required Guid ActorSetupCardId { get; init; }
	internal required OneUseRolePowerResourceIdentity SpentResourceIdentity
		{ get; init; }

	internal override void EnforceValidity()
	{
		new ActorBorrowedVillageIdiotPardonCommit(
			PowerIdentity,
			ActorSetupCardId,
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
				"This Session Mutator does not project Actor borrowed Village Idiot pardons.");
		}

		var integrityCommitment =
			actorMutator.ApplyActorBorrowedVillageIdiotPardon(this);
		return new ActorBorrowedRolePowerCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase,
			IntegrityCommitment = integrityCommitment
		};
	}

	public override string ToString() =>
		"ActorBorrowedVillageIdiotPardonCommand";
}
