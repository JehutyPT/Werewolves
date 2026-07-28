using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records the single end-of-Day expiry of a voter-eligibility restriction.
/// </summary>
public sealed record VoterEligibilityRestrictionExpiredLogEntry
	: GameLogEntryBase,
		IGameFactLogEntry
{
	public required string ScopeId { get; init; }

	internal override void EnforceValidity()
	{
		if (string.IsNullOrWhiteSpace(ScopeId) ||
		    CurrentPhase != Enums.GamePhase.Day)
		{
			throw new InvalidOperationException(
				"The voter-eligibility restriction expiry is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

	public override string ToString() =>
		$"VoterEligibilityRestrictionExpired: scope {ScopeId}";
}
