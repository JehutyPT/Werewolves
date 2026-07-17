using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using Werewolves.Client.BrowserQaHost;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Xunit;

namespace Werewolves.Client.BrowserQa.Tests;

public sealed class RoleSelectionEvaluationBrowserQaTests : PlaywrightTest, IClassFixture<BrowserQaHostFixture>
{
	private const double FixedActionGap = 8;
	private readonly BrowserQaHostFixture _host;

	public RoleSelectionEvaluationBrowserQaTests(BrowserQaHostFixture host)
	{
		_host = host;
		BrowserQaHostCulture.UsePortuguese();
	}

	[Theory]
	[InlineData(BrowserQaPage.PhoneFrameWidth, BrowserQaPage.PhoneFrameHeight)]
	[InlineData(BrowserQaPage.WideViewportWidth, BrowserQaPage.WideViewportHeight)]
	public async Task ProbabilityScenario_ShowsNoInsightsAndStartRemainsUsable(
		int viewportWidth,
		int viewportHeight)
	{
		await using var browser = await Playwright.Chromium.LaunchAsync();
		var page = await browser.NewPageAsync();
		await page.SetViewportSizeAsync(viewportWidth, viewportHeight);
		await page.GotoAsync(_host.ProbabilityScenarioUri.ToString(), new()
		{
			WaitUntil = BrowserQaPage.ScenarioWaitUntil
		});

		await page.GetByRole(AriaRole.Button, new() { Name = ClientStrings.LobbyRoster_ContinueToRolesButton })
			.ClickAsync();

		await Expect(page.GetByTestId("browser-qa-evaluation-state")).ToHaveAttributeAsync(
			"data-state",
			nameof(LobbyEvaluationStateKind.ScreeningPassed));

		var back = page.GetByRole(AriaRole.Button, new() { Name = ClientStrings.Common_Back });
		var start = page.GetByTestId(ModeratorUiTestIds.RoleSelectionStartGame);
		await Expect(start).ToBeVisibleAsync();
		await Expect(start).ToBeEnabledAsync();
		await Expect(page.GetByTestId(ModeratorUiTestIds.LobbyEvaluationPanel)).ToHaveCountAsync(0);
		await Expect(page.GetByTestId(ModeratorUiTestIds.LobbyEvaluationDisclosure)).ToHaveCountAsync(0);
		await Expect(page.GetByTestId(ModeratorUiTestIds.LobbyEvaluationRetry)).ToHaveCountAsync(0);
		(await page.Locator("main").InnerTextAsync()).Should().NotContain("70%");
		(await page.Locator("main").InnerTextAsync()).Should().NotContain("30%");

		await back.FocusAsync();
		await page.Keyboard.PressAsync("Tab");
		(await IsFocusedAsync(start)).Should().BeTrue();
		await AssertVisibleKeyboardFocusAsync(start);
		await page.Keyboard.PressAsync("Shift+Tab");
		(await IsFocusedAsync(back)).Should().BeTrue();
		await AssertVisibleKeyboardFocusAsync(back);

		await start.ClickAsync();

		await Expect(page.GetByTestId(ModeratorUiTestIds.DashboardShell)).ToBeVisibleAsync();
		await Expect(page.GetByTestId(ModeratorUiTestIds.RoleSelectionStartGame)).ToHaveCountAsync(0);
	}

