using System.Text.RegularExpressions;
using FluentAssertions;
using Werewolves.Client.Tests.Helpers;
using Css = Werewolves.Client.Tests.Helpers.ClientTestReferences.Css;
using Xunit;

namespace Werewolves.Client.Tests.Styling;

public class InstructionTransitionTests
{
    [Fact]
    public void DesignTokens_DefineInstructionAnimationDurationBetween200And300Ms()
    {
        var css = File.ReadAllText(ClientTestReferences.Paths.SharedPath("wwwroot/css/design-tokens.css"));
        var match = Regex.Match(css, Css.Animations.InstructionAnimationDurationPattern);

        match.Success.Should().BeTrue(ClientTestReferences.AssertionReasons.DesignTokensDefineInstructionAnimation);
        var duration = int.Parse(match.Groups[1].Value);
        duration.Should().BeInRange(200, 300, ClientTestReferences.AssertionReasons.InstructionAnimationDurationMatchesNightTempo);
    }
}
