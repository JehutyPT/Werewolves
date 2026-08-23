using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Web;
using Werewolves.Client.Components;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public sealed class LandingNavigationBunitTests
{
	[Fact]
	public void EmptyRecovery_RendersLandingBeforeAnyLobbySurface()
	{
		using var context = new ModeratorComponentTestContext();

		var cut = context.RenderModeratorComponent<Routes>();

		cut.FindAll(TestId(ModeratorUiTestIds.LandingShell)).Should().ContainSingle();
	}

	[Fact]
	public void EmptyRecovery_NewGameRendersCurrentLobbyRoster()
	{
		using var context = new ModeratorComponentTestContext();
		var cut = context.RenderModeratorComponent<Routes>();
		var newGameButtons = cut.FindAll(TestId(ModeratorUiTestIds.LandingNewGameButton));

		newGameButtons.Should().ContainSingle();
		newGameButtons.Single().TextContent.Trim().Should().Be(ClientStrings.Landing_NewGameButton);
		newGameButtons.Single().Click();

		cut.FindAll("h1").Should().ContainSingle()
			.Which.TextContent.Trim().Should().Be(ClientStrings.LobbyRoster_Title);
	}

	[Fact]
	public void RecentSetups_RenderNewestFirstWithVariantCContent()
	{
		var clock = new ManualTimeProvider(
			new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero));
		var recentStore = new InMemoryRecentSetupStore(clock);
		recentStore.Capture(
			["Ana", "Bruno", "Carla", "Diogo", "Eva", "Filipe"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 5
			});
		clock.Advance(TimeSpan.FromHours(2));
		recentStore.Capture(
			["Fábio", "Gabi", "Hugo", "Inês", "João", "Lia"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.Seer] = 1,
				[MainRoleType.SimpleVillager] = 4
			});
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<TimeProvider>(clock);
		context.Services.AddSingleton<IRecentSetupStore>(recentStore);

		var cut = context.RenderModeratorComponent<Routes>();

		var rows = cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow));
		rows.Should().HaveCount(2);
		rows[0].TextContent.Should().Contain("6");
		rows[0].TextContent.Should().Contain("🐺");
		rows[0].TextContent.Should().Contain("×1");
		rows[0].TextContent.Should().Contain("🔮");
		rows[0].TextContent.Should().Contain(ClientStrings.RecentSetup_RelativeNow);
		rows[1].TextContent.Should().Contain("6");
		var olderRelativeDate = string.Format(
			System.Globalization.CultureInfo.CurrentCulture,
			ClientStrings.RecentSetup_RelativeHoursFormat,
			2);
		rows[1].TextContent.Should().Contain(olderRelativeDate);
		cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupDelete))
			.Should().HaveCount(2);
		var select = rows[0].QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupSelect))!;
		select.LocalName.Should().Be("button");
		select.GetAttribute("type").Should().Be("button");
		var metadata = context.Services.GetRequiredService<LobbySetupMetadata>();
		string RoleCount(MainRoleType role, int count) => string.Format(
			System.Globalization.CultureInfo.CurrentCulture,
			ClientStrings.Landing_RecentRoleCountFormat,
			metadata.AvailableRoles.Single(item => item.Role == role).DisplayName,
			count);
		var newestComposition = string.Join(
			", ",
			RoleCount(MainRoleType.SimpleWerewolf, 1),
			RoleCount(MainRoleType.Seer, 1),
			RoleCount(MainRoleType.SimpleVillager, 4));
		var newestSummary = string.Format(
			System.Globalization.CultureInfo.CurrentCulture,
			ClientStrings.Landing_RecentSetupSummaryFormat,
			6,
			"Fábio, Gabi, Hugo, Inês, João, Lia",
			newestComposition,
			ClientStrings.RecentSetup_RelativeNow);
		select.GetAttribute("aria-label").Should().Be(
			string.Format(
				System.Globalization.CultureInfo.CurrentCulture,
				ClientStrings.Landing_RecentSetupSelectAriaFormat,
				newestSummary));
		var delete = rows[0].QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupDelete))!;
		delete.LocalName.Should().Be("button");
		delete.GetAttribute("type").Should().Be("button");
		delete.GetAttribute("aria-label").Should().Be(
			string.Format(
				System.Globalization.CultureInfo.CurrentCulture,
				ClientStrings.Landing_RecentSetupDeleteAriaFormat,
				newestSummary));
		var olderComposition = string.Join(
			", ",
			RoleCount(MainRoleType.SimpleWerewolf, 1),
			RoleCount(MainRoleType.SimpleVillager, 5));
		var olderSummary = string.Format(
			System.Globalization.CultureInfo.CurrentCulture,
			ClientStrings.Landing_RecentSetupSummaryFormat,
			6,
			"Ana, Bruno, Carla, Diogo, Eva, Filipe",
			olderComposition,
			olderRelativeDate);
		rows[1].QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupSelect))!
			.GetAttribute("aria-label").Should().Be(
				string.Format(
					System.Globalization.CultureInfo.CurrentCulture,
					ClientStrings.Landing_RecentSetupSelectAriaFormat,
					olderSummary));
		rows[1].QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupDelete))!
			.GetAttribute("aria-label").Should().Be(
				string.Format(
					System.Globalization.CultureInfo.CurrentCulture,
					ClientStrings.Landing_RecentSetupDeleteAriaFormat,
					olderSummary));
		olderSummary.Should().NotBe(newestSummary);
		rows[0].QuerySelectorAll(TestId(ModeratorUiTestIds.LandingRecentSetupGroup))
			.Select(group => group.GetAttribute("aria-label"))
			.Should().Equal(
				string.Format(
					System.Globalization.CultureInfo.CurrentCulture,
					ClientStrings.Landing_RecentRoleGroupAriaFormat,
					RoleGroup.Werewolves.GetDisplayName(),
					1),
				string.Format(
					System.Globalization.CultureInfo.CurrentCulture,
					ClientStrings.Landing_RecentRoleGroupAriaFormat,
					RoleGroup.Villagers.GetDisplayName(),
					5));
		var bar = rows[0].QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupBar))!;
		bar.GetAttribute("role").Should().Be("img");
		bar.GetAttribute("aria-label").Should().Be(
			string.Format(
				System.Globalization.CultureInfo.CurrentCulture,
				ClientStrings.Landing_RecentRoleBarAriaFormat,
				6));
	}

	[Fact]
	public void RecentSetups_SameNamesAndDateWithDifferentRoleCountsExposeDistinctActionNames()
	{
		var clock = new ManualTimeProvider(
			new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero));
		var recentStore = new InMemoryRecentSetupStore(clock);
		var names = new[] { "Ana", "Bruno", "Carla", "Diogo", "Eva" };
		recentStore.Capture(
			names,
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 4
			});
		recentStore.Capture(
			names,
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.Seer] = 1,
				[MainRoleType.SimpleVillager] = 3
			});
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<TimeProvider>(clock);
		context.Services.AddSingleton<IRecentSetupStore>(recentStore);

		var cut = context.RenderModeratorComponent<Routes>();
		var rows = cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow));
		var selectNames = rows.Select(row => row
			.QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupSelect))!
			.GetAttribute("aria-label"));
		var deleteNames = rows.Select(row => row
			.QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupDelete))!
			.GetAttribute("aria-label"));

		rows.Should().HaveCount(2);
		selectNames.Should().OnlyHaveUniqueItems();
		deleteNames.Should().OnlyHaveUniqueItems();
		var metadata = context.Services.GetRequiredService<LobbySetupMetadata>();
		var seerRoleCount = string.Format(
			System.Globalization.CultureInfo.CurrentCulture,
			ClientStrings.Landing_RecentRoleCountFormat,
			metadata.AvailableRoles.Single(item => item.Role == MainRoleType.Seer).DisplayName,
			1);
		selectNames.Should().ContainSingle(name =>
			name!.Contains(seerRoleCount, StringComparison.Ordinal));
		deleteNames.Should().ContainSingle(name =>
			name!.Contains(seerRoleCount, StringComparison.Ordinal));
	}

	[Fact]
	public async Task RecentSetup_LeftwardPointerSwipeDeletesTheSelectedRow()
	{
		var clock = new ManualTimeProvider(
			new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero));
		var recentStore = new InMemoryRecentSetupStore(clock);
		recentStore.Capture(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 4
			});
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<TimeProvider>(clock);
		context.Services.AddSingleton<IRecentSetupStore>(recentStore);
		var cut = context.RenderModeratorComponent<Routes>();
		var row = cut.Find(TestId(ModeratorUiTestIds.LandingRecentSetupRow));

		await row.TriggerEventAsync(
			"onpointerdown",
			new PointerEventArgs { ClientX = 160, ClientY = 20 });
		await row.TriggerEventAsync(
			"onpointerup",
			new PointerEventArgs { ClientX = 80, ClientY = 24 });

		cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow)).Should().BeEmpty();
		recentStore.Load().Should().BeEmpty();
	}

	[Theory]
	[InlineData("onpointerup", 160, 20, 160, 20)]
	[InlineData("onpointerup", 160, 20, 220, 20)]
	[InlineData("onpointerup", 160, 20, 80, 140)]
	[InlineData("onpointerup", 160, 20, 120, 20)]
	[InlineData("onpointercancel", 160, 20, 80, 20)]
	[InlineData("onpointerleave", 160, 20, 80, 20)]
	public async Task RecentSetup_InertPointerGesturesDoNotDelete(
		string completionEvent,
		double startX,
		double startY,
		double endX,
		double endY)
	{
		var recentStore = new InMemoryRecentSetupStore();
		recentStore.Capture(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 4
			});
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IRecentSetupStore>(recentStore);
		var cut = context.RenderModeratorComponent<Routes>();
		var row = cut.Find(TestId(ModeratorUiTestIds.LandingRecentSetupRow));

		await row.TriggerEventAsync(
			"onpointerdown",
			new PointerEventArgs { ClientX = startX, ClientY = startY });
		await row.TriggerEventAsync(
			completionEvent,
			new PointerEventArgs { ClientX = endX, ClientY = endY });

		cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow)).Should().ContainSingle();
		recentStore.Load().Should().ContainSingle();
	}

	[Fact]
	public async Task RecentSetup_FailedSwipeDeleteDoesNotSuppressTheNextIntentionalTap()
	{
		var setup = new RecentSetup(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 4
			},
			DateTimeOffset.UtcNow);
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IRecentSetupStore>(
			new ThrowingDeleteRecentSetupStore(setup));
		var cut = context.RenderModeratorComponent<Routes>();
		var row = cut.Find(TestId(ModeratorUiTestIds.LandingRecentSetupRow));
		await row.TriggerEventAsync(
			"onpointerdown",
			new PointerEventArgs { ClientX = 160, ClientY = 20 });
		await row.TriggerEventAsync(
			"onpointerup",
			new PointerEventArgs { ClientX = 80, ClientY = 20 });
		row = cut.Find(TestId(ModeratorUiTestIds.LandingRecentSetupRow));

		await row.TriggerEventAsync(
			"onpointerdown",
			new PointerEventArgs { ClientX = 160, ClientY = 20 });
		await row.TriggerEventAsync(
			"onpointerup",
			new PointerEventArgs { ClientX = 160, ClientY = 20 });
		row.QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupSelect))!.Click();

		cut.FindAll("h1").Should().ContainSingle()
			.Which.TextContent.Trim().Should().Be(ClientStrings.LobbyRoster_Title);
	}

	[Fact]
	public void RecentSetup_SemanticDeleteRemovesOnlyTheSelectedRow()
	{
		var recentStore = new InMemoryRecentSetupStore();
		recentStore.Capture(
			["Ana", "Bruno", "Carla", "Diogo", "Eva"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 4
			});
		recentStore.Capture(
			["Fábio", "Gabi", "Hugo", "Inês", "João"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.Seer] = 1,
				[MainRoleType.SimpleVillager] = 3
			});
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IRecentSetupStore>(recentStore);
		var cut = context.RenderModeratorComponent<Routes>();

		cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupDelete))[1].Click();

		var remaining = cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow));
		remaining.Should().ContainSingle();
		remaining.Single().TextContent.Should().Contain("🔮");
		recentStore.Load().Should().ContainSingle()
			.Which.PlayerNames.Should().Equal("Fábio", "Gabi", "Hugo", "Inês", "João");
	}

	[Fact]
	public void RecentSetup_FailedDeleteKeepsRowAndActiveSessionUnchanged()
	{
		var recoveryStore = new RecordingSaveStore();
		var setup = new RecentSetup(
			["Fábio", "Gabi", "Hugo", "Inês", "João"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.Seer] = 1,
				[MainRoleType.SimpleVillager] = 3
			},
			DateTimeOffset.UtcNow);
		var recentStore = new ThrowingDeleteRecentSetupStore(setup);
		using var context = CreateContext(recoveryStore);
		context.Services.AddSingleton<IRecentSetupStore>(recentStore);
		StartRecoverableSession(context);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var recoveryPayload = recoveryStore.Load();
		var cut = context.RenderModeratorComponent<Routes>();

		cut.Find(TestId(ModeratorUiTestIds.LandingRecentSetupDelete)).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow)).Should().ContainSingle();
		manager.HasActiveSession.Should().BeTrue();
		recoveryStore.Load().Should().Be(recoveryPayload);
		recentStore.Load().Should().ContainSingle();
	}

	[Fact]
	public void RecentSetup_DeleteDuringActiveSessionLeavesSessionAndRecoveryUntouched()
	{
		var recoveryStore = new RecordingSaveStore();
		var recentStore = new InMemoryRecentSetupStore();
		recentStore.Capture(
			["Fábio", "Gabi", "Hugo", "Inês", "João"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.Seer] = 1,
				[MainRoleType.SimpleVillager] = 3
			});
		using var context = CreateContext(recoveryStore);
		context.Services.AddSingleton<IRecentSetupStore>(recentStore);
		StartRecoverableSession(context);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var activeGameId = manager.ActiveGameId;
		var recoveryPayload = recoveryStore.Load();
		var cut = context.RenderModeratorComponent<Routes>();
		var storedRow = cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow))
			.Single(row => row.TextContent.Contains("🔮", StringComparison.Ordinal));

		storedRow.QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupDelete))!.Click();

		manager.ActiveGameId.Should().Be(activeGameId);
		manager.HasActiveSession.Should().BeTrue();
		recoveryStore.Load().Should().Be(recoveryPayload);
		recentStore.Load().Should().ContainSingle()
			.Which.PlayerNames.Should().Equal(PlayerNames);
	}

	[Fact]
	public void RecentSetup_WithoutRecoverableSessionLoadsExactSetupIntoRoster()
	{
		var recentStore = new InMemoryRecentSetupStore();
		recentStore.Capture(
			["Fábio", "Gabi", "Hugo", "Inês", "João"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.Seer] = 1,
				[MainRoleType.SimpleVillager] = 3
			});
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IRecentSetupStore>(recentStore);
		var cut = context.RenderModeratorComponent<Routes>();

		cut.Find(TestId(ModeratorUiTestIds.LandingRecentSetupSelect)).Click();

		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		lobby.PlayerNames.Should().Equal("Fábio", "Gabi", "Hugo", "Inês", "João");
		lobby.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(1);
		lobby.GetRoleCount(MainRoleType.Seer).Should().Be(1);
		lobby.GetRoleCount(MainRoleType.SimpleVillager).Should().Be(3);
		lobby.AcceptedRoleLockIn.Should().BeNull();
		cut.FindAll("h1").Should().ContainSingle()
			.Which.TextContent.Trim().Should().Be(ClientStrings.LobbyRoster_Title);
	}

	[Fact]
	public void RecentSetup_WithActiveSessionCancelIsInertAndConfirmAbandonsThenLoads()
	{
		var recoveryStore = new RecordingSaveStore();
		var recentStore = new InMemoryRecentSetupStore();
		recentStore.Capture(
			["Fábio", "Gabi", "Hugo", "Inês", "João"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.Seer] = 1,
				[MainRoleType.SimpleVillager] = 3
			});
		using var context = CreateContext(recoveryStore);
		context.Services.AddSingleton<IRecentSetupStore>(recentStore);
		StartRecoverableSession(context);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var activeRoster = manager.CurrentSession!.GetPlayers().Select(player => player.Id).ToArray();
		var recoveryPayload = recoveryStore.Load();
		var cut = context.RenderModeratorComponent<Routes>();
		var storedRow = cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow))
			.Single(row => row.TextContent.Contains("🔮", StringComparison.Ordinal));
		storedRow.QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupSelect))!.Click();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameCancel)).Click();

		manager.HasActiveSession.Should().BeTrue();
		manager.CurrentSession!.GetPlayers().Select(player => player.Id).Should().Equal(activeRoster);
		recoveryStore.Load().Should().Be(recoveryPayload);
		lobby.PlayerNames.Should().Equal(PlayerNames);

		storedRow = cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow))
			.Single(row => row.TextContent.Contains("🔮", StringComparison.Ordinal));
		storedRow.QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupSelect))!.Click();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameConfirm)).Click();

		manager.HasActiveSession.Should().BeFalse();
		lobby.PlayerNames.Should().Equal("Fábio", "Gabi", "Hugo", "Inês", "João");
		lobby.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(1);
		lobby.GetRoleCount(MainRoleType.Seer).Should().Be(1);
		lobby.GetRoleCount(MainRoleType.SimpleVillager).Should().Be(3);
		lobby.AcceptedRoleLockIn.Should().BeNull();
		recoveryStore.Load().Should().BeNull();
		cut.FindAll("h1").Should().ContainSingle()
			.Which.TextContent.Trim().Should().Be(ClientStrings.LobbyRoster_Title);
	}

	[Fact]
	public void RecentSetup_WithActiveSessionAndFailedRecoveryClearKeepsSessionLobbyRecoveryAndLanding()
	{
		var recoveryStore = new RecordingSaveStore();
		var recentStore = new InMemoryRecentSetupStore();
		recentStore.Capture(
			["Fábio", "Gabi", "Hugo", "Inês", "João"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.Seer] = 1,
				[MainRoleType.SimpleVillager] = 3
			});
		using var context = CreateContext(recoveryStore);
		context.Services.AddSingleton<IRecentSetupStore>(recentStore);
		StartRecoverableSession(context);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var activeGameIdBefore = manager.ActiveGameId;
		var serializedSessionBefore = manager.CurrentSession!.Serialize();
		var rosterBefore = lobby.PlayerRoster.ToArray();
		var recoveryPayloadBefore = recoveryStore.Load();
		recoveryStore.ThrowOnClear = true;
		var cut = context.RenderModeratorComponent<Routes>();
		cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow))
			.Count(row => row.TextContent.Contains("🔮", StringComparison.Ordinal))
			.Should().Be(1);

		cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow))
			.Single(row => row.TextContent.Contains("🔮", StringComparison.Ordinal))
			.QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupSelect))!
			.Click();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameConfirm)).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.LandingShell)).Should().ContainSingle();
		cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow))
			.Count(row => row.TextContent.Contains("🔮", StringComparison.Ordinal))
			.Should().Be(1);
		manager.ActiveGameId.Should().Be(activeGameIdBefore);
		manager.CurrentSession!.Serialize().Should().Be(serializedSessionBefore);
		lobby.PlayerRoster.Should().Equal(rosterBefore);
		recoveryStore.Load().Should().Be(recoveryPayloadBefore);
	}

	[Fact]
	public void RecentSetup_UnexpectedLoadFailureIsNotSilentlyConvertedToEmpty()
	{
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IRecentSetupStore>(new UnexpectedFailureRecentSetupStore());

		var render = () => context.RenderModeratorComponent<Routes>();

		render.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void RecentSetup_WithFinishedRecoverableSessionConfirmAbandonsThenLoads()
	{
		var recoveryStore = new RecordingSaveStore();
		var recentStore = new InMemoryRecentSetupStore();
		recentStore.Capture(
			["Fábio", "Gabi", "Hugo", "Inês", "João"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.Seer] = 1,
				[MainRoleType.SimpleVillager] = 3
			});
		using var context = CreateContext(recoveryStore);
		context.Services.AddSingleton<IRecentSetupStore>(recentStore);
		var start = StartRecoverableSession(context);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		PostGameLobbyPrefillBunitTests.PlayToWerewolfVictoryAtDawn(manager, start);
		manager.CurrentInstruction.Should().BeOfType<FinishedGameConfirmationInstruction>();
		var cut = context.RenderModeratorComponent<Routes>();
		var storedRow = cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow))
			.Single(row => row.TextContent.Contains("🔮", StringComparison.Ordinal));

		storedRow.QuerySelector(TestId(ModeratorUiTestIds.LandingRecentSetupSelect))!.Click();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameConfirm)).Click();

		manager.HasActiveSession.Should().BeFalse();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		lobby.PlayerNames.Should().Equal("Fábio", "Gabi", "Hugo", "Inês", "João");
		lobby.GetRoleCount(MainRoleType.Seer).Should().Be(1);
		lobby.AcceptedRoleLockIn.Should().BeNull();
		recoveryStore.Load().Should().BeNull();
		cut.FindAll("h1").Should().ContainSingle()
			.Which.TextContent.Trim().Should().Be(ClientStrings.LobbyRoster_Title);
	}

	[Fact]
	public void ActiveRecovery_ContinueRendersExistingDashboardDestination()
	{
		var store = new RecordingSaveStore();
		using (var seedContext = CreateContext(store))
		{
			StartRecoverableSession(seedContext);
		}

		using var recoveredContext = CreateContext(store);
		var cut = recoveredContext.RenderModeratorComponent<Routes>();
		var manager = recoveredContext.Services.GetRequiredService<GameClientManager>();

		manager.HasActiveSession.Should().BeTrue();
		cut.FindAll(TestId(ModeratorUiTestIds.LandingShell)).Should().ContainSingle();
		var continueButtons = cut.FindAll(TestId(ModeratorUiTestIds.LandingContinueButton));
		continueButtons.Should().ContainSingle();
		continueButtons.Single().TextContent.Trim().Should().Be(ClientStrings.Landing_ContinueButton);

		continueButtons.Single().Click();

		cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().ContainSingle();
	}

	[Fact]
	public void FinishedRecovery_ContinueRendersExistingVictoryDestination()
	{
		var store = new RecordingSaveStore();
		using (var seedContext = CreateContext(store))
		{
			var seedManager = seedContext.Services.GetRequiredService<GameClientManager>();
			var startInstruction = StartRecoverableSession(seedContext);
			PostGameLobbyPrefillBunitTests.PlayToWerewolfVictoryAtDawn(
				seedManager,
				startInstruction);
		}

		using var recoveredContext = CreateContext(store);
		var cut = recoveredContext.RenderModeratorComponent<Routes>();
		var recoveredManager = recoveredContext.Services.GetRequiredService<GameClientManager>();

		recoveredManager.CurrentInstruction.Should().BeOfType<FinishedGameConfirmationInstruction>();
		cut.Find(TestId(ModeratorUiTestIds.LandingContinueButton)).Click();

		cut.FindAll("h1").Should().ContainSingle()
			.Which.TextContent.Trim().Should().Be(ClientStrings.Victory_Title);
	}

	[Fact]
	public void StagedLobbyRecovery_NewGameRendersRecoveredLobbyWithoutContinueOrResave()
	{
		var store = new RecordingSaveStore();
		using (var seedContext = CreateContext(store))
		{
			StageRecoverableLobby(seedContext);
		}
		var savesBeforeRecovery = store.SaveCount;

		using var recoveredContext = CreateContext(store);
		var cut = recoveredContext.RenderModeratorComponent<Routes>();

		cut.FindAll(TestId(ModeratorUiTestIds.LandingContinueButton)).Should().BeEmpty();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();

		AssertRenderedPlayerOrder(cut, PlayerNames);
		store.SaveCount.Should().Be(savesBeforeRecovery);
	}

	[Fact]
	public void UnreadableRecovery_NewGameRendersBlankLobbyWithoutContinue()
	{
		var store = new RecordingSaveStore("not-a-recovery-payload");
		using var context = CreateContext(store);
		var cut = context.RenderModeratorComponent<Routes>();

		cut.FindAll(TestId(ModeratorUiTestIds.LandingContinueButton)).Should().BeEmpty();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();

		context.Services.GetRequiredService<LobbySetupState>().PlayerRoster.Should().BeEmpty();
		store.ClearCount.Should().Be(1);
		cut.FindAll("h1").Should().ContainSingle()
			.Which.TextContent.Trim().Should().Be(ClientStrings.LobbyRoster_Title);
	}

	[Fact]
	public void ActiveRecovery_NewGameCancelIsInertAndConfirmUsesExistingClearBoundaryOnce()
	{
		var store = new RecordingSaveStore();
		using (var seedContext = CreateContext(store))
		{
			StartRecoverableSession(seedContext);
		}

		using var recoveredContext = CreateContext(store);
		var cut = recoveredContext.RenderModeratorComponent<Routes>();
		var manager = recoveredContext.Services.GetRequiredService<GameClientManager>();
		var lobby = recoveredContext.Services.GetRequiredService<LobbySetupState>();
		var saveCountBeforeChoice = store.SaveCount;
		var clearCountBeforeChoice = store.ClearCount;

		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();

		var dialogs = cut.FindAll("[role='dialog']");
		dialogs.Should().ContainSingle();
		dialogs.Single().TextContent.Should().Contain(ClientStrings.Landing_NewGameDialogTitle);
		dialogs.Single().TextContent.Should().Contain(ClientStrings.Landing_NewGameDialogDescription);
		dialogs.Single().TextContent.Should().Contain(ClientStrings.Landing_NewGameCancelButton);
		dialogs.Single().TextContent.Should().Contain(ClientStrings.Landing_NewGameConfirmButton);
		store.SaveCount.Should().Be(saveCountBeforeChoice);
		store.ClearCount.Should().Be(clearCountBeforeChoice);
		manager.HasActiveSession.Should().BeTrue();

		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameCancel)).Click();

		cut.FindAll("[role='dialog']").Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.LandingShell)).Should().ContainSingle();
		store.SaveCount.Should().Be(saveCountBeforeChoice);
		store.ClearCount.Should().Be(clearCountBeforeChoice);
		manager.HasActiveSession.Should().BeTrue();

		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameConfirm)).Click();

		manager.HasActiveSession.Should().BeFalse();
		store.ClearCount.Should().Be(clearCountBeforeChoice + 1);
		store.SaveCount.Should().Be(saveCountBeforeChoice);
		store.Load().Should().BeNull();
		lobby.PlayerRoster.Select(player => player.Name).Should().Equal(PlayerNames);
		lobby.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(1);
		lobby.GetRoleCount(MainRoleType.SimpleVillager).Should().Be(4);
		cut.FindAll("h1").Should().ContainSingle()
			.Which.TextContent.Trim().Should().Be(ClientStrings.LobbyRoster_Title);
	}

	[Fact]
	public void RosterBackThenNewGamePreservesCompleteInProcessLobby()
	{
		var store = new RecordingSaveStore();
		using var context = CreateContext(store);
		StageRecoverableLobby(context);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var expectedRoster = lobby.PlayerRoster.ToArray();
		var expectedRoleLockIn = lobby.AcceptedRoleLockIn;
		var saveCountBeforeNavigation = store.SaveCount;
		var cut = context.RenderModeratorComponent<Routes>();

		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		var backButtons = cut.FindAll(TestId(ModeratorUiTestIds.LobbyRosterBack));

		backButtons.Should().ContainSingle();
		backButtons.Single().TextContent.Trim().Should().Be(ClientStrings.LobbyRoster_BackButton);
		backButtons.Single().Click();
		cut.FindAll(TestId(ModeratorUiTestIds.LandingShell)).Should().ContainSingle();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();

		lobby.PlayerRoster.Should().Equal(expectedRoster);
		lobby.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(1);
		lobby.GetRoleCount(MainRoleType.SimpleVillager).Should().Be(4);
		lobby.AcceptedRoleLockIn.Should().Be(expectedRoleLockIn);
		store.SaveCount.Should().Be(saveCountBeforeNavigation);
		AssertRenderedPlayerOrder(cut, PlayerNames);
	}

	[Fact]
	public void ReturningToLandingAfterLobbyExitRefreshesTheCapturedRecentSetup()
	{
		var recentStore = new InMemoryRecentSetupStore();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IRecentSetupStore>(recentStore);
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedLobby(lobby);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		manager.StartGame(lobby);
		manager.ClearSession();

		cut.Find(TestId(ModeratorUiTestIds.LobbyRosterBack)).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.LandingRecentSetupRow))
			.Should().ContainSingle();
		recentStore.Load().Should().ContainSingle()
			.Which.PlayerNames.Should().Equal(PlayerNames);
	}

	[Fact]
	public void ColdLaunch_LandingUsesNoWakeLock()
	{
		var wakeLock = new RecordingScreenWakeLock { KeepScreenOn = true };
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IScreenWakeLock>(wakeLock);

		context.RenderModeratorComponent<Routes>();

		wakeLock.KeepScreenOn.Should().BeFalse();
	}

	private static ModeratorComponentTestContext CreateContext(RecordingSaveStore store)
	{
		var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IGameSessionSaveStore>(store);
		return context;
	}

	private static StartGameConfirmationInstruction StartRecoverableSession(
		ModeratorComponentTestContext context)
	{
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedLobby(lobby);

		return context.Services.GetRequiredService<GameClientManager>().StartGame(lobby);
	}

	private static void StageRecoverableLobby(ModeratorComponentTestContext context)
	{
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedLobby(lobby);
		context.Services.GetRequiredService<GameClientManager>()
			.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
	}

	private static void SeedLobby(LobbySetupState lobby)
	{
		foreach (var playerName in PlayerNames)
		{
			lobby.AddPlayer(playerName);
		}

		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 4; index++)
		{
			lobby.IncrementRole(MainRoleType.SimpleVillager);
		}

	}

	private static void AssertRenderedPlayerOrder(
		Bunit.IRenderedComponent<Routes> cut,
		IReadOnlyList<string> expectedPlayerNames)
	{
		var roster = cut.FindAll("[aria-label]")
			.Single(element => element.GetAttribute("aria-label") == ClientStrings.LobbyRoster_SeatOrderLabel);
		var rows = roster.QuerySelectorAll("li");

		rows.Should().HaveCount(expectedPlayerNames.Count);
		rows.Select(row => expectedPlayerNames.Single(name => row.TextContent.Contains(name, StringComparison.Ordinal)))
			.Should().Equal(expectedPlayerNames);
	}

	private static string[] PlayerNames =>
	[
		ClientTestReferences.PlayerNames.Ana,
		ClientTestReferences.PlayerNames.Bruno,
		ClientTestReferences.PlayerNames.Catarina,
		ClientTestReferences.PlayerNames.Diana,
		ClientTestReferences.PlayerNames.Eduardo
	];

	private static string TestId(string value) => $"[data-testid='{value}']";

	private sealed class RecordingSaveStore(string? initialPayload = null) : IGameSessionSaveStore
	{
		private string? _payload = initialPayload;

		public int SaveCount { get; private set; }
		public int ClearCount { get; private set; }
		public bool ThrowOnClear { get; set; }

		public string? Load() => _payload;

		public void Save(string serializedSession)
		{
			SaveCount++;
			_payload = serializedSession;
		}

		public void Clear()
		{
			ClearCount++;
			if (ThrowOnClear)
			{
				throw new IOException("Clear unavailable.");
			}
			_payload = null;
		}
	}

	private sealed class RecordingScreenWakeLock : IScreenWakeLock
	{
		public bool KeepScreenOn { get; set; }
	}

	private sealed class ThrowingDeleteRecentSetupStore(RecentSetup setup)
		: IRecentSetupStore
	{
		public IReadOnlyList<RecentSetup> Load() => [setup];

		public void Capture(
			IReadOnlyList<string> playerNames,
			IReadOnlyDictionary<MainRoleType, int> roleCounts)
		{
		}

		public void Delete(RecentSetup selected) =>
			throw new IOException(ClientTestReferences.ExceptionMessages.SaveFailed);
	}

	private sealed class UnexpectedFailureRecentSetupStore : IRecentSetupStore
	{
		public IReadOnlyList<RecentSetup> Load() =>
			throw new InvalidOperationException("Unexpected recents defect.");

		public void Capture(
			IReadOnlyList<string> playerNames,
			IReadOnlyDictionary<MainRoleType, int> roleCounts)
		{
		}

		public void Delete(RecentSetup setup)
		{
		}
	}
}
