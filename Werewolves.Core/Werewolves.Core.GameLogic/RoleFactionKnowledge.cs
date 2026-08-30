using System.Collections.Immutable;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic;

internal readonly record struct InitialWerewolfAgentGroupOpportunity(
	ImmutableHashSet<Guid> CandidatePlayerIds,
	int RequiredCount);

internal static class RoleFactionKnowledge
{
	private readonly record struct IndexedFactionAgentFact(
		FactionFact Fact,
		FactionFactSource Source,
		int BatchIndex);

	internal static bool EstablishesInitialWerewolfAgency(MainRoleType role) =>
		GetRoleIdentificationWerewolfFactionAgentKnowledge(role) ==
		FactionAgentKnowledge.KnownAgent;

	internal static InitialWerewolfAgentGroupOpportunity
		RequireInitialWerewolfAgentGroupOpportunity(IGameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		var players = session.GetPlayers().ToArray();
		if (session.TurnNumber != 1 ||
		    session.GetCurrentPhase() != GamePhase.Night ||
		    players.Any(player => player.State.Health != PlayerHealth.Alive))
		{
			throw new InvalidOperationException(
				"The initial Werewolf Agent-group opportunity requires the all-alive first Night boundary.");
		}

		var playersWithKnowledge = players
			.Select(player => new
			{
				Player = player,
				Knowledge = session.GetFactionAgentKnowledge(
					player.Id,
					Faction.Werewolf)
			})
			.ToArray();
		if (playersWithKnowledge.All(item =>
			    item.Knowledge != FactionAgentKnowledge.Unknown))
		{
			throw new InvalidOperationException(
				"The living Werewolf Agent partition is already complete.");
		}

		var establishedAgentRoles = playersWithKnowledge
			.Where(item =>
				item.Knowledge == FactionAgentKnowledge.KnownAgent)
			.Select(item => GetEstablishedRole(item.Player))
			.ToArray();
		if (establishedAgentRoles.Any(role => role is null))
		{
			throw new InvalidOperationException(
				"A known Werewolf Agent has no established Role relationship.");
		}

		var establishedAgentRoleValues = establishedAgentRoles
			.Select(role => role!.Value)
			.ToArray();
		var activeAgencyCardCounts = session
			.GetModeratorPhysicalCharacterCards()
			.Where(cardState =>
				(cardState.Zone is PhysicalCharacterCardZone.DealPool or
				 PhysicalCharacterCardZone.PlayerOwned) &&
				EstablishesInitialWerewolfAgency(cardState.Card.PrintedRole))
			.GroupBy(cardState => cardState.Card.PrintedRole)
			.ToDictionary(group => group.Key, group => group.Count());
		var establishedAgencyAgentCounts = establishedAgentRoleValues
			.Where(EstablishesInitialWerewolfAgency)
			.GroupBy(role => role)
			.ToDictionary(group => group.Key, group => group.Count());
		if (establishedAgencyAgentCounts.Any(pair =>
			    !activeAgencyCardCounts.TryGetValue(
				    pair.Key,
				    out var cardCount) ||
			    pair.Value > cardCount))
		{
			throw new InvalidOperationException(
				"A known Werewolf Agent cannot be related exactly to an active agency-capable card.");
		}

		var requiredCount = activeAgencyCardCounts.Values.Sum() +
			establishedAgentRoleValues.Count(role =>
				!EstablishesInitialWerewolfAgency(role));
		var candidatePlayerIds = playersWithKnowledge
			.Where(item =>
				item.Knowledge != FactionAgentKnowledge.KnownNonAgent)
			.Select(item => item.Player.Id)
			.ToImmutableHashSet();
		if (requiredCount <= 0 || requiredCount > candidatePlayerIds.Count)
		{
			throw new InvalidOperationException(
				"The initial Werewolf Agent-group cardinality is not exact for the current candidates.");
		}

		return new InitialWerewolfAgentGroupOpportunity(
			candidatePlayerIds,
			requiredCount);
	}

	internal static FactionFactEffectiveBoundary
		CommitInitialWerewolfAgentGroupObservation(
			GameSession session,
			IReadOnlySet<Guid> observedPlayerIds)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(observedPlayerIds);
		var opportunity = RequireInitialWerewolfAgentGroupOpportunity(session);
		var observedAgentIds = observedPlayerIds.ToImmutableHashSet();
		if (observedAgentIds.Count != opportunity.RequiredCount)
		{
			throw new InvalidOperationException(
				"Werewolf Agent-group observation requires the exact established Player count.");
		}

