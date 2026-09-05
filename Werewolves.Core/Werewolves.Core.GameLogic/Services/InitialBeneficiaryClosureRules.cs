using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Services;

internal enum InitialBeneficiaryClosureReadiness
{
	Incomplete = 0,
	Ready = 1,
	AlreadyCommitted = 2
}

internal enum InitialBeneficiaryClosureResult
{
	Incomplete = 0,
	Committed = 1,
	AlreadyCommitted = 2
}

internal sealed record InitialBeneficiaryClosurePrerequisite
{
	public InitialBeneficiaryClosurePrerequisite(
		string identifier,
		bool isComplete)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
		Identifier = identifier;
		IsComplete = isComplete;
	}

	public string Identifier { get; }

	public bool IsComplete { get; }
}

internal sealed class InitialBeneficiaryClosureDeferredResult
{
	private InitialBeneficiaryClosureDeferredResult(
		string identifier,
		bool isComplete,
		IReadOnlyCollection<FactionFact> facts,
		IReadOnlyCollection<Guid> privatelyEstablishedBeneficiaryPlayerIds)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
		ArgumentNullException.ThrowIfNull(facts);
		ArgumentNullException.ThrowIfNull(
			privatelyEstablishedBeneficiaryPlayerIds);
		if (facts.Any(fact =>
			fact is null || fact.Type != FactionFactType.Beneficiary))
		{
			throw new ArgumentException(
				"Deferred Initial Beneficiary Closure results may establish only Beneficiary facts.",
				nameof(facts));
		}

		if (!isComplete && facts.Count != 0)
		{
			throw new ArgumentException(
				"An incomplete deferred result cannot contain facts.",
				nameof(facts));
		}
		if (!isComplete && privatelyEstablishedBeneficiaryPlayerIds.Count != 0 ||
			privatelyEstablishedBeneficiaryPlayerIds.Any(
				playerId => playerId == Guid.Empty) ||
			privatelyEstablishedBeneficiaryPlayerIds.Distinct().Count() !=
			privatelyEstablishedBeneficiaryPlayerIds.Count)
		{
			throw new ArgumentException(
				"Private Initial Beneficiary Closure coverage must contain distinct Player identifiers only for a complete result.",
				nameof(privatelyEstablishedBeneficiaryPlayerIds));
		}

		Identifier = identifier;
		IsComplete = isComplete;
		Facts = Array.AsReadOnly(facts.ToArray());
		PrivatelyEstablishedBeneficiaryPlayerIds = Array.AsReadOnly(
			privatelyEstablishedBeneficiaryPlayerIds.ToArray());
	}

	public string Identifier { get; }

	public bool IsComplete { get; }

	public IReadOnlyList<FactionFact> Facts { get; }

	public IReadOnlyList<Guid> PrivatelyEstablishedBeneficiaryPlayerIds { get; }

	public static InitialBeneficiaryClosureDeferredResult Pending(
		string identifier) =>
		new(identifier, isComplete: false, [], []);

	public static InitialBeneficiaryClosureDeferredResult Complete(
		string identifier,
		IReadOnlyCollection<FactionFact> facts) =>
		new(identifier, isComplete: true, facts, []);

	internal static InitialBeneficiaryClosureDeferredResult
		CompleteWithPrivateBeneficiaryCoverage(
			string identifier,
			IReadOnlyCollection<Guid> playerIds) =>
		new(identifier, isComplete: true, [], playerIds);
}

