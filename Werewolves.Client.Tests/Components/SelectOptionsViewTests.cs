using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class SelectOptionsViewTests
{
	[Fact]
	public void Markup_RendersButtonForEachOption()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered option-list checks.
		var markup = File.ReadAllText(GetViewPath());

		// The view iterates over Instruction.SelectableOptions and renders each as a button
		markup.Should().Contain("Instruction.SelectableOptions");
		markup.Should().Contain("@option");
	}

	[Fact]
	public void Markup_UsesSelectedCssClassForHighlighting()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered interaction checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ww-option-btn--selected");
	}

	[Fact]
	public void Markup_HasSelectionRangeValidation()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit interaction checks.
		var markup = File.ReadAllText(GetViewPath());

		// The view must reference SelectionRange for constraint enforcement
		markup.Should().Contain("SelectionRange");
	}

	[Fact]
	public void Markup_AcceptsInstructionAndOnResponseParameters()
	{
		// Deprecated temporary scaffold: remove after ADR-0006/bUnit instantiates the component through public parameters.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("[Parameter");
		markup.Should().Contain("SelectOptionsInstruction Instruction");
		markup.Should().Contain("EventCallback<ModeratorResponse> OnResponse");
	}

	[Fact]
	public void Markup_CallsCreateResponseOnSubmit()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit submit-callback checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Instruction.CreateResponse");
		markup.Should().Contain("OnResponse");
	}

	[Fact]
	public void Markup_SubmitUsesPressAndHoldPattern()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered submit-event checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("<HoldButton");
		markup.Should().Contain("Label=\"@ClientStrings.Dashboard_ContinueButton\"");
		markup.Should().Contain("OnHoldComplete=\"HandleSubmit\"");
	}

	[Fact]
	public void Markup_PinsSubmitButtonInDashboardActionZone()
	{
		// Deprecated temporary scaffold: replace with browser-host layout checks or bUnit rendered structure checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().MatchRegex(@"(?s)<footer class=""ww-dashboard-action-zone"">\s*<HoldButton");
	}

	[Fact]
	public void Markup_SubmitButtonIsDisabledWhenSelectionInvalid()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered interaction checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Disabled=\"@(!IsSelectionValid)\"");
	}

	[Fact]
	public void Markup_EnforcesMaximumSelectionCount()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered interaction checks.
		var markup = File.ReadAllText(GetViewPath());

		// Prevents selecting more than Maximum
		markup.Should().Contain("SelectionRange.Maximum");
	}

	[Fact]
	public void Markup_UsesClientStringsResourceKeys()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered Portuguese text checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ClientStrings.SelectOptions_Title");
		markup.Should().Contain("ClientStrings.SelectOptions_SelectionCountFormat");
		markup.Should().Contain("ClientStrings.Dashboard_ContinueButton");
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

		throw new FileNotFoundException("SelectOptionsView.razor could not be found from the test output directory.");
	}
}
