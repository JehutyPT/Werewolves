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
    ResolveEliminationCascade
}

internal enum DaySubPhaseStage
{
	Debate,
	RequestVote,
	HandleVoteResponse,
    ResolveEliminationCascade,
    VoteOutcomeNavigation,
    ExpireScapegoatVoterRestriction
}
