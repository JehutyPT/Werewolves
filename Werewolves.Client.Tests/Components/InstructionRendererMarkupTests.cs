using FluentAssertions;
using Werewolves.Client.Tests.Helpers;
using Xunit;
using RazorMarkup = Werewolves.Client.Tests.Helpers.ClientTestReferences.RazorMarkup;

namespace Werewolves.Client.Tests.Components;

public class InstructionRendererMarkupTests
{
	[Fact]
	public void Markup_ContainsSelectPlayersInstructionBranch()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit rendered branch checks.
		var markup = File.ReadAllText(GetRendererPath());

		markup.Should().Contain(RazorMarkup.SelectPlayersInstructionBranch);
		markup.Should().Contain(RazorMarkup.SelectPlayersViewTag);
	}

	[Fact]
	public void Markup_PassesRosterParameterToSelectPlayersView()
	{
		// Deprecated temporary scaffold: replace with ADR-0006/bUnit visible Player-name checks.
		var markup = File.ReadAllText(GetRendererPath());

		markup.Should().Contain(RazorMarkup.RosterAttribute);
	}

	[Fact]
	public void Markup_DoesNotRenderEmptyFixedActionZoneForInputViews()
	{
		// Deprecated temporary scaffold: replace with browser-host or bUnit rendered layout checks.
		var markup = File.ReadAllText(GetRendererPath());

		markup.Should().Contain(RazorMarkup.ShouldRenderDashboardActionZone);
		markup.Should().Contain(RazorMarkup.InputViewsWithoutDashboardActionZonePredicate);
	}

	[Fact]
	public void Markup_DeclaresRosterParameter()
	{
		// Deprecated temporary scaffold: remove after ADR-0006/bUnit instantiates the component through public parameters.
		var markup = File.ReadAllText(GetRendererPath());

		markup.Should().Contain(RazorMarkup.RosterParameter);
	}

	private static string GetRendererPath()
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
