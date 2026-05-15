using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class InstructionRendererTests
{
	[Fact]
	public void Markup_HasSelectOptionsInstructionBranch()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("SelectOptionsInstruction");
		markup.Should().Contain("<SelectOptionsView");
	}

	[Fact]
	public void Markup_HasAssignRolesInstructionBranch()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("AssignRolesInstruction");
		markup.Should().Contain("<AssignRolesView");
	}

	[Fact]
	public void Markup_PassesRosterToAssignRolesView()
	{
		var markup = File.ReadAllText(GetViewPath());

		// AssignRolesView needs roster for player name resolution
		markup.Should().Contain("Roster=");
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
				"InstructionRenderer.razor");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException("InstructionRenderer.razor could not be found from the test output directory.");
	}
}
