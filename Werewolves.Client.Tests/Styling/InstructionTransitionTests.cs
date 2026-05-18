using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Styling;

public class InstructionTransitionTests
{
    [Fact]
    public void DesignTokens_DefineInstructionAnimationDurationBetween200And300Ms()
    {
        var css = File.ReadAllText(SharedPath("wwwroot/css/design-tokens.css"));
        var match = Regex.Match(css, @"--ww-anim-instruction:\s*(\d+)ms");

        match.Success.Should().BeTrue("design-tokens.css should define --ww-anim-instruction");
        var duration = int.Parse(match.Groups[1].Value);
        duration.Should().BeInRange(200, 300, "instruction animation should be 200-300ms for Night Phase tempo");
    }

    [Fact]
    public void AppCss_DefinesInstructionEnterKeyframes()
    {
        // Deprecated temporary scaffold: replace with browser-host computed-style or motion checks.
        var css = File.ReadAllText(SharedPath("wwwroot/css/app.css"));

        css.Should().Contain("@keyframes ww-instruction-enter",
            "app.css should define a keyframes animation for instruction transitions");
    }

    [Fact]
    public void AppCss_InstructionBlockUsesAnimationToken()
    {
        // Deprecated temporary scaffold: replace with browser-host computed-style checks for rendered instruction blocks.
        var css = File.ReadAllText(SharedPath("wwwroot/css/app.css"));

        css.Should().Contain("ww-instruction-enter",
            "instruction block should reference the enter animation");
        css.Should().Contain("var(--ww-anim-instruction)",
            "instruction block should use the design token for animation duration");
    }

    private static string SharedPath(params string[] relativeSegments)
    {
        return Path.Combine([RepositoryRoot, "Werewolves.Client.Shared", .. relativeSegments]);
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
