using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.GameLogic.Models.StateMachine;

/// <summary>
/// Documents and defines a potential transition out of a game phase,
/// including the input expected upon arrival at the target phase.
/// </summary>
/// <param name="TargetPhase">The phase transitioned to.</param>
public record PhaseTransitionInfo(
    GamePhase TargetPhase
); 