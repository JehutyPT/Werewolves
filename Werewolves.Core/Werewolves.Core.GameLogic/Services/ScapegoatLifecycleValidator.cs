using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;

namespace Werewolves.Core.GameLogic.Services;

internal static class ScapegoatLifecycleValidator
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
				case ScapegoatVoterRestrictionCommittedLogEntry restriction:
					ValidateVoterRestriction(precedingEntries, restriction);
					break;
				case
					ScapegoatVoterRestrictionAnnouncementAcknowledgedLogEntry
						acknowledgment:
					ValidateAnnouncementAcknowledgment(
						precedingEntries,
						acknowledgment);
					break;
				case ScapegoatVoterRestrictionExpiredLogEntry expiry:
					ValidateVoterRestrictionExpiry(precedingEntries, expiry);
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
		ScapegoatVoterRestrictionCommittedLogEntry restriction)
	{
		var replacement = precedingEntries
			.OfType<ScapegoatTieReplacementLogEntry>()
			.SingleOrDefault(existing =>
				existing.ScopeId == restriction.ScopeId);
		if (replacement == null ||
		    replacement.ScapegoatPlayerId != restriction.ScapegoatPlayerId ||
		    replacement.TurnNumber != restriction.TurnNumber ||
		    restriction.CurrentPhase != GamePhase.Day ||
		    precedingEntries
			    .OfType<ScapegoatVoterRestrictionCommittedLogEntry>()
			    .Any(existing => existing.ScopeId == restriction.ScopeId))
		{
			throw new InvalidOperationException(
				"The Scapegoat voter restriction does not match one unique tie replacement.");
		}
	}

	private static void ValidateAnnouncementAcknowledgment(
		IReadOnlyList<GameLogEntryBase> precedingEntries,
		ScapegoatVoterRestrictionAnnouncementAcknowledgedLogEntry
			acknowledgment)
	{
		var restriction = precedingEntries
			.OfType<ScapegoatVoterRestrictionCommittedLogEntry>()
			.SingleOrDefault(existing =>
				existing.ScopeId == acknowledgment.ScopeId);
		if (restriction == null ||
		    restriction.AnnouncementInstructionId !=
		    acknowledgment.AnnouncementInstructionId ||
		    restriction.TurnNumber != acknowledgment.TurnNumber ||
		    acknowledgment.CurrentPhase != GamePhase.Day ||
		    precedingEntries
			    .OfType<
				    ScapegoatVoterRestrictionAnnouncementAcknowledgedLogEntry>()
			    .Any(existing =>
				    existing.ScopeId == acknowledgment.ScopeId))
		{
			throw new InvalidOperationException(
				"The Scapegoat voter restriction acknowledgment is stale, mismatched, or duplicated.");
		}
	}

	private static void ValidateVoterRestrictionExpiry(
		IReadOnlyList<GameLogEntryBase> precedingEntries,
		ScapegoatVoterRestrictionExpiredLogEntry expiry)
	{
		var restriction = precedingEntries
			.OfType<ScapegoatVoterRestrictionCommittedLogEntry>()
			.SingleOrDefault(existing =>
				existing.ScopeId == expiry.ScopeId);
		var acknowledged = precedingEntries
			.OfType<
				ScapegoatVoterRestrictionAnnouncementAcknowledgedLogEntry>()
			.Any(existing => existing.ScopeId == expiry.ScopeId);
		if (restriction == null ||
		    !acknowledged ||
		    expiry.TurnNumber != restriction.AppliesOnTurnNumber ||
		    expiry.CurrentPhase != GamePhase.Day ||
		    precedingEntries
			    .OfType<ScapegoatVoterRestrictionExpiredLogEntry>()
			    .Any(existing => existing.ScopeId == expiry.ScopeId))
		{
			throw new InvalidOperationException(
				"The Scapegoat voter restriction expiry is stale, premature, or duplicated.");
		}
	}
}
