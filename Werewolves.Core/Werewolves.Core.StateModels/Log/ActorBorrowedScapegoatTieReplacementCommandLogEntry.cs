using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorBorrowedScapegoatTieReplacementCommandLogEntry
	: GameLogEntryBase
{
	internal required RolePowerInstanceIdentity PowerIdentity { get; init; }
	internal required Guid ActorSetupCardId { get; init; }
	internal required int TriggeringVoteOutcomeLogIndex { get; init; }
	internal required int VoteOrdinal { get; init; }
	internal required string CascadeScopeId { get; init; }

	internal override void EnforceValidity()
	{
		new ActorBorrowedScapegoatTieReplacementCommit(
			PowerIdentity,
			ActorSetupCardId,
			TriggeringVoteOutcomeLogIndex,
			VoteOrdinal,
			CascadeScopeId,
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
				"This Session Mutator does not project Actor borrowed Scapegoat tie replacements.");
		}

		var integrityCommitment =
			actorMutator.ApplyActorBorrowedScapegoatTieReplacement(this);
		return new ActorBorrowedRolePowerCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase,
			IntegrityCommitment = integrityCommitment
		};
	}

	public override string ToString() =>
		"ActorBorrowedScapegoatTieReplacementCommand";
}