internal sealed class InitialBeneficiaryClosureRequest
{
	public InitialBeneficiaryClosureRequest(
		FactionFactEffectiveBoundary
			earliestCompleteWerewolfAgentPartitionBoundary,
		IReadOnlyCollection<InitialBeneficiaryClosurePrerequisite>
			applicableExceptionPrerequisites,
		IReadOnlyCollection<InitialBeneficiaryClosureDeferredResult>
			deferredResults)
	{
		ArgumentNullException.ThrowIfNull(
			earliestCompleteWerewolfAgentPartitionBoundary);
		ArgumentNullException.ThrowIfNull(applicableExceptionPrerequisites);
		ArgumentNullException.ThrowIfNull(deferredResults);
		if (applicableExceptionPrerequisites.Any(item => item is null)
			|| applicableExceptionPrerequisites
				.GroupBy(item => item.Identifier, StringComparer.Ordinal)
				.Any(group => group.Count() > 1))
		{
			throw new ArgumentException(
				"Initial Beneficiary Closure prerequisites must be non-null and uniquely identified.",
				nameof(applicableExceptionPrerequisites));
		}

		if (deferredResults.Any(item => item is null)
			|| deferredResults
				.GroupBy(item => item.Identifier, StringComparer.Ordinal)
				.Any(group => group.Count() > 1))
		{
			throw new ArgumentException(
				"Initial Beneficiary Closure deferred results must be non-null and uniquely identified.",
				nameof(deferredResults));
		}

		EarliestCompleteWerewolfAgentPartitionBoundary =
			earliestCompleteWerewolfAgentPartitionBoundary;
		ApplicableExceptionPrerequisites = Array.AsReadOnly(
			applicableExceptionPrerequisites.ToArray());
		DeferredResults = Array.AsReadOnly(deferredResults.ToArray());
	}

	public FactionFactEffectiveBoundary
		EarliestCompleteWerewolfAgentPartitionBoundary
	{ get; }

	public IReadOnlyList<InitialBeneficiaryClosurePrerequisite>
		ApplicableExceptionPrerequisites
	{ get; }

	public IReadOnlyList<InitialBeneficiaryClosureDeferredResult>
		DeferredResults
	{ get; }
}

internal static class InitialBeneficiaryClosureRules
{
	private const string SourceIdentifier = "initial-beneficiary-closure";
	private const string WhiteWerewolfDeferredResultIdentifier =
		"white-werewolf-beneficiary";
	private const string PiperDeferredResultIdentifier =
		"piper-beneficiary";
	private const string PrejudicedManipulatorDeferredResultIdentifier =
		"prejudiced-manipulator-beneficiary";
	private const string LoversDeferredResultIdentifier =
		"lovers-beneficiary";
	internal const int CrossFactionLoversBeneficiaryPrecedence = 1;

	internal static InitialBeneficiaryClosureResult TryCommitCurrentSession(
		GameSession session,
		FactionFactEffectiveBoundary?
			earliestCompleteWerewolfAgentPartitionBoundary = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		if (session.TurnNumber != 1)
		{
			return InitialBeneficiaryClosureResult.Incomplete;
		}

		earliestCompleteWerewolfAgentPartitionBoundary ??=
			FindEarliestCompleteWerewolfAgentPartitionBoundary(session);
		if (earliestCompleteWerewolfAgentPartitionBoundary == null)
		{
			return InitialBeneficiaryClosureResult.Incomplete;
		}

		var history = session.GameHistoryLog.ToArray();
		return TryCommit(
			session,
			new InitialBeneficiaryClosureRequest(
				earliestCompleteWerewolfAgentPartitionBoundary,
				applicableExceptionPrerequisites: [],
				deferredResults:
					CreateCurrentDeferredResults(
						session,
						earliestCompleteWerewolfAgentPartitionBoundary,
						history)));
	}

	internal static InitialBeneficiaryClosureReadiness GetReadiness(
		GameSession session,
		InitialBeneficiaryClosureRequest request)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(request);
		if (HasCommitted(session))
		{
			return InitialBeneficiaryClosureReadiness.AlreadyCommitted;
		}

