using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.Tests.Helpers;

public static class CoreTestReferences
{
	public static class InstructionContexts
	{
		public static string NightStartConfirmation => nameof(NightStartConfirmation);
		public static string NightEndConfirmation => nameof(NightEndConfirmation);
		public static string WerewolfWakeIdentification => nameof(WerewolfWakeIdentification);
		public static string WerewolfWakeConfirmation => nameof(WerewolfWakeConfirmation);
		public static string WerewolfIdentification => nameof(WerewolfIdentification);
		public static string WerewolfIdentificationInstruction => nameof(WerewolfIdentificationInstruction);
		public static string WerewolfVictimSelection => nameof(WerewolfVictimSelection);
		public static string WerewolfVictimSelectionNightTwo => nameof(WerewolfVictimSelectionNightTwo);
		public static string WerewolfSleepConfirmation => nameof(WerewolfSleepConfirmation);
		public static string SeerIdentification => nameof(SeerIdentification);
		public static string SeerTargetSelection => nameof(SeerTargetSelection);
		public static string SeerTargetSelectionNightTwo => nameof(SeerTargetSelectionNightTwo);
		public static string SeerResultConfirmation => nameof(SeerResultConfirmation);
		public static string SeerFeedbackInstruction => nameof(SeerFeedbackInstruction);
		public static string SeerSleepConfirmation => nameof(SeerSleepConfirmation);
		public static string DebateConfirmation => nameof(DebateConfirmation);
		public static string DebateConfirmationInstruction => nameof(DebateConfirmationInstruction);
		public static string VotingInstruction => nameof(VotingInstruction);
		public static string VotingSelectionInstruction => nameof(VotingSelectionInstruction);
		public static string VoteSelection => nameof(VoteSelection);
		public static string DeathAnnouncementConfirmation => nameof(DeathAnnouncementConfirmation);
		public static string VillagerRoleAssignment => nameof(VillagerRoleAssignment);
		public static string DeathConfirmation => nameof(DeathConfirmation);
		public static string RoleAssignmentForEliminatedVictim => nameof(RoleAssignmentForEliminatedVictim);
		public static string RoleAssignmentAfterLynch => nameof(RoleAssignmentAfterLynch);
		public static string WildChildIdentification => nameof(WildChildIdentification);
		public static string WildChildModelSelection => nameof(WildChildModelSelection);
		public static string WildChildSleepConfirmation => nameof(WildChildSleepConfirmation);
		public static string RoleRevealForEliminatedModel => nameof(RoleRevealForEliminatedModel);
	}

