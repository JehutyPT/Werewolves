using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records the single end-of-Day expiry of a Scapegoat voter restriction.
/// </summary>
public sealed record ScapegoatVoterRestrictionExpiredLogEntry
	: GameLogEntryBase
{
	public required string ScopeId { get; init; }

	internal void EnforceValidity()
	{
		if (string.IsNullOrWhiteSpace(ScopeId))
		{
			throw new InvalidOperationException(
				"The Scapegoat voter restriction expiry is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;
}
