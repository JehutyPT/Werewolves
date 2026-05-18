using System.Text.RegularExpressions;
using FluentAssertions;
using Werewolves.Client.Tests.Helpers;
using Xunit;

namespace Werewolves.Client.Tests.Styling;

public class InstructionTransitionTests
{
    [Fact]
    public void DesignTokens_DefineInstructionAnimationDurationBetween200And300Ms()
    {
        var css = File.ReadAllText(ClientTestReferences.Paths.SharedPath("wwwroot/css/design-tokens.css"));
        var match = Regex.Match(css, @"--ww-anim-instruction:\s*(\d+)ms");

        match.Success.Should().BeTrue(ClientTestReferences.AssertionReasons.DesignTokensDefineInstructionAnimation);
        var duration = int.Parse(match.Groups[1].Value);
        duration.Should().BeInRange(200, 300, ClientTestReferences.AssertionReasons.InstructionAnimationDurationMatchesNightTempo);
    }

    [Fact]
    public void AppCss_DefinesInstructionEnterKeyframes()
    {
        // Deprecated temporary scaffold: replace with browser-host computed-style or motion checks.
        var css = File.ReadAllText(ClientTestReferences.Paths.SharedPath("wwwroot/css/app.css"));

        css.Should().Contain("@keyframes ww-instruction-enter",
            ClientTestReferences.AssertionReasons.AppCssDefinesInstructionEnterKeyframes);
    }

    [Fact]
    public void AppCss_InstructionBlockUsesAnimationToken()
    {
        // Deprecated temporary scaffold: replace with browser-host computed-style checks for rendered instruction blocks.
        var css = File.ReadAllText(ClientTestReferences.Paths.SharedPath("wwwroot/css/app.css"));

        css.Should().Contain("ww-instruction-enter",
            ClientTestReferences.AssertionReasons.InstructionBlockReferencesEnterAnimation);
        css.Should().Contain("var(--ww-anim-instruction)",
            ClientTestReferences.AssertionReasons.InstructionBlockUsesAnimationDurationToken);
    }
}
