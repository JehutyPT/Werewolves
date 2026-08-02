using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorBorrowedSeerCheckCommandLogEntry
	: GameLogEntryBase
{
	internal required RolePowerInstanceIdentity PowerIdentity { get; init; }
	internal required Guid ActorSetupCardId { get; init; }
	internal required Guid TargetPlayerId { get; init; }
	internal required FactionAgentKnowledge TargetAgentKnowledge { get; init; }

	internal override void EnforceValidity()
	{
		new ActorBorrowedSeerCheckCommit(
			PowerIdentity,
			ActorSetupCardId,
			TargetPlayerId,
			TargetAgentKnowledge,
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
				"This Session Mutator does not project Actor borrowed Seer checks.");
		}

		var publicMarker = new ActorBorrowedRolePowerCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase
		};
		actorMutator.ApplyActorBorrowedSeerCheck(this);
		return publicMarker;
	}

	public override string ToString() => "ActorBorrowedSeerCheckCommand";
}

/// <summary>
/// Non-identifying public-history marker for one committed Actor borrowed
/// Role Power. All source, action, holder, activation, card, resource, target,
/// and result facts remain in the private recovery projection.
/// </summary>
public sealed record ActorBorrowedRolePowerCommittedLogEntry : GameLogEntryBase
{
	internal override void EnforceValidity()
	{
		if (CurrentPhase is not (GamePhase.Night or GamePhase.Day))
		{
			throw new InvalidOperationException(
				"An Actor borrowed Role Power can only commit during Night or Day.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

	public override string ToString() => "ActorBorrowedRolePowerCommitted";
}
