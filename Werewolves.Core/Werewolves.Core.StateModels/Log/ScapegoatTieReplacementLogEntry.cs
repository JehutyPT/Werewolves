using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records that the Scapegoat replaced one tied Day Vote outcome.
/// The raw tied Vote remains a separate, unchanged fact.
/// </summary>
public sealed record ScapegoatTieReplacementLogEntry
	: GameLogEntryBase,
		IGameFactLogEntry
{
	public required Guid ScapegoatPlayerId { get; init; }
	public required int VoteOrdinal { get; init; }
	public required int VoteLogIndex { get; init; }
	public required string ScopeId { get; init; }

	internal override void EnforceValidity()
	{
		if (ScapegoatPlayerId == Guid.Empty ||
		    VoteOrdinal <= 0 ||
		    VoteLogIndex < 0 ||
		    string.IsNullOrWhiteSpace(ScopeId) ||
		    ScopeId != $"Day:{TurnNumber}:Vote:{VoteOrdinal}" ||
		    CurrentPhase != Enums.GamePhase.Day)
		{
			throw new InvalidOperationException(
				"The Scapegoat tie replacement is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

	public override string ToString() =>
		$"ScapegoatTieReplacement: {ScapegoatPlayerId}, Vote {VoteOrdinal}";
}
