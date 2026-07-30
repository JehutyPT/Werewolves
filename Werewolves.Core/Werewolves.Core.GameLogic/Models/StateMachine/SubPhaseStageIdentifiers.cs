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
    ResolveEliminationCascade,
    EnsureVictoryFactsReady
}

internal enum DaySubPhaseStage
{
	Debate,
	RequestVote,
	HandleVoteResponse,
	ResolveEliminationCascade,
	VoteOutcomeNavigation,
	ExpireVoterEligibilityRestriction,
	EnsureVictoryFactsReady
}
