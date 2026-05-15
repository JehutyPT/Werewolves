using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class SelectPlayersViewMarkupTests
{
	[Fact]
	public void Markup_ContainsPlayerListWithSeatNumberAndName()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ww-select-players-list");
		markup.Should().Contain("ww-select-players-item");
		markup.Should().Contain("ww-seat-number");
		markup.Should().Contain("ww-player-name");
	}

	[Fact]
	public void Markup_ContainsSelectedStateToggle()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ww-select-players-item--selected");
		markup.Should().Contain("aria-selected");
		markup.Should().Contain("ww-select-players-check");
	}

	[Fact]
	public void Markup_ContainsPressAndHoldSubmitButton()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("<HoldButton");
		markup.Should().Contain("Label=\"@ClientStrings.SelectPlayers_SubmitButton\"");
		markup.Should().Contain("OnHoldComplete=\"HandleSubmit\"");
	}

	[Fact]
	public void Markup_UsesClientStringsResourceKeys()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ClientStrings.SelectPlayers_SubmitButton");
		markup.Should().Contain("ClientStrings.SelectPlayers_ListAria");
	}

	[Fact]
	public void Markup_DeclaresRequiredParameters()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("[Parameter, EditorRequired]");
		markup.Should().Contain("SelectPlayersInstruction Instruction");
		markup.Should().Contain("IReadOnlyList<DashboardRosterEntry> Roster");
		markup.Should().Contain("EventCallback<ModeratorResponse> OnResponse");
	}

	[Fact]
	public void Markup_SubmitButtonDisabledWhenCannotSubmit()
	{
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
