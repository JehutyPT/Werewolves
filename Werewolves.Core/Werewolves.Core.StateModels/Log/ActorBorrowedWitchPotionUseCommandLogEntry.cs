using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorBorrowedWitchPotionUseCommandLogEntry
	: GameLogEntryBase
{
	internal required RolePowerInstanceIdentity PowerIdentity { get; init; }
	internal required Guid ActorSetupCardId { get; init; }
	internal required OneUseRolePowerResourceIdentity SpentResourceIdentity
		{ get; init; }
	internal required Guid TargetPlayerId { get; init; }

	internal override void EnforceValidity()
	{
		new ActorBorrowedWitchPotionUseCommit(
			PowerIdentity,
			ActorSetupCardId,
			SpentResourceIdentity,
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
				"This Session Mutator does not project Actor borrowed Witch potion uses.");
		}

		var publicMarker = new ActorBorrowedRolePowerCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase
		};
		actorMutator.ApplyActorBorrowedWitchPotionUse(this);
		return publicMarker;
	}

	public override string ToString() =>
		"ActorBorrowedWitchPotionUseCommand";
}
