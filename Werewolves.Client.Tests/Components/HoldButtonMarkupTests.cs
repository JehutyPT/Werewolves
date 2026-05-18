using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Werewolves.Client.Tests.Helpers;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class HoldButtonMarkupTests
{
	[Fact]
	public void Markup_UsesPointerEventsForHoldDetection()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit pointer-event rendering coverage.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("@onpointerdown");
		markup.Should().Contain("@onpointerup");
		markup.Should().Contain("@onpointerleave");
		markup.Should().Contain("@onpointercancel");
	}

	[Fact]
	public void DesignTokens_AnimateHoldProgressOverProductionDuration()
	{
		// Deprecated temporary scaffold: replace with browser-host computed-style checks for rendered hold progress.
		var designTokens = File.ReadAllText(GetSharedPath("wwwroot", "css", "design-tokens.css"));

		designTokens.Should().Contain("transition: width 400ms linear;");
		designTokens.Should().Contain("transition: left 400ms linear, opacity 80ms ease-in;");
		designTokens.Should().NotContain("transition: width 600ms linear;");
		designTokens.Should().NotContain("transition: left 600ms linear, opacity 80ms ease-in;");
	}

	[Fact]
	public void Markup_UsesHoldToConfirmResourceString()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered text checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ClientStrings.Common_HoldToConfirm");
	}

	[Fact]
	public void Markup_DeclaresRequiredParameters()
	{
		// Deprecated temporary scaffold: remove after ADR-0006/bUnit instantiates HoldButton through public parameters.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("[Parameter, EditorRequired]");
		markup.Should().Contain("string Label");
		markup.Should().Contain("bool Disabled");
		markup.Should().Contain(nameof(EventCallback) + " OnHoldComplete");
	}

	[Fact]
	public void Markup_RendersHoldButtonStructure()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered markup or browser-host visual checks.
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
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered state-transition checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("is-holding");
		markup.Should().Contain("is-complete");
	}

	[Fact]
	public void Markup_DisablesButtonWhenDisabledParameterIsTrue()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered disabled-attribute checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("disabled=\"@Disabled\"");
	}

	private static string GetViewPath()
	{
		return GetSharedPath("Components", "Game", "Views", "HoldButton.razor");
	}

	private static string GetSharedPath(params string[] relativeSegments)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			var candidate = Path.Combine([directory.FullName, "Werewolves.Client.Shared", .. relativeSegments]);

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException(
			ClientTestReferences.ExceptionMessages.TestFileNotFound(Path.Combine(relativeSegments)));
	}
}
