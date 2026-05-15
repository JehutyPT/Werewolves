using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class HoldButtonMarkupTests
{
	[Fact]
	public void Markup_UsesPointerEventsForHoldDetection()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("@onpointerdown");
		markup.Should().Contain("@onpointerup");
		markup.Should().Contain("@onpointerleave");
		markup.Should().Contain("@onpointercancel");
	}

	[Fact]
	public void Markup_UsesDelayWithCancellationForHoldTiming()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Task.Delay");
		markup.Should().Contain("CancellationTokenSource");
	}

	[Fact]
	public void Markup_InjectsHapticFeedbackService()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("@inject IHapticFeedbackService Haptic");
		markup.Should().Contain("Haptic.Click()");
	}

	[Fact]
	public void Markup_UsesHoldToConfirmResourceString()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ClientStrings.Common_HoldToConfirm");
	}

	[Fact]
	public void Markup_DeclaresRequiredParameters()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("[Parameter, EditorRequired]");
		markup.Should().Contain("string Label");
		markup.Should().Contain("bool Disabled");
		markup.Should().Contain("EventCallback OnHoldComplete");
	}

	[Fact]
	public void Markup_RendersHoldButtonStructure()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ww-hold-zone");
		markup.Should().Contain("ww-btn-hold");
		markup.Should().Contain("ww-btn-hold__fill");
		markup.Should().Contain("ww-btn-hold__edge");
		markup.Should().Contain("ww-btn-hold__label");
		markup.Should().Contain("ww-hold-hint");
	}

	[Fact]
	public void Markup_UsesCssStateClassesForVisualFeedback()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("is-holding");
		markup.Should().Contain("is-complete");
	}

	[Fact]
	public void Markup_DisablesButtonWhenDisabledParameterIsTrue()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("disabled=\"@Disabled\"");
	}

	private static string GetViewPath()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			var candidate = Path.Combine(
				directory.FullName,
				"Werewolves.Client",
				"Components",
				"Game",
				"Views",
				"HoldButton.razor");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException("HoldButton.razor could not be found from the test output directory.");
	}
}