	public static class AssertionReasons
	{
		public static string WerewolvesNeedValidTarget => nameof(WerewolvesNeedValidTarget);
		public static string WerewolvesCannotTargetWerewolves => nameof(WerewolvesCannotTargetWerewolves);
		public static string VictimSelectionRequiresVictim => nameof(VictimSelectionRequiresVictim);
		public static string VictimIncludedInRoleAssignment => nameof(VictimIncludedInRoleAssignment);
		public static string TieVotesAllowNoSelection => nameof(TieVotesAllowNoSelection);
		public static string SinglePlayerCanBeLynched => nameof(SinglePlayerCanBeLynched);
		public static string TieVoteDoesNotEliminatePlayer => nameof(TieVoteDoesNotEliminatePlayer);
		public static string TieVoteLoggedWithEmptyPlayerId => nameof(TieVoteLoggedWithEmptyPlayerId);
		public static string DeadPlayerInvalidVoteTarget => nameof(DeadPlayerInvalidVoteTarget);
		public static string RoleAssignmentsLogged => nameof(RoleAssignmentsLogged);
		public static string NightActionsLogged => nameof(NightActionsLogged);
		public static string NightOneActionsUseTurnOne => nameof(NightOneActionsUseTurnOne);
		public static string NightActionsRecordedWithNightPhase => nameof(NightActionsRecordedWithNightPhase);
		public static string TurnNumberIncrementsAfterDayToNight => nameof(TurnNumberIncrementsAfterDayToNight);
		public static string DawnSubPhaseStageClearedAfterDayTransition => nameof(DawnSubPhaseStageClearedAfterDayTransition);
		public static string NightPhaseHasActiveInstruction => nameof(NightPhaseHasActiveInstruction);
		public static string GameHasPendingInstructionAfterStart => nameof(GameHasPendingInstructionAfterStart);
		public static string SeerCannotCheckSelf => nameof(SeerCannotCheckSelf);
		public static string NightOneKilledVillagerNotSelectable => nameof(NightOneKilledVillagerNotSelectable);
		public static string DayOneLynchedVillagerNotSelectable => nameof(DayOneLynchedVillagerNotSelectable);
		public static string LivingWerewolfSelectable => nameof(LivingWerewolfSelectable);
		public static string LivingVillagerSelectable => nameof(LivingVillagerSelectable);
		public static string SeerTargetedNightOneStillWakes => nameof(SeerTargetedNightOneStillWakes);
		public static string SeerActedDespiteBeingTargeted => nameof(SeerActedDespiteBeingTargeted);
		public static string DeadSeerSkippedAtNightEnd => nameof(DeadSeerSkippedAtNightEnd);
		public static string SeerActedOnlyBeforeDeath => nameof(SeerActedOnlyBeforeDeath);
		public static string RoleNotYetIdentified => nameof(RoleNotYetIdentified);
		public static string NightOneKilledVillagerNotWerewolfTarget => nameof(NightOneKilledVillagerNotWerewolfTarget);
		public static string DayOneLynchedVillagerNotWerewolfTarget => nameof(DayOneLynchedVillagerNotWerewolfTarget);
		public static string LivingSeerSelectable => nameof(LivingSeerSelectable);
		public static string WerewolfCannotTargetSelf => nameof(WerewolfCannotTargetSelf);
		public static string PublicAnnouncementMentionsRoleName => nameof(PublicAnnouncementMentionsRoleName);
		public static string FirstNightWakeUpIncludesPrivateIdentificationPrompt => nameof(FirstNightWakeUpIncludesPrivateIdentificationPrompt);
		public static string PrivateInstructionIdentifiesRole => nameof(PrivateInstructionIdentifiesRole);
		public static string SerializedSessionValidJson => nameof(SerializedSessionValidJson);
		public static string FreshSessionsStartWithoutActiveStatusEffects => nameof(FreshSessionsStartWithoutActiveStatusEffects);

		public static string PlayerPreserved(string playerName) =>
			WithPlayer(nameof(PlayerPreserved), playerName);

		public static string PlayerRoleShouldMatch(string playerName) =>
			WithPlayer(nameof(PlayerRoleShouldMatch), playerName);

		public static string PlayerHealthShouldMatch(string playerName) =>
			WithPlayer(nameof(PlayerHealthShouldMatch), playerName);

		public static string PlayerStatusEffectsShouldMatch(string playerName) =>
			WithPlayer(nameof(PlayerStatusEffectsShouldMatch), playerName);

		public static string PlayerHealthFromReplayMatchesCachedState(string playerName) =>
			WithPlayer(nameof(PlayerHealthFromReplayMatchesCachedState), playerName);

		public static string PlayerRoleFromReplayMatchesCachedState(string playerName) =>
			WithPlayer(nameof(PlayerRoleFromReplayMatchesCachedState), playerName);

		public static string PlayerRoleMismatch(string playerName) =>
			WithPlayer(nameof(PlayerRoleMismatch), playerName);

		public static string PlayerHealthMismatch(string playerName) =>
			WithPlayer(nameof(PlayerHealthMismatch), playerName);

		public static string PlayerStatusEffectsMismatch(string playerName) =>
			WithPlayer(nameof(PlayerStatusEffectsMismatch), playerName);

		public static string GetRoleGroupHandlesRole(MainRoleType role) =>
			$"{nameof(GetRoleGroupHandlesRole)}: {role}";

		private static string WithPlayer(string reason, string playerName) =>
			$"{reason}: {playerName}";
	}

	public static class DiagnosticLogLabels
	{
		public static string Phase => nameof(Phase);
	}

