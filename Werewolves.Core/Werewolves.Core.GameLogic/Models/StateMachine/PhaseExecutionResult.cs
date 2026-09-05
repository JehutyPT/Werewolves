using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Models.StateMachine;

/// <summary>Stops phase interpretation at an instruction or a Main Phase exit.</summary>
internal abstract record PhaseExecutionResult
{
    private PhaseExecutionResult() { }

    internal sealed record InstructionReady : PhaseExecutionResult
    {
        internal InstructionReady(ModeratorInstruction instruction)
        {
            ArgumentNullException.ThrowIfNull(instruction);
            Instruction = instruction;
        }

        internal ModeratorInstruction Instruction { get; }
    }

    internal sealed record PhaseExited : PhaseExecutionResult
    {
        internal PhaseExited(
            GamePhase previousPhase,
            GamePhase currentPhase,
            ModeratorInstruction? transitionInstruction)
        {
            if (previousPhase == currentPhase)
            {
                throw new InvalidOperationException("A phase exit requires a Main Phase change.");
            }

            PreviousPhase = previousPhase;
            CurrentPhase = currentPhase;
            TransitionInstruction = transitionInstruction;
        }

        internal GamePhase PreviousPhase { get; }
        internal GamePhase CurrentPhase { get; }
        internal ModeratorInstruction? TransitionInstruction { get; }
    }
}
