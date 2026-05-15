using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class InstructionRendererMarkupTests
{
	[Fact]
	public void Markup_ContainsSelectPlayersInstructionBranch()
	{
		var markup = File.ReadAllText(GetRendererPath());

		markup.Should().Contain("is SelectPlayersInstruction selectPlayersInstruction");
		markup.Should().Contain("<SelectPlayersView");
	}

	[Fact]
	public void Markup_PassesRosterParameterToSelectPlayersView()
	{
		var markup = File.ReadAllText(GetRendererPath());

		markup.Should().Contain("Roster=\"Roster\"");
	}

	[Fact]
	public void Markup_DeclaresRosterParameter()
	{
		var markup = File.ReadAllText(GetRendererPath());

		markup.Should().Contain("IReadOnlyList<DashboardRosterEntry> Roster");
	}

	private static string GetRendererPath()
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
