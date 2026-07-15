using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using Werewolves.Client.BrowserQaHost;
using Werewolves.Client.Resources;
using Werewolves.Client.Testing;
using Xunit;

namespace Werewolves.Client.BrowserQa.Tests;

public sealed class RoleSelectionEvaluationBrowserQaTests : PlaywrightTest, IClassFixture<BrowserQaHostFixture>
{
	private const double FixedActionGap = 8;
	private const double MinimumPointerTarget = 44;
	private readonly BrowserQaHostFixture _host;

	public RoleSelectionEvaluationBrowserQaTests(BrowserQaHostFixture host)
	{
		_host = host;
		BrowserQaHostCulture.UsePortuguese();
	}

	[Theory]
	[InlineData(BrowserQaPage.PhoneFrameWidth, BrowserQaPage.PhoneFrameHeight)]
	[InlineData(BrowserQaPage.WideViewportWidth, BrowserQaPage.WideViewportHeight)]
	public async Task ProbabilityScenario_IsReachableFocusRetainingAndUnobscured(
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

		var frame = page.Locator(BrowserQaCss.PhoneFrameSelector);
		var shell = page.Locator(".ww-app-shell");
		var panel = page.GetByTestId(ModeratorUiTestIds.LobbyEvaluationPanel);
		var disclosure = page.GetByTestId(ModeratorUiTestIds.LobbyEvaluationDisclosure);
		var actionBar = page.GetByTestId(ModeratorUiTestIds.RoleSelectionActionBar);
		await Expect(panel).ToBeVisibleAsync();
		await Expect(disclosure).ToBeVisibleAsync();
		await Expect(disclosure).ToHaveAttributeAsync(
			BrowserQaAttributes.AriaExpanded,
			BrowserQaAttributes.AriaFalse);

		var frameLayout = await BrowserQaPage.ReadLayoutAsync(frame);
		var panelLayout = await BrowserQaPage.ReadLayoutAsync(panel);
		var disclosureLayout = await BrowserQaPage.ReadLayoutAsync(disclosure);
		var collapsedOverflow = await ReadOverflowAsync(shell);
		collapsedOverflow.ScrollWidth.Should().BeLessThanOrEqualTo(collapsedOverflow.ClientWidth + 1);
		panelLayout.X.Should().BeGreaterThanOrEqualTo(frameLayout.X);
		panelLayout.Right.Should().BeLessThanOrEqualTo(frameLayout.Right + BrowserQaPage.LayoutPrecision);
		disclosureLayout.Width.Should().BeGreaterThanOrEqualTo(MinimumPointerTarget);
		disclosureLayout.Height.Should().BeGreaterThanOrEqualTo(MinimumPointerTarget);

		await disclosure.FocusAsync();
		await disclosure.PressAsync("Tab");
		await page.Keyboard.PressAsync("Shift+Tab");
		(await IsFocusedAsync(disclosure)).Should().BeTrue();
		var outlineStyle = await BrowserQaPage.ReadComputedStyleAsync(
			disclosure,
			BrowserQaCss.OutlineStyleProperty);
		var outlineWidth = await BrowserQaPage.ReadComputedPixelValueAsync(
			disclosure,
			BrowserQaCss.OutlineWidthProperty);
		var outlineOffset = await BrowserQaPage.ReadComputedPixelValueAsync(
			disclosure,
			BrowserQaCss.OutlineOffsetProperty);
		outlineStyle.Should().NotBe("none");
		outlineWidth.Should().BeGreaterThanOrEqualTo(2);
		(disclosureLayout.X - outlineWidth - outlineOffset)
			.Should().BeGreaterThanOrEqualTo(panelLayout.X);
		(disclosureLayout.Right + outlineWidth + outlineOffset)
			.Should().BeLessThanOrEqualTo(panelLayout.Right);

		await disclosure.PressAsync("Enter");

		await Expect(disclosure).ToHaveAttributeAsync(
			BrowserQaAttributes.AriaExpanded,
			BrowserQaAttributes.AriaTrue);
		(await IsFocusedAsync(disclosure)).Should().BeTrue();
		var detail = page.GetByTestId(ModeratorUiTestIds.LobbyEvaluationDetail);
		await Expect(detail).ToBeVisibleAsync();
		var expandedOverflow = await ReadOverflowAsync(shell);
		expandedOverflow.ScrollWidth.Should().BeLessThanOrEqualTo(expandedOverflow.ClientWidth + 1);

		await shell.EvaluateAsync("element => { element.scrollTop = element.scrollHeight; }");
		var lastTurnEntry = page.GetByTestId(ModeratorUiTestIds.LobbyEvaluationTurnEntry).Last;
		var caveat = detail.Locator(".ww-lobby-evaluation__caveat");
		await Expect(lastTurnEntry).ToBeVisibleAsync();
		await Expect(caveat).ToBeVisibleAsync();
		var actionLayout = await BrowserQaPage.ReadLayoutAsync(actionBar);
		var finalTurnLayout = await BrowserQaPage.ReadLayoutAsync(lastTurnEntry);
		var caveatLayout = await BrowserQaPage.ReadLayoutAsync(caveat);
		finalTurnLayout.Bottom.Should().BeLessThan(actionLayout.Y - FixedActionGap);
		caveatLayout.Bottom.Should().BeLessThan(actionLayout.Y - FixedActionGap);

		await disclosure.FocusAsync();
		await disclosure.PressAsync("Enter");
		await Expect(disclosure).ToHaveAttributeAsync(
			BrowserQaAttributes.AriaExpanded,
			BrowserQaAttributes.AriaFalse);
		(await IsFocusedAsync(disclosure)).Should().BeTrue();
	}

	private static Task<bool> IsFocusedAsync(ILocator locator) =>
		locator.EvaluateAsync<bool>("element => document.activeElement === element");

	private static Task<OverflowMetrics> ReadOverflowAsync(ILocator locator) =>
		locator.EvaluateAsync<OverflowMetrics>(
			"element => ({ clientWidth: element.clientWidth, scrollWidth: element.scrollWidth })");

	private sealed class OverflowMetrics
	{
		public double ClientWidth { get; set; }
		public double ScrollWidth { get; set; }
	}
}
