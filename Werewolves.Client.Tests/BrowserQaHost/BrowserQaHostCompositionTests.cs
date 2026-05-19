using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
}
