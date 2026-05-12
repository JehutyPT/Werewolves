namespace Werewolves.Core.StateModels.Enums;

/// *****************************************************************
/// ********************* CRITICAL INFO *****************************
/// 
/// Each sub-phase must be idempotent, meaning that re-entering the same sub-phase
/// multiple times should not cause unintended side effects.
/// 
/// For example, do not have a sub-phase that starts by modifying some game state
/// and afterwards firing a hook to notify players. If the sub-phase is re-entered,
/// the game state would be modified again, leading to inconsistent behavior.
///
/// We're currently enforcing this by ensuring that each sub-phase consists of discrete stages,
/// that can only be executed once per sub-phase entry, and forcibly move to either another stage,
/// sub-phase, or main-phase.
/// *****************************************************************


/// <summary>
/// Internal sub-phases for the consolidated Night phase.
/// </summary>
public enum NightSubPhases
{
    Start,            // handles the entire night phase
}

/// <summary>
/// Internal sub-phases for the consolidated Day_Dawn phase.
/// </summary>
public enum DawnSubPhases
{
    CalculateVictims,   // Process night actions to determine final victims
    AnnounceVictims,    // Moderator announces all victims from the night
    ProcessRoleReveals, // Reveal roles for each eliminated player
    Finalize           // Complete dawn processing and transition to debate
}

/// <summary>
/// Internal sub-phases for Day_Debate phase.
/// </summary>
public enum DaySubPhases
{
    Debate,            // Moderator confirms debate is complete
    DetermineVoteType,  // Determine type of vote to be held
    NormalVoting,
    AccusationVoting,
    FriendVoting,
    HandleNonTieVote,
    ProcessVoteOutcome,
    ProcessVoteDeathLoop,
    Finalize
}

public enum VictorySubPhases
{
    Complete
}
