using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class InstructionDisplayFlowTests
{
    [Fact]
    public void TransitionKey_ChangesWhenInstructionChanges()
    {
        var first = CreateInstruction("First announcement");
        var second = CreateInstruction("Second announcement");
        var flow = new InstructionDisplayFlow(first);
        var firstKey = flow.TransitionKey;

        flow.SetInstruction(second);

        flow.TransitionKey.Should().NotBe(firstKey,
            "transition key must change between instructions to trigger re-mount animation");
    }

    [Fact]
    public void TransitionKey_ChangesWhenAdvancingFromPublicToPrivate()
    {
        var instruction = CreateTwoPartInstruction("Village sleeps", "Check who the wolves chose");
        var flow = new InstructionDisplayFlow(instruction);
        var publicKey = flow.TransitionKey;

        flow.Advance();

        flow.TransitionKey.Should().NotBe(publicKey,
            "transition key must change on public-to-private reveal to trigger animation");
    }

    [Fact]
    public void TransitionKey_StaysStableWithoutStateChange()
    {
        var instruction = CreateTwoPartInstruction("Village sleeps", "Check who the wolves chose");
        var flow = new InstructionDisplayFlow(instruction);
        var firstRead = flow.TransitionKey;
        var secondRead = flow.TransitionKey;

        firstRead.Should().Be(secondRead,
            "transition key should be stable when the flow state has not changed");
    }

    [Fact]
    public void TransitionKey_IsNullWhenNoInstruction()
    {
        var flow = new InstructionDisplayFlow();

        flow.TransitionKey.Should().BeNull(
            "transition key should be null when there is no instruction");
    }

    [Fact]
    public void SetInstruction_ResetsShowingInputToFalse()
    {
        var instruction = CreateTwoPartInstruction("Village sleeps", "Private text");
        var flow = new InstructionDisplayFlow(instruction);
        flow.Advance();
        flow.IsShowingInput.Should().BeTrue();

        var next = CreateTwoPartInstruction("Next announcement", "Next private");
        flow.SetInstruction(next);

        flow.IsShowingInput.Should().BeFalse();
    }

    private static TestInstruction CreateInstruction(string publicAnnouncement) =>
        new(publicAnnouncement: publicAnnouncement);

    private static TestInstruction CreateTwoPartInstruction(string publicAnnouncement, string privateInstruction) =>
        new(publicAnnouncement: publicAnnouncement, privateInstruction: privateInstruction);

    private sealed record TestInstruction : ModeratorInstruction
    {
        public TestInstruction(string? publicAnnouncement = null, string? privateInstruction = null)
            : base(publicAnnouncement: publicAnnouncement, privateInstruction: privateInstruction)
        {
        }
    }
}
