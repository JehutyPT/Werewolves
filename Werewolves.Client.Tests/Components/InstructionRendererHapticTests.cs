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
            "InstructionRenderer should inject IHapticFeedbackService to fire haptic on game-advancing taps");
    }

    [Fact]
    public void InstructionRenderer_UsesTransitionKeyForAnimationReMount()
    {
        var markup = File.ReadAllText(InstructionRendererPath());

        markup.Should().Contain("_flow.TransitionKey",
            "InstructionRenderer should use _flow.TransitionKey as @key so both instruction changes and public-to-private reveals trigger animation");
    }

    [Fact]
    public void ConfirmationView_InjectsHapticFeedbackService()
    {
        var markup = File.ReadAllText(ConfirmationViewPath());

        markup.Should().Contain("IHapticFeedbackService",
            "ConfirmationView should inject IHapticFeedbackService to fire haptic on confirm");
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
