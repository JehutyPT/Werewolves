using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.StateModels.Models.Instructions;

/// <summary>
/// Instruction that requires a one-way Continue acknowledgment from the moderator.
/// </summary>
public record ConfirmationInstruction : ModeratorInstruction
{
    /// <summary>
    /// Initializes a new instance of ConfirmationInstruction.
    /// </summary>
    /// <param name="publicAnnouncement">The text to be read aloud to players.</param>
    /// <param name="privateInstruction">Private guidance for the moderator.</param>
    /// <param name="affectedPlayerIds">Optional list of affected player IDs for context.</param>
    [JsonConstructor]
    internal ConfirmationInstruction(
        string? publicAnnouncement = null,
        string? privateInstruction = null,
        IReadOnlyList<Guid>? affectedPlayerIds = null,
        Guid instructionId = default)
        : base(
            publicAnnouncement,
            privateInstruction,
            affectedPlayerIds,
            instructionId: instructionId)
    {
    }

    /// <summary>
    /// Creates a one-way Continue acknowledgment.
    /// </summary>
    /// <returns>A validated ModeratorResponse.</returns>
    public virtual ModeratorResponse CreateResponse()
    {
        return new ModeratorResponse
        {
            InstructionId = InstructionId,
            Type = ExpectedInputType.Continue
        };
    }
}

public record StartGameConfirmationInstruction : ConfirmationInstruction
{
    public Guid GameGuid { get; }

    public StartGameConfirmationInstruction(Guid GameGuid)
        : this(GameGuid, instructionId: default)
    {
    }

    [JsonConstructor]
    internal StartGameConfirmationInstruction(Guid GameGuid, Guid instructionId)
        : base(GameStrings.GameStartPrompt, instructionId: instructionId)
    {
        this.GameGuid = GameGuid;
    }

    public void Deconstruct(out Guid GameGuid) => GameGuid = this.GameGuid;
}

public record FinishedGameConfirmationInstruction : ConfirmationInstruction
{
    public string VictoryDescription { get; }

    public FinishedGameConfirmationInstruction(string VictoryDescription)
        : this(VictoryDescription, instructionId: default)
    {
    }

    [JsonConstructor]
    internal FinishedGameConfirmationInstruction(string VictoryDescription, Guid instructionId)
        : base(
            GameStrings.GameOverMessage.Format(VictoryDescription),
            instructionId: instructionId)
    {
        this.VictoryDescription = VictoryDescription;
    }

    public void Deconstruct(out string VictoryDescription) =>
        VictoryDescription = this.VictoryDescription;
}
