using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Werewolves.Client.BrowserQaHost;
using Werewolves.Client.Components;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Xunit;

namespace Werewolves.Client.Tests.BrowserQaHost;

public class BrowserQaHostCompositionTests
{
	[Fact]
	public async Task Services_RenderSharedRoutesWithBrowserSafeAdapters()
	{
		using var context = new BunitContext();

		BrowserQaHostCulture.UsePortuguese();
		context.Services.AddBrowserQaHostModeratorServices();

		context.Services.GetRequiredService<GameClientManager>().Should().NotBeNull();
		context.Services.GetRequiredService<LobbySetupState>().PlayerNames.Should().NotBeEmpty();
		context.Services.GetRequiredService<GameplayWakeLockController>().MoveTo(GameplayWakeLockArea.Lobby);
		context.Services.GetRequiredService<IScreenWakeLock>().KeepScreenOn.Should().BeTrue();
		context.Services.GetRequiredService<IHapticFeedbackService>().Invoking(haptic => haptic.Click()).Should().NotThrow();
		context.Services.GetRequiredService<IGameSessionSaveStore>().Load().Should().BeNull();

		var audio = context.Services.GetRequiredService<IInstructionAudioPlayback>();
		await audio.SetMutedAsync(true, instruction: null);
		audio.IsMuted.Should().BeTrue();

		var rendered = context.Render<Routes>();

		rendered.Markup.Should().Contain("ww-app-shell");
		rendered.Markup.Should().Contain(ClientStrings.LobbyRoster_Title);
	}

	[Fact]
	public void SharedRoutes_WhenDashboardScenarioIsSeeded_RendersDashboardFlow()
	{
		using var context = new BunitContext();
		BrowserQaHostCulture.UsePortuguese();
		context.Services.AddBrowserQaHostModeratorServices();

		BrowserQaScenarioSeeder.Apply(
			BrowserQaScenario.Dashboard,
			context.Services.GetRequiredService<LobbySetupState>(),
			context.Services.GetRequiredService<GameClientManager>());

		var rendered = context.Render<Routes>();

		rendered.Markup.Should().Contain("ww-dashboard-shell");
		rendered.Markup.Should().Contain(ClientStrings.Dashboard_TabAction);
	}

	[Fact]
	public void SharedRoutes_WhenVictoryScenarioIsSeeded_RendersVictoryFlow()
	{
		using var context = new BunitContext();
		BrowserQaHostCulture.UsePortuguese();
		context.Services.AddBrowserQaHostModeratorServices();

		BrowserQaScenarioSeeder.Apply(
			BrowserQaScenario.Victory,
			context.Services.GetRequiredService<LobbySetupState>(),
			context.Services.GetRequiredService<GameClientManager>());

		var rendered = context.Render<Routes>();

		rendered.Markup.Should().Contain("victory-title");
		rendered.Markup.Should().Contain(ClientStrings.Victory_Title);
	}

	[Fact]
	public void BrowserQaHostProject_ReferencesSharedBoundaryWithoutMaui()
	{
		var project = XDocument.Load(ClientTestReferences.Paths.RepositoryPath(
			"Werewolves.Client.BrowserQaHost",
			"Werewolves.Client.BrowserQaHost.csproj"));

		project.Descendants("TargetFramework").Single().Value.Should().Be("net10.0");
		project.Descendants("TargetFrameworks").Should().BeEmpty();
		project.Descendants("UseMaui").Should().BeEmpty();

		var projectReferences = project.Descendants("ProjectReference")
			.Select(reference => reference.Attribute("Include")?.Value)
			.Where(reference => reference is not null)
			.ToArray();

		projectReferences.Should().Contain(@"..\Werewolves.Client.Shared\Werewolves.Client.Shared.csproj");
		projectReferences.Should().NotContain(reference =>
			reference!.Contains("Werewolves.UI.MobileClient", StringComparison.Ordinal) ||
			reference.Contains(@"..\Werewolves.Client\", StringComparison.Ordinal));
	}

	[Fact]
	public void BrowserQaHostWebApplication_ComposesWithAspNetCoreScopeValidation()
	{
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			ApplicationName = typeof(Program).Assembly.GetName().Name,
			ContentRootPath = ClientTestReferences.Paths.RepositoryPath("Werewolves.Client.BrowserQaHost"),
			EnvironmentName = Environments.Development
		});

		builder.Services.AddDataProtection()
			.UseEphemeralDataProtectionProvider();
		builder.Services.AddRazorComponents()
			.AddInteractiveServerComponents();
		builder.Services.AddBrowserQaHostModeratorServices();

		using var app = builder.Build();

		using var scope = app.Services.CreateScope();
		scope.ServiceProvider.GetRequiredService<GameClientManager>().Should().NotBeNull();
	}

