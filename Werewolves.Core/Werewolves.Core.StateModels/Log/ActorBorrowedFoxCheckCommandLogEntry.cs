using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorBorrowedFoxCheckCommandLogEntry
	: GameLogEntryBase
{
	internal required RolePowerInstanceIdentity PowerIdentity { get; init; }
	internal required Guid ActorSetupCardId { get; init; }
	internal required Guid CenterPlayerId { get; init; }
	internal required FactionAgentKnowledge NeighborhoodAgentKnowledge
		{ get; init; }
	internal OneUseRolePowerResourceIdentity? SpentResourceIdentity
		{ get; init; }

	internal override void EnforceValidity()
	{
		new ActorBorrowedFoxCheckCommit(
			PowerIdentity,
			ActorSetupCardId,
			CenterPlayerId,
			NeighborhoodAgentKnowledge,
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
				"This Session Mutator does not project Actor borrowed Fox checks.");
		}

		var integrityCommitment = actorMutator.ApplyActorBorrowedFoxCheck(this);
		return new ActorBorrowedRolePowerCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase,
			IntegrityCommitment = integrityCommitment
		};
	}

	public override string ToString() => "ActorBorrowedFoxCheckCommand";
}
