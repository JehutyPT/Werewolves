using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class InstructionRendererHapticTests
{
    [Fact]
    public void InstructionRenderer_UsesTransitionKeyForAnimationReMount()
    {
        // Deprecated temporary scaffold: replace with browser-host motion checks or bUnit render-tree evidence.
        var markup = File.ReadAllText(InstructionRendererPath());

        markup.Should().Contain("_flow.TransitionKey",
            "InstructionRenderer should use _flow.TransitionKey as @key so instruction changes trigger animation");
    }

    private static string InstructionRendererPath()
    {
        return Path.Combine(RepositoryRoot, "Werewolves.Client.Shared", "Components", "Game", "Views", "InstructionRenderer.razor");
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
