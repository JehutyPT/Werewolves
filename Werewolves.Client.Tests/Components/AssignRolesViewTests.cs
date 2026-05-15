using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class AssignRolesViewTests
{
	[Fact]
	public void Markup_RendersRolesFromInstruction()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Instruction.RolesForAssignment");
	}

	[Fact]
	public void Markup_RendersPlayersForAssignment()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Instruction.PlayersForAssignment");
	}

	[Fact]
	public void Markup_AcceptsInstructionAndOnResponseParameters()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("[Parameter");
		markup.Should().Contain("AssignRolesInstruction Instruction");
		markup.Should().Contain("EventCallback<ModeratorResponse> OnResponse");
	}

	[Fact]
	public void Markup_AcceptsRosterParameterForPlayerNameResolution()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("IReadOnlyList<DashboardRosterEntry> Roster");
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
	public void Markup_SubmitButtonIsDisabledWhenAssignmentsIncomplete()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Disabled=\"@(!AllPlayersAssigned)\"");
	}

	[Fact]
	public void Markup_UsesGetPublicNameForRoleDisplay()
	{
		var markup = File.ReadAllText(GetViewPath());

		// Roles should use the existing GetPublicName() extension for localization
		markup.Should().Contain("GetPublicName");
	}

	[Fact]
	public void Markup_UsesClientStringsResourceKeys()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ClientStrings.AssignRoles_Title");
		markup.Should().Contain("ClientStrings.AssignRoles_SelectRolePrompt");
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
				"AssignRolesView.razor");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException("AssignRolesView.razor could not be found from the test output directory.");
	}
}
