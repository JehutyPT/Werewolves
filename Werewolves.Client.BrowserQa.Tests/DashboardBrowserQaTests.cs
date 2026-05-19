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

		shellLayout.Width.Should().BeApproximately(360, precision: 0.5);
		shellLayout.Height.Should().BeApproximately(800, precision: 0.5);
		(await BrowserQaPage.ReadComputedStyleAsync(statusBar, BrowserQaCss.PositionProperty))
			.Should().Be(BrowserQaCss.FixedPositionValue);
		(await BrowserQaPage.ReadComputedStyleAsync(actionZone, BrowserQaCss.PositionProperty))
			.Should().Be(BrowserQaCss.FixedPositionValue);

		tabsLayout.X.Should().BeApproximately(shellLayout.X + horizontalInset, precision: 0.75);
		tabsLayout.Width.Should().BeApproximately(shellLayout.Width - (horizontalInset * 2), precision: 0.75);
		statusLayout.X.Should().BeApproximately(shellLayout.X + horizontalInset, precision: 0.75);
		statusLayout.Width.Should().BeApproximately(shellLayout.Width - (horizontalInset * 2), precision: 0.75);
		actionLayout.X.Should().BeApproximately(shellLayout.X, precision: 0.75);
		actionLayout.Width.Should().BeApproximately(shellLayout.Width, precision: 0.75);
		actionLayout.Bottom.Should().BeApproximately(shellLayout.Bottom, precision: 0.75);
	}
}
