using System.Globalization;
using FluentAssertions;
using Microsoft.Playwright;

namespace Werewolves.Client.BrowserQa.Tests;

internal static class BrowserQaPage
{
	public const int WideViewportWidth = 900;
	public const int WideViewportHeight = 900;

	public static Task SetWideViewportAsync(IPage page) =>
		page.SetViewportSizeAsync(WideViewportWidth, WideViewportHeight);

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

	public static Task SetScrollTopAsync(ILocator locator, double scrollTop) =>
		locator.EvaluateAsync(
			"(element, scrollTop) => { element.scrollTop = scrollTop; }",
			scrollTop);

	public static Task<double> ReadScrollTopAsync(ILocator locator) =>
		locator.EvaluateAsync<double>("element => element.scrollTop");
}

internal sealed record ElementLayout(double X, double Y, double Width, double Height)
{
	public double Bottom => Y + Height;
	public double Right => X + Width;
}
