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
		var page = await BrowserQaPage.OpenScenarioAsync(browser, _host.DashboardScenarioUri);

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

		phoneFrameLayout.Width.Should().BeApproximately(BrowserQaPage.PhoneFrameWidth, BrowserQaPage.PhoneFramePrecision);
		phoneFrameLayout.Height.Should().BeApproximately(BrowserQaPage.PhoneFrameHeight, BrowserQaPage.PhoneFramePrecision);
		AssertPinnedLayout(phoneFrameLayout, shellLayout);
		shellLayout.Width.Should().BeApproximately(BrowserQaPage.PhoneFrameWidth, BrowserQaPage.PhoneFramePrecision);
		shellLayout.Height.Should().BeApproximately(BrowserQaPage.PhoneFrameHeight, BrowserQaPage.PhoneFramePrecision);
		(await BrowserQaPage.ReadComputedStyleAsync(compactTabs, BrowserQaCss.PositionProperty))
			.Should().Be(BrowserQaCss.FixedPositionValue);
		(await BrowserQaPage.ReadComputedStyleAsync(statusBar, BrowserQaCss.PositionProperty))
			.Should().Be(BrowserQaCss.FixedPositionValue);
		(await BrowserQaPage.ReadComputedStyleAsync(actionZone, BrowserQaCss.PositionProperty))
			.Should().Be(BrowserQaCss.FixedPositionValue);

		tabsLayout.X.Should().BeApproximately(shellLayout.X + horizontalInset, BrowserQaPage.LayoutPrecision);
		tabsLayout.Y.Should().BeApproximately(shellLayout.Y, BrowserQaPage.LayoutPrecision);
		tabsLayout.Width.Should().BeApproximately(shellLayout.Width - (horizontalInset * 2), BrowserQaPage.LayoutPrecision);
		tabsLayout.Right.Should().BeApproximately(shellLayout.Right - horizontalInset, BrowserQaPage.LayoutPrecision);
		statusLayout.X.Should().BeApproximately(shellLayout.X + horizontalInset, BrowserQaPage.LayoutPrecision);
		statusLayout.Y.Should().BeApproximately(tabsLayout.Bottom, BrowserQaPage.LayoutPrecision);
		statusLayout.Width.Should().BeApproximately(shellLayout.Width - (horizontalInset * 2), BrowserQaPage.LayoutPrecision);
		statusLayout.Right.Should().BeApproximately(shellLayout.Right - horizontalInset, BrowserQaPage.LayoutPrecision);
		actionLayout.X.Should().BeApproximately(shellLayout.X, BrowserQaPage.LayoutPrecision);
		actionLayout.Width.Should().BeApproximately(shellLayout.Width, BrowserQaPage.LayoutPrecision);
		actionLayout.Right.Should().BeApproximately(shellLayout.Right, BrowserQaPage.LayoutPrecision);
		actionLayout.Bottom.Should().BeApproximately(shellLayout.Bottom, BrowserQaPage.LayoutPrecision);
	}

	[Fact]
	public async Task DashboardScenario_KeepsFixedOverlaysStableWhileDashboardContentScrolls()
	{
		await using var browser = await Playwright.Chromium.LaunchAsync();
		var page = await BrowserQaPage.OpenScenarioAsync(browser, _host.DashboardScenarioUri);

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
		after.X.Should().BeApproximately(before.X, BrowserQaPage.LayoutPrecision);
		after.Y.Should().BeApproximately(before.Y, BrowserQaPage.LayoutPrecision);
		after.Width.Should().BeApproximately(before.Width, BrowserQaPage.LayoutPrecision);
		after.Height.Should().BeApproximately(before.Height, BrowserQaPage.LayoutPrecision);
	}

	[Fact]
	public async Task DashboardScenario_HoldButtonProgressUsesRenderedProductionTiming()
	{
		await using var browser = await Playwright.Chromium.LaunchAsync();
		var page = await BrowserQaPage.OpenScenarioAsync(browser, _host.DashboardScenarioUri);

		var initialActionZone = page.GetByTestId(ModeratorUiTestIds.DashboardActionZone);
		var initialContinue = BrowserQaHoldProgress.HoldZoneIn(initialActionZone)
			.GetByRole(AriaRole.Button, new() { Name = ClientStrings.Common_HoldToConfirm });
		await Expect(initialContinue).ToContainTextAsync(ClientStrings.Dashboard_ContinueButton);
		await initialContinue.ClickAsync(new()
		{
			Delay = HoldButtonTimingContract.HoldDurationMs + 50
		});
		var playerOptions = page.GetByRole(AriaRole.Option);
		await playerOptions.Nth(0).ClickAsync();
		await playerOptions.Nth(1).ClickAsync();

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

	[Fact]
	public async Task DashboardScenario_RecordsInstructionTransitionAnimationAndTiming()
	{
		await using var browser = await Playwright.Chromium.LaunchAsync();
		var page = await browser.NewPageAsync();

		await BrowserQaInstructionAnimations.InstallRecorderAsync(page, ModeratorUiTestIds.InstructionBlock);
		await BrowserQaPage.SetWideViewportAsync(page);
		await page.GotoAsync(_host.DashboardScenarioUri.ToString(), new()
		{
			WaitUntil = WaitUntilState.NetworkIdle
		});

		var instructionBlock = page.GetByTestId(ModeratorUiTestIds.InstructionBlock).First;

		await Expect(instructionBlock).ToBeVisibleAsync();
		await BrowserQaInstructionAnimations.WaitForRecordedEvidenceAsync(
			page,
			ModeratorUiTestIds.InstructionBlock);
		var evidence = await BrowserQaInstructionAnimations.ReadRecordedEvidenceAsync(
			page,
			ModeratorUiTestIds.InstructionBlock);

		evidence.AnimationName.Should().NotBeNullOrWhiteSpace();
		evidence.ComputedAnimationName.Should().Contain(evidence.AnimationName);
		evidence.DurationMs.Should().BeInRange(200, 300);
		evidence.ResolvedTokenDurationMs.Should().BeApproximately(evidence.DurationMs, precision: 0.5);
		evidence.TimingFunction.Should().Be(BrowserQaCss.EaseOutTimingFunctionValue);
		evidence.WebAnimationsDurationMs.Should().NotBeNull();
		evidence.WebAnimationsDurationMs!.Value.Should().BeInRange(200, 300);
		evidence.KeyframeCount.Should().BeGreaterThanOrEqualTo(2);
		evidence.FirstOpacity.Should().Be(BrowserQaCss.HiddenOpacityValue);
		evidence.LastOpacity.Should().Be(BrowserQaCss.VisibleOpacityValue);
		evidence.FirstTransform.Should().NotBeNullOrWhiteSpace();
		evidence.LastTransform.Should().NotBeNullOrWhiteSpace();
		evidence.FirstTransform.Should().NotBe(evidence.LastTransform);
	}
}
