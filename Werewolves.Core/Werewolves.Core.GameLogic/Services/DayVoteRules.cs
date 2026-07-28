using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Services;

/// <summary>
/// Owns durable rules that modify how Day Votes are conducted.
/// Roles install rules through their hooks; the Day flow consumes them.
/// </summary>
internal static class DayVoteRules
{
	internal static void CommitOneUseDayAction(
		GameSession session,
		DayPowerType actionType,
		OneUseRolePowerResourceIdentity resourceIdentity,
		IReadOnlyCollection<Guid>? targetIds = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		resourceIdentity.EnforceValidity();
		if (actionType == DayPowerType.Unknown)
		{
			throw new ArgumentOutOfRangeException(nameof(actionType));
		}

		session.CommitGameFact(context =>
			new OneUseRolePowerDayActionCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				ActionType = actionType,
				TargetIds = targetIds?.ToList(),
				ResourceIdentity = resourceIdentity
			});
	}

	internal static void CommitVoterEligibilityRestriction(
		GameSession session,
		string scopeId,
		MainRoleType sourceRole,
		IReadOnlyCollection<Guid> candidatePlayerIds,
		IReadOnlyCollection<Guid> permittedVoterIds,
		int appliesOnTurnNumber,
		Guid announcementInstructionId)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(candidatePlayerIds);
		ArgumentNullException.ThrowIfNull(permittedVoterIds);

		session.CommitGameFact(context =>
			new VoterEligibilityRestrictionCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				ScopeId = scopeId,
				SourceRole = sourceRole,
				CandidatePlayerIds = candidatePlayerIds.ToImmutableArray(),
				PermittedVoterIds = permittedVoterIds.ToImmutableArray(),
				AppliesOnTurnNumber = appliesOnTurnNumber,
				AnnouncementInstructionId = announcementInstructionId
			});
	}

	internal static void AcknowledgeVoterEligibilityRestrictionAnnouncement(
		GameSession session,
		string scopeId,
		Guid announcementInstructionId)
	{
		ArgumentNullException.ThrowIfNull(session);
		session.CommitGameFact(context =>
			new VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				ScopeId = scopeId,
				AnnouncementInstructionId = announcementInstructionId
			});
	}

	internal static VoterEligibilityRestrictionCommittedLogEntry?
		GetVoterEligibilityRestriction(
			IGameSession session,
			string scopeId)
	{
		ArgumentNullException.ThrowIfNull(session);
		return session.GameHistoryLog
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.SingleOrDefault(entry => entry.ScopeId == scopeId);
	}

	internal static bool IsVoterEligibilityRestrictionAnnouncementAcknowledged(
		IGameSession session,
		string scopeId,
		Guid announcementInstructionId)
	{
		ArgumentNullException.ThrowIfNull(session);
		return session.GameHistoryLog
			.OfType<VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry>()
			.Any(entry =>
				entry.ScopeId == scopeId &&
				entry.AnnouncementInstructionId == announcementInstructionId);
	}

	internal static VoterEligibilityRestrictionCommittedLogEntry?
		GetActiveVoterEligibilityRestriction(IGameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		return session.GameHistoryLog
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.SingleOrDefault(entry =>
				entry.AppliesOnTurnNumber == session.TurnNumber &&
				!session.GameHistoryLog
					.OfType<VoterEligibilityRestrictionExpiredLogEntry>()
					.Any(expiry => expiry.ScopeId == entry.ScopeId));
	}

	internal static IReadOnlyList<IPlayer> GetEffectiveVoters(
		IGameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		var eligible = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.State.HasVotingRight);
		var restriction = GetActiveVoterEligibilityRestriction(session);
		if (restriction != null)
		{
			var permittedIds = restriction.PermittedVoterIds.ToHashSet();
			eligible = eligible.Where(player =>
				permittedIds.Contains(player.Id));
		}

		return eligible.ToArray();
	}

	internal static bool ShouldConductConsecutiveVote(IGameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		var currentTurn = NumberRangeConstraint.Exact(session.TurnNumber);
		var hasConsecutiveVoteRule =
			GameSessionQueries.FindLogEntries<DayActionLogEntry>(
					session,
					currentTurn,
					GamePhase.Day,
					entry =>
						entry.ActionType == DayPowerType.JudgeExtraVote)
				.Any();
		var currentTurnVoteCount =
			GameSessionQueries.FindLogEntries<VoteOutcomeReportedLogEntry>(
					session,
					currentTurn,
					GamePhase.Day)
				.Count();

		return hasConsecutiveVoteRule && currentTurnVoteCount == 1;
	}

	internal static void ExpireActiveVoterEligibilityRestriction(
		GameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		var restriction = GetActiveVoterEligibilityRestriction(session);
		if (restriction == null)
		{
			return;
		}

		session.CommitGameFact(context =>
			new VoterEligibilityRestrictionExpiredLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				ScopeId = restriction.ScopeId
			});
	}

	internal static void EnforceValidHistory(IGameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		var precedingEntries = new List<GameLogEntryBase>();
		foreach (var entry in session.GameHistoryLog)
		{
			switch (entry)
			{
				case VoterEligibilityRestrictionCommittedLogEntry restriction:
					ValidateRestriction(precedingEntries, restriction);
					break;
				case VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry
					acknowledgment:
					ValidateAcknowledgment(precedingEntries, acknowledgment);
					break;
				case VoterEligibilityRestrictionExpiredLogEntry expiry:
					ValidateExpiry(precedingEntries, expiry);
					break;
			}

			precedingEntries.Add(entry);
		}
	}

	private static void ValidateRestriction(
		IReadOnlyCollection<GameLogEntryBase> precedingEntries,
		VoterEligibilityRestrictionCommittedLogEntry restriction)
	{
		if (precedingEntries
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.Any(existing => existing.ScopeId == restriction.ScopeId))
		{
			throw new InvalidOperationException(
				"The voter-eligibility restriction scope is duplicated.");
		}
	}

	private static void ValidateAcknowledgment(
		IReadOnlyCollection<GameLogEntryBase> precedingEntries,
		VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry
			acknowledgment)
	{
		var restriction = precedingEntries
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.SingleOrDefault(existing =>
				existing.ScopeId == acknowledgment.ScopeId);
		if (restriction == null ||
		    restriction.AnnouncementInstructionId !=
		    acknowledgment.AnnouncementInstructionId ||
		    restriction.TurnNumber != acknowledgment.TurnNumber ||
		    precedingEntries
			    .OfType<
				    VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry>()
			    .Any(existing =>
				    existing.ScopeId == acknowledgment.ScopeId))
		{
			throw new InvalidOperationException(
				"The voter-eligibility restriction acknowledgment is stale, mismatched, or duplicated.");
		}
	}

	private static void ValidateExpiry(
		IReadOnlyCollection<GameLogEntryBase> precedingEntries,
		VoterEligibilityRestrictionExpiredLogEntry expiry)
	{
		var restriction = precedingEntries
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.SingleOrDefault(existing => existing.ScopeId == expiry.ScopeId);
		var acknowledged = precedingEntries
			.OfType<VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry>()
			.Any(existing => existing.ScopeId == expiry.ScopeId);
		if (restriction == null ||
		    !acknowledged ||
		    expiry.TurnNumber != restriction.AppliesOnTurnNumber ||
		    precedingEntries
			    .OfType<VoterEligibilityRestrictionExpiredLogEntry>()
			    .Any(existing => existing.ScopeId == expiry.ScopeId))
		{
			throw new InvalidOperationException(
				"The voter-eligibility restriction expiry is stale, premature, or duplicated.");
		}
	}
}
