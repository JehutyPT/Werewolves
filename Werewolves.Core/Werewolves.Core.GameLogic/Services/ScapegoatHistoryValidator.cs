using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;

namespace Werewolves.Core.GameLogic.Services;

internal static class ScapegoatHistoryValidator
{
	internal static void EnforceValidHistory(IGameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		var precedingEntries = new List<GameLogEntryBase>();
		foreach (var entry in session.GameHistoryLog)
		{
			switch (entry)
			{
				case ScapegoatTieReplacementLogEntry replacement:
					ValidateTieReplacement(precedingEntries, replacement);
					break;
				case VoterEligibilityRestrictionCommittedLogEntry
					{
						SourceRole: MainRoleType.Scapegoat
					} restriction:
					ValidateVoterRestriction(precedingEntries, restriction);
					break;
			}

			precedingEntries.Add(entry);
		}
	}

	private static void ValidateTieReplacement(
		IReadOnlyList<GameLogEntryBase> precedingEntries,
		ScapegoatTieReplacementLogEntry replacement)
	{
		var referencedVote =
			replacement.VoteLogIndex < precedingEntries.Count
				? precedingEntries[replacement.VoteLogIndex] as
					VoteOutcomeReportedLogEntry
				: null;
		var expectedOrdinal = precedingEntries
			.Take(replacement.VoteLogIndex + 1)
			.OfType<VoteOutcomeReportedLogEntry>()
			.Count(vote =>
				vote.TurnNumber == replacement.TurnNumber &&
				vote.CurrentPhase == GamePhase.Day);
		if (referencedVote == null ||
		    referencedVote.ReportedOutcomePlayerId != Guid.Empty ||
		    referencedVote.TurnNumber != replacement.TurnNumber ||
		    referencedVote.CurrentPhase != GamePhase.Day ||
		    expectedOrdinal != replacement.VoteOrdinal ||
		    precedingEntries
			    .OfType<ScapegoatTieReplacementLogEntry>()
			    .Any(existing =>
				    existing.ScopeId == replacement.ScopeId ||
				    existing.VoteLogIndex == replacement.VoteLogIndex))
		{
			throw new InvalidOperationException(
				"The Scapegoat tie replacement does not reference one unique tied Day Vote.");
		}
	}

	private static void ValidateVoterRestriction(
		IReadOnlyList<GameLogEntryBase> precedingEntries,
		VoterEligibilityRestrictionCommittedLogEntry restriction)
	{
		var replacement = precedingEntries
			.OfType<ScapegoatTieReplacementLogEntry>()
			.SingleOrDefault(existing =>
				existing.ScopeId == restriction.ScopeId);
		if (replacement == null ||
		    restriction.CandidatePlayerIds.Contains(
			    replacement.ScapegoatPlayerId) ||
		    replacement.TurnNumber != restriction.TurnNumber ||
		    restriction.CurrentPhase != GamePhase.Day)
		{
			throw new InvalidOperationException(
				"The Scapegoat voter restriction does not match one unique tie replacement.");
		}
	}
}
