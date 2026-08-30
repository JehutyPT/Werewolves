using System.Collections.Immutable;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic;

internal static class RoleFactionKnowledge
{
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
