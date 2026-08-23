using Werewolves.Core.GameLogic.Models;
using Werewolves.Client.Resources;
using System.Globalization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;

namespace Werewolves.Client.Services;

public sealed record RecentSetupRowPresentation(
	RecentSetup Setup,
	int PlayerCount,
	int TotalRoleCardCount,
	IReadOnlyList<RecentSetupRoleGroupPresentation> RoleGroups,
	string RelativeDate);

public sealed record RecentSetupRoleGroupPresentation(
	RoleGroup Group,
	string DisplayName,
	int RoleCardCount,
	IReadOnlyList<RecentSetupRolePresentation> Roles);

public sealed record RecentSetupRolePresentation(
	MainRoleType Role,
	string DisplayName,
	string Glyph,
	int? Multiplier);

public static class RecentSetupProjection
{
	private static readonly RoleGroup[] GroupOrder =
		[RoleGroup.Werewolves, RoleGroup.Villagers, RoleGroup.Ambiguous, RoleGroup.Loners];

	public static RecentSetupRowPresentation Project(
		RecentSetup setup,
		LobbySetupMetadata metadata,
		DateTimeOffset currentUtc)
	{
		ArgumentNullException.ThrowIfNull(setup);
		ArgumentNullException.ThrowIfNull(metadata);

		var groups = GroupOrder
			.Select(group => ProjectGroup(setup, metadata, group))
			.Where(group => group.Roles.Count > 0)
			.ToArray();
		return new RecentSetupRowPresentation(
			setup,
			setup.PlayerNames.Count,
			setup.RoleCounts.Values.Sum(),
			groups,
			RelativeDate(setup.CapturedAtUtc, currentUtc));
	}

	private static string RelativeDate(
		DateTimeOffset capturedAtUtc,
		DateTimeOffset currentUtc)
	{
		var elapsed = currentUtc - capturedAtUtc;
		if (elapsed < TimeSpan.FromMinutes(1))
		{
			return ClientStrings.RecentSetup_RelativeNow;
		}
		if (elapsed < TimeSpan.FromHours(1))
		{
			return FormatUnit(
				(int)Math.Floor(elapsed.TotalMinutes),
				ClientStrings.RecentSetup_RelativeMinute,
				ClientStrings.RecentSetup_RelativeMinutesFormat);
		}
		if (elapsed < TimeSpan.FromDays(1))
		{
			return FormatUnit(
				(int)Math.Floor(elapsed.TotalHours),
				ClientStrings.RecentSetup_RelativeHour,
				ClientStrings.RecentSetup_RelativeHoursFormat);
		}
		if (elapsed < TimeSpan.FromDays(7))
		{
			return FormatUnit(
				(int)Math.Floor(elapsed.TotalDays),
				ClientStrings.RecentSetup_RelativeDay,
				ClientStrings.RecentSetup_RelativeDaysFormat);
		}
		if (elapsed < TimeSpan.FromDays(30))
		{
			return FormatUnit(
				(int)Math.Floor(elapsed.TotalDays / 7),
				ClientStrings.RecentSetup_RelativeWeek,
				ClientStrings.RecentSetup_RelativeWeeksFormat);
		}
		if (elapsed < TimeSpan.FromDays(365))
		{
			return FormatUnit(
				(int)Math.Floor(elapsed.TotalDays / 30),
				ClientStrings.RecentSetup_RelativeMonth,
				ClientStrings.RecentSetup_RelativeMonthsFormat);
		}
		return FormatUnit(
			(int)Math.Floor(elapsed.TotalDays / 365),
			ClientStrings.RecentSetup_RelativeYear,
			ClientStrings.RecentSetup_RelativeYearsFormat);
	}

	private static string FormatUnit(
		int value,
		string singular,
		string pluralFormat) =>
		value == 1
			? singular
			: string.Format(CultureInfo.CurrentCulture, pluralFormat, value);

	private static RecentSetupRoleGroupPresentation ProjectGroup(
		RecentSetup setup,
		LobbySetupMetadata metadata,
		RoleGroup group)
	{
		var roleMetadata = metadata.AvailableRoles
			.Where(role => role.Group == group && setup.RoleCounts.ContainsKey(role.Role))
			.OrderBy(role => role.Role == MainRoleType.SimpleVillager)
			.ToArray();
		var roles = roleMetadata
			.Select(role => new RecentSetupRolePresentation(
				role.Role,
				role.DisplayName,
				Glyph(role.Role),
				role.Role is MainRoleType.SimpleWerewolf or MainRoleType.SimpleVillager
					? setup.RoleCounts[role.Role]
					: null))
			.ToArray();
		return new RecentSetupRoleGroupPresentation(
			group,
			roleMetadata.FirstOrDefault()?.GroupDisplayName ?? group.GetDisplayName(),
			roleMetadata.Sum(role => setup.RoleCounts[role.Role]),
			roles);
	}

	private static string Glyph(MainRoleType role) => role switch
	{
		MainRoleType.SimpleWerewolf => "🐺",
		MainRoleType.BigBadWolf => "👹",
		MainRoleType.AccursedWolfFather => "🧛",
		MainRoleType.WhiteWerewolf => "🌕",
		MainRoleType.SimpleVillager => "🧑‍🌾",
		MainRoleType.Seer => "🔮",
		MainRoleType.Witch => "🧪",
		MainRoleType.Hunter => "🏹",
		MainRoleType.Cupid => "💘",
		MainRoleType.LittleGirl => "👧",
		MainRoleType.Defender => "🛡️",
		MainRoleType.Elder => "👴",
		MainRoleType.Fox => "🦊",
		MainRoleType.BearTamer => "🐻",
		MainRoleType.KnightWithRustySword => "⚔️",
		MainRoleType.StutteringJudge => "⚖️",
		MainRoleType.TwoSisters => "👭",
		MainRoleType.ThreeBrothers => "👬",
		MainRoleType.Thief => "🥷",
		MainRoleType.DevotedServant => "🧹",
		MainRoleType.Actor => "🎭",
		MainRoleType.WildChild => "🐒",
		MainRoleType.WolfHound => "🐕",
		MainRoleType.Angel => "👼",
		MainRoleType.Piper => "🪈",
		MainRoleType.PrejudicedManipulator => "🧠",
		MainRoleType.VillagerVillager => "👥",
		MainRoleType.Scapegoat => "🐐",
		MainRoleType.VillageIdiot => "🃏",
		_ => throw new InvalidOperationException(
			$"Role {role} has no recent-setup presentation glyph.")
	};
}
