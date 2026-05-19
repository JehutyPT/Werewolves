using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using Werewolves.Client.BrowserQaHost;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Testing;
using Xunit;

namespace Werewolves.Client.BrowserQa.Tests;

public sealed class DashboardBrowserQaTests : PlaywrightTest, IClassFixture<BrowserQaHostFixture>
{
	private readonly BrowserQaHostFixture _host;

	public DashboardBrowserQaTests(BrowserQaHostFixture host)
	{
		_host = host;
		BrowserQaHostCulture.UsePortuguese();
	}

	[Fact]
	public async Task DashboardScenario_RendersSharedUiAndConstrainsFixedOverlaysToPhoneFrame()
	{
		await using var browser = await Playwright.Chromium.LaunchAsync();
		var page = await BrowserQaPage.OpenScenarioAsync(browser, _host.DashboardScenarioUri);

		var shell = page.GetByTestId(ModeratorUiTestIds.DashboardShell);
		var compactTabs = page.GetByTestId(ModeratorUiTestIds.DashboardCompactTabs);
		var statusBar = page.GetByTestId(ModeratorUiTestIds.DashboardStatusBar);
		var actionZone = page.GetByTestId(ModeratorUiTestIds.DashboardActionZone);

		await Expect(shell).ToBeVisibleAsync();
		await Expect(compactTabs).ToBeVisibleAsync();
		await Expect(statusBar).ToBeVisibleAsync();
		await Expect(actionZone).ToBeVisibleAsync();
		await Expect(compactTabs.GetByRole(AriaRole.Button, new() { Name = ClientStrings.Dashboard_TabAction }))
			.ToHaveAttributeAsync(BrowserQaAttributes.AriaSelected, BrowserQaAttributes.AriaTrue);

		var shellLayout = await BrowserQaPage.ReadLayoutAsync(shell);
		var tabsLayout = await BrowserQaPage.ReadLayoutAsync(compactTabs);
		var statusLayout = await BrowserQaPage.ReadLayoutAsync(statusBar);
		var actionLayout = await BrowserQaPage.ReadLayoutAsync(actionZone);
		var horizontalInset = await BrowserQaPage.ReadComputedPixelValueAsync(shell, BrowserQaCss.PageHorizontalInset);

		shellLayout.Width.Should().BeApproximately(BrowserQaPage.PhoneFrameWidth, BrowserQaPage.PhoneFramePrecision);
		shellLayout.Height.Should().BeApproximately(BrowserQaPage.PhoneFrameHeight, BrowserQaPage.PhoneFramePrecision);
		(await BrowserQaPage.ReadComputedStyleAsync(statusBar, BrowserQaCss.PositionProperty))
			.Should().Be(BrowserQaCss.FixedPositionValue);
		(await BrowserQaPage.ReadComputedStyleAsync(actionZone, BrowserQaCss.PositionProperty))
			.Should().Be(BrowserQaCss.FixedPositionValue);

		tabsLayout.X.Should().BeApproximately(shellLayout.X + horizontalInset, BrowserQaPage.LayoutPrecision);
		tabsLayout.Width.Should().BeApproximately(shellLayout.Width - (horizontalInset * 2), BrowserQaPage.LayoutPrecision);
		statusLayout.X.Should().BeApproximately(shellLayout.X + horizontalInset, BrowserQaPage.LayoutPrecision);
		statusLayout.Width.Should().BeApproximately(shellLayout.Width - (horizontalInset * 2), BrowserQaPage.LayoutPrecision);
		actionLayout.X.Should().BeApproximately(shellLayout.X, BrowserQaPage.LayoutPrecision);
		actionLayout.Width.Should().BeApproximately(shellLayout.Width, BrowserQaPage.LayoutPrecision);
		actionLayout.Bottom.Should().BeApproximately(shellLayout.Bottom, BrowserQaPage.LayoutPrecision);
	}

	[Fact]
	public async Task DashboardScenario_HoldButtonProgressUsesRenderedProductionTiming()
	{
		await using var browser = await Playwright.Chromium.LaunchAsync();
		var page = await BrowserQaPage.OpenScenarioAsync(browser, _host.DashboardScenarioUri);

		var actionZone = page.GetByTestId(ModeratorUiTestIds.DashboardActionZone);
		var holdZone = BrowserQaHoldProgress.HoldZoneIn(actionZone);
		var holdButton = holdZone.GetByRole(AriaRole.Button, new() { Name = ClientStrings.Common_HoldToConfirm });

		await Expect(actionZone).ToBeVisibleAsync();
		await Expect(holdButton).ToBeVisibleAsync();
		await Expect(holdButton).ToBeEnabledAsync();

		var evidence = await BrowserQaHoldProgress.ReadTransitionEvidenceWhileHoldingAsync(holdZone);
		var fillTransition = RenderedTransition.From(
			evidence.FillTransitionProperty,
			evidence.FillTransitionDuration,
			evidence.FillTransitionTimingFunction);
		var edgeTransition = RenderedTransition.From(
			evidence.EdgeTransitionProperty,
			evidence.EdgeTransitionDuration,
			evidence.EdgeTransitionTimingFunction);

		AssertProductionLinearProgressTransition(fillTransition, BrowserQaCss.WidthProperty);
		AssertProductionLinearProgressTransition(edgeTransition, BrowserQaCss.LeftProperty);
	}

	private static void AssertProductionLinearProgressTransition(
		RenderedTransition transition,
		string progressProperty)
	{
		transition.DurationMsFor(progressProperty)
			.Should()
			.Be(HoldButtonTimingContract.HoldDurationMs, "the rendered hold progress should track the production hold duration");
		transition.TimingFunctionFor(progressProperty)
			.Should()
			.Be(BrowserQaCss.LinearTimingFunction, "the rendered progress should advance linearly during the hold");
	}
}
