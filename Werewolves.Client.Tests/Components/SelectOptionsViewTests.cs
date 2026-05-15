using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class SelectOptionsViewTests
{
	[Fact]
	public void Markup_RendersButtonForEachOption()
	{
		var markup = File.ReadAllText(GetViewPath());

		// The view iterates over Instruction.SelectableOptions and renders each as a button
		markup.Should().Contain("Instruction.SelectableOptions");
		markup.Should().Contain("@option");
	}

	[Fact]
	public void Markup_UsesSelectedCssClassForHighlighting()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ww-option-btn--selected");
	}

	[Fact]
	public void Markup_HasSelectionRangeValidation()
	{
		var markup = File.ReadAllText(GetViewPath());

		// The view must reference SelectionRange for constraint enforcement
		markup.Should().Contain("SelectionRange");
	}

	[Fact]
	public void Markup_AcceptsInstructionAndOnResponseParameters()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("[Parameter");
		markup.Should().Contain("SelectOptionsInstruction Instruction");
		markup.Should().Contain("EventCallback<ModeratorResponse> OnResponse");
	}

	[Fact]
	public void Markup_CallsCreateResponseOnSubmit()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Instruction.CreateResponse");
		markup.Should().Contain("OnResponse");
	}

	[Fact]
	public void Markup_SubmitUsesPressAndHoldPattern()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("<HoldButton");
		markup.Should().Contain("Label=\"@ClientStrings.Dashboard_ContinueButton\"");
		markup.Should().Contain("OnHoldComplete=\"HandleSubmit\"");
	}

	[Fact]
	public void Markup_SubmitButtonIsDisabledWhenSelectionInvalid()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Disabled=\"@(!IsSelectionValid)\"");
	}

	[Fact]
	public void Markup_EnforcesMaximumSelectionCount()
	{
		var markup = File.ReadAllText(GetViewPath());

		// Prevents selecting more than Maximum
		markup.Should().Contain("SelectionRange.Maximum");
	}

	[Fact]
	public void Markup_UsesClientStringsResourceKeys()
	{
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
				"Werewolves.Client",
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