	[Fact]
	public void BrowserQaHostPhoneFrame_KeepsFixedOverlaysPinnedWhileShellsScroll()
	{
		var css = File.ReadAllText(ClientTestReferences.Paths.RepositoryPath(
			"Werewolves.Client.BrowserQaHost",
			"wwwroot",
			"css",
			"browser-qa-host.css"));

		css.Should().MatchRegex(SelectorBlockPattern(".phone-frame", "position: relative"));
		css.Should().MatchRegex(SelectorBlockPattern(".phone-frame", "height: min(800px, 100vh)"));
		css.Should().MatchRegex(SelectorBlockPattern(".phone-frame", "overflow: hidden"));
		css.Should().MatchRegex(SelectorBlockPattern(".phone-frame", "transform: translateZ(0)"));
		css.Should().MatchRegex(SelectorBlockPattern(
			@"\.phone-frame\s+\.ww-app-shell,\s*\.phone-frame\s+\.ww-dashboard-shell",
			"min-height: 100%"));
		css.Should().MatchRegex(SelectorBlockPattern(
			@"\.phone-frame\s+\.ww-app-shell,\s*\.phone-frame\s+\.ww-dashboard-shell",
			"height: 100%"));
		css.Should().MatchRegex(SelectorBlockPattern(
			@"\.phone-frame\s+\.ww-app-shell,\s*\.phone-frame\s+\.ww-dashboard-shell",
			"overflow-y: auto"));
	}

	[Fact]
	public void BrowserQaHostActionArea_ReservesScrollableRosterSpaceWithoutCatchingScrimClicks()
	{
		var css = File.ReadAllText(ClientTestReferences.Paths.RepositoryPath(
			"Werewolves.Client.BrowserQaHost",
			"wwwroot",
			"css",
			"browser-qa-host.css"));

		css.Should().MatchRegex(SelectorBlockPattern(".phone-frame", "--browser-qa-action-space: 120px"));
		css.Should().MatchRegex(SelectorBlockPattern(
			@"\.phone-frame\s+\.ww-app-shell",
			"padding-bottom: calc(var(--browser-qa-action-space) + 24px)"));
		css.Should().MatchRegex(SelectorBlockPattern(
			@"\.phone-frame\s+\.ww-action-bar,\s*\.phone-frame\s+\.ww-dashboard-action-zone",
			"pointer-events: none"));
		css.Should().MatchRegex(SelectorBlockPattern(
			@"\.phone-frame\s+\.ww-action-bar\s+>\s+\*,\s*\.phone-frame\s+\.ww-dashboard-action-zone\s+>\s+\*",
			"pointer-events: auto"));
	}

	[Fact]
	public void BrowserQaHostResponsiveRules_FollowPhoneFrameWidthInsteadOfBrowserViewport()
	{
		var css = File.ReadAllText(ClientTestReferences.Paths.RepositoryPath(
			"Werewolves.Client.BrowserQaHost",
			"wwwroot",
			"css",
			"browser-qa-host.css"));

		css.Should().MatchRegex(SelectorBlockPattern(".phone-frame", "container: browser-qa-phone / inline-size"));
		css.Should().Contain("@container browser-qa-phone (max-width: 390px)");
		css.Should().MatchRegex(SelectorBlockPattern(@"\.phone-frame\s+\.ww-add-row", "grid-template-columns: 1fr"));
		css.Should().MatchRegex(SelectorBlockPattern(@"\.phone-frame\s+\.ww-roster-item", "grid-template-columns: 28px minmax(0, 1fr)"));
		css.Should().MatchRegex(SelectorBlockPattern(@"\.phone-frame\s+\.ww-row-actions", "grid-column: 1 / -1"));
		css.Should().MatchRegex(SelectorBlockPattern(@"\.phone-frame\s+\.ww-dashboard-tab", "font-size: 11px"));
		css.Should().MatchRegex(SelectorBlockPattern(@"\.phone-frame\s+\.ww-stats-grid", "grid-template-columns: 1fr"));
		css.Should().MatchRegex(SelectorBlockPattern(@"\.phone-frame\s+\.ww-elimination-log__entry", "grid-template-columns: 1fr"));
	}

	private static string SelectorBlockPattern(string selector, string declaration)
	{
		return $@"(?s){selector}\s*\{{(?:(?!\}}).)*{Regex.Escape(declaration)}";
	}
}
