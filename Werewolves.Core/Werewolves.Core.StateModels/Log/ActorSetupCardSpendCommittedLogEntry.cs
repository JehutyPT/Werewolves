using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

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
