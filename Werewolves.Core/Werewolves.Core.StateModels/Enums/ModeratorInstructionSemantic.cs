namespace Werewolves.Core.StateModels.Enums;

/// <summary>
/// Machine-stable gameplay meaning of a Moderator Instruction.
/// </summary>
public enum ModeratorInstructionSemantic
{
	Unspecified = 0,
	StartGame = 1,
	FinishedGame = 2,
	StartNight = 3,
	FinishNightActions = 4,
	WakeRole = 5,
	IdentifyRoleHolders = 6,
	PutRoleToSleep = 7,
	SelectWerewolfVictim = 8,
	SelectSeerTarget = 9,
	RevealSeerResult = 10,
	SelectWildChildModel = 11,
	AnnounceDawnVictims = 12,
	AssignDawnVictimRoles = 13,
	StartDayDebate = 14,
	RecordDayVote = 15,
	AssignDayVoteTargetRole = 16,
	AnnounceLynchingImmunity = 17,
	AnnounceDayElimination = 18,
	GameSessionNotFound = 19,
	ObserveVillagerVillagerFromDeal = 20,
	RecognizeRoleHolders = 21,
	CommunicateAsRoleHolders = 22,
	SelectWitchHealingTarget = 23,
	SelectWitchPoisonTarget = 24,
	AnnounceEliminationCascadeVictims = 25,
	AssignEliminationCascadeRoles = 26,
	SelectHunterFinalShotTarget = 27,
	EstablishStutteringJudgeSignal = 28,
	ObserveStutteringJudgeSignal = 29,
	ConductDayVote = 30
}
