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

	public static class Css
	{
		public const double MinimumTextContrastRatio = 4.5;

		public static class Animations
		{
			public const string InstructionEnterKeyframes = "@keyframes ww-instruction-enter";
			public const string InstructionEnterName = "ww-instruction-enter";
			public static string InstructionAnimationTokenReference => $"var({Tokens.InstructionAnimation})";
			public static string InstructionAnimationDurationPattern => $@"{Tokens.InstructionAnimation}:\s*(\d+)ms";
		}

		public static class Classes
		{
			public const string AudioToggle = "ww-audio-toggle";
			public const string AudioToggleMuted = "ww-audio-toggle--muted";
			public const string DashboardActionZone = "ww-dashboard-action-zone";
			public const string DashboardStatusBar = "ww-dashboard-status-bar";
			public const string DashboardTab = "ww-dashboard-tab";
			public const string DashboardTabActive = "ww-dashboard-tab--active";
			public const string DashboardTabsCompact = "ww-dashboard-tabs--compact";
			public const string Expanded = "is-expanded";
			public const string HoldButton = "ww-btn-hold";
			public const string HoldButtonEdge = "ww-btn-hold__edge";
			public const string HoldButtonFill = "ww-btn-hold__fill";
			public const string HoldButtonLabel = "ww-btn-hold__label";
			public const string HoldHint = "ww-hold-hint";
			public const string HoldZone = "ww-hold-zone";
			public const string Holding = "is-holding";
			public const string HoldComplete = "is-complete";
			public const string IconButton = "ww-icon-button";
			public const string InstructionAnnouncement = "ww-announcement";
			public const string InstructionBlockAnnouncement = "ww-instruction-block--announcement";
			public const string InstructionBlockPrivate = "ww-instruction-block--private";
			public const string InstructionPrivate = "ww-private-instruction";
			public const string LabsStatusBar = "ww-labs-status-bar";
			public const string OptionButtonSelected = "ww-option-btn--selected";
			public const string RoleButtonSelected = "ww-role-btn--selected";
			public const string SelectPlayersItemSelected = "ww-select-players-item--selected";
			public const string SelectPlayersList = "ww-select-players-list";
		}

		public static class ColorValues
		{
			public const string Accent = "#3FE0C8";
			public const string DarkBackground = "#070C12";
		}

		public static class Declarations
		{
			public const string DashboardActionPaddingFallback = "padding-bottom: var(--ww-dashboard-action-height, 88px)";
			public const string HoldEdgeProductionTransition = "transition: left 400ms linear, opacity 80ms ease-in;";
			public const string HoldEdgeSlowTransition = "transition: left 600ms linear, opacity 80ms ease-in;";
			public const string HoldFillProductionTransition = "transition: width 400ms linear;";
			public const string HoldFillSlowTransition = "transition: width 600ms linear;";
			public const string PositionFixed = "position: fixed";
			public const string WidthAuto = "width: auto";
			public const string WidthFull = "width: 100%";

			public static string DashboardPaddingBottom =>
				$"padding-bottom: calc(var({Tokens.DashboardActionHeight}) + 24px)";

			public static string DashboardPaddingTop =>
				$"padding-top: calc(var({Tokens.DashboardTabsHeight}) + var({Tokens.DashboardStatusHeight}) + 10px)";
		}

		public static class Selectors
		{
			public const string ProductionDashboard = @"\[data-production-dashboard\]";

			public static string DashboardActionZone => ClassSelector(Classes.DashboardActionZone);
			public static string LabsDashboardStatusBar => $"{ClassSelector(Classes.LabsStatusBar)}{ClassSelector(Classes.DashboardStatusBar)}";
			public static string ProductionDashboardCompactTabs => $@"{ProductionDashboard}\s+{ClassSelector(Classes.DashboardTabsCompact)}";
			public static string ProductionDashboardStatusBar => $@"{ProductionDashboard}\s+{ClassSelector(Classes.DashboardStatusBar)}";

			private static string ClassSelector(string className) => $@"\.{className}";
		}

		public static class Tokens
		{
			public const string Accent = "--ww-accent";
			public const string AccentBright = "--ww-accent-bright";
			public const string Background = "--ww-bg";
			public const string BackgroundRaised = "--ww-bg-raised";
			public const string DashboardActionHeight = "--ww-dashboard-action-height";
			public const string DashboardStatusHeight = "--ww-dashboard-status-height";
			public const string DashboardTabsHeight = "--ww-dashboard-tabs-height";
			public const string FactionAmbiguous = "--ww-faction-ambiguous";
			public const string FactionLoner = "--ww-faction-loner";
			public const string FactionVillager = "--ww-faction-villager";
			public const string FactionWerewolf = "--ww-faction-werewolf";
			public const string InstructionAnimation = "--ww-anim-instruction";
			public const string Surface = "--ww-surface";
			public const string SurfaceDeeper = "--ww-surface-deeper";
			public const string SurfaceHi = "--ww-surface-hi";
			public const string Text = "--ww-text";
			public const string TextDim = "--ww-text-dim";
			public const string TextMuted = "--ww-text-muted";

			public static IReadOnlyList<string> DarkSurfaces => new[]
			{
				Background,
				BackgroundRaised,
				Surface,
				SurfaceHi,
				SurfaceDeeper
			};

			public static IReadOnlyList<string> ReadableForegrounds => new[]
			{
				Text,
				TextDim,
				TextMuted,
				Accent,
				AccentBright,
				FactionWerewolf,
				FactionVillager,
				FactionLoner,
				FactionAmbiguous
			};
		}

		public static string RootDocumentDarkThemePattern =>
			$@"(?s)html,\s*body,\s*#app\s*\{{.*background:\s*var\({Tokens.Background}\).*color:\s*var\({Tokens.Text}\).*color-scheme:\s*dark";

		public const string PageDarkShellPattern = "<main\\s+class=\"ww-(?:app|dashboard)-shell\"";
	}

	public static class PlatformChrome
	{
		public static string AndroidAccentColor =>
			$"<color name=\"colorAccent\">{Css.ColorValues.Accent}</color>";

		public static string AndroidPrimaryColor =>
			$"<color name=\"colorPrimary\">{Css.ColorValues.DarkBackground}</color>";

		public static string AndroidPrimaryDarkColor =>
			$"<color name=\"colorPrimaryDark\">{Css.ColorValues.DarkBackground}</color>";

		public static string AppBackgroundColorResource =>
			$"<Color x:Key=\"WerewolvesBackground\">{Css.ColorValues.DarkBackground}</Color>";

		public const string AppDarkThemeAssignment =
			"UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Dark;";

		public const string MainPageBackgroundResource =
			"BackgroundColor=\"{StaticResource WerewolvesBackground}\"";

		public static string MauiIconDarkBackground =>
			$"MauiIcon Include=\"Resources\\AppIcon\\appicon.svg\" ForegroundFile=\"Resources\\AppIcon\\appiconfg.svg\" Color=\"{Css.ColorValues.DarkBackground}\"";

		public static string MauiSplashDarkBackground =>
			$"MauiSplashScreen Include=\"Resources\\Splash\\splash.svg\" Color=\"{Css.ColorValues.DarkBackground}\"";

		public const string PlistDarkStyle = "<string>Dark</string>";
		public const string PlistUserInterfaceStyleKey = "<key>UIUserInterfaceStyle</key>";
	}

	public static class RazorMarkup
	{
		public const string AssignRolesInstructionParameter = "AssignRolesInstruction Instruction";
		public const string AssignRolesPromptResource = "ClientStrings.AssignRoles_SelectRolePrompt";
		public const string AssignRolesTitleResource = "ClientStrings.AssignRoles_Title";
		public const string CreateResponseCall = "Instruction.CreateResponse";
		public const string DisabledAssignmentsIncompleteAttribute = "Disabled=\"@(!AllPlayersAssigned)\"";
		public const string DisabledParameterAttribute = "disabled=\"@Disabled\"";
		public const string DisabledSelectionInvalidAttribute = "Disabled=\"@(!IsSelectionValid)\"";
		public const string GetPublicNameCall = "GetPublicName";
		public const string HoldButtonTag = "<HoldButton";
		public const string HoldToConfirmResource = "ClientStrings.Common_HoldToConfirm";
		public const string InputViewsWithoutDashboardActionZonePredicate =
			"Instruction is not (SelectPlayersInstruction or SelectOptionsInstruction or AssignRolesInstruction)";
		public const string OnHoldCompleteHandleSubmitAttribute = "OnHoldComplete=\"HandleSubmit\"";
		public const string PointerCancelEventName = "onpointercancel";
		public const string PointerDownEventName = "onpointerdown";
		public const string PointerLeaveEventName = "onpointerleave";
		public const string PointerUpEventName = "onpointerup";
		public const string OnPointerCancel = "@" + PointerCancelEventName;
		public const string OnPointerDown = "@" + PointerDownEventName;
		public const string OnPointerLeave = "@" + PointerLeaveEventName;
		public const string OnPointerUp = "@" + PointerUpEventName;
		public const string OptionVariable = "@option";
		public const string ParameterAttribute = "[Parameter";
		public const string RequiredParameterAttribute = "[Parameter, EditorRequired]";
		public const string RolesForAssignment = "Instruction.RolesForAssignment";
		public const string RosterAttribute = "Roster=\"Roster\"";
		public const string RosterParameter = "IReadOnlyList<DashboardRosterEntry> Roster";
		public const string SelectableOptions = "Instruction.SelectableOptions";
		public const string SelectionRange = "SelectionRange";
		public const string SelectionRangeMaximum = "SelectionRange.Maximum";
		public const string SelectOptionsCountResource = "ClientStrings.SelectOptions_SelectionCountFormat";
		public const string SelectOptionsInstructionParameter = "SelectOptionsInstruction Instruction";
		public const string SelectOptionsTitleResource = "ClientStrings.SelectOptions_Title";
		public const string SelectPlayersInstructionBranch = "is SelectPlayersInstruction selectPlayersInstruction";
		public const string SelectPlayersViewTag = "<SelectPlayersView";
		public const string ShouldRenderDashboardActionZone = "ShouldRenderDashboardActionZone";
		public const string SubmitButtonResource = "ClientStrings.Dashboard_ContinueButton";
		public const string SubmitButtonResourceLabelAttribute = "Label=\"@ClientStrings.Dashboard_ContinueButton\"";

		public static string DashboardActionFooterWithHoldButtonPattern =>
			$@"(?s)<footer class=""{Css.Classes.DashboardActionZone}"">\s*{HoldButtonTag}";

		public const string EventCallbackModeratorResponseParameterSuffix = "<ModeratorResponse> OnResponse";
		public const string EventCallbackOnHoldCompleteParameterSuffix = " OnHoldComplete";
		public const string PlayerAssignmentSource = "Instruction.PlayersForAssignment";
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
