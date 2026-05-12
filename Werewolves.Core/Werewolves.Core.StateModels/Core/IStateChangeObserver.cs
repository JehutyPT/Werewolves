using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Core;

/// <summary>
/// Optional observer for state change diagnostics.
/// Used by tests to capture intermediate state mutations.
/// </summary>
internal interface IStateChangeObserver
{
    void OnMainPhaseChanged(GamePhase newPhase);
    void OnSubPhaseChanged(string? newSubPhase);
    void OnSubPhaseStageChanged(string? newSubPhaseStage);
    void OnListenerChanged(ListenerIdentifier? listener, string? listenerState);
    void OnTurnNumberChanged(int newTurnNumber);
    void OnPendingInstructionChanged(ModeratorInstruction? instruction);
    void OnLogEntryApplied(GameLogEntryBase entry);
}