	public static class ExceptionMessages
	{
		public static string MinimumPlayersRequired(int minimum) =>
			$"Minimum {minimum} players required";

		public static string PlayerCountMustMatchRoleCount(int playerCount, int roleCount) =>
			$"Player count ({playerCount}) must match role count ({roleCount})";

		public static string LastInstructionNotStartGameConfirmation =>
			"Last instruction is not a StartGameConfirmationInstruction";

		public static string SeerTargetRequiredWithSeer =>
			"seerTargetId must be provided when seerId is specified";

		public static string UnexpectedSelectPlayersDuringDawnPhase(string? privateInstruction) =>
			$"Unexpected SelectPlayersInstruction during dawn phase. " +
			$"Dawn hooks requiring player selection are not handled by CompleteDawnPhase(). " +
			$"Instruction: {privateInstruction}";

		public static string NoCurrentInstructionDuringDawnPhase =>
			"No current instruction available during dawn phase processing.";

		public static string ObservedRoleAssignmentsRequired =>
			"Role-reveal helpers require a complete physically observed Role mapping.";

		public static string UnexpectedInstructionTypeDuringDawnPhase(string instructionType) =>
			$"Unexpected instruction type during dawn phase: {instructionType}";

		public static string NoCurrentInstructionDuringDayPhase =>
			"No current instruction available during day phase processing.";

		public static string UnexpectedInstructionTypeDuringDayPhase(string instructionType) =>
			$"Unexpected instruction type during day phase: {instructionType}";

		public static string GameMustBeStartedFirst =>
			"Game must be started first. Call StartGame().";

		public static string ExpectedInstructionReceivedNull(string expectedType) =>
			$"Expected instruction of type {expectedType}, but received null.";

		public static string ExpectedInstructionReceivedType(string expectedType, string actualType) =>
			$"Expected instruction of type {expectedType}, but received {actualType}.";

		public static string ExpectedSuccessfulProcessResult =>
			"Expected successful ProcessResult, but IsSuccess was false.";

		public static string WithContext(string context, string message) =>
			$"{context}: {message}";
	}

	public static class ExceptionPatterns
	{
		public static string MinimumSelectionCount(int required, int provided) =>
			Wildcard(nameof(Minimum), required.ToString(), Required, provided.ToString(), Provided);

		public static string MaximumSelectionCount(int allowed, int provided) =>
			Wildcard(nameof(Maximum), allowed.ToString(), Allowed, provided.ToString(), Provided);

		public static string InvalidSelectedPlayerIds =>
			Wildcard(nameof(Selected), Player, nameof(IDs), Not, Valid);

		public static string UnsupportedRole(MainRoleType role) =>
			Wildcard(Unsupported, Role, role.ToString());

		public static string RoleNotAssignable(MainRoleType role) =>
			Wildcard(role.ToString(), Not, Assignable, Roles);

		public static string PlayerCannotBeAssigned(Guid playerId) =>
			Wildcard(playerId.ToString(), Not, Assigned, Roles);

		private static string Wildcard(params string[] fragments) =>
			$"*{string.Join('*', fragments)}*";

		private static string Minimum => nameof(Minimum);
		private static string Maximum => nameof(Maximum);
		private static string Required => nameof(Required).ToLowerInvariant();
		private static string Allowed => nameof(Allowed).ToLowerInvariant();
		private static string Provided => nameof(Provided).ToLowerInvariant();
		private static string Selected => nameof(Selected);
		private static string Player => nameof(Player).ToLowerInvariant();
		private static string IDs => nameof(IDs);
		private static string Not => nameof(Not).ToLowerInvariant();
		private static string Valid => nameof(Valid).ToLowerInvariant();
		private static string Unsupported => nameof(Unsupported).ToLowerInvariant();
		private static string Role => nameof(Role);
		private static string Assignable => nameof(Assignable).ToLowerInvariant();
		private static string Roles => nameof(Roles).ToLowerInvariant();
		private static string Assigned => nameof(Assigned).ToLowerInvariant();
	}
}
