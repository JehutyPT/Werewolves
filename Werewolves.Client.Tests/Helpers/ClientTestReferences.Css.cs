namespace Werewolves.Client.Tests.Helpers;

public static partial class ClientTestReferences
{
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
			public const string AppShell = "ww-app-shell";
			public const string AudioToggle = "ww-audio-toggle";
			public const string AudioToggleMuted = "ww-audio-toggle--muted";
			public const string DashboardActionZone = "ww-dashboard-action-zone";
			public const string DashboardShell = "ww-dashboard-shell";
			public const string DashboardTab = "ww-dashboard-tab";
			public const string DashboardTabActive = "ww-dashboard-tab--active";
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
			public const string HoldEdgeProductionTransition = "transition: left 400ms linear, opacity 80ms ease-in;";
			public const string HoldEdgeSlowTransition = "transition: left 600ms linear, opacity 80ms ease-in;";
			public const string HoldFillProductionTransition = "transition: width 400ms linear;";
			public const string HoldFillSlowTransition = "transition: width 600ms linear;";
		}

		public static class Tokens
		{
			public const string Accent = "--ww-accent";
			public const string AccentBright = "--ww-accent-bright";
			public const string Background = "--ww-bg";
			public const string BackgroundRaised = "--ww-bg-raised";
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