	[Theory]
	[InlineData(BrowserQaPage.PhoneFrameWidth, BrowserQaPage.PhoneFrameHeight)]
	[InlineData(BrowserQaPage.WideViewportWidth, BrowserQaPage.WideViewportHeight)]
	public async Task DegenerateScenario_ShowsUnobscuredWarningAndBlocksStart(
		int viewportWidth,
		int viewportHeight)
	{
		await using var browser = await Playwright.Chromium.LaunchAsync();
		var page = await browser.NewPageAsync();
		await page.SetViewportSizeAsync(viewportWidth, viewportHeight);
		await page.GotoAsync(_host.DegenerateScenarioUri.ToString(), new()
		{
			WaitUntil = BrowserQaPage.ScenarioWaitUntil
		});

		await page.GetByRole(AriaRole.Button, new() { Name = ClientStrings.LobbyRoster_ContinueToRolesButton })
			.ClickAsync();

		var frame = page.Locator(BrowserQaCss.PhoneFrameSelector);
		var shell = page.Locator(".ww-app-shell");
		var panel = page.GetByTestId(ModeratorUiTestIds.LobbyEvaluationPanel);
		var summary = page.GetByTestId(ModeratorUiTestIds.LobbyEvaluationSummary);
		var actionBar = page.GetByTestId(ModeratorUiTestIds.RoleSelectionActionBar);
		var start = page.GetByTestId(ModeratorUiTestIds.RoleSelectionStartGame);
		await Expect(panel).ToBeVisibleAsync();
		await Expect(summary).ToContainTextAsync(ClientStrings.LobbyEvaluation_Degenerate);
		await Expect(start).ToBeEnabledAsync();

		await shell.EvaluateAsync("element => { element.scrollTop = element.scrollHeight; }");
		var frameLayout = await BrowserQaPage.ReadLayoutAsync(frame);
		var panelLayout = await BrowserQaPage.ReadLayoutAsync(panel);
		var actionLayout = await BrowserQaPage.ReadLayoutAsync(actionBar);
		var overflow = await ReadOverflowAsync(shell);
		overflow.ScrollWidth.Should().BeLessThanOrEqualTo(overflow.ClientWidth + 1);
		panelLayout.X.Should().BeGreaterThanOrEqualTo(frameLayout.X);
		panelLayout.Right.Should().BeLessThanOrEqualTo(frameLayout.Right + BrowserQaPage.LayoutPrecision);
		panelLayout.Bottom.Should().BeLessThan(actionLayout.Y - FixedActionGap);

		await start.ClickAsync();

		await Expect(page.GetByTestId(ModeratorUiTestIds.LobbyEvaluationStatus))
			.ToContainTextAsync(ClientStrings.LobbyEvaluation_DegenerateBlock);
		await Expect(page.GetByTestId(ModeratorUiTestIds.DashboardShell)).ToHaveCountAsync(0);
		await Expect(start).ToBeVisibleAsync();

		var status = page.GetByTestId(ModeratorUiTestIds.LobbyEvaluationStatus);
		var panelLayoutAfterAttempt = await BrowserQaPage.ReadLayoutAsync(panel);
		var actionLayoutAfterAttempt = await BrowserQaPage.ReadLayoutAsync(actionBar);
		var statusLayout = await BrowserQaPage.ReadLayoutAsync(status);
		panelLayoutAfterAttempt.Bottom.Should().BeLessThan(
			actionLayoutAfterAttempt.Y - FixedActionGap);
		statusLayout.X.Should().BeGreaterThanOrEqualTo(actionLayoutAfterAttempt.X);
		statusLayout.Right.Should().BeLessThanOrEqualTo(
			actionLayoutAfterAttempt.Right + BrowserQaPage.LayoutPrecision);
		statusLayout.Bottom.Should().BeLessThanOrEqualTo(
			actionLayoutAfterAttempt.Bottom + BrowserQaPage.LayoutPrecision);
		actionLayoutAfterAttempt.Bottom.Should().BeLessThanOrEqualTo(
			frameLayout.Bottom + BrowserQaPage.LayoutPrecision);
	}

	private static Task<bool> IsFocusedAsync(ILocator locator) =>
		locator.EvaluateAsync<bool>("element => document.activeElement === element");

	private static async Task AssertVisibleKeyboardFocusAsync(ILocator locator)
	{
		var outlineStyle = await BrowserQaPage.ReadComputedStyleAsync(
			locator,
			BrowserQaCss.OutlineStyleProperty);
		var outlineWidth = await BrowserQaPage.ReadComputedPixelValueAsync(
			locator,
			BrowserQaCss.OutlineWidthProperty);
		outlineStyle.Should().NotBe("none");
		outlineWidth.Should().BeGreaterThanOrEqualTo(2);
	}

	private static Task<OverflowMetrics> ReadOverflowAsync(ILocator locator) =>
		locator.EvaluateAsync<OverflowMetrics>(
			"element => ({ clientWidth: element.clientWidth, scrollWidth: element.scrollWidth })");

	private sealed class OverflowMetrics
	{
		public double ClientWidth { get; set; }
		public double ScrollWidth { get; set; }
	}
}
