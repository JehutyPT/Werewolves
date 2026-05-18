using FluentAssertions;
using Werewolves.Client.Tests.Helpers;
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
            ClientTestReferences.AssertionReasons.InstructionRendererUsesTransitionKey);
    }

    private static string InstructionRendererPath() =>
        ClientTestReferences.Paths.SharedPath("Components", "Game", "Views", "InstructionRenderer.razor");
}
