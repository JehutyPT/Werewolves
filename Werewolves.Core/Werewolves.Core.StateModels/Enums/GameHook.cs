namespace Werewolves.Core.StateModels.Enums;

/// <summary>
/// Represents specific moments in the game flow where listeners can be triggered.
/// Updated per Revision 2.1: Night hooks simplification.
/// </summary>
public enum GameHook
{
    // Core phase hooks
	NightMainActionLoop,                    // Main hook for iterating through night role actions
    
	    // Player lifecycle hooks
	    OnVoteConducted,                  // Fired after the physical Vote and before its outcome is recorded
		OnVoteConcluded,                  // Fired when any day vote concludes
    
    
    // Day phase hooks
    DawnMainActionLoop,                    // Fired at each dawn after any potential victims are fully dealt with

}
