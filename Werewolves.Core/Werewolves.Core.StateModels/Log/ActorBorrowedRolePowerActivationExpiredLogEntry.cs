using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

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
