using FluentAssertions;
using Werewolves.Client.Tests.Helpers;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class InstructionRendererTests
{
	[Fact]
	public void Markup_HasSelectOptionsInstructionBranch()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered branch checks.
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("SelectOptionsInstruction");
		markup.Should().Contain("<SelectOptionsView");
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
				"InstructionRenderer.razor");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException(ClientTestReferences.ExceptionMessages.ComponentViewNotFound("InstructionRenderer.razor"));
	}
}
