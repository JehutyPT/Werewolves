using FluentAssertions;
using System.Globalization;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public sealed class RecentSetupProjectionTests
{
	[Fact]
	public void Project_UsesCanonicalGroupMetadataOrderAndEverySelectableRoleGlyph()
	{
		var metadata = LobbySetupMetadataFixture.Default();
		var glyphs = new Dictionary<MainRoleType, string>
		{
			[MainRoleType.SimpleWerewolf] = "🐺",
			[MainRoleType.BigBadWolf] = "👹",
			[MainRoleType.AccursedWolfFather] = "🧛",
			[MainRoleType.WhiteWerewolf] = "🌕",
			[MainRoleType.SimpleVillager] = "🧑‍🌾",
			[MainRoleType.Seer] = "🔮",
			[MainRoleType.Witch] = "🧪",
			[MainRoleType.Hunter] = "🏹",
			[MainRoleType.Cupid] = "💘",
			[MainRoleType.LittleGirl] = "👧",
			[MainRoleType.Defender] = "🛡️",
			[MainRoleType.Elder] = "👴",
			[MainRoleType.Fox] = "🦊",
			[MainRoleType.BearTamer] = "🐻",
			[MainRoleType.KnightWithRustySword] = "⚔️",
			[MainRoleType.StutteringJudge] = "⚖️",
			[MainRoleType.TwoSisters] = "👭",
			[MainRoleType.ThreeBrothers] = "👬",
			[MainRoleType.Thief] = "🥷",
			[MainRoleType.DevotedServant] = "🧹",
			[MainRoleType.Actor] = "🎭",
			[MainRoleType.WildChild] = "🐒",
			[MainRoleType.WolfHound] = "🐕",
			[MainRoleType.Angel] = "👼",
			[MainRoleType.Piper] = "🪈",
			[MainRoleType.PrejudicedManipulator] = "🧠",
			[MainRoleType.VillagerVillager] = "👥",
			[MainRoleType.Scapegoat] = "🐐",
			[MainRoleType.VillageIdiot] = "🃏"
		};
		var setup = new RecentSetup(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			metadata.AvailableRoles.ToDictionary(role => role.Role, _ => 2),
			DateTimeOffset.UnixEpoch);

		var row = RecentSetupProjection.Project(
			setup,
			metadata,
			DateTimeOffset.UnixEpoch);

		row.RoleGroups.Select(group => group.Group).Should().Equal(
			RoleGroup.Werewolves,
			RoleGroup.Villagers,
			RoleGroup.Ambiguous,
			RoleGroup.Loners);
		foreach (var group in row.RoleGroups)
		{
			var expectedOrder = metadata.AvailableRoles
				.Where(role => role.Group == group.Group)
				.OrderBy(role => role.Role == MainRoleType.SimpleVillager)
				.Select(role => role.Role);
			group.Roles.Select(role => role.Role).Should().Equal(expectedOrder);
		}
		row.RoleGroups.SelectMany(group => group.Roles)
			.Should().OnlyContain(role => role.Glyph == glyphs[role.Role]);
		row.RoleGroups.SelectMany(group => group.Roles)
			.Single(role => role.Role == MainRoleType.SimpleWerewolf)
			.Multiplier.Should().Be(2);
		row.RoleGroups.SelectMany(group => group.Roles)
			.Single(role => role.Role == MainRoleType.SimpleVillager)
			.Multiplier.Should().Be(2);
		row.RoleGroups.SelectMany(group => group.Roles)
			.Where(role => role.Role is not MainRoleType.SimpleWerewolf and
				not MainRoleType.SimpleVillager)
			.Should().OnlyContain(role => role.Multiplier == null);
		row.RoleGroups.Single(group => group.Group == RoleGroup.Loners)
			.Roles.Should().ContainSingle(role => role.Role == MainRoleType.WhiteWerewolf);
		var villagerRoles = row.RoleGroups
			.Single(group => group.Group == RoleGroup.Villagers)
			.Roles.Select(role => role.Role).ToArray();
		villagerRoles.Should().Contain(MainRoleType.Actor);
		villagerRoles.Should().Contain(MainRoleType.VillagerVillager);
		villagerRoles.Should().Contain(MainRoleType.Scapegoat);
		villagerRoles.Should().Contain(MainRoleType.VillageIdiot);
		row.TotalRoleCardCount.Should().Be(metadata.AvailableRoles.Count * 2);
	}

	[Fact]
	public void Project_WhenElapsedTimeIsUnderOneMinute_UsesLocalizedNowLabel()
	{
		var now = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
		var setup = new RecentSetup(
			["Ana"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleVillager] = 1
			},
			now - TimeSpan.FromSeconds(59));

		RecentSetupProjection.Project(
			setup,
			LobbySetupMetadataFixture.Default(),
			now).RelativeDate.Should().Be(ClientStrings.RecentSetup_RelativeNow);

		var futureSetup = new RecentSetup(
			setup.PlayerNames,
			setup.RoleCounts,
			now + TimeSpan.FromDays(1));
		RecentSetupProjection.Project(
			futureSetup,
			LobbySetupMetadataFixture.Default(),
			now).RelativeDate.Should().Be(ClientStrings.RecentSetup_RelativeNow);
	}

	[Fact]
	public void Project_FloorsElapsedUtcToTheLargestLocalizedRelativeDateUnit()
	{
		var cases = new (TimeSpan Elapsed, string Expected)[]
		{
			(TimeSpan.FromMinutes(1), ClientStrings.RecentSetup_RelativeMinute),
			(TimeSpan.FromMinutes(2), Format(ClientStrings.RecentSetup_RelativeMinutesFormat, 2)),
			(TimeSpan.FromMinutes(59), Format(ClientStrings.RecentSetup_RelativeMinutesFormat, 59)),
			(TimeSpan.FromHours(1), ClientStrings.RecentSetup_RelativeHour),
			(TimeSpan.FromHours(23), Format(ClientStrings.RecentSetup_RelativeHoursFormat, 23)),
			(TimeSpan.FromDays(1), ClientStrings.RecentSetup_RelativeDay),
			(TimeSpan.FromDays(6), Format(ClientStrings.RecentSetup_RelativeDaysFormat, 6)),
			(TimeSpan.FromDays(7), ClientStrings.RecentSetup_RelativeWeek),
			(TimeSpan.FromDays(29), Format(ClientStrings.RecentSetup_RelativeWeeksFormat, 4)),
			(TimeSpan.FromDays(30), ClientStrings.RecentSetup_RelativeMonth),
			(TimeSpan.FromDays(364), Format(ClientStrings.RecentSetup_RelativeMonthsFormat, 12)),
			(TimeSpan.FromDays(365), ClientStrings.RecentSetup_RelativeYear),
			(TimeSpan.FromDays(730), Format(ClientStrings.RecentSetup_RelativeYearsFormat, 2))
		};
		var now = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
		var metadata = LobbySetupMetadataFixture.Default();

		foreach (var (elapsed, expected) in cases)
		{
			var setup = new RecentSetup(
				["Ana"],
				new Dictionary<MainRoleType, int>
				{
					[MainRoleType.SimpleVillager] = 1
				},
				now - elapsed);

			RecentSetupProjection.Project(setup, metadata, now).RelativeDate
				.Should().Be(expected, $"the elapsed duration is {elapsed}");
		}
	}

	private static string Format(string format, int value) =>
		string.Format(CultureInfo.CurrentCulture, format, value);
}
