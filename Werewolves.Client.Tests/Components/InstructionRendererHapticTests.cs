using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class InstructionRendererHapticTests
{
    [Fact]
    public void InstructionRenderer_InjectsHapticFeedbackService()
    {
        var markup = File.ReadAllText(InstructionRendererPath());

        markup.Should().Contain("IHapticFeedbackService",
            "InstructionRenderer should inject IHapticFeedbackService to fire haptic on instruction expansion taps");
    }

    [Fact]
    public void InstructionRenderer_UsesTransitionKeyForAnimationReMount()
    {
        var markup = File.ReadAllText(InstructionRendererPath());

        markup.Should().Contain("_flow.TransitionKey",
            "InstructionRenderer should use _flow.TransitionKey as @key so instruction changes trigger animation");
    }

    [Fact]
    public void ConfirmationView_UsesHoldButtonForSubmission()
    {
        var markup = File.ReadAllText(ConfirmationViewPath());

        markup.Should().Contain("<HoldButton",
            "confirmation game actions should use the same press-and-hold confirmation gate as other submissions");
        markup.Should().Contain("OnHoldComplete=\"Confirm\"");
        markup.Should().NotContain("@onclick=\"Confirm\"",
            "confirmation must not keep an instant-click submit path");
    }

    private static string InstructionRendererPath()
    {
        return Path.Combine(RepositoryRoot, "Werewolves.Client", "Components", "Game", "Views", "InstructionRenderer.razor");
    }

    private static string ConfirmationViewPath()
    {
        return Path.Combine(RepositoryRoot, "Werewolves.Client", "Components", "Game", "Views", "ConfirmationView.razor");
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
