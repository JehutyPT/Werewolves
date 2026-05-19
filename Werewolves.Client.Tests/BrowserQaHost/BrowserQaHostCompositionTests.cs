using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Xml.Linq;
using Werewolves.Client.BrowserQaHost;
using Werewolves.Client.BrowserQaHost.Components;
using Werewolves.Client.Components;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;

namespace Werewolves.Client.Tests.BrowserQaHost;

public class BrowserQaHostCompositionTests
{
	[Fact]
	public async Task Services_RenderSharedRoutesWithBrowserSafeAdapters()
	{
		using var context = CreateBrowserQaHostContext();

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

		RenderedText(rendered).Should().Contain(ClientStrings.LobbyRoster_Title);
		FindButtonByText(rendered, ClientStrings.LobbyRoster_ContinueToRolesButton)
			.HasAttribute(Html.Attributes.Disabled)
			.Should()
			.BeFalse();
	}

	[Fact]
	public void BrowserQaRoot_WhenDashboardScenarioIsRequested_SeedsAndRendersDashboardFlow()
	{
		using var context = CreateBrowserQaHostContext(BrowserQaScenario.Dashboard);

		var rendered = context.Render<BrowserQaRoot>();

		var game = context.Services.GetRequiredService<GameClientManager>();
		game.HasActiveSession.Should().BeTrue();
		game.CurrentInstruction.Should().NotBeNull();
		context.Services.GetRequiredService<IScreenWakeLock>().KeepScreenOn.Should().BeTrue();

		FindButtonByText(rendered, ClientStrings.Dashboard_TabRoster).Should().NotBeNull();
		FindButtonByText(rendered, ClientStrings.Dashboard_TabAction).Should().NotBeNull();
		FindButtonByText(rendered, ClientStrings.Dashboard_TabStats).Should().NotBeNull();
	}

	[Fact]
	public void BrowserQaRoot_WhenVictoryScenarioIsRequested_SeedsVictoryFlow()
	{
		using var context = CreateBrowserQaHostContext(BrowserQaScenario.Victory);

		var rendered = context.Render<BrowserQaRoot>();

		var game = context.Services.GetRequiredService<GameClientManager>();
		game.CurrentInstruction.Should().BeOfType<FinishedGameConfirmationInstruction>();
		RenderedText(rendered).Should().Contain(ClientStrings.Victory_Title);

		FindButtonByText(rendered, ClientStrings.Victory_ReturnToLobbyButton).Click();

		game.HasActiveSession.Should().BeFalse();
		RenderedText(rendered).Should().Contain(ClientStrings.LobbyRoster_Title);
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

	private static BunitContext CreateBrowserQaHostContext(BrowserQaScenario scenario = BrowserQaScenario.Lobby)
	{
		var context = new BunitContext();
		BrowserQaHostCulture.UsePortuguese();
		context.Services.AddBrowserQaHostModeratorServices();
		context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/?qa={scenario}");
		return context;
	}

	private static string RenderedText<TComponent>(IRenderedComponent<TComponent> rendered)
		where TComponent : IComponent =>
		string.Join(" ", rendered.Nodes.Select(node => node.TextContent));

	private static IElement FindButtonByText<TComponent>(IRenderedComponent<TComponent> rendered, string text)
		where TComponent : IComponent =>
		rendered.FindAll(Html.Selectors.Button)
			.Single(button => button.TextContent.Contains(text, StringComparison.CurrentCulture));
}
