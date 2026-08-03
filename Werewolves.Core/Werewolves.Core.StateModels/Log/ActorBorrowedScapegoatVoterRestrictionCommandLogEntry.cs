using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorBorrowedScapegoatVoterRestrictionCommandLogEntry
	: GameLogEntryBase
{
	internal required RolePowerInstanceIdentity PowerIdentity { get; init; }
	internal required Guid ActorSetupCardId { get; init; }
	internal required int TieReplacementPublicMarkerLogIndex { get; init; }
	internal required string CascadeScopeId { get; init; }
	internal required IReadOnlyList<Guid> CandidatePlayerIds { get; init; }
	internal required IReadOnlyList<Guid> PermittedVoterIds { get; init; }
	internal required int AppliesOnTurnNumber { get; init; }
	internal required Guid AnnouncementInstructionId { get; init; }

	internal override void EnforceValidity()
	{
		new ActorBorrowedScapegoatVoterRestrictionCommit(
			PowerIdentity,
			ActorSetupCardId,
			TieReplacementPublicMarkerLogIndex,
			CascadeScopeId,
			CandidatePlayerIds,
			PermittedVoterIds,
			AppliesOnTurnNumber,
			AnnouncementInstructionId,
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
				"This Session Mutator does not project Actor borrowed Scapegoat voter restrictions.");
		}

		var integrityCommitment =
			actorMutator.ApplyActorBorrowedScapegoatVoterRestriction(this);
		return new ActorBorrowedRolePowerCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase,
			IntegrityCommitment = integrityCommitment
		};
	}

	public override string ToString() =>
		"ActorBorrowedScapegoatVoterRestrictionCommand";
}
