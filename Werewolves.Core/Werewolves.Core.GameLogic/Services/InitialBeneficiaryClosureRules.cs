using System.Collections.Immutable;
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

	private static bool TryBuildFacts(
		GameSession session,
		InitialBeneficiaryClosureRequest request,
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
		var history = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
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

		var facts = new List<FactionFact>();
		foreach (var playerId in playerIds)
		{
			if (projectionAtGroupBoundary.Beneficiaries[playerId].IsKnown)
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
		foreach (var deferredFact in request.DeferredResults
			.SelectMany(result => result.Facts))
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
}