		if (!observedAgentIds.IsSubsetOf(opportunity.CandidatePlayerIds))
		{
			throw new InvalidOperationException(
				"Werewolf Agent-group observation contains a Player outside the current candidates.");
		}

		var livingPlayers = session.GetPlayers()
			.Where(player => player.State.Health == PlayerHealth.Alive)
			.ToArray();
		if (livingPlayers.Any(player =>
		{
			var knowledge = session.GetFactionAgentKnowledge(
				player.Id,
				Faction.Werewolf);
			return knowledge == FactionAgentKnowledge.KnownAgent &&
			       !observedAgentIds.Contains(player.Id) ||
			       knowledge == FactionAgentKnowledge.KnownNonAgent &&
			       observedAgentIds.Contains(player.Id);
		}))
		{
			throw new InvalidOperationException(
				"Werewolf Agent-group observation contradicts committed Faction facts.");
		}

		FactionFactEffectiveBoundary? committedBoundary = null;
		session.CommitFactionFactBatch(context =>
		{
			committedBoundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			var facts = livingPlayers
				.Select(player => FactionFact.Agent(
					player.Id,
					Faction.Werewolf,
					observedAgentIds.Contains(player.Id)
						? FactionAgentKnowledge.KnownAgent
						: FactionAgentKnowledge.KnownNonAgent,
					committedBoundary))
				.ToImmutableArray();
			return new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ScheduledObservation,
					FactionFactSource
						.WerewolfFactionAgentGroupObservationIdentifier),
				Facts = facts
			};
		});

		return committedBoundary
			?? throw new InvalidOperationException(
				"Werewolf Agent-group observation did not establish a boundary.");
	}

	internal static void CommitRoleIdentification(
		GameSession session,
		IReadOnlySet<Guid> observedCompletePlayerIds,
		MainRoleType role)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(observedCompletePlayerIds);
		var entailedWerewolfFactionAgentKnowledge =
			GetRoleIdentificationWerewolfFactionAgentKnowledge(role);
		var playerIds = observedCompletePlayerIds.ToHashSet();

		session.CommitRoleIdentificationEntry(context =>
			new RoleIdentificationLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				PlayerIds = playerIds,
				Role = role
			});

		if (entailedWerewolfFactionAgentKnowledge is null)
		{
			return;
		}

		var boundary = new FactionFactEffectiveBoundary(
			session.TurnNumber,
			session.GetCurrentPhase(),
			session.GameHistoryLog.Count());
		var facts = session.GetPlayers()
			.Where(player => playerIds.Contains(player.Id))
			.Where(player => session.GetFactionAgentKnowledge(
				player.Id,
				Faction.Werewolf) == FactionAgentKnowledge.Unknown)
			.Select(player => FactionFact.Agent(
				player.Id,
				Faction.Werewolf,
				entailedWerewolfFactionAgentKnowledge.Value,
				boundary))
			.ToImmutableArray();
		if (facts.IsEmpty)
		{
			return;
		}

		session.CommitFactionFactBatch(context =>
			new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ScheduledObservation,
					FactionFactSource
						.RoleIdentificationWerewolfFactionAgencyEntailmentIdentifier),
				Facts = facts
			});
	}

	internal static MainRoleType? GetEstablishedRole(IPlayer player) =>
		player.State.PhysicalCharacterCardRole ??
		player.State.ModeratorKnownRole ??
		player.State.CurrentRole;

	internal static IReadOnlyList<MainRoleType> GetPossibleRoles(
		IGameSession session,
		Guid playerId)
	{
		ArgumentNullException.ThrowIfNull(session);
		session.GetPlayer(playerId);
		var possibleRoles = GetUnclaimedRoles(session);
		var agencyKnowledge = session.GetFactionAgentKnowledge(
			playerId,
			Faction.Werewolf);
		if (agencyKnowledge == FactionAgentKnowledge.KnownNonAgent)
		{
			return Array.AsReadOnly(possibleRoles
				.Where(role => !EstablishesInitialWerewolfAgency(role))
				.ToArray());
		}

		var earliestAgencyFact = GetEarliestWerewolfAgencyFact(
			session,
			playerId);
		if (agencyKnowledge == FactionAgentKnowledge.KnownAgent &&
			earliestAgencyFact is { } earliest &&
			IsInitialWerewolfAgencyProvenance(earliest.Source))
		{
			return Array.AsReadOnly(possibleRoles
				.Where(EstablishesInitialWerewolfAgency)
				.ToArray());
		}

		return Array.AsReadOnly(possibleRoles.ToArray());
	}

	internal static FactionAgentFactProvenance?
		GetEarliestWerewolfAgencyFact(
			IGameSession session,
			Guid playerId)
	{
		ArgumentNullException.ThrowIfNull(session);
		session.GetPlayer(playerId);
		return FindEarliestWerewolfAgencyFact(session, playerId) is { } earliest
			? new FactionAgentFactProvenance(earliest.Fact, earliest.Source)
			: null;
	}

	private static List<MainRoleType> GetUnclaimedRoles(IGameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		var unclaimedRoles = session.GetModeratorPhysicalCharacterCards()
			.Where(cardState =>
				cardState.Zone == PhysicalCharacterCardZone.DealPool)
			.Select(cardState => cardState.Card.PrintedRole)
			.ToList();
		foreach (var player in session.GetPlayers())
		{
			if (player.State.PhysicalCharacterCardId is null &&
				GetEstablishedRole(player) is { } establishedRole)
			{
				unclaimedRoles.Remove(establishedRole);
			}
		}

		return unclaimedRoles;
	}

	private static IndexedFactionAgentFact? FindEarliestWerewolfAgencyFact(
		IGameSession session,
		Guid playerId) =>
		session.GameHistoryLog
			.OfType<IFactionFactBatchLogEntry>()
			.SelectMany((entry, batchIndex) => entry.Facts
				.Where(fact =>
					fact.PlayerId == playerId &&
					fact.Type == FactionFactType.Agent &&
					fact.Faction == Faction.Werewolf &&
					fact.AgentKnowledge == FactionAgentKnowledge.KnownAgent)
				.Select(fact => new IndexedFactionAgentFact(
					fact,
					entry.Source,
					batchIndex)))
			.OrderBy(
				item => item.Fact.EffectiveBoundary,
				Comparer<FactionFactEffectiveBoundary>.Create(
					FactionFactProjection.CompareBoundaries))
			.ThenBy(item => item.BatchIndex)
			.Cast<IndexedFactionAgentFact?>()
			.FirstOrDefault();

	private static bool IsInitialWerewolfAgencyProvenance(
		FactionFactSource source) =>
		source.Kind == FactionFactSourceKind.SimulationStartState ||
		source.Kind == FactionFactSourceKind.ScheduledObservation &&
		StringComparer.Ordinal.Equals(
			source.Identifier,
			FactionFactSource
				.WerewolfFactionAgentGroupObservationIdentifier);

	private static FactionAgentKnowledge?
		GetRoleIdentificationWerewolfFactionAgentKnowledge(
			MainRoleType role) => role switch
		{
			MainRoleType.SimpleWerewolf or
			MainRoleType.BigBadWolf or
			MainRoleType.AccursedWolfFather or
			MainRoleType.WhiteWerewolf => FactionAgentKnowledge.KnownAgent,
			MainRoleType.SimpleVillager or
			MainRoleType.VillagerVillager or
			MainRoleType.Seer or
			MainRoleType.Cupid or
			MainRoleType.Witch or
			MainRoleType.Hunter or
			MainRoleType.LittleGirl or
			MainRoleType.Defender or
			MainRoleType.Elder or
			MainRoleType.Scapegoat or
			MainRoleType.VillageIdiot or
			MainRoleType.TwoSisters or
			MainRoleType.ThreeBrothers or
			MainRoleType.Fox or
			MainRoleType.BearTamer or
			MainRoleType.StutteringJudge or
			MainRoleType.KnightWithRustySword or
			MainRoleType.Actor or
			MainRoleType.Piper or
			MainRoleType.Angel or
			MainRoleType.PrejudicedManipulator or
			MainRoleType.Gypsy or
			MainRoleType.WildChild => FactionAgentKnowledge.KnownNonAgent,
			MainRoleType.WolfHound or
			MainRoleType.Thief or
			MainRoleType.DevotedServant => null,
			_ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
		};
}
