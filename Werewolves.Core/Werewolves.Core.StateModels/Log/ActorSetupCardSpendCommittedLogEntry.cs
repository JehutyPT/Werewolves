using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

internal sealed record ActorSetupCardSpendCommandLogEntry : GameLogEntryBase
{
	internal required ActorBorrowedRolePowerActivation Activation { get; init; }

	internal override void EnforceValidity()
	{
		ArgumentNullException.ThrowIfNull(Activation);
		if (CurrentPhase != GamePhase.Night)
		{
			throw new InvalidOperationException(
				"An Actor setup-card spend can only commit during Night.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		if (mutator is not IActorSessionMutator actorMutator)
		{
			throw new NotSupportedException(
				"This Session Mutator does not project Actor setup-card spends.");
		}

		var publicMarker = new ActorSetupCardSpendCommittedLogEntry
		{
			Timestamp = Timestamp,
			TurnNumber = TurnNumber,
			CurrentPhase = CurrentPhase
		};
		actorMutator.ApplyActorSetupCardSpend(this);
		return publicMarker;
	}

	public override string ToString() => "ActorSetupCardSpendCommand";
}

/// <summary>
/// Non-identifying public-history marker for a committed Actor setup-card spend.
/// Exact card, source Role, acting Player, and activation identities remain in
/// the Moderator recovery projection.
/// </summary>
public sealed record ActorSetupCardSpendCommittedLogEntry : GameLogEntryBase
{
	internal override void EnforceValidity()
	{
		if (CurrentPhase != GamePhase.Night)
		{
			throw new InvalidOperationException(
				"An Actor setup-card spend can only commit during Night.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

	public override string ToString() => "ActorSetupCardSpendCommitted";
}
