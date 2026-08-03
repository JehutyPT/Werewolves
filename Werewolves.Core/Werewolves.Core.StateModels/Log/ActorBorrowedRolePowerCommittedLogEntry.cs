using System.Text.Json.Serialization;
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

		var integrityCommitment =
			actorMutator.ApplyActorBorrowedSeerCheck(this);
		return new ActorBorrowedRolePowerCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase,
			IntegrityCommitment = integrityCommitment
		};
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
	/// <summary>
	/// Opaque session-keyed commitment to the complete private commit. It carries
	/// no independently enumerable Actor, source, action, target, resource, or
	/// result fact.
	/// </summary>
	[JsonInclude]
	internal string IntegrityCommitment { get; init; } = string.Empty;

	internal override void EnforceValidity()
	{
		if (CurrentPhase is not (
				GamePhase.Night or GamePhase.Dawn or GamePhase.Day) ||
			!ActorBorrowedRolePowerCommitment.IsWellFormed(IntegrityCommitment))
		{
			throw new InvalidOperationException(
				"An Actor borrowed Role Power marker is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

	public override string ToString() => "ActorBorrowedRolePowerCommitted";
}
