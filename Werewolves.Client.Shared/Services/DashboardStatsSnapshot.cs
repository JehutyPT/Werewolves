using System.Globalization;
using Werewolves.Client.Resources;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;

namespace Werewolves.Client.Services;

public sealed record DashboardStatsSnapshot(
	IReadOnlyList<RoleGroupStats> RoleGroups,
	IReadOnlyList<EliminationLogItem> EliminationLog)
{
	private static readonly RoleGroup[] RoleGroupOrder =
		[RoleGroup.Villagers, RoleGroup.Werewolves, RoleGroup.Ambiguous, RoleGroup.Loners];

	public static DashboardStatsSnapshot Empty { get; } = FromSession(null);

	public static DashboardStatsSnapshot FromSession(IGameSession? session)
	{
		if (session is null)
		{
			return new DashboardStatsSnapshot(CreateEmptyRoleGroups(), []);
		}

		var aliveKnownRoles = session.GetPlayers()
			.Where(player => player.State.Health == PlayerHealth.Alive)
			.Select(player => player.State.MainRole)
			.OfType<MainRoleType>()
			.ToList();

		var roleCounts = aliveKnownRoles
			.GroupBy(role => role)
			.ToDictionary(group => group.Key, group => group.Count());

		var roleGroups = RoleGroupOrder
			.Select(group =>
			{
				var roles = roleCounts
					.Where(pair => pair.Key.GetRoleGroup() == group)
					.OrderBy(pair => pair.Key.GetPublicName())
					.Select(pair => new RoleCountStats(pair.Key, pair.Key.GetPublicName(), pair.Value))
					.ToArray();

				return new RoleGroupStats(
					group,
					group.GetDisplayName(),
					roles.Sum(role => role.RemainingCount),
					roles);
			})
			.ToArray();

		var eliminationLog = session.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Select(log => CreateEliminationLogItem(session, log))
			.ToArray();

		return new DashboardStatsSnapshot(roleGroups, eliminationLog);
	}

	private static IReadOnlyList<RoleGroupStats> CreateEmptyRoleGroups() =>
		RoleGroupOrder
			.Select(group => new RoleGroupStats(group, group.GetDisplayName(), 0, []))
			.ToArray();

	private static EliminationLogItem CreateEliminationLogItem(IGameSession session, PlayerEliminatedLogEntry log)
	{
		var player = session.GetPlayer(log.PlayerId);
		return new EliminationLogItem(
			player.Name,
			log.TurnNumber,
			log.CurrentPhase,
			string.Format(
				CultureInfo.CurrentCulture,
				ClientStrings.Dashboard_PhaseTurnFormat,
				PhaseLabel(log.CurrentPhase),
				log.TurnNumber),
			log.Reason,
			EliminationReasonLabel(log.Reason));
	}

	public static string PhaseLabel(GamePhase phase) => phase switch
	{
		GamePhase.Night => ClientStrings.Dashboard_PhaseNight,
		GamePhase.Dawn => ClientStrings.Dashboard_PhaseDawn,
		GamePhase.Day => ClientStrings.Dashboard_PhaseDay,
		_ => ClientStrings.Dashboard_PhaseFallback
	};

	public static string EliminationReasonLabel(EliminationReason reason) => reason switch
	{
		EliminationReason.WerewolfAttack => ClientStrings.Dashboard_EliminationReasonWerewolfAttack,
		EliminationReason.WitchKill => ClientStrings.Dashboard_EliminationReasonWitchKill,
		EliminationReason.HunterShot => ClientStrings.Dashboard_EliminationReasonHunterShot,
		EliminationReason.LoversHeartbreak => ClientStrings.Dashboard_EliminationReasonLoversHeartbreak,
		EliminationReason.RustySword => ClientStrings.Dashboard_EliminationReasonRustySword,
		EliminationReason.ScapegoatSacrifice => ClientStrings.Dashboard_EliminationReasonScapegoatSacrifice,
		EliminationReason.EventElimination => ClientStrings.Dashboard_EliminationReasonEventElimination,
		EliminationReason.DayVote => ClientStrings.Dashboard_EliminationReasonDayVote,
		_ => ClientStrings.Dashboard_EliminationReasonUnknown
	};
}

public sealed record RoleGroupStats(
	RoleGroup Group,
	string DisplayName,
	int RemainingCount,
	IReadOnlyList<RoleCountStats> Roles);

public sealed record RoleCountStats(
	MainRoleType Role,
	string DisplayName,
	int RemainingCount);

public sealed record EliminationLogItem(
	string PlayerName,
	int TurnNumber,
	GamePhase Phase,
	string TurnPhaseLabel,
	EliminationReason Reason,
	string ReasonLabel);
