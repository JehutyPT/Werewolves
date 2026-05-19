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

		markup.Should().Contain(ClientTestReferences.RazorMarkup.OnPointerDown);
		markup.Should().Contain(ClientTestReferences.RazorMarkup.OnPointerUp);
		markup.Should().Contain(ClientTestReferences.RazorMarkup.OnPointerLeave);
		markup.Should().Contain(ClientTestReferences.RazorMarkup.OnPointerCancel);
	}

	[Fact]
	public void DesignTokens_AnimateHoldProgressOverProductionDuration()
	{
		// Deprecated temporary scaffold: replace with browser-host computed-style checks for rendered hold progress.
		var designTokens = File.ReadAllText(GetSharedPath("wwwroot", "css", "design-tokens.css"));

		designTokens.Should().Contain(ClientTestReferences.Css.Declarations.HoldFillProductionTransition);
		designTokens.Should().Contain(ClientTestReferences.Css.Declarations.HoldEdgeProductionTransition);
		designTokens.Should().NotContain(ClientTestReferences.Css.Declarations.HoldFillSlowTransition);
		designTokens.Should().NotContain(ClientTestReferences.Css.Declarations.HoldEdgeSlowTransition);
	}

	[Fact]
	public void Markup_UsesHoldToConfirmResourceString()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered text checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain(ClientTestReferences.RazorMarkup.HoldToConfirmResource);
	}

	[Fact]
	public void Markup_DeclaresRequiredParameters()
	{
		// Deprecated temporary scaffold: remove after ADR-0006/bUnit instantiates HoldButton through public parameters.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain(ClientTestReferences.RazorMarkup.RequiredParameterAttribute);
		markup.Should().Contain("string Label");
		markup.Should().Contain("bool Disabled");
		markup.Should().Contain(
			nameof(EventCallback) + ClientTestReferences.RazorMarkup.EventCallbackOnHoldCompleteParameterSuffix);
	}

	[Fact]
	public void Markup_RendersHoldButtonStructure()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered markup or browser-host visual checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain(ClientTestReferences.Css.Classes.HoldZone);
		markup.Should().Contain(ClientTestReferences.Css.Classes.HoldButton);
		markup.Should().Contain(ClientTestReferences.Css.Classes.HoldButtonFill);
		markup.Should().Contain(ClientTestReferences.Css.Classes.HoldButtonEdge);
		markup.Should().Contain(ClientTestReferences.Css.Classes.HoldButtonLabel);
		markup.Should().Contain(ClientTestReferences.Css.Classes.HoldHint);
	}

	[Fact]
	public void Markup_UsesCssStateClassesForVisualFeedback()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered state-transition checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain(ClientTestReferences.Css.Classes.Holding);
		markup.Should().Contain(ClientTestReferences.Css.Classes.HoldComplete);
	}

	[Fact]
	public void Markup_DisablesButtonWhenDisabledParameterIsTrue()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered disabled-attribute checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain(ClientTestReferences.RazorMarkup.DisabledParameterAttribute);
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
