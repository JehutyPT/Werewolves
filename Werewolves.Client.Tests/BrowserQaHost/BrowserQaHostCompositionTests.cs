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
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;

namespace Werewolves.Client.Tests.BrowserQaHost;

public class BrowserQaHostCompositionTests
{
	[Fact]
	public async Task BrowserComposition_ProbabilityScenario_UsesDeterministicScreeningEvaluatorWithoutLocalCache()
	{
		using var context = CreateBrowserQaHostContext(BrowserQaScenario.Probability);
		var local = context.Services.GetRequiredService<ILocalTerminalLobbyCacheStore>();
		var localBytes = await local.ReadAsync();

		local.Should().BeOfType<BrowserQaScenarioTerminalLobbyCacheStore>();
		localBytes.Should().BeNull();
		context.Services.GetRequiredService<ILobbyTerminalEvaluator>()
			.Should().BeOfType<BrowserQaScreeningPassedLobbyTerminalEvaluator>();

		var coordinator = context.Services.GetRequiredService<LobbyEvaluationCoordinator>();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.ScreeningPassed);

		coordinator.Capability.Should().Be(SimulatorCapability.SafetyScreening);
		coordinator.Depth.Should().Be(LobbyEvaluationDepth.DegenerateScreeningOnly);
		coordinator.State.Identity.Should().Be(new SimulationCompatibilityIdentity(
			context.Services.GetRequiredService<LobbySetupState>()
				.CreateSimulationScenario()
				.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity));
		coordinator.State.Probability.Should().BeNull();
	}

	[Fact]
	public async Task BrowserComposition_DegenerateScenario_UsesExactCurrentSafetyScreeningLocalCache()
	{
		using var context = CreateBrowserQaHostContext(BrowserQaScenario.Degenerate);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var expectedScenario = lobby.CreateSimulationScenario();
		var expectedIdentity = new SimulationCompatibilityIdentity(
			expectedScenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var local = context.Services.GetRequiredService<ILocalTerminalLobbyCacheStore>();
		var localBytes = await local.ReadAsync();

		local.Should().BeOfType<BrowserQaScenarioTerminalLobbyCacheStore>();
		localBytes.Should().NotBeNull();
		var read = TerminalLobbyCache.ReadDocument(
			localBytes!.Value.Span,
			SimulatorCapabilityRegistry.Production);
		read.IsUsable.Should().BeTrue();
		TerminalLobbyCache.TryGet(
			read.Document!,
			expectedScenario,
			SimulatorCapability.SafetyScreening,
			out var record).Should().BeTrue();
		record.Should().BeOfType<DegenerateTerminalCacheRecord>();
		record!.CompatibilityIdentity.Should().Be(expectedIdentity);

		var coordinator = context.Services.GetRequiredService<LobbyEvaluationCoordinator>();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.Degenerate);

		coordinator.State.Identity.Should().Be(expectedIdentity);
	}

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
		context.Services.GetRequiredService<IRecentSetupStore>()
			.Should().BeOfType<InMemoryRecentSetupStore>();
		context.Services.GetRequiredService<LobbyEvaluationCoordinator>().Depth
			.Should().Be(LobbyEvaluationDepth.DegenerateScreeningOnly);
		context.Services.GetRequiredService<LobbyEvaluationCoordinator>().Capability
			.Should().Be(SimulatorCapability.SafetyScreening);
		context.Services.GetRequiredService<LobbyEvaluationSettings>().Depth
			.Should().Be(LobbyEvaluationDepth.DegenerateScreeningOnly);
		context.Services.GetRequiredService<LobbyEvaluationSettings>().Capability
			.Should().Be(SimulatorCapability.SafetyScreening);
		context.Services.GetRequiredService<ILocalTerminalLobbyCacheStore>()
			.Should().BeOfType<BrowserQaScenarioTerminalLobbyCacheStore>();
		context.Services.GetRequiredService<ILobbyTerminalEvaluator>()
			.Should().BeOfType<BrowserQaScreeningPassedLobbyTerminalEvaluator>();

		var audio = context.Services.GetRequiredService<IInstructionAudioPlayback>();
		await audio.SetMutedAsync(true, instruction: null);
		audio.IsMuted.Should().BeTrue();

		var rendered = context.Render<Routes>();

		rendered.Find($"[data-testid='{ModeratorUiTestIds.LandingNewGameButton}']").Click();
		RenderedText(rendered).Should().Contain(ClientStrings.LobbyRoster_Title);
		FindButtonByText(rendered, ClientStrings.LobbyRoster_ContinueToRolesButton)
			.HasAttribute(Html.Attributes.Disabled)
			.Should()
			.BeFalse();
	}

	[Fact]
	public void BrowserQaRoot_WhenProbabilityScenarioIsRequested_HidesInsightsAndAllowsStart()
	{
		using var context = CreateBrowserQaHostContext(BrowserQaScenario.Probability);
		var rendered = context.Render<BrowserQaRoot>();

		rendered.Find($"[data-testid='{ModeratorUiTestIds.LandingNewGameButton}']").Click();
		FindButtonByText(rendered, ClientStrings.LobbyRoster_ContinueToRolesButton).Click();

		rendered.WaitForAssertion(() =>
		{
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.ScreeningPassed);
			rendered.Find("[data-testid='browser-qa-evaluation-state']")
				.GetAttribute("data-state").Should().Be(nameof(LobbyEvaluationStateKind.ScreeningPassed));
			rendered.FindAll($"[data-testid='{ModeratorUiTestIds.LobbyEvaluationPanel}']")
				.Should().BeEmpty();
			rendered.FindAll($"[data-testid='{ModeratorUiTestIds.LobbyEvaluationDisclosure}']")
				.Should().BeEmpty();
			rendered.FindAll($"[data-testid='{ModeratorUiTestIds.LobbyEvaluationRetry}']")
				.Should().BeEmpty();
		});

		rendered.Find($"[data-testid='{ModeratorUiTestIds.RoleSelectionStartGame}']").Click();

		rendered.WaitForAssertion(() =>
		{
			context.Services.GetRequiredService<GameClientManager>().HasActiveSession.Should().BeTrue();
			rendered.FindAll($"[data-testid='{ModeratorUiTestIds.DashboardShell}']")
				.Should().ContainSingle();
			rendered.FindAll($"[data-testid='{ModeratorUiTestIds.RoleSelectionStartGame}']")
				.Should().BeEmpty();
		});
	}

	[Fact]
	public void BrowserQaRoot_WhenDegenerateScenarioIsRequested_ShowsWarningAndBlocksStart()
	{
		using var context = CreateBrowserQaHostContext(BrowserQaScenario.Degenerate);
		var rendered = context.Render<BrowserQaRoot>();

		// Synchronize the fixture's background evaluation before driving rendered navigation.
		rendered.WaitForAssertion(() =>
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.Degenerate));
		rendered.Find($"[data-testid='{ModeratorUiTestIds.LandingNewGameButton}']").Click();
		FindButtonByText(rendered, ClientStrings.LobbyRoster_ContinueToRolesButton).Click();

		rendered.WaitForAssertion(() =>
		{
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.Degenerate);
			rendered.Find($"[data-testid='{ModeratorUiTestIds.LobbyEvaluationSummary}']")
				.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_Degenerate);
		});

		rendered.Find($"[data-testid='{ModeratorUiTestIds.RoleSelectionStartGame}']").Click();

		rendered.WaitForAssertion(() =>
		{
			context.Services.GetRequiredService<GameClientManager>().HasActiveSession.Should().BeFalse();
			rendered.Find($"[data-testid='{ModeratorUiTestIds.LobbyEvaluationStatus}']")
				.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_DegenerateBlock);
		});
	}

	[Fact]
	public void BrowserQaRoot_WhenDashboardScenarioIsRequested_SeedsAndRendersDashboardFlow()
	{
		using var context = CreateBrowserQaHostContext(BrowserQaScenario.Dashboard);

		var rendered = context.Render<BrowserQaRoot>();

		var game = context.Services.GetRequiredService<GameClientManager>();
		game.HasActiveSession.Should().BeTrue();
		game.CurrentInstruction.Should().NotBeNull();
		context.Services.GetRequiredService<IScreenWakeLock>().KeepScreenOn.Should().BeFalse();

		rendered.Find($"[data-testid='{ModeratorUiTestIds.LandingContinueButton}']").Click();

		context.Services.GetRequiredService<IScreenWakeLock>().KeepScreenOn.Should().BeTrue();

		FindButtonByText(rendered, ClientStrings.Dashboard_TabRoster).Should().NotBeNull();
		FindButtonByText(rendered, ClientStrings.Dashboard_TabAction).Should().NotBeNull();
		FindButtonByText(rendered, ClientStrings.Dashboard_TabStats).Should().NotBeNull();
	}

	[Fact]
	public void Routes_WhenTerminalSaveIsRecovered_RendersTypedVictoryAndDismissesLocally()
	{
		using var seedContext = CreateBrowserQaHostContext(BrowserQaScenario.Victory);
		seedContext.Render<BrowserQaRoot>();
		var saveStore = seedContext.Services
			.GetRequiredService<IGameSessionSaveStore>();
		var seededGame = seedContext.Services
			.GetRequiredService<GameClientManager>();
		var seededFinished = seededGame.CurrentInstruction.Should()
			.BeOfType<FinishedGameConfirmationInstruction>().Subject;
		saveStore.Load().Should().NotBeNullOrWhiteSpace();

		var recoveredService = new GameService();
		var recoveredGame = new GameClientManager(
			recoveredService,
			saveStore: saveStore);
		recoveredGame.ActiveGameId.Should().HaveValue();
		var recoveredGameId = recoveredGame.ActiveGameId!.Value;

		using var context = new BunitContext();
		BrowserQaHostCulture.UsePortuguese();
		context.Services.AddBrowserQaHostModeratorServices();
		context.Services.AddSingleton(recoveredGame);
		var rendered = context.Render<Routes>();
		rendered.Find($"[data-testid='{ModeratorUiTestIds.LandingContinueButton}']").Click();

		var victoryPage = rendered.FindComponent<VictoryPage>();
		victoryPage.Instance.GameResult.Should().Be(seededFinished.GameResult);
		victoryPage.Instance.VictoryCheckWindow.Should()
			.Be(seededFinished.VictoryCheckWindow);
		RenderedText(rendered).Should().Contain(ClientStrings.Victory_Title);

		FindButtonByText(rendered, ClientStrings.Victory_ReturnToLobbyButton).Click();

		rendered.WaitForAssertion(() =>
		{
			recoveredGame.HasActiveSession.Should().BeFalse();
			recoveredService.GetGameStateView(recoveredGameId).Should().BeNull();
			saveStore.Load().Should().BeNull();
			rendered.FindComponent<LobbyRosterPage>().Should().NotBeNull();
			RenderedText(rendered).Should().Contain(ClientStrings.LobbyRoster_Title);
		});
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
		scope.ServiceProvider.GetRequiredService<LobbyEvaluationCoordinator>().Should().NotBeNull();
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

	private static Task WaitForStateAsync(
		LobbyEvaluationCoordinator coordinator,
		LobbyEvaluationStateKind expected)
	{
		if (coordinator.State.Kind == expected)
		{
			return Task.CompletedTask;
		}
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		EventHandler? changed = null;
		changed = (_, _) =>
		{
			if (coordinator.State.Kind != expected)
			{
				return;
			}
			coordinator.StateChanged -= changed;
			completion.TrySetResult();
		};
		coordinator.StateChanged += changed;
		return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

}
