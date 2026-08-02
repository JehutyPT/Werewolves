using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorBorrowedCupidLoversCommandLogEntry
	: GameLogEntryBase
{
	internal required RolePowerInstanceIdentity PowerIdentity { get; init; }
	internal required Guid ActorSetupCardId { get; init; }
	internal required Guid FirstPlayerId { get; init; }
	internal required Guid SecondPlayerId { get; init; }
	internal required ActorBorrowedCupidLoversDisposition Disposition { get; init; }

	internal override void EnforceValidity()
	{
		new ActorBorrowedCupidLoversCommit(
			PowerIdentity,
			ActorSetupCardId,
			FirstPlayerId,
			SecondPlayerId,
			Disposition,
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
				"This Session Mutator does not project Actor borrowed Cupid Lovers commits.");
		}

		var publicMarker = new ActorBorrowedRolePowerCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase
		};
		actorMutator.ApplyActorBorrowedCupidLovers(this);
		return publicMarker;
	}

	public override string ToString() => "ActorBorrowedCupidLoversCommand";
}
