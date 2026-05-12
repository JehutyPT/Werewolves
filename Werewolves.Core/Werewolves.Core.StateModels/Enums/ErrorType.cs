namespace Werewolves.Core.StateModels.Enums;

/// <summary>
/// Defines high-level categories of game errors.
/// Based on Architecture doc.
/// </summary>
public enum ErrorType
{
    Unknown, // Should not happen - indicates an internal error
    GameNotFound, // Specified GameSession ID not found
    InvalidInput, // Input data format/type/basic validity incorrect
    RuleViolation, // Input violates game rules based on current state
    InvalidOperation // Operation not valid in current game phase/state
} 