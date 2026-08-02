using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorBorrowedRolePowerActivationExpiryCommandLogEntry
	: GameLogEntryBase
{
	internal required ActorBorrowedRolePowerActivation ExpectedActivation
	{
		get;
		init;
	}

	internal override void EnforceValidity()
	{
		ArgumentNullException.ThrowIfNull(ExpectedActivation);
		if (CurrentPhase != GamePhase.Night)
		{
			throw new InvalidOperationException(
				"An Actor borrowed Role Power activation can only expire during Night.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		if (mutator is not IActorSessionMutator actorMutator)
		{
			throw new NotSupportedException(
				"This Session Mutator does not project Actor borrowed Role Power activation expiry.");
		}

		var publicMarker =
			new ActorBorrowedRolePowerActivationExpiredLogEntry
			{
				Timestamp = Timestamp,
				TurnNumber = TurnNumber,
				CurrentPhase = CurrentPhase
			};
		actorMutator.ApplyActorBorrowedRolePowerActivationExpiry(this);
		return publicMarker;
	}

	public override string ToString() =>
		"ActorBorrowedRolePowerActivationExpiryCommand";
}

/// <summary>
/// Non-identifying public-history marker for Actor's opening-slot expiry.
/// </summary>
public sealed record ActorBorrowedRolePowerActivationExpiredLogEntry
	: GameLogEntryBase
{
	internal override void EnforceValidity()
	{
		if (CurrentPhase != GamePhase.Night)
		{
			throw new InvalidOperationException(
				"An Actor borrowed Role Power activation can only expire during Night.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

	public override string ToString() =>
		"ActorBorrowedRolePowerActivationExpired";
}
