namespace Werewolves.Client.Tests.Helpers;

public static partial class ClientTestReferences
{
	public static class AssertionReasons
	{
		public static string TransitionKeyChangesBetweenInstructions => nameof(TransitionKeyChangesBetweenInstructions);
		public static string TransitionKeyChangesOnPublicReveal => nameof(TransitionKeyChangesOnPublicReveal);
		public static string TransitionKeyStableWithoutStateChange => nameof(TransitionKeyStableWithoutStateChange);
		public static string TransitionKeyNullWithoutInstruction => nameof(TransitionKeyNullWithoutInstruction);
		public static string ReturnedSelectionMutationDoesNotAffectState => nameof(ReturnedSelectionMutationDoesNotAffectState);
		public static string RosterContainsEntriesForRoleAssignmentPlayers => nameof(RosterContainsEntriesForRoleAssignmentPlayers);
		public static string NativeChecklistExists => nameof(NativeChecklistExists);
		public static string TestProjectsUseProductionLocalizationContracts => nameof(TestProjectsUseProductionLocalizationContracts);
		public static string AndroidVibratePermissionSupportsHaptics => nameof(AndroidVibratePermissionSupportsHaptics);
		public static string DesignTokensDefineInstructionAnimation => nameof(DesignTokensDefineInstructionAnimation);
		public static string InstructionAnimationDurationMatchesNightTempo => nameof(InstructionAnimationDurationMatchesNightTempo);
		public static string StartGameConfirmationShownDirectly => nameof(StartGameConfirmationShownDirectly);
		public static string ConfirmationDoesNotSubmitFromInstantClick => nameof(ConfirmationDoesNotSubmitFromInstantClick);
		public static string ConfirmationUsesPressAndHoldGate => nameof(ConfirmationUsesPressAndHoldGate);
		public static string HoldingConfirmAdvancesInstruction => nameof(HoldingConfirmAdvancesInstruction);
		public static string HapticFailureDoesNotBlockProgression => nameof(HapticFailureDoesNotBlockProgression);
		public static string DashboardEventsRemainDispatchableAfterGameAction => nameof(DashboardEventsRemainDispatchableAfterGameAction);
		public static string GameAdvancedThroughMultipleInstructions => nameof(GameAdvancedThroughMultipleInstructions);
		public static string RosterTabRendered => nameof(RosterTabRendered);
		public static string RosterTabHasClickHandler => nameof(RosterTabHasClickHandler);
		public static string DashboardRendersButtons => nameof(DashboardRendersButtons);
		public static string AudioToggleExposedInStatusBar => nameof(AudioToggleExposedInStatusBar);
		public static string ZeroMillisecondLongPressPulseFiresImmediately => nameof(ZeroMillisecondLongPressPulseFiresImmediately);

		public static string StepRendersButtons(int step, string instructionType, string buttonLabels) =>
			$"{nameof(StepRendersButtons)}: step={step}; instruction={instructionType}; buttons=[{buttonLabels}]";

		public static string ButtonHasHandlersOrIsDisabled(string buttonText, string? className) =>
			$"{nameof(ButtonHasHandlersOrIsDisabled)}: text={buttonText}; class={className}";
	}
}
