using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using Werewolves.Client.BrowserQaHost;
using Werewolves.Client.Resources;
using Werewolves.Client.Testing;
using Xunit;

namespace Werewolves.Client.BrowserQa.Tests;

public sealed class DashboardBrowserQaTests : PlaywrightTest, IClassFixture<BrowserQaHostFixture>
{
	private const double LayoutPrecision = 0.75;
	private const double OverlayGap = 8;
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
		var page = await browser.NewPageAsync();

		await BrowserQaPage.SetWideViewportAsync(page);
		await page.GotoAsync(_host.DashboardScenarioUri.ToString(), new()
		{
			WaitUntil = WaitUntilState.NetworkIdle
		});

		var phoneFrame = page.Locator(BrowserQaCss.PhoneFrameSelector);
		var shell = page.GetByTestId(ModeratorUiTestIds.DashboardShell);
		var compactTabs = page.GetByTestId(ModeratorUiTestIds.DashboardCompactTabs);
		var statusBar = page.GetByTestId(ModeratorUiTestIds.DashboardStatusBar);
		var actionZone = page.GetByTestId(ModeratorUiTestIds.DashboardActionZone);

		await Expect(phoneFrame).ToBeVisibleAsync();
		await Expect(shell).ToBeVisibleAsync();
		await Expect(compactTabs).ToBeVisibleAsync();
		await Expect(statusBar).ToBeVisibleAsync();
		await Expect(actionZone).ToBeVisibleAsync();
		await Expect(compactTabs.GetByRole(AriaRole.Button, new() { Name = ClientStrings.Dashboard_TabAction }))
			.ToHaveAttributeAsync(BrowserQaAttributes.AriaSelected, BrowserQaAttributes.AriaTrue);

		var phoneFrameLayout = await BrowserQaPage.ReadLayoutAsync(phoneFrame);
		var shellLayout = await BrowserQaPage.ReadLayoutAsync(shell);
		var tabsLayout = await BrowserQaPage.ReadLayoutAsync(compactTabs);
		var statusLayout = await BrowserQaPage.ReadLayoutAsync(statusBar);
		var actionLayout = await BrowserQaPage.ReadLayoutAsync(actionZone);
		var horizontalInset = await BrowserQaPage.ReadComputedPixelValueAsync(shell, BrowserQaCss.PageHorizontalInset);

		phoneFrameLayout.Width.Should().BeApproximately(360, precision: 0.5);
		phoneFrameLayout.Height.Should().BeApproximately(800, precision: 0.5);
		AssertPinnedLayout(phoneFrameLayout, shellLayout);
		shellLayout.Width.Should().BeApproximately(360, precision: 0.5);
		shellLayout.Height.Should().BeApproximately(800, precision: 0.5);
		(await BrowserQaPage.ReadComputedStyleAsync(compactTabs, BrowserQaCss.PositionProperty))
			.Should().Be(BrowserQaCss.FixedPositionValue);
		(await BrowserQaPage.ReadComputedStyleAsync(statusBar, BrowserQaCss.PositionProperty))
			.Should().Be(BrowserQaCss.FixedPositionValue);
		(await BrowserQaPage.ReadComputedStyleAsync(actionZone, BrowserQaCss.PositionProperty))
			.Should().Be(BrowserQaCss.FixedPositionValue);

