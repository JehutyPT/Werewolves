using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Models.InternalMessages;

/// <summary>
/// Abstract base record representing the outcome of a phase handler's execution.
/// </summary>
internal abstract record PhaseHandlerResult(ModeratorInstruction? ModeratorInstruction);

/// <summary>
/// This only exists to group MajorNavigationPhaseHandlerResults: MainPhaseHandlerResult and SubPhaseHandlerResult.
/// </summary>
/// <param name="ModeratorInstruction"></param>
internal abstract record MajorNavigationPhaseHandlerResult(ModeratorInstruction? ModeratorInstruction)
    : PhaseHandlerResult(ModeratorInstruction);

/// <summary>
/// For transitioning between main phases (e.g., Night -> Day_Dawn).
/// </summary>
internal sealed record MainPhaseHandlerResult(
    ModeratorInstruction? ModeratorInstruction, 
    GamePhase MainPhase) : MajorNavigationPhaseHandlerResult(ModeratorInstruction)
{
    /// <summary>
    /// Creates a main phase transition result.
    /// </summary>
    public static MainPhaseHandlerResult TransitionPhase(ModeratorInstruction nextInstruction, GamePhase mainPhase) =>
        new(nextInstruction, mainPhase);
}

/// <summary>
/// For transitioning between sub-phases (e.g., Night.Start -> Night.ActionLoop).
/// </summary>
internal sealed record SubPhaseHandlerResult(
    ModeratorInstruction? ModeratorInstruction, 
    Enum SubGamePhase) : MajorNavigationPhaseHandlerResult(ModeratorInstruction)
{
    /// <summary>
    /// Creates a sub-phase transition result.
    /// </summary>
    public static SubPhaseHandlerResult TransitionSubPhase(ModeratorInstruction nextInstruction, Enum subGamePhase) =>
        new(nextInstruction, subGamePhase);

    public static SubPhaseHandlerResult TransitionSubPhaseSilent(Enum subGamePhase) =>
        new(null, subGamePhase);
}

/// <summary>
/// For remaining in the current sub-phase (e.g., while awaiting hook listener input).
/// If ModeratorInstruction is null, no action is required by the moderator.
/// So next sub-phase stage execution will proceed automatically.
/// </summary>
/// <param name="ModeratorInstruction">The instruction to send to the moderator, if any.</param>
/// <param name="StageComplete">
/// If true, the current sub-phase stage is marked complete and won't be re-entered.
/// If false, the stage remains active and will continue execution on the next input.
/// This distinction is critical for hook stages that pause mid-execution waiting for listener input.
/// </param>
internal sealed record StayInSubPhaseHandlerResult(
    ModeratorInstruction? ModeratorInstruction,
    bool StageComplete) : PhaseHandlerResult(ModeratorInstruction)
{
	/// <summary>
	/// Creates a stay-in-sub-phase result that marks the current stage as complete.
	/// Use this when the stage has finished its work and the next stage should execute.
	/// </summary>
	/// <param name="nextInstruction">The instruction for the moderator, or null to proceed silently.</param>
	/// <returns>A result indicating the stage is complete.</returns>
	public static StayInSubPhaseHandlerResult CompleteSubPhaseStage(ModeratorInstruction? nextInstruction) =>
        new(nextInstruction, StageComplete: true);

	/// <summary>
	/// Creates a stay-in-sub-phase result that keeps the current stage active.
	/// Use this when pausing mid-stage to await moderator input (e.g., during hook listener execution).
	/// The stage will be re-entered on the next input to continue where it left off.
	/// </summary>
	/// <param name="nextInstruction">The instruction for the moderator (required when pausing).</param>
	/// <returns>A result indicating the stage should remain active.</returns>
	public static StayInSubPhaseHandlerResult PauseSubPhaseStage(ModeratorInstruction nextInstruction) =>
        new(nextInstruction, StageComplete: false);
}


