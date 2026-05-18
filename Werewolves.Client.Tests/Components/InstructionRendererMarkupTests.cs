using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class InstructionRendererMarkupTests
{
	[Fact]
	public void Markup_ContainsSelectPlayersInstructionBranch()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered branch checks.
		var markup = File.ReadAllText(GetRendererPath());

		markup.Should().Contain("is SelectPlayersInstruction selectPlayersInstruction");
		markup.Should().Contain("<SelectPlayersView");
	}

	[Fact]
	public void Markup_PassesRosterParameterToSelectPlayersView()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit visible Player-name checks.
		var markup = File.ReadAllText(GetRendererPath());

		markup.Should().Contain("Roster=\"Roster\"");
	}

	[Fact]
	public void Markup_DoesNotRenderEmptyFixedActionZoneForInputViews()
	{
		// Deprecated temporary scaffold: replace with browser-host or bUnit rendered layout checks.
		var markup = File.ReadAllText(GetRendererPath());

		markup.Should().Contain("ShouldRenderDashboardActionZone");
		markup.Should().Contain("Instruction is not (SelectPlayersInstruction or SelectOptionsInstruction or AssignRolesInstruction)");
	}

	[Fact]
	public void Markup_DeclaresRosterParameter()
	{
		// Deprecated temporary scaffold: remove after ADR-0006/bUnit instantiates the component through public parameters.
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
