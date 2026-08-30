using System.Collections.Immutable;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic;

internal static class RoleFactionKnowledge
{
	private readonly record struct IndexedFactionAgentFact(
		FactionFact Fact,
		FactionFactSource Source,
		int BatchIndex);

	internal static bool EstablishesInitialWerewolfAgency(MainRoleType role) =>
		GetRoleIdentificationWerewolfFactionAgentKnowledge(role) ==
		FactionAgentKnowledge.KnownAgent;

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
