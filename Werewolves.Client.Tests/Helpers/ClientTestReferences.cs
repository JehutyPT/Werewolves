namespace Werewolves.Client.Tests.Helpers;

public static class ClientTestReferences
{
	public static class FixtureLabels
	{
		public const string CollapsedInstructionPreviewSuffix = " ...";
		public static string UnexpectedRoleGroupDisplayName => nameof(UnexpectedRoleGroupDisplayName);

		public static string RenderedInstructionStep(int step, string instructionType) =>
			$"{nameof(RenderedInstructionStep)}: step={step}; instruction={instructionType}";
	}

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
		public static string AppCssDefinesInstructionEnterKeyframes => nameof(AppCssDefinesInstructionEnterKeyframes);
		public static string InstructionBlockReferencesEnterAnimation => nameof(InstructionBlockReferencesEnterAnimation);
		public static string InstructionBlockUsesAnimationDurationToken => nameof(InstructionBlockUsesAnimationDurationToken);
		public static string InstructionRendererUsesTransitionKey => nameof(InstructionRendererUsesTransitionKey);
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

	public static class ExceptionMessages
	{
		public const string MissingActiveGameSession =
			"Cannot process moderator response without an active game session.";

		public const string RepositoryRootNotFound =
			"Could not locate the repository root from the test output directory.";

		public const string SyntheticHapticFailure =
			"Synthetic haptic failure.";

		public const string PlatformWakeLockUnavailable =
			"Platform wake lock unavailable.";

		public const string SaveFailed =
			"Save failed.";

		public static string ComponentViewNotFound(string viewName) =>
			$"{viewName} could not be found from the test output directory.";

		public static string TestFileNotFound(string path) =>
			$"{path} could not be found from the test output directory.";

		public static string ComponentRenderOrDispatchFailure(string componentName) =>
			$"Unhandled exception during {componentName} rendering or event dispatch.";

		public static string ComponentRenderFailure(string componentName) =>
			$"The {componentName} component failed while rendering.";

		public static string UnexpectedInstructionWhileReachingVictory(string? instructionType) =>
			$"Unexpected instruction while reaching victory: {instructionType ?? NullInstruction}.";

		public static string UnexpectedInstruction(string? instructionType) =>
			$"Unexpected instruction: {instructionType ?? NullInstruction}";

		public static string UnexpectedInstructionWhileAdvancingToDebate(string? instructionType) =>
			$"Unexpected instruction while advancing to debate: {instructionType ?? NullInstruction}";

		public static string VictoryNotReached =>
			"Victory was not reached within the expected number of inputs.";

		public static string GameEndedBeforeNextDebate =>
			"Game ended before reaching next debate.";

		public static string NextDebateNotReached =>
			"Next debate instruction was not reached within the expected number of inputs.";

		public static string DebateNotReached =>
			"Debate instruction was not reached within the expected number of inputs.";

		public static string ClientStringAccessorReturnedNonString(string key) =>
			$"ClientStrings.{key} did not return a string.";

		private static string NullInstruction => "null";
	}

	public static class ExceptionPatterns
	{
		public const string MissingActiveGameSession = ExceptionMessages.MissingActiveGameSession;
	}

	public static class Paths
	{
		private const string SolutionFileName = "Werewolves.sln";
		private const string ClientSharedProjectDirectory = "Werewolves.Client.Shared";
		private const string ClientProjectDirectory = "Werewolves.Client";

		public static string RepositoryRoot
		{
			get
			{
				var directory = new DirectoryInfo(AppContext.BaseDirectory);

				while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
				{
					directory = directory.Parent;
				}

				return directory?.FullName
					?? throw new InvalidOperationException(ExceptionMessages.RepositoryRootNotFound);
			}
		}

		public static string RepositoryPath(params string[] relativeSegments)
		{
			var segments = new string[relativeSegments.Length + 1];
			segments[0] = RepositoryRoot;
			Array.Copy(relativeSegments, 0, segments, 1, relativeSegments.Length);

			return Path.Combine(segments);
		}

		public static string ClientPath(params string[] relativeSegments) =>
			RepositoryPathWithProject(ClientProjectDirectory, relativeSegments);

		public static string SharedPath(params string[] relativeSegments) =>
			RepositoryPathWithProject(ClientSharedProjectDirectory, relativeSegments);

		private static string RepositoryPathWithProject(string projectDirectory, string[] relativeSegments)
		{
			var segments = new string[relativeSegments.Length + 2];
			segments[0] = RepositoryRoot;
			segments[1] = projectDirectory;
			Array.Copy(relativeSegments, 0, segments, 2, relativeSegments.Length);

			return Path.Combine(segments);
		}
	}
}
