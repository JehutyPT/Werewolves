using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Werewolves.Client.Tests.Helpers;
using Xunit;
using Css = Werewolves.Client.Tests.Helpers.ClientTestReferences.Css;
using RazorMarkup = Werewolves.Client.Tests.Helpers.ClientTestReferences.RazorMarkup;

namespace Werewolves.Client.Tests.Components;

public class SelectOptionsViewTests
{
	[Fact]
	public void Markup_RendersButtonForEachOption()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered option-list checks.
		var markup = File.ReadAllText(GetViewPath());

		// The view iterates over Instruction.SelectableOptions and renders each as a button
		markup.Should().Contain(RazorMarkup.SelectableOptions);
		markup.Should().Contain(RazorMarkup.OptionVariable);
	}

	[Fact]
	public void Markup_UsesSelectedCssClassForHighlighting()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered interaction checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain(Css.Classes.OptionButtonSelected);
	}

	[Fact]
	public void Markup_HasSelectionRangeValidation()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit interaction checks.
		var markup = File.ReadAllText(GetViewPath());

		// The view must reference SelectionRange for constraint enforcement
		markup.Should().Contain(RazorMarkup.SelectionRange);
	}

	[Fact]
	public void Markup_AcceptsInstructionAndOnResponseParameters()
	{
		// Deprecated temporary scaffold: remove after ADR-0006/bUnit instantiates the component through public parameters.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain(RazorMarkup.ParameterAttribute);
		markup.Should().Contain(RazorMarkup.SelectOptionsInstructionParameter);
		markup.Should().Contain(nameof(EventCallback) + RazorMarkup.EventCallbackModeratorResponseParameterSuffix);
	}

	[Fact]
	public void Markup_CallsCreateResponseOnSubmit()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit submit-callback checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain(RazorMarkup.CreateResponseCall);
		markup.Should().Contain(RazorMarkup.OnResponseParameterName);
	}

	[Fact]
	public void Markup_SubmitUsesPressAndHoldPattern()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered submit-event checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain(RazorMarkup.HoldButtonTag);
		markup.Should().Contain(RazorMarkup.SubmitButtonResourceLabelAttribute);
		markup.Should().Contain(RazorMarkup.OnHoldCompleteHandleSubmitAttribute);
	}

	[Fact]
	public void Markup_PinsSubmitButtonInDashboardActionZone()
	{
		// Deprecated temporary scaffold: replace with browser-host layout checks or bUnit rendered structure checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().MatchRegex(RazorMarkup.DashboardActionFooterWithHoldButtonPattern);
	}

	[Fact]
	public void Markup_SubmitButtonIsDisabledWhenSelectionInvalid()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered interaction checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain(RazorMarkup.DisabledSelectionInvalidAttribute);
	}

	[Fact]
	public void Markup_EnforcesMaximumSelectionCount()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered interaction checks.
		var markup = File.ReadAllText(GetViewPath());

		// Prevents selecting more than Maximum
		markup.Should().Contain(RazorMarkup.SelectionRangeMaximum);
	}

	[Fact]
	public void Markup_UsesClientStringsResourceKeys()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered Portuguese text checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain(RazorMarkup.SelectOptionsTitleResource);
		markup.Should().Contain(RazorMarkup.SelectOptionsCountResource);
		markup.Should().Contain(RazorMarkup.SubmitButtonResource);
	}

	private static string GetViewPath()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			var candidate = Path.Combine(
				directory.FullName,
				"Werewolves.Client.Shared",
				"Components",
				"Game",
				"Views",
				"SelectOptionsView.razor");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException(ClientTestReferences.ExceptionMessages.ComponentViewNotFound("SelectOptionsView.razor"));
	}
}
