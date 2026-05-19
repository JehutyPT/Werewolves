using System.Text.RegularExpressions;
using FluentAssertions;
using Werewolves.Client.Tests.Helpers;
using Css = Werewolves.Client.Tests.Helpers.ClientTestReferences.Css;
using Xunit;

namespace Werewolves.Client.Tests.Styling;

public class DashboardOverlayLayoutTests
{
	[Fact]
	public void ProductionDashboard_FixesTopAndBottomOverlays()
	{
		// Deprecated temporary scaffold: replace with local browser QA host viewport/computed-layout checks.
		var css = File.ReadAllText(SharedPath("wwwroot/css/app.css"));

		css.Should().MatchRegex(SelectorBlockPattern(
			Css.Selectors.ProductionDashboardCompactTabs,
			Css.Declarations.PositionFixed));
		css.Should().MatchRegex(SelectorBlockPattern(
			Css.Selectors.ProductionDashboardStatusBar,
			Css.Declarations.PositionFixed));
		css.Should().MatchRegex(SelectorBlockPattern(
			Css.Selectors.DashboardActionZone,
			Css.Declarations.PositionFixed));
	}

	[Fact]
	public void ProductionDashboard_AddsScrollPaddingForFixedOverlays()
	{
		// Deprecated temporary scaffold: replace with local browser QA host viewport/computed-layout checks.
		var css = File.ReadAllText(SharedPath("wwwroot/css/app.css"));

		css.Should().Contain(Css.Tokens.DashboardTabsHeight);
		css.Should().Contain(Css.Tokens.DashboardStatusHeight);
		css.Should().Contain(Css.Tokens.DashboardActionHeight);
		css.Should().MatchRegex(SelectorBlockPattern(
			Css.Selectors.ProductionDashboard,
			Css.Declarations.DashboardPaddingTop));
		css.Should().MatchRegex(SelectorBlockPattern(
			Css.Selectors.ProductionDashboard,
			Css.Declarations.DashboardPaddingBottom));
		css.Should().Contain(Css.Declarations.DashboardActionPaddingFallback);
	}

	[Fact]
	public void ProductionDashboard_StatusBarUsesInsetWidthInsteadOfViewportWidth()
	{
		// Deprecated temporary scaffold: replace with local browser QA host safe-area and viewport checks.
		var css = File.ReadAllText(SharedPath("wwwroot/css/app.css"));

		css.Should().MatchRegex(SelectorBlockPattern(
			Css.Selectors.LabsDashboardStatusBar,
			Css.Declarations.WidthAuto));
		css.Should().NotMatchRegex(SelectorBlockPattern(
			Css.Selectors.LabsDashboardStatusBar,
			Css.Declarations.WidthFull));
	}

	private static string SelectorBlockPattern(string selector, string declaration)
	{
		return $@"(?s){selector}\s*\{{(?:(?!\}}).)*{Regex.Escape(declaration)}";
	}

	private static string SharedPath(params string[] relativeSegments)
	{
		return ClientTestReferences.Paths.SharedPath(relativeSegments);
	}
}
