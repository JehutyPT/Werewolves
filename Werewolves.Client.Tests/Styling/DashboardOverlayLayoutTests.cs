using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Styling;

public class DashboardOverlayLayoutTests
{
	[Fact]
	public void ProductionDashboard_FixesTopAndBottomOverlays()
	{
		// Deprecated temporary scaffold: replace with local browser QA host viewport/computed-layout checks.
		var css = File.ReadAllText(ClientPath("wwwroot/css/app.css"));

		css.Should().MatchRegex(SelectorBlockPattern(@"\[data-production-dashboard\]\s+\.ww-dashboard-tabs--compact", "position: fixed"));
		css.Should().MatchRegex(SelectorBlockPattern(@"\[data-production-dashboard\]\s+\.ww-dashboard-status-bar", "position: fixed"));
		css.Should().MatchRegex(SelectorBlockPattern(@"\.ww-dashboard-action-zone", "position: fixed"));
	}

	[Fact]
	public void ProductionDashboard_AddsScrollPaddingForFixedOverlays()
	{
		// Deprecated temporary scaffold: replace with local browser QA host viewport/computed-layout checks.
		var css = File.ReadAllText(ClientPath("wwwroot/css/app.css"));

		css.Should().Contain("--ww-dashboard-tabs-height");
		css.Should().Contain("--ww-dashboard-status-height");
		css.Should().Contain("--ww-dashboard-action-height");
		css.Should().MatchRegex(SelectorBlockPattern(@"\[data-production-dashboard\]", "padding-top: calc(var(--ww-dashboard-tabs-height) + var(--ww-dashboard-status-height) + 10px)"));
		css.Should().MatchRegex(SelectorBlockPattern(@"\[data-production-dashboard\]", "padding-bottom: calc(var(--ww-dashboard-action-height) + 24px)"));
		css.Should().Contain("padding-bottom: var(--ww-dashboard-action-height, 88px)");
	}

	[Fact]
	public void ProductionDashboard_StatusBarUsesInsetWidthInsteadOfViewportWidth()
	{
		// Deprecated temporary scaffold: replace with local browser QA host safe-area and viewport checks.
		var css = File.ReadAllText(ClientPath("wwwroot/css/app.css"));

		css.Should().MatchRegex(SelectorBlockPattern(@"\.ww-labs-status-bar\.ww-dashboard-status-bar", "width: auto"));
		css.Should().NotMatchRegex(SelectorBlockPattern(@"\.ww-labs-status-bar\.ww-dashboard-status-bar", "width: 100%"));
	}

	private static string SelectorBlockPattern(string selector, string declaration)
	{
		return $@"(?s){selector}\s*\{{(?:(?!\}}).)*{Regex.Escape(declaration)}";
	}

	private static string ClientPath(params string[] relativeSegments)
	{
		return Path.Combine([RepositoryRoot, "Werewolves.Client", .. relativeSegments]);
	}

	private static string RepositoryRoot
	{
		get
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);

			while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Werewolves.sln")))
			{
				directory = directory.Parent;
			}

			return directory?.FullName
				?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
		}
	}
}