		return TryBuildFacts(session, request, out _)
			? InitialBeneficiaryClosureReadiness.Ready
			: InitialBeneficiaryClosureReadiness.Incomplete;
	}

	internal static InitialBeneficiaryClosureResult TryCommit(
		GameSession session,
		InitialBeneficiaryClosureRequest request)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(request);
		if (HasCommitted(session))
		{
			return InitialBeneficiaryClosureResult.AlreadyCommitted;
		}

		if (!TryBuildFacts(session, request, out var facts))
		{
			return InitialBeneficiaryClosureResult.Incomplete;
		}

		FactionFactsCommittedLogEntry CreateClosureEntry(
			GameFactContext context) =>
			new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.InitialBeneficiaryClosure,
					SourceIdentifier),
				Facts = facts
			};

		var history = session.GameHistoryLog.ToArray();
		var actorPair = GetInitialActorBorrowedCupidPair(session, history);
		if (actorPair is
			{
				Disposition: ActorBorrowedCupidLoversDisposition
					.DeferredToInitialBeneficiaryClosure
			})
		{
			var disposition =
				ClassifyInitialLoversDisposition(
					session,
					actorPair.PlayerIds,
					actorPair.LinkBoundary,
					history.OfType<IFactionFactBatchLogEntry>().ToArray(),
					request.EarliestCompleteWerewolfAgentPartitionBoundary);
			if (disposition is null)
			{
				return InitialBeneficiaryClosureResult.Incomplete;
			}

			session.CommitActorBorrowedCupidInitialBeneficiaryClosure(
				CreateClosureEntry,
				actorPair,
				disposition.Value);
		}
		else
		{
			session.CommitFactionFactBatch(CreateClosureEntry);
		}
		return InitialBeneficiaryClosureResult.Committed;
	}

	internal static bool HasCommitted(GameSession session) =>
		session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Any(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);

	internal static bool HasConsistentInitialBeneficiaryClosure(
		GameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		if (session.TurnNumber != 1 ||
		    session.Execution.CurrentPhase != GamePhase.Night)
		{
			return false;
		}

		var closureEntries = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Where(entry =>
				entry.Source.Kind ==
					FactionFactSourceKind.InitialBeneficiaryClosure)
			.ToArray();
		var allApplicableExceptionHolderSetsKnown = new[]
			{
				MainRoleType.Cupid,
				MainRoleType.WhiteWerewolf,
				MainRoleType.Piper,
				MainRoleType.PrejudicedManipulator,
				MainRoleType.WolfHound
			}
			.Where(role => session.RoleInPlayCount(role) > 0)
			.All(role =>
				GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
					session,
					role));
		if (closureEntries.Length == 0)
		{
			return !allApplicableExceptionHolderSetsKnown ||
				FindEarliestCompleteWerewolfAgentPartitionBoundary(session) == null;
		}

		if (!allApplicableExceptionHolderSetsKnown)
		{
			return false;
		}

		if (closureEntries is not [var closure] ||
		    closure.TurnNumber != 1 ||
		    closure.CurrentPhase != GamePhase.Night ||
		    !StringComparer.Ordinal.Equals(
			    closure.Source.Identifier,
			    SourceIdentifier) ||
		    closure.Facts.Any(fact =>
			    fact.Type != FactionFactType.Beneficiary))
		{
			return false;
		}

		var playerIds = session.GetPlayers()
			.Select(player => player.Id)
			.ToArray();

		var committedHistory = session.GameHistoryLog.ToArray();
		var closureIndex = Array.FindIndex(
			committedHistory,
			entry => ReferenceEquals(entry, closure));
		if (closureIndex < 0)
		{
			return false;
		}

		var historyBeforeClosure = committedHistory
			.Take(closureIndex)
			.ToArray();
		var factionHistoryBeforeClosure = historyBeforeClosure
			.OfType<IFactionFactBatchLogEntry>()
			.ToArray();
		var earliestCompleteWerewolfAgentPartitionBoundary =
			FindEarliestCompleteWerewolfAgentPartitionBoundary(
				session,
				factionHistoryBeforeClosure);
		if (earliestCompleteWerewolfAgentPartitionBoundary == null ||
		    !TryBuildFacts(
			    session,
			    new InitialBeneficiaryClosureRequest(
				    earliestCompleteWerewolfAgentPartitionBoundary,
				    applicableExceptionPrerequisites: [],
				    deferredResults: CreateCurrentDeferredResults(
					    session,
					    earliestCompleteWerewolfAgentPartitionBoundary,
					    historyBeforeClosure)),
			    factionHistoryBeforeClosure,
			    out var expectedFacts) ||
		    !closure.Facts.SequenceEqual(expectedFacts))
		{
			return false;
		}

		var projection = FactionFactProjection.Create(
			committedHistory
				.OfType<IFactionFactBatchLogEntry>()
				.Concat(session.GetActorBorrowedCupidLoversCommits()),
			playerIds);
		return playerIds.All(playerId =>
			       projection.Beneficiaries[playerId].IsKnown &&
			       session.GetFactionBeneficiaryKnowledge(playerId) ==
			       projection.Beneficiaries[playerId]);
	}

	private static bool TryBuildFacts(
		GameSession session,
		InitialBeneficiaryClosureRequest request,
		out ImmutableArray<FactionFact> closureFacts) =>
		TryBuildFacts(
			session,
			request,
			session.GameHistoryLog
				.OfType<IFactionFactBatchLogEntry>()
				.ToArray(),
			out closureFacts);

	private static bool TryBuildFacts(
		GameSession session,
		InitialBeneficiaryClosureRequest request,
		IReadOnlyCollection<IFactionFactBatchLogEntry> history,
		out ImmutableArray<FactionFact> closureFacts)
	{
		closureFacts = [];
		var currentBoundary = new FactionFactEffectiveBoundary(
			session.TurnNumber,
			session.Execution.CurrentPhase,
			int.MaxValue);
		if (FactionFactProjection.CompareBoundaries(
			request.EarliestCompleteWerewolfAgentPartitionBoundary,
			currentBoundary) > 0)
		{
			throw new InvalidOperationException(
				"Initial Beneficiary Closure cannot use a future boundary.");
		}

		if (request.ApplicableExceptionPrerequisites.Any(
				prerequisite => !prerequisite.IsComplete)
			|| request.DeferredResults.Any(result => !result.IsComplete))
		{
			return false;
		}

		var playerIds = session.GetPlayers()
			.Select(player => player.Id)
			.ToArray();
		var projectionAtEarliestCompleteWerewolfAgentPartition =
			FactionFactProjection.Create(
				history,
				playerIds,
				request.EarliestCompleteWerewolfAgentPartitionBoundary);
		if (playerIds.Any(playerId =>
			projectionAtEarliestCompleteWerewolfAgentPartition
				.Agents[playerId][Faction.Werewolf] ==
			FactionAgentKnowledge.Unknown))
		{
			return false;
		}

		var privatelyEstablishedBeneficiaryPlayerIds = request.DeferredResults
			.SelectMany(result =>
				result.PrivatelyEstablishedBeneficiaryPlayerIds)
			.ToHashSet();
		var deferredFacts = request.DeferredResults
			.SelectMany(result => result.Facts)
			.Where(fact =>
				!privatelyEstablishedBeneficiaryPlayerIds.Contains(fact.PlayerId))
			.ToArray();
		var deferredBeneficiaryPlayerIds = request.DeferredResults
			.SelectMany(result =>
				result.Facts.Select(fact => fact.PlayerId)
					.Concat(result.PrivatelyEstablishedBeneficiaryPlayerIds))
			.ToHashSet();
		var facts = new List<FactionFact>();
		foreach (var playerId in playerIds)
		{
			if (projectionAtEarliestCompleteWerewolfAgentPartition
					.Beneficiaries[playerId].IsKnown ||
			    deferredBeneficiaryPlayerIds.Contains(playerId))
			{
				continue;
			}

			var faction =
				projectionAtEarliestCompleteWerewolfAgentPartition
					.Agents[playerId][Faction.Werewolf] ==
				FactionAgentKnowledge.KnownAgent
					? Faction.Werewolf
					: Faction.Villager;
			facts.Add(FactionFact.Beneficiary(
				playerId,
				faction,
				request.EarliestCompleteWerewolfAgentPartitionBoundary));
		}

		var players = playerIds.ToHashSet();
		var existingFacts = history
			.SelectMany(entry => entry.Facts)
			.ToHashSet();
		foreach (var deferredFact in deferredFacts)
		{
			if (!players.Contains(deferredFact.PlayerId))
			{
				throw new InvalidOperationException(
					"Initial Beneficiary Closure references a Player outside the Game Session.");
			}

			if (!existingFacts.Contains(deferredFact))
			{
				facts.Add(deferredFact);
			}
		}

		var seatingOrder = playerIds
			.Select((playerId, index) => (playerId, index))
			.ToDictionary(pair => pair.playerId, pair => pair.index);
		closureFacts = facts
			.OrderBy(
				fact => fact.EffectiveBoundary,
				Comparer<FactionFactEffectiveBoundary>.Create(
					FactionFactProjection.CompareBoundaries))
			.ThenBy(fact => seatingOrder[fact.PlayerId])
			.ThenBy(fact => fact.BeneficiaryPrecedence)
			.ToImmutableArray();
		return true;
	}

	private static IReadOnlyCollection<
			InitialBeneficiaryClosureDeferredResult>
		CreateCurrentDeferredResults(
			GameSession session,
			FactionFactEffectiveBoundary
				earliestCompleteWerewolfAgentPartitionBoundary,
			IReadOnlyCollection<GameLogEntryBase> history)
	{
		var factionHistory = history
			.OfType<IFactionFactBatchLogEntry>()
			.ToArray();
		return new[]
			{
				CreateCurrentExclusiveBeneficiaryResult(
					session,
					earliestCompleteWerewolfAgentPartitionBoundary,
					factionHistory,
					MainRoleType.WhiteWerewolf,
					Faction.WhiteWerewolf,
					WhiteWerewolfDeferredResultIdentifier),
					CreateCurrentExclusiveBeneficiaryResult(
						session,
						earliestCompleteWerewolfAgentPartitionBoundary,
						factionHistory,
						MainRoleType.Piper,
						Faction.Piper,
						PiperDeferredResultIdentifier),
					CreateCurrentExclusiveBeneficiaryResult(
						session,
						earliestCompleteWerewolfAgentPartitionBoundary,
						factionHistory,
						MainRoleType.PrejudicedManipulator,
						Faction.PrejudicedManipulator,
						PrejudicedManipulatorDeferredResultIdentifier),
					CreateCurrentLoversResult(
					session,
					earliestCompleteWerewolfAgentPartitionBoundary,
					history)
			}
			.OfType<InitialBeneficiaryClosureDeferredResult>()
			.ToArray();
	}

	private static InitialBeneficiaryClosureDeferredResult?
		CreateCurrentLoversResult(
			GameSession session,
			FactionFactEffectiveBoundary
				earliestCompleteWerewolfAgentPartitionBoundary,
			IReadOnlyCollection<GameLogEntryBase> history)
	{
		var actorPair = GetInitialActorBorrowedCupidPair(session, history);
		var nativePair =
			GameSessionQueries.GetCommittedLoversPairFromHistory(history);
		if (actorPair is not null && nativePair is not null)
		{
			throw new InvalidOperationException(
				"Initial Beneficiary Closure cannot classify multiple Lovers pairs.");
		}

		if (actorPair is null &&
			session.RoleInPlayCount(MainRoleType.Cupid) == 0)
		{
			return null;
		}

		if (actorPair is null &&
			!GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			    session,
			    MainRoleType.Cupid))
		{
			return InitialBeneficiaryClosureDeferredResult.Pending(
				LoversDeferredResultIdentifier);
		}

		if (actorPair is null && nativePair is null)
		{
			return InitialBeneficiaryClosureDeferredResult.Complete(
				LoversDeferredResultIdentifier,
				[]);
		}

		var pairPlayerIds = actorPair?.PlayerIds ?? nativePair!.PlayerIds;
		ValidateLoversPairStatuses(session, pairPlayerIds);
		var factionHistory = history
			.OfType<IFactionFactBatchLogEntry>()
			.ToArray();
		foreach (var prerequisiteRole in new[]
		         {
				         MainRoleType.WhiteWerewolf,
				         MainRoleType.Piper,
				         MainRoleType.PrejudicedManipulator,
				         MainRoleType.WolfHound
		         })
		{
			if (session.RoleInPlayCount(prerequisiteRole) > 0 &&
			    !GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
				    session,
				    prerequisiteRole))
			{
				return InitialBeneficiaryClosureDeferredResult.Pending(
					LoversDeferredResultIdentifier);
			}
		}

		var linkBoundary = actorPair?.LinkBoundary ??
			GetLoversLinkBoundary(session, nativePair!);
		var disposition = ClassifyInitialLoversDisposition(
			session,
			pairPlayerIds,
			linkBoundary,
			factionHistory,
			earliestCompleteWerewolfAgentPartitionBoundary);
		if (disposition is null)
		{
			return InitialBeneficiaryClosureDeferredResult.Pending(
				LoversDeferredResultIdentifier);
		}

		if (disposition == ActorBorrowedCupidLoversDisposition.SameFaction)
		{
			return InitialBeneficiaryClosureDeferredResult.Complete(
				LoversDeferredResultIdentifier,
				[]);
		}

		if (actorPair is not null)
		{
			return InitialBeneficiaryClosureDeferredResult
				.CompleteWithPrivateBeneficiaryCoverage(
					LoversDeferredResultIdentifier,
					pairPlayerIds);
		}

		var facts = pairPlayerIds
				.Select(playerId => FactionFact.Beneficiary(
					playerId,
					Faction.CrossFactionLovers,
					linkBoundary,
					CrossFactionLoversBeneficiaryPrecedence))
				.ToArray();
		return InitialBeneficiaryClosureDeferredResult.Complete(
			LoversDeferredResultIdentifier,
			facts);
	}

	private static ActorBorrowedCupidLoversDisposition?
		ClassifyInitialLoversDisposition(
			GameSession session,
			IReadOnlyCollection<Guid> pairPlayerIds,
			FactionFactEffectiveBoundary linkBoundary,
			IReadOnlyCollection<IFactionFactBatchLogEntry> factionHistory,
			FactionFactEffectiveBoundary
				earliestCompleteWerewolfAgentPartitionBoundary)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(pairPlayerIds);
		ArgumentNullException.ThrowIfNull(linkBoundary);
		ArgumentNullException.ThrowIfNull(factionHistory);
		ArgumentNullException.ThrowIfNull(
			earliestCompleteWerewolfAgentPartitionBoundary);
		var playerIds = session.GetPlayers()
			.Select(player => player.Id)
			.ToArray();
		if (pairPlayerIds.Count != 2 ||
		    pairPlayerIds.Distinct().Count() != 2 ||
		    pairPlayerIds.Any(playerId => !playerIds.Contains(playerId)))
		{
			throw new InvalidOperationException(
				"The Lovers initial classification coordinate is invalid.");
		}

		var projectionAtLink = FactionFactProjection.Create(
			factionHistory,
			playerIds,
			linkBoundary);
		var projectionAtEarliestCompleteWerewolfAgentPartition =
			FactionFactProjection.Create(
				factionHistory,
				playerIds,
				earliestCompleteWerewolfAgentPartitionBoundary);
		var candidates = new List<Faction>(2);
		foreach (var playerId in pairPlayerIds)
		{
			var projected = projectionAtLink.Beneficiaries[playerId];
			if (projected.IsKnown)
			{
				candidates.Add(projected.Faction!.Value);
				continue;
			}

			var currentRole = session.GetPlayerState(playerId).CurrentRole;
			Faction? exclusiveRoleFaction = currentRole switch
				{
					MainRoleType.WhiteWerewolf => Faction.WhiteWerewolf,
					_ when currentRole is { } role &&
						RoleFactionKnowledge.EstablishesInitialWerewolfAgency(role) =>
						Faction.Werewolf,
					MainRoleType.WolfHound => Faction.Villager,
					MainRoleType.Piper => Faction.Piper,
					MainRoleType.PrejudicedManipulator =>
						Faction.PrejudicedManipulator,
					_ => null
				};
			if (exclusiveRoleFaction.HasValue)
			{
				candidates.Add(exclusiveRoleFaction.Value);
				continue;
			}

			var werewolfAgency =
				projectionAtEarliestCompleteWerewolfAgentPartition
					.Agents[playerId][Faction.Werewolf];
			if (werewolfAgency == FactionAgentKnowledge.Unknown)
			{
				return null;
			}

			candidates.Add(
				werewolfAgency == FactionAgentKnowledge.KnownAgent
					? Faction.Werewolf
					: Faction.Villager);
		}

		return candidates[0] == candidates[1]
			? ActorBorrowedCupidLoversDisposition.SameFaction
			: ActorBorrowedCupidLoversDisposition.CrossFaction;
	}

	private static ActorBorrowedCupidLoversCommit?
		GetInitialActorBorrowedCupidPair(
			GameSession session,
			IReadOnlyCollection<GameLogEntryBase> history)
	{
		var pairs = session.GetActorBorrowedCupidLoversCommits()
			.Where(commit => commit.TurnNumber == 1)
			.ToArray();
		if (pairs.Length == 0)
		{
			return null;
		}

		if (pairs is not [var pair])
		{
			throw new InvalidOperationException(
				"Initial Beneficiary Closure cannot classify multiple Actor borrowed Cupid pairs.");
		}

		var committedHistory = history.ToArray();
		if (pair.PublicMarkerLogIndex < 0 ||
			pair.PublicMarkerLogIndex >= committedHistory.Length ||
			committedHistory[pair.PublicMarkerLogIndex] is not
				ActorBorrowedRolePowerCommittedLogEntry marker ||
			marker.Timestamp != pair.Timestamp ||
			marker.TurnNumber != pair.TurnNumber ||
			marker.CurrentPhase != pair.CurrentPhase)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid pair boundary does not match committed history.");
		}

		return pair;
	}

	private static FactionFactEffectiveBoundary GetLoversLinkBoundary(
		GameSession session,
		LoversPairCommittedLogEntry pair)
	{
		if (pair.LinkBoundary.Order !=
		    GameSessionQueries.GetCommittedLogIndex(session, pair))
		{
			throw new InvalidOperationException(
				"The Lovers pair boundary does not match committed history.");
		}

		return pair.LinkBoundary;
	}

	private static void ValidateLoversPairStatuses(
		GameSession session,
		IReadOnlyCollection<Guid> playerIds)
	{
		if (playerIds.Any(playerId =>
			    !session.GetPlayerState(playerId)
				    .HasStatusEffect(StatusEffectTypes.Lovers)))
		{
			throw new InvalidOperationException(
				"The committed Lovers pair requires both durable Lovers statuses.");
		}
	}

	private static InitialBeneficiaryClosureDeferredResult?
		CreateCurrentExclusiveBeneficiaryResult(
			GameSession session,
			FactionFactEffectiveBoundary
				earliestCompleteWerewolfAgentPartitionBoundary,
			IReadOnlyCollection<IFactionFactBatchLogEntry> history,
			MainRoleType role,
			Faction faction,
			string identifier)
	{
		if (session.RoleInPlayCount(role) == 0)
		{
			return null;
		}

		if (!GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			    session,
			    role))
		{
			return InitialBeneficiaryClosureDeferredResult.Pending(
				identifier);
		}

		var playerIds = session.GetPlayers()
			.Select(player => player.Id)
			.ToArray();
		var projectionAtEarliestCompleteWerewolfAgentPartition =
			FactionFactProjection.Create(
				history,
				playerIds,
				earliestCompleteWerewolfAgentPartitionBoundary);
		var facts = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.State.CurrentRole == role &&
				!projectionAtEarliestCompleteWerewolfAgentPartition
					.Beneficiaries[player.Id]
					.IsKnown)
			.Select(player => FactionFact.Beneficiary(
				player.Id,
				faction,
				earliestCompleteWerewolfAgentPartitionBoundary))
			.ToArray();
		return InitialBeneficiaryClosureDeferredResult.Complete(
			identifier,
			facts);
	}

	private static FactionFactEffectiveBoundary?
		FindEarliestCompleteWerewolfAgentPartitionBoundary(
			GameSession session,
			IReadOnlyCollection<IFactionFactBatchLogEntry>? history =
				null)
	{
		var playerIds = session.GetPlayers()
			.Select(player => player.Id)
			.ToArray();
		history ??= session.GameHistoryLog
			.OfType<IFactionFactBatchLogEntry>()
			.ToArray();
		var candidateBoundaries = history
			.SelectMany(entry => entry.Facts)
			.Where(fact =>
				fact.Type == FactionFactType.Agent &&
				fact.Faction == Faction.Werewolf)
			.Select(fact => fact.EffectiveBoundary)
			.Distinct()
			.OrderBy(
				boundary => boundary,
				Comparer<FactionFactEffectiveBoundary>.Create(
					FactionFactProjection.CompareBoundaries));

		foreach (var boundary in candidateBoundaries)
		{
			var projection = FactionFactProjection.Create(
				history,
				playerIds,
				boundary);
			if (playerIds.All(playerId =>
				    projection.Agents[playerId][Faction.Werewolf] !=
				    FactionAgentKnowledge.Unknown))
			{
				return boundary;
			}
		}

		return null;
	}
}
