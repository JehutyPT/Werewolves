namespace Werewolves.Core.GameLogic.Models.StateMachine;

internal enum NightSubPhaseStage
{
    RequestVillagerVillagerPublicFromDealObservation,
    RecordVillagerVillagerPublicFromDealObservation,
    NightStart,
    NightEnd
}

internal enum DawnSubPhaseStage
{
    CheckForVictims,
    AnnounceVictimsAndRequestRoles,
    AssignVictimRoles
}

internal enum DaySubPhaseStage
{
    Debate,
    RequestVote,
    HandleVoteResponse,
    VerifyLynchingOcurred,
    VoteOutcomeNavigation
}