		tabsLayout.X.Should().BeApproximately(shellLayout.X + horizontalInset, precision: LayoutPrecision);
		tabsLayout.Y.Should().BeApproximately(shellLayout.Y, precision: LayoutPrecision);
		tabsLayout.Width.Should().BeApproximately(shellLayout.Width - (horizontalInset * 2), precision: LayoutPrecision);
		tabsLayout.Right.Should().BeApproximately(shellLayout.Right - horizontalInset, precision: LayoutPrecision);
		statusLayout.X.Should().BeApproximately(shellLayout.X + horizontalInset, precision: LayoutPrecision);
		statusLayout.Y.Should().BeApproximately(tabsLayout.Bottom, precision: LayoutPrecision);
		statusLayout.Width.Should().BeApproximately(shellLayout.Width - (horizontalInset * 2), precision: LayoutPrecision);
		statusLayout.Right.Should().BeApproximately(shellLayout.Right - horizontalInset, precision: LayoutPrecision);
		actionLayout.X.Should().BeApproximately(shellLayout.X, precision: LayoutPrecision);
		actionLayout.Width.Should().BeApproximately(shellLayout.Width, precision: LayoutPrecision);
		actionLayout.Right.Should().BeApproximately(shellLayout.Right, precision: LayoutPrecision);
		actionLayout.Bottom.Should().BeApproximately(shellLayout.Bottom, precision: LayoutPrecision);
	}

	[Fact]
	public async Task DashboardScenario_KeepsFixedOverlaysStableWhileDashboardContentScrolls()
	{
		await using var browser = await Playwright.Chromium.LaunchAsync();
		var page = await browser.NewPageAsync();

		await BrowserQaPage.SetWideViewportAsync(page);
		await page.GotoAsync(_host.DashboardScenarioUri.ToString(), new()
		{
			WaitUntil = WaitUntilState.NetworkIdle
		});

		var shell = page.GetByTestId(ModeratorUiTestIds.DashboardShell);
		var compactTabs = page.GetByTestId(ModeratorUiTestIds.DashboardCompactTabs);
		var statusBar = page.GetByTestId(ModeratorUiTestIds.DashboardStatusBar);
		var actionZone = page.GetByTestId(ModeratorUiTestIds.DashboardActionZone);
		var actionPanel = page.Locator(BrowserQaCss.DashboardActionPanelSelector);
		var publicInstruction = page.GetByRole(AriaRole.Button, new() { Name = ClientStrings.Dashboard_AnnounceLabel });
		var privateInstruction = page.GetByRole(AriaRole.Button, new() { Name = ClientStrings.Dashboard_ModeratorLabel });

		await Expect(publicInstruction).ToBeVisibleAsync();
		await Expect(privateInstruction).ToBeVisibleAsync();

		var tabsBefore = await BrowserQaPage.ReadLayoutAsync(compactTabs);
		var statusBefore = await BrowserQaPage.ReadLayoutAsync(statusBar);
		var actionBefore = await BrowserQaPage.ReadLayoutAsync(actionZone);
		var instructionBefore = await BrowserQaPage.ReadLayoutAsync(publicInstruction);
		var privateInstructionBefore = await BrowserQaPage.ReadLayoutAsync(privateInstruction);
		var shellPaddingTop = await BrowserQaPage.ReadComputedPixelValueAsync(shell, BrowserQaCss.PaddingTopProperty);
		var shellPaddingBottom = await BrowserQaPage.ReadComputedPixelValueAsync(shell, BrowserQaCss.PaddingBottomProperty);
		var actionPanelPaddingBottom = await BrowserQaPage.ReadComputedPixelValueAsync(
			actionPanel,
			BrowserQaCss.PaddingBottomProperty);

		shellPaddingTop.Should().BeGreaterThan(tabsBefore.Height + statusBefore.Height + OverlayGap);
		shellPaddingBottom.Should().BeGreaterThan(actionBefore.Height + OverlayGap);
		actionPanelPaddingBottom.Should().BeGreaterThan(actionBefore.Height + OverlayGap);
		instructionBefore.Y.Should().BeGreaterThan(statusBefore.Bottom + OverlayGap);
		privateInstructionBefore.Bottom.Should().BeLessThan(actionBefore.Y - OverlayGap);

		await BrowserQaPage.SetScrollTopAsync(shell, 160);
		var scrollTop = await BrowserQaPage.ReadScrollTopAsync(shell);

		scrollTop.Should().BeGreaterThan(0);
		AssertPinnedLayout(tabsBefore, await BrowserQaPage.ReadLayoutAsync(compactTabs));
		AssertPinnedLayout(statusBefore, await BrowserQaPage.ReadLayoutAsync(statusBar));
		AssertPinnedLayout(actionBefore, await BrowserQaPage.ReadLayoutAsync(actionZone));

		var instructionAfter = await BrowserQaPage.ReadLayoutAsync(publicInstruction);
		instructionAfter.Y.Should().BeApproximately(instructionBefore.Y - scrollTop, precision: 1.5);
	}

	private static void AssertPinnedLayout(ElementLayout before, ElementLayout after)
	{
		after.X.Should().BeApproximately(before.X, precision: LayoutPrecision);
		after.Y.Should().BeApproximately(before.Y, precision: LayoutPrecision);
		after.Width.Should().BeApproximately(before.Width, precision: LayoutPrecision);
		after.Height.Should().BeApproximately(before.Height, precision: LayoutPrecision);
	}
}
