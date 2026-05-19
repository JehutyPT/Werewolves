using System.Globalization;
using FluentAssertions;

namespace Werewolves.Client.BrowserQa.Tests;

internal sealed record RenderedTransition(
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
			.Split(BrowserQaCss.ListSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

	private static int TransitionDurationMilliseconds(string value)
	{
		if (value.EndsWith(BrowserQaCss.MillisecondUnit, StringComparison.Ordinal))
		{
			return (int)Math.Round(
				double.Parse(value[..^BrowserQaCss.MillisecondUnit.Length], CultureInfo.InvariantCulture));
		}

		if (value.EndsWith(BrowserQaCss.SecondUnit, StringComparison.Ordinal))
		{
			return (int)Math.Round(
				double.Parse(value[..^BrowserQaCss.SecondUnit.Length], CultureInfo.InvariantCulture) *
				BrowserQaCss.MillisecondsPerSecond);
		}

		throw new FormatException($"Unsupported CSS transition duration: {value}");
	}
}
