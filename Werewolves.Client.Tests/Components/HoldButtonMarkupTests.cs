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
		markup.Should().Contain("Haptic.TryLongPress()");
		markup.Should().NotContain("Haptic.TryClick()");
	}

	[Fact]
	public void Markup_LocksProductionHapticPresetTiming()
	{
		var sequence = File.ReadAllText(GetClientPath("Components", "Game", "Views", "HoldButtonHapticSequence.cs"));

		sequence.Should().Contain("HoldDurationMs = 400");
		sequence.Should().Contain("[0, 200, 280, 330, 360, 380]");
		sequence.Should().Contain("PendingLongPressHapticOffsetsMs");
	}

	[Fact]
	public void DesignTokens_AnimateHoldProgressOverProductionDuration()
	{
		var designTokens = File.ReadAllText(GetClientPath("wwwroot", "css", "design-tokens.css"));

		designTokens.Should().Contain("transition: width 400ms linear;");
		designTokens.Should().Contain("transition: left 400ms linear, opacity 80ms ease-in;");
		designTokens.Should().NotContain("transition: width 600ms linear;");
		designTokens.Should().NotContain("transition: left 600ms linear, opacity 80ms ease-in;");
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
		return GetClientPath("Components", "Game", "Views", "HoldButton.razor");
	}

	private static string GetClientPath(params string[] relativeSegments)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			var candidate = Path.Combine([directory.FullName, "Werewolves.Client", .. relativeSegments]);

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException(
			$"{Path.Combine(relativeSegments)} could not be found from the test output directory.");
	}
}
