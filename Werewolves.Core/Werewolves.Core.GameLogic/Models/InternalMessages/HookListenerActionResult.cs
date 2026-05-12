using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Models.InternalMessages;

/// <summary>
/// Represents the result of a listener's state machine advancement.
/// This communicates a precise, unambiguous outcome to the GFM dispatcher.
/// </summary>
internal sealed class HookListenerActionResult
{
    /// <summary>
    /// The outcome type (NeedInput, Complete, Error).
    /// </summary>
    public HookListenerOutcome Outcome { get; }
    public string? NextListenerPhase { get; private set; }
    
    /// <summary>
    /// Optional instruction for the moderator when Outcome is NeedInput or Complete.
    /// </summary>
    public ModeratorInstruction? Instruction { get; }
    

    private HookListenerActionResult(HookListenerOutcome outcome, string? nextListenerPhase, ModeratorInstruction? instruction = null)
    {
        Outcome = outcome;
        NextListenerPhase = nextListenerPhase;
        Instruction = instruction;
    }

    /// <summary>
    /// Creates a NeedInput result with the provided instruction.
    /// </summary>
    /// <param name="instruction">The instruction to show to the moderator.</param>
    /// <param name="nextListenerPhase"></param>
    /// <returns>A HookListenerActionResult indicating input is needed.</returns>
    public static HookListenerActionResult NeedInput<T>(ModeratorInstruction instruction, T nextListenerPhase) where T : struct, Enum
    {
        return new HookListenerActionResult(HookListenerOutcome.NeedInput, nextListenerPhase.ToString(), instruction: instruction);
    }

    /// <summary>
    /// Creates a Complete result with an optional instruction.
    /// </summary>
    /// <param name="nextListenerPhase"></param>
    /// <returns>A HookListenerActionResult indicating completion.</returns>
    public static HookListenerActionResult Complete<T>(T nextListenerPhase) where T : struct, Enum
    {
        return new HookListenerActionResult(HookListenerOutcome.Complete, nextListenerPhase.ToString());
    }

	/// <summary>
	/// Used when the listener should be skipped (i.e. no players alive with a given role)
	/// </summary>
	/// <returns></returns>
	public static HookListenerActionResult Skip()
    {
        return new HookListenerActionResult(HookListenerOutcome.Skip, null);
	}
}