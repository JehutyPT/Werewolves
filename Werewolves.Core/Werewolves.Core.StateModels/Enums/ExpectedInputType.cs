namespace Werewolves.Core.StateModels.Enums;

/// <summary>
/// Specifies the type of input expected from the moderator.
/// Based on Architecture doc list.
/// </summary>
public enum ExpectedInputType
{
    None,
    PlayerSelection,
    AssignPlayerRoles,
    OptionSelection,
    Confirmation,
	FinishedGame //special case where no input is expected because the game is over
} 