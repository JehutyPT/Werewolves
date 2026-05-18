using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class SelectPlayersViewMarkupTests
{
	[Fact]
	public void Markup_ContainsPlayerListWithSeatNumberAndName()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered list checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ww-select-players-list");
		markup.Should().Contain("ww-select-players-item");
		markup.Should().Contain("ww-seat-number");
		markup.Should().Contain("ww-player-name");
	}

	[Fact]
	public void Markup_ContainsSelectedStateToggle()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered interaction checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ww-select-players-item--selected");
		markup.Should().Contain("aria-selected");
		markup.Should().Contain("ww-select-players-check");
	}

	[Fact]
	public void Markup_ContainsPressAndHoldSubmitButton()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered submit-event checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("<HoldButton");
		markup.Should().Contain("Label=\"@ClientStrings.SelectPlayers_SubmitButton\"");
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
	public void Markup_UsesClientStringsResourceKeys()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered Portuguese text checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ClientStrings.SelectPlayers_SubmitButton");
		markup.Should().Contain("ClientStrings.SelectPlayers_ListAria");
	}

	[Fact]
	public void Markup_DeclaresRequiredParameters()
	{
		// Deprecated temporary scaffold: remove after ADR-0006/bUnit instantiates the component through public parameters.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("[Parameter, EditorRequired]");
		markup.Should().Contain("SelectPlayersInstruction Instruction");
		markup.Should().Contain("IReadOnlyList<DashboardRosterEntry> Roster");
		markup.Should().Contain("EventCallback<ModeratorResponse> OnResponse");
	}

	[Fact]
	public void Markup_SubmitButtonDisabledWhenCannotSubmit()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered interaction checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Disabled=\"@(!_state.CanSubmit)\"");
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
				"SelectPlayersView.razor");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException("SelectPlayersView.razor could not be found from the test output directory.");
	}
}
