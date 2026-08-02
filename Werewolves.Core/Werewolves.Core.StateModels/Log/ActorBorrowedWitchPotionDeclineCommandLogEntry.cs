using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorBorrowedWitchPotionDeclineCommandLogEntry
	: GameLogEntryBase
{
	internal required RolePowerInstanceIdentity PowerIdentity { get; init; }
	internal required Guid ActorSetupCardId { get; init; }
	internal required OneUseRolePowerResourceIdentity OfferedResourceIdentity
		{ get; init; }

	internal override void EnforceValidity()
	{
		new ActorBorrowedWitchPotionDeclineCommit(
			PowerIdentity,
			ActorSetupCardId,
			OfferedResourceIdentity,
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
				"This Session Mutator does not project Actor borrowed Witch potion declines.");
		}

		var integrityCommitment =
			actorMutator.ApplyActorBorrowedWitchPotionDecline(this);
		return new ActorBorrowedRolePowerCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase,
			IntegrityCommitment = integrityCommitment
		};
	}

	public override string ToString() =>
		"ActorBorrowedWitchPotionDeclineCommand";
}
