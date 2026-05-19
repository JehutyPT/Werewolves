namespace Werewolves.Client.Tests.Helpers;

public static partial class ClientTestReferences
{
	public static class Css
	{
		public const double MinimumTextContrastRatio = 4.5;

		public static class Animations
		{
			public static string InstructionAnimationDurationPattern => $@"{Tokens.InstructionAnimation}:\s*(\d+)ms";
		}

		public static class Classes
		{
			public const string AppShell = "ww-app-shell";
			public const string AudioToggle = "ww-audio-toggle";
			public const string AudioToggleMuted = "ww-audio-toggle--muted";
			public const string DashboardActionZone = "ww-dashboard-action-zone";
			public const string DashboardShell = "ww-dashboard-shell";
			public const string DashboardStatusBar = "ww-dashboard-status-bar";
			public const string DashboardTab = "ww-dashboard-tab";
			public const string DashboardTabActive = "ww-dashboard-tab--active";
			public const string DashboardTabsCompact = "ww-dashboard-tabs--compact";
			public const string Expanded = "is-expanded";
			public const string HoldButton = "ww-btn-hold";
			public const string HoldZone = "ww-hold-zone";
			public const string Holding = "is-holding";
			public const string HoldComplete = "is-complete";
			public const string IconButton = "ww-icon-button";
			public const string InstructionAnnouncement = "ww-announcement";
			public const string InstructionBlockAnnouncement = "ww-instruction-block--announcement";
			public const string InstructionBlockPrivate = "ww-instruction-block--private";
			public const string InstructionPrivate = "ww-private-instruction";
			public const string InstructionStack = "ww-instruction-stack";
			public const string LabsStatusBar = "ww-labs-status-bar";
			public const string OptionButtonSelected = "ww-option-btn--selected";
			public const string RoleButton = "ww-role-btn";
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
	}
}
