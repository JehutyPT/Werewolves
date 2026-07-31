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
		IReadOnlyCollection<FactionFact> facts)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
		ArgumentNullException.ThrowIfNull(facts);
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

		Identifier = identifier;
		IsComplete = isComplete;
		Facts = Array.AsReadOnly(facts.ToArray());
	}

	public string Identifier { get; }

	public bool IsComplete { get; }

	public IReadOnlyList<FactionFact> Facts { get; }

	public static InitialBeneficiaryClosureDeferredResult Pending(
		string identifier) =>
		new(identifier, isComplete: false, []);

	public static InitialBeneficiaryClosureDeferredResult Complete(
		string identifier,
		IReadOnlyCollection<FactionFact> facts) =>
		new(identifier, isComplete: true, facts);
}

internal sealed class InitialBeneficiaryClosureRequest
{
	public InitialBeneficiaryClosureRequest(
		FactionFactEffectiveBoundary initialAgentGroupBoundary,
		IReadOnlyCollection<InitialBeneficiaryClosurePrerequisite>
			applicableExceptionPrerequisites,
		IReadOnlyCollection<InitialBeneficiaryClosureDeferredResult>
			deferredResults)
	{
		ArgumentNullException.ThrowIfNull(initialAgentGroupBoundary);
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

		InitialAgentGroupBoundary = initialAgentGroupBoundary;
		ApplicableExceptionPrerequisites = Array.AsReadOnly(
			applicableExceptionPrerequisites.ToArray());
		DeferredResults = Array.AsReadOnly(deferredResults.ToArray());
	}

	public FactionFactEffectiveBoundary InitialAgentGroupBoundary { get; }

	public IReadOnlyList<InitialBeneficiaryClosurePrerequisite>
		ApplicableExceptionPrerequisites { get; }

	public IReadOnlyList<InitialBeneficiaryClosureDeferredResult>
		DeferredResults { get; }
}

internal static class InitialBeneficiaryClosureRules
{
	private const string SourceIdentifier = "initial-beneficiary-closure";
	private const string WhiteWerewolfDeferredResultIdentifier =
		"white-werewolf-beneficiary";
	private const string PiperDeferredResultIdentifier =
		"piper-beneficiary";
	private const string LoversDeferredResultIdentifier =
		"lovers-beneficiary";
	private const string LoversClassificationSourceIdentifier =
		"cupid-lovers-classification";
	internal const int CrossFactionLoversBeneficiaryPrecedence = 1;

	internal static bool TryCommitKnownLoversClassification(
		GameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		var pair = FindCommittedLoversPair(session);
		if (pair is null)
		{
			return false;
		}

		ValidateLoversPairStatuses(session, pair);
		var beneficiaries = pair.PlayerIds
			.Select(session.GetFactionBeneficiaryKnowledge)
			.ToArray();
		if (beneficiaries.Any(beneficiary => !beneficiary.IsKnown))
		{
			return false;
		}

		if (beneficiaries[0].Faction == beneficiaries[1].Faction)
		{
			return true;
		}

		var linkBoundary = GetLoversLinkBoundary(session, pair);
		CommitCrossFactionLoversFacts(session, pair, linkBoundary);
		return true;
	}

