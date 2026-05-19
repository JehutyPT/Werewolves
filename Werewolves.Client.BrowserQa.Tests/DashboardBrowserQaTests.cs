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

	[Fact]
	public async Task DashboardScenario_RecordsInstructionTransitionAnimationAndTiming()
	{
		await using var browser = await Playwright.Chromium.LaunchAsync();
		var page = await browser.NewPageAsync();

		await InstallInstructionAnimationRecorderAsync(page);
		await BrowserQaPage.SetWideViewportAsync(page);
		await page.GotoAsync(_host.DashboardScenarioUri.ToString(), new()
		{
			WaitUntil = WaitUntilState.NetworkIdle
		});

		var instructionBlock = page.GetByTestId(ModeratorUiTestIds.InstructionBlock).First;

		await Expect(instructionBlock).ToBeVisibleAsync();
		await page.WaitForFunctionAsync(
			"testId => window.__wwInstructionAnimations?.some(animation => animation.TestId === testId)",
			ModeratorUiTestIds.InstructionBlock);

		var evidence = await page.EvaluateAsync<InstructionAnimationEvidence>(
			"testId => window.__wwInstructionAnimations.find(animation => animation.TestId === testId)",
			ModeratorUiTestIds.InstructionBlock);

		evidence.AnimationName.Should().NotBeNullOrWhiteSpace();
		evidence.ComputedAnimationName.Should().Contain(evidence.AnimationName);
		evidence.DurationMs.Should().BeInRange(200, 300);
		evidence.ResolvedTokenDurationMs.Should().BeApproximately(evidence.DurationMs, precision: 0.5);
		evidence.TimingFunction.Should().Be("ease-out");
		evidence.WebAnimationsDurationMs.Should().NotBeNull();
		evidence.WebAnimationsDurationMs!.Value.Should().BeInRange(200, 300);
		evidence.KeyframeCount.Should().BeGreaterThanOrEqualTo(2);
		evidence.FirstOpacity.Should().Be("0");
		evidence.LastOpacity.Should().Be("1");
		evidence.FirstTransform.Should().NotBeNullOrWhiteSpace();
		evidence.LastTransform.Should().NotBeNullOrWhiteSpace();
		evidence.FirstTransform.Should().NotBe(evidence.LastTransform);
	}

	private static Task InstallInstructionAnimationRecorderAsync(IPage page) =>
		page.AddInitScriptAsync($$"""
			(() => {
				window.__wwInstructionAnimations = [];

				const instructionTestId = "{{ModeratorUiTestIds.InstructionBlock}}";
				const animationToken = "--ww-anim-instruction";
				const toMilliseconds = value => {
					const firstValue = value.split(",")[0].trim();
					if (firstValue.endsWith("ms")) {
						return Number.parseFloat(firstValue.slice(0, -2));
					}

					if (firstValue.endsWith("s")) {
						return Number.parseFloat(firstValue.slice(0, -1)) * 1000;
					}

					return Number.NaN;
				};

				document.addEventListener("animationstart", event => {
					const target = event.target;
					if (!(target instanceof HTMLElement) ||
						target.getAttribute("data-testid") !== instructionTestId) {
						return;
					}

					const styles = getComputedStyle(target);
					const runtimeAnimation = target.getAnimations()
						.find(animation => animation.animationName === event.animationName);
					const runtimeTiming = runtimeAnimation?.effect?.getTiming();
					const runtimeDuration = runtimeTiming?.duration;
					const runtimeKeyframes = runtimeAnimation?.effect?.getKeyframes() ?? [];
					const firstKeyframe = runtimeKeyframes[0] ?? {};
					const lastKeyframe = runtimeKeyframes[runtimeKeyframes.length - 1] ?? {};

					window.__wwInstructionAnimations.push({
						TestId: instructionTestId,
						AnimationName: event.animationName,
						ComputedAnimationName: styles.animationName,
						DurationMs: toMilliseconds(styles.animationDuration),
						ResolvedTokenDurationMs: toMilliseconds(styles.getPropertyValue(animationToken)),
						TimingFunction: styles.animationTimingFunction,
						WebAnimationsDurationMs: typeof runtimeDuration === "number" ? runtimeDuration : null,
						KeyframeCount: runtimeKeyframes.length,
						FirstOpacity: String(firstKeyframe.opacity ?? ""),
						LastOpacity: String(lastKeyframe.opacity ?? ""),
						FirstTransform: String(firstKeyframe.transform ?? ""),
						LastTransform: String(lastKeyframe.transform ?? "")
					});
				}, true);
			})();
			""");

	private sealed class InstructionAnimationEvidence
	{
		public string AnimationName { get; init; } = string.Empty;
		public string ComputedAnimationName { get; init; } = string.Empty;
		public double DurationMs { get; init; }
		public double ResolvedTokenDurationMs { get; init; }
		public string TimingFunction { get; init; } = string.Empty;
		public double? WebAnimationsDurationMs { get; init; }
		public int KeyframeCount { get; init; }
		public string FirstOpacity { get; init; } = string.Empty;
		public string LastOpacity { get; init; } = string.Empty;
		public string FirstTransform { get; init; } = string.Empty;
		public string LastTransform { get; init; } = string.Empty;
	}
}
