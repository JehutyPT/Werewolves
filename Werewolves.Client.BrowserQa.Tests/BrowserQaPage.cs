using System.Globalization;
using FluentAssertions;
using Microsoft.Playwright;

namespace Werewolves.Client.BrowserQa.Tests;

internal static class BrowserQaPage
{
	public const int PhoneFrameHeight = 800;
	public const int PhoneFrameWidth = 360;
	public const double PhoneFramePrecision = 0.5;
	public const double LayoutPrecision = 0.75;
	public const int WideViewportWidth = 900;
	public const int WideViewportHeight = 900;
	public const WaitUntilState ScenarioWaitUntil = WaitUntilState.NetworkIdle;

	public static Task SetWideViewportAsync(IPage page) =>
		page.SetViewportSizeAsync(WideViewportWidth, WideViewportHeight);

	public static async Task<IPage> OpenScenarioAsync(IBrowser browser, Uri scenarioUri)
	{
		var page = await browser.NewPageAsync();

		await SetWideViewportAsync(page);
		await page.GotoAsync(scenarioUri.ToString(), new()
		{
			WaitUntil = ScenarioWaitUntil
		});

		return page;
	}

	public static async Task<ElementLayout> ReadLayoutAsync(ILocator locator)
	{
		var box = await locator.BoundingBoxAsync();
		box.Should().NotBeNull("the browser can only measure layout after the element is rendered");

		return new ElementLayout(box!.X, box.Y, box.Width, box.Height);
	}

	public static Task<string> ReadComputedStyleAsync(ILocator locator, string propertyName) =>
		locator.EvaluateAsync<string>(
			"(element, propertyName) => getComputedStyle(element).getPropertyValue(propertyName)",
			propertyName);

	public static async Task<double> ReadComputedPixelValueAsync(ILocator locator, string propertyName)
	{
		var value = await ReadComputedStyleAsync(locator, propertyName);
		return double.Parse(value.Replace("px", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
	}
}

internal sealed record ElementLayout(double X, double Y, double Width, double Height)
{
	public double Bottom => Y + Height;
}