	internal static InitialBeneficiaryClosureResult TryCommitCurrentSession(
		GameSession session,
		FactionFactEffectiveBoundary? initialAgentGroupBoundary = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		if (session.TurnNumber != 1)
		{
			return InitialBeneficiaryClosureResult.Incomplete;
		}

		initialAgentGroupBoundary ??=
			FindInitialCompleteWerewolfAgentGroupBoundary(session);
		if (initialAgentGroupBoundary == null)
		{
			return InitialBeneficiaryClosureResult.Incomplete;
		}

		var history = session.GameHistoryLog.ToArray();
		return TryCommit(
			session,
			new InitialBeneficiaryClosureRequest(
				initialAgentGroupBoundary,
				applicableExceptionPrerequisites: [],
				deferredResults:
					CreateCurrentDeferredResults(
						session,
						initialAgentGroupBoundary,
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

		session.CommitFactionFactBatch(context =>
			new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.InitialBeneficiaryClosure,
					SourceIdentifier),
				Facts = facts
			});
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
		    session.GetCurrentPhase() != GamePhase.Night)
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
			       FindInitialCompleteWerewolfAgentGroupBoundary(session) ==
			       null;
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
			.OfType<FactionFactsCommittedLogEntry>()
			.ToArray();
		var initialAgentGroupBoundary =
			FindInitialCompleteWerewolfAgentGroupBoundary(
				session,
				factionHistoryBeforeClosure);
		if (initialAgentGroupBoundary == null ||
		    !TryBuildFacts(
			    session,
			    new InitialBeneficiaryClosureRequest(
				    initialAgentGroupBoundary,
				    applicableExceptionPrerequisites: [],
				    deferredResults: CreateCurrentDeferredResults(
					    session,
					    initialAgentGroupBoundary,
					    historyBeforeClosure)),
			    factionHistoryBeforeClosure,
			    out var expectedFacts) ||
		    !closure.Facts.SequenceEqual(expectedFacts))
		{
			return false;
		}

		var projection = FactionFactProjection.Create(
			committedHistory.OfType<FactionFactsCommittedLogEntry>(),
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
				.OfType<FactionFactsCommittedLogEntry>()
				.ToArray(),
			out closureFacts);

	private static bool TryBuildFacts(
		GameSession session,
		InitialBeneficiaryClosureRequest request,
		IReadOnlyCollection<FactionFactsCommittedLogEntry> history,
		out ImmutableArray<FactionFact> closureFacts)
	{
		closureFacts = [];
		var currentBoundary = new FactionFactEffectiveBoundary(
			session.TurnNumber,
			session.GetCurrentPhase(),
			int.MaxValue);
		if (FactionFactProjection.CompareBoundaries(
			request.InitialAgentGroupBoundary,
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
		var projectionAtGroupBoundary = FactionFactProjection.Create(
			history,
			playerIds,
			request.InitialAgentGroupBoundary);
		if (playerIds.Any(playerId =>
			projectionAtGroupBoundary.Agents[playerId][Faction.Werewolf] ==
			FactionAgentKnowledge.Unknown))
		{
			return false;
		}

		var deferredFacts = request.DeferredResults
			.SelectMany(result => result.Facts)
			.ToArray();
		var deferredBeneficiaryPlayerIds = deferredFacts
			.Select(fact => fact.PlayerId)
			.ToHashSet();
		var facts = new List<FactionFact>();
		foreach (var playerId in playerIds)
		{
			if (projectionAtGroupBoundary.Beneficiaries[playerId].IsKnown ||
			    deferredBeneficiaryPlayerIds.Contains(playerId))
			{
				continue;
			}

			var faction =
				projectionAtGroupBoundary.Agents[playerId][Faction.Werewolf] ==
				FactionAgentKnowledge.KnownAgent
					? Faction.Werewolf
					: Faction.Villager;
			facts.Add(FactionFact.Beneficiary(
				playerId,
				faction,
				request.InitialAgentGroupBoundary));
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
			FactionFactEffectiveBoundary initialAgentGroupBoundary,
			IReadOnlyCollection<GameLogEntryBase> history)
	{
		var factionHistory = history
			.OfType<FactionFactsCommittedLogEntry>()
			.ToArray();
		return new[]
			{
				CreateCurrentExclusiveBeneficiaryResult(
					session,
					initialAgentGroupBoundary,
					factionHistory,
					MainRoleType.WhiteWerewolf,
					Faction.WhiteWerewolf,
					WhiteWerewolfDeferredResultIdentifier),
				CreateCurrentExclusiveBeneficiaryResult(
					session,
					initialAgentGroupBoundary,
					factionHistory,
					MainRoleType.Piper,
					Faction.Piper,
					PiperDeferredResultIdentifier),
				CreateCurrentLoversResult(
					session,
					initialAgentGroupBoundary,
					history)
			}
			.OfType<InitialBeneficiaryClosureDeferredResult>()
			.ToArray();
	}

	private static InitialBeneficiaryClosureDeferredResult?
		CreateCurrentLoversResult(
			GameSession session,
			FactionFactEffectiveBoundary initialAgentGroupBoundary,
			IReadOnlyCollection<GameLogEntryBase> history)
	{
		if (session.RoleInPlayCount(MainRoleType.Cupid) == 0)
		{
			return null;
		}

		if (!GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			    session,
			    MainRoleType.Cupid))
		{
			return InitialBeneficiaryClosureDeferredResult.Pending(
				LoversDeferredResultIdentifier);
		}

		var pair = history
			.OfType<LoversPairCommittedLogEntry>()
			.SingleOrDefault();
		if (pair is null)
		{
			return InitialBeneficiaryClosureDeferredResult.Complete(
				LoversDeferredResultIdentifier,
				[]);
		}

		ValidateLoversPairStatuses(session, pair);
		var factionHistory = history
			.OfType<FactionFactsCommittedLogEntry>()
			.ToArray();
		foreach (var prerequisiteRole in new[]
		         {
			         MainRoleType.WhiteWerewolf,
			         MainRoleType.Piper,
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

		var existingClassification = factionHistory
			.SingleOrDefault(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.ExplicitTransition &&
				StringComparer.Ordinal.Equals(
					entry.Source.Identifier,
					LoversClassificationSourceIdentifier));
		if (existingClassification is not null)
		{
			ValidateCrossFactionLoversFacts(
				pair,
				GetLoversLinkBoundary(session, pair),
				existingClassification.Facts);
			return InitialBeneficiaryClosureDeferredResult.Complete(
				LoversDeferredResultIdentifier,
				existingClassification.Facts);
		}

		var playerIds = session.GetPlayers()
			.Select(player => player.Id)
			.ToArray();
		var linkBoundary = GetLoversLinkBoundary(session, pair);
		var projectionAtLink = FactionFactProjection.Create(
			factionHistory,
			playerIds,
			linkBoundary);
		var projectionAtInitialGroup = FactionFactProjection.Create(
			factionHistory,
			playerIds,
			initialAgentGroupBoundary);
		var candidates = new List<Faction>(2);
		foreach (var playerId in pair.PlayerIds)
		{
			var projected = projectionAtLink.Beneficiaries[playerId];
			if (projected.IsKnown)
			{
				candidates.Add(projected.Faction!.Value);
				continue;
			}

			var role = session.GetPlayerState(playerId).CurrentRole;
			if (role == MainRoleType.WolfHound)
			{
				candidates.Add(Faction.Villager);
				continue;
			}

			if (role == MainRoleType.WhiteWerewolf)
			{
				candidates.Add(Faction.WhiteWerewolf);
				continue;
			}

			if (role == MainRoleType.Piper)
			{
				candidates.Add(Faction.Piper);
				continue;
			}

			var werewolfAgency =
				projectionAtInitialGroup.Agents[playerId][Faction.Werewolf];
			if (werewolfAgency == FactionAgentKnowledge.Unknown)
			{
				return InitialBeneficiaryClosureDeferredResult.Pending(
					LoversDeferredResultIdentifier);
			}

			candidates.Add(
				werewolfAgency == FactionAgentKnowledge.KnownAgent
					? Faction.Werewolf
					: Faction.Villager);
		}

		var facts = candidates[0] == candidates[1]
			? []
			: pair.PlayerIds
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

	private static LoversPairCommittedLogEntry? FindCommittedLoversPair(
		GameSession session) =>
		session.GameHistoryLog
			.OfType<LoversPairCommittedLogEntry>()
			.SingleOrDefault();

	private static FactionFactEffectiveBoundary GetLoversLinkBoundary(
		GameSession session,
		LoversPairCommittedLogEntry pair)
	{
		var history = session.GameHistoryLog.ToArray();
		if (pair.LinkBoundary.Order >= history.Length ||
		    !ReferenceEquals(history[pair.LinkBoundary.Order], pair))
		{
			throw new InvalidOperationException(
				"The Lovers pair boundary does not match committed history.");
		}

		return pair.LinkBoundary;
	}

	private static void CommitCrossFactionLoversFacts(
		GameSession session,
		LoversPairCommittedLogEntry pair,
		FactionFactEffectiveBoundary linkBoundary)
	{
		if (session.GameHistoryLog
		    .OfType<FactionFactsCommittedLogEntry>()
		    .Any(entry =>
			    entry.Source.Kind ==
			    FactionFactSourceKind.ExplicitTransition &&
			    StringComparer.Ordinal.Equals(
				    entry.Source.Identifier,
				    LoversClassificationSourceIdentifier)))
		{
			throw new InvalidOperationException(
				"The Lovers classification is already committed.");
		}

		session.CommitFactionFactBatch(context =>
			new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ExplicitTransition,
					LoversClassificationSourceIdentifier),
				Facts = pair.PlayerIds
					.Select(playerId => FactionFact.Beneficiary(
						playerId,
						Faction.CrossFactionLovers,
						linkBoundary,
						CrossFactionLoversBeneficiaryPrecedence))
					.ToImmutableArray()
			});
	}

	private static void ValidateCrossFactionLoversFacts(
		LoversPairCommittedLogEntry pair,
		FactionFactEffectiveBoundary linkBoundary,
		IReadOnlyCollection<FactionFact> facts)
	{
		if (facts.Count != 2 ||
		    !facts.Select(fact => fact.PlayerId)
			    .ToHashSet()
			    .SetEquals(pair.PlayerIds) ||
		    facts.Any(fact =>
			    fact.Type != FactionFactType.Beneficiary ||
			    fact.Faction != Faction.CrossFactionLovers ||
			    fact.EffectiveBoundary != linkBoundary ||
			    fact.BeneficiaryPrecedence !=
			    CrossFactionLoversBeneficiaryPrecedence))
		{
			throw new InvalidOperationException(
				"The committed Cross-Faction Lovers classification is invalid.");
		}
	}

	private static void ValidateLoversPairStatuses(
		GameSession session,
		LoversPairCommittedLogEntry pair)
	{
		if (pair.PlayerIds.Any(playerId =>
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
			FactionFactEffectiveBoundary initialAgentGroupBoundary,
			IReadOnlyCollection<FactionFactsCommittedLogEntry> history,
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
		var projectionAtGroupBoundary = FactionFactProjection.Create(
			history,
			playerIds,
			initialAgentGroupBoundary);
		var facts = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.State.CurrentRole == role &&
				!projectionAtGroupBoundary
					.Beneficiaries[player.Id]
					.IsKnown)
			.Select(player => FactionFact.Beneficiary(
				player.Id,
				faction,
				initialAgentGroupBoundary))
			.ToArray();
		return InitialBeneficiaryClosureDeferredResult.Complete(
			identifier,
			facts);
	}

	private static FactionFactEffectiveBoundary?
		FindInitialCompleteWerewolfAgentGroupBoundary(
			GameSession session,
			IReadOnlyCollection<FactionFactsCommittedLogEntry>? history =
				null)
	{
		var playerIds = session.GetPlayers()
			.Select(player => player.Id)
			.ToArray();
		history ??= session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
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
