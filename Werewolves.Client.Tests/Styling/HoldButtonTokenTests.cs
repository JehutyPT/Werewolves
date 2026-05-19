using FluentAssertions;
using Werewolves.Client.Tests.Helpers;
using Css = Werewolves.Client.Tests.Helpers.ClientTestReferences.Css;
using Xunit;

namespace Werewolves.Client.Tests.Styling;

public class HoldButtonTokenTests
{
	[Fact]
	public void DesignTokens_AnimateHoldProgressOverProductionDuration()
	{
		// Deprecated temporary scaffold: replace with browser-host computed-style checks for rendered hold progress.
		var designTokens = File.ReadAllText(ClientTestReferences.Paths.SharedPath("wwwroot/css/design-tokens.css"));

		designTokens.Should().Contain(Css.Declarations.HoldFillProductionTransition);
		designTokens.Should().Contain(Css.Declarations.HoldEdgeProductionTransition);
		designTokens.Should().NotContain(Css.Declarations.HoldFillSlowTransition);
		designTokens.Should().NotContain(Css.Declarations.HoldEdgeSlowTransition);
	}
}
