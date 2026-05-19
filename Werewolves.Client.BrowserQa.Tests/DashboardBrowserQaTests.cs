using FluentAssertions;
using System.Globalization;
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
	public async Task DashboardScenario_HoldButtonProgressUsesRenderedProductionTiming()
	{
		await using var browser = await Playwright.Chromium.LaunchAsync();
		var page = await browser.NewPageAsync();

		await BrowserQaPage.SetWideViewportAsync(page);
		await page.GotoAsync(_host.DashboardScenarioUri.ToString(), new()
		{
			WaitUntil = WaitUntilState.NetworkIdle
		});

		var actionZone = page.GetByTestId(ModeratorUiTestIds.DashboardActionZone);
		var holdZone = actionZone.Locator(BrowserQaCss.HoldZoneSelector).First;
		var holdButton = holdZone.GetByRole(AriaRole.Button, new() { Name = ClientStrings.Common_HoldToConfirm });

		await Expect(actionZone).ToBeVisibleAsync();
		await Expect(holdButton).ToBeVisibleAsync();
		await Expect(holdButton).ToBeEnabledAsync();

		var evidence = await ReadHoldProgressTransitionEvidenceWhileHoldingAsync(holdZone);
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

	private static Task<HoldProgressTransitionEvidence> ReadHoldProgressTransitionEvidenceWhileHoldingAsync(ILocator holdZone) =>
		holdZone.EvaluateAsync<HoldProgressTransitionEvidence>(
			"""
			async (zone) => {
				const button = zone.querySelector('.ww-btn-hold');
				const fill = zone.querySelector('.ww-btn-hold__fill');
				const edge = zone.querySelector('.ww-btn-hold__edge');

				if (!button || !fill || !edge) {
					throw new Error('The rendered hold button did not expose the progress fill and edge.');
				}

				const pointerDown = new PointerEvent('pointerdown', {
					bubbles: true,
					pointerId: 1,
					pointerType: 'mouse',
					isPrimary: true,
					button: 0,
					buttons: 1
				});
				const pointerUp = new PointerEvent('pointerup', {
					bubbles: true,
					pointerId: 1,
					pointerType: 'mouse',
					isPrimary: true,
					button: 0,
					buttons: 0
				});

				button.dispatchEvent(pointerDown);
				try {
					await new Promise((resolve, reject) => {
						const timeout = window.setTimeout(
							() => reject(new Error('The rendered hold button never entered its holding state.')),
							350);
						const observe = () => {
							if (zone.classList.contains('is-holding')) {
								window.clearTimeout(timeout);
								resolve();
								return;
							}

							window.requestAnimationFrame(observe);
						};

						observe();
					});

					const fillStyle = getComputedStyle(fill);
					const edgeStyle = getComputedStyle(edge);

					return {
						FillTransitionProperty: fillStyle.transitionProperty,
						FillTransitionDuration: fillStyle.transitionDuration,
						FillTransitionTimingFunction: fillStyle.transitionTimingFunction,
						EdgeTransitionProperty: edgeStyle.transitionProperty,
						EdgeTransitionDuration: edgeStyle.transitionDuration,
						EdgeTransitionTimingFunction: edgeStyle.transitionTimingFunction
					};
				} finally {
					button.dispatchEvent(pointerUp);
				}
			}
			""");

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

	private sealed class HoldProgressTransitionEvidence
	{
		public string FillTransitionProperty { get; set; } = string.Empty;
		public string FillTransitionDuration { get; set; } = string.Empty;
		public string FillTransitionTimingFunction { get; set; } = string.Empty;
		public string EdgeTransitionProperty { get; set; } = string.Empty;
		public string EdgeTransitionDuration { get; set; } = string.Empty;
		public string EdgeTransitionTimingFunction { get; set; } = string.Empty;
	}

	private sealed record RenderedTransition(
		IReadOnlyList<string> Properties,
		IReadOnlyList<string> Durations,
		IReadOnlyList<string> TimingFunctions)
	{
		public static RenderedTransition From(
			string transitionProperty,
			string transitionDuration,
			string transitionTimingFunction) =>
			new(
				SplitComputedList(transitionProperty),
				SplitComputedList(transitionDuration),
				SplitComputedList(transitionTimingFunction));

		public int DurationMsFor(string property) =>
			TransitionDurationMilliseconds(Durations[PropertyIndex(property) % Durations.Count]);

		public string TimingFunctionFor(string property) =>
			TimingFunctions[PropertyIndex(property) % TimingFunctions.Count];

		private int PropertyIndex(string property)
		{
			var index = Properties
				.Select((value, position) => new { Value = value, Position = position })
				.SingleOrDefault(candidate => candidate.Value == property)
				?.Position;

			index.Should().NotBeNull($"the rendered transition should include the {property} progress property");
			return index!.Value;
		}

		private static IReadOnlyList<string> SplitComputedList(string value) =>
			value
				.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

		private static int TransitionDurationMilliseconds(string value)
		{
			if (value.EndsWith("ms", StringComparison.Ordinal))
			{
				return (int)Math.Round(double.Parse(value[..^2], CultureInfo.InvariantCulture));
			}

			if (value.EndsWith("s", StringComparison.Ordinal))
			{
				return (int)Math.Round(double.Parse(value[..^1], CultureInfo.InvariantCulture) * 1000);
			}

			throw new FormatException($"Unsupported CSS transition duration: {value}");
		}
	}
}
