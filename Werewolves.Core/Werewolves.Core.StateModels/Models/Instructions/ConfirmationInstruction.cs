using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models.Simulation;
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
		: this(
			ModeratorInstructionSemantic.Unspecified,
			publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			instructionId)
    {
    }

	internal ConfirmationInstruction(
		ModeratorInstructionSemantic semantic,
		string? publicAnnouncement = null,
		string? privateInstruction = null,
		IReadOnlyList<Guid>? affectedPlayerIds = null,
		Guid instructionId = default)
		: base(
			publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			instructionId: instructionId,
			semantic: semantic)
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
        : base(
			ModeratorInstructionSemantic.StartGame,
			GameStrings.GameStartPrompt,
			instructionId: instructionId)
    {
        this.GameGuid = GameGuid;
    }

    public void Deconstruct(out Guid GameGuid) => GameGuid = this.GameGuid;
}

public record FinishedGameConfirmationInstruction : ModeratorInstruction
{
    public GameResult GameResult { get; }
    public VictoryCheckWindow VictoryCheckWindow { get; }

    public FinishedGameConfirmationInstruction(
        GameResult gameResult,
        VictoryCheckWindow victoryCheckWindow)
        : this(gameResult, victoryCheckWindow, instructionId: default)
    {
    }

    [JsonConstructor]
    internal FinishedGameConfirmationInstruction(
        GameResult gameResult,
        VictoryCheckWindow victoryCheckWindow,
        Guid instructionId)
        : base(
            publicAnnouncement: GameStrings.GameOverMessage.Format(
                Describe(gameResult)),
            instructionId: instructionId,
            semantic: ModeratorInstructionSemantic.FinishedGame)
    {
        ArgumentNullException.ThrowIfNull(gameResult);
        if (!Enum.IsDefined(victoryCheckWindow))
        {
            throw new ArgumentOutOfRangeException(nameof(victoryCheckWindow));
        }

        GameResult = gameResult;
        VictoryCheckWindow = victoryCheckWindow;
    }

    private static string Describe(GameResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            SingleFactionGameResult { Faction: Faction.Villager } =>
                GameStrings.VictoryConditionAllWerewolvesEliminated,
            SingleFactionGameResult { Faction: Faction.Werewolf } =>
                GameStrings.VictoryConditionWerewolvesOutnumber,
            SharedVictoryGameResult => GameStrings.VictoryConditionShared,
            NoWinnerGameResult => GameStrings.VictoryConditionNoWinner,
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
