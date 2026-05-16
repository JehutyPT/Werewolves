using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class DashboardPageMarkupTests
{
	[Fact]
	public void Markup_UsesCompactStatusBarInsteadOfLargePhaseStrip()
	{
		var markup = File.ReadAllText(GetPagePath());

		markup.Should().Contain("ww-labs-status-bar ww-dashboard-status-bar");
		markup.Should().Contain("ww-labs-status-bar__turn");
		markup.Should().Contain("ww-labs-status-bar__phase");
		markup.Should().Contain("ww-audio-toggle");
		markup.Should().Contain("aria-label=\"@AudioToggleLabel\"");
		markup.Should().NotContain("<AudioControls>");
		markup.Should().NotContain("ww-phase-strip");
	}

	[Fact]
	public void Markup_KeepsDashboardTabsOperable()
	{
		var markup = File.ReadAllText(GetPagePath());

		markup.Should().Contain("ww-dashboard-tabs ww-dashboard-tabs--compact");
		markup.Should().Contain("SelectTab(DashboardTab.Roster)");
		markup.Should().Contain("SelectTab(DashboardTab.Action)");
		markup.Should().Contain("SelectTab(DashboardTab.Stats)");
	}

	private static string GetPagePath()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			var candidate = Path.Combine(
				directory.FullName,
				"Werewolves.Client",
				"Components",
				"Pages",
				"DashboardPage.razor");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException("DashboardPage.razor could not be found from the test output directory.");
	}
}
