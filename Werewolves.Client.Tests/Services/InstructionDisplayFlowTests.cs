using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Resources;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class InstructionDisplayFlowTests
{
    [Fact]
    public void TransitionKey_ChangesWhenInstructionChanges()
    {
        var first = CreateInstruction(GameStrings.GameStartPrompt);
        var second = CreateInstruction(GameStrings.NightStartsPrompt);
        var flow = new InstructionDisplayFlow(first);
        var firstKey = flow.TransitionKey;

        flow.SetInstruction(second);

        flow.TransitionKey.Should().NotBe(firstKey,
            ClientTestReferences.AssertionReasons.TransitionKeyChangesBetweenInstructions);
    }

    [Fact]
    public void TransitionKey_ChangesWhenAdvancingFromPublicToPrivate()
    {
        var instruction = CreateTwoPartInstruction(GameStrings.NightStartsPrompt, GameStrings.WerewolvesChooseVictimPrompt);
        var flow = new InstructionDisplayFlow(instruction);
        var publicKey = flow.TransitionKey;

        flow.Advance();

        flow.TransitionKey.Should().NotBe(publicKey,
            ClientTestReferences.AssertionReasons.TransitionKeyChangesOnPublicReveal);
    }

    [Fact]
    public void TransitionKey_StaysStableWithoutStateChange()
    {
        var instruction = CreateTwoPartInstruction(GameStrings.NightStartsPrompt, GameStrings.WerewolvesChooseVictimPrompt);
        var flow = new InstructionDisplayFlow(instruction);
        var firstRead = flow.TransitionKey;
        var secondRead = flow.TransitionKey;

        firstRead.Should().Be(secondRead,
            ClientTestReferences.AssertionReasons.TransitionKeyStableWithoutStateChange);
    }

    [Fact]
    public void TransitionKey_IsNullWhenNoInstruction()
    {
        var flow = new InstructionDisplayFlow();

        flow.TransitionKey.Should().BeNull(
            ClientTestReferences.AssertionReasons.TransitionKeyNullWithoutInstruction);
    }

    [Fact]
    public void SetInstruction_ResetsShowingInputToFalse()
    {
        var instruction = CreateTwoPartInstruction(GameStrings.NightStartsPrompt, GameStrings.ConfirmNightStarted);
        var flow = new InstructionDisplayFlow(instruction);
        flow.Advance();
        flow.IsShowingInput.Should().BeTrue();

        var next = CreateTwoPartInstruction(GameStrings.DebateStartsPrompt, GameStrings.DebateModeratorInstructions);
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
