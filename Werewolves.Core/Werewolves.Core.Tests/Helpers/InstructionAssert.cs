using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.Tests.Helpers;

/// <summary>
/// Helper methods for asserting and casting ModeratorInstruction types in tests.
/// </summary>
public static class InstructionAssert
{
    /// <summary>
    /// Asserts that the instruction is of the expected type and returns it cast to that type.
    /// Throws a descriptive exception if the type doesn't match.
    /// </summary>
    /// <typeparam name="TInstruction">The expected instruction subclass type.</typeparam>
    /// <param name="instruction">The instruction to check.</param>
    /// <param name="context">Optional context message for better error reporting.</param>
    /// <returns>The instruction cast to the expected type.</returns>
    /// <exception cref="InvalidOperationException">Thrown when instruction is null.</exception>
    /// <exception cref="AssertionException">Thrown when instruction type doesn't match.</exception>
    public static TInstruction ExpectType<TInstruction>(
        ModeratorInstruction? instruction,
        string? context = null)
        where TInstruction : ModeratorInstruction
    {
        if (instruction is null)
        {
            var message = $"Expected instruction of type {typeof(TInstruction).Name}, but received null.";
            if (context is not null)
                message = $"{context}: {message}";
            throw new InvalidOperationException(message);
        }

        if (instruction is TInstruction typed)
        {
            return typed;
        }

        var errorMessage = $"Expected instruction of type {typeof(TInstruction).Name}, but received {instruction.GetType().Name}.";
        if (context is not null)
            errorMessage = $"{context}: {errorMessage}";
        
        throw new AssertionException(errorMessage);
    }

    /// <summary>
    /// Asserts that the ProcessResult is successful and the instruction is of the expected type.
    /// Returns the instruction cast to that type.
    /// </summary>
    /// <typeparam name="TInstruction">The expected instruction subclass type.</typeparam>
    /// <param name="result">The process result to check.</param>
    /// <param name="context">Optional context message for better error reporting.</param>
    /// <returns>The instruction cast to the expected type.</returns>
    /// <exception cref="AssertionException">Thrown when result is not successful or type doesn't match.</exception>
    public static TInstruction ExpectSuccessWithType<TInstruction>(
        ProcessResult result,
        string? context = null)
        where TInstruction : ModeratorInstruction
    {
        if (!result.IsSuccess)
        {
            var message = "Expected successful ProcessResult, but IsSuccess was false.";
            if (context is not null)
                message = $"{context}: {message}";
            throw new AssertionException(message);
        }

        return ExpectType<TInstruction>(result.ModeratorInstruction, context);
    }

    /// <summary>
    /// Asserts that the instruction is of the expected type without returning it.
    /// Use when you only need to verify the type, not use the typed instruction.
    /// </summary>
    /// <typeparam name="TInstruction">The expected instruction subclass type.</typeparam>
    /// <param name="instruction">The instruction to check.</param>
    /// <param name="context">Optional context message for better error reporting.</param>
    /// <exception cref="InvalidOperationException">Thrown when instruction is null.</exception>
    /// <exception cref="AssertionException">Thrown when instruction type doesn't match.</exception>
    public static void AssertType<TInstruction>(
        ModeratorInstruction? instruction,
        string? context = null)
        where TInstruction : ModeratorInstruction
    {
        ExpectType<TInstruction>(instruction, context);
    }
}

/// <summary>
/// Exception thrown when a test assertion fails.
/// </summary>
public class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
    public AssertionException(string message, Exception innerException) : base(message, innerException) { }
}
