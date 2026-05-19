namespace Werewolves.Client.Tests.Documentation;

internal static class QaStrategyContract
{
	public const string NativeChecklistRelativePath = "Werewolves.Client/docs/native-device-qa-checklist.md";

	public static readonly string[] RequiredStrategyContent =
	[
		"claim-first",
		"cheapest reliable evidence",
		"QA Evidence Matrix",
		"Choose Evidence",
		"Source-Test Rules",
		"generated resource/localization accessors",
		"derive expected localized values from resource contract data",
		"source, style, selector, and CSS class assertions",
		"policy, allowlisted, or contractual claims",
		"Allowed Source Tests",
		"Manual Device Checks",
		"manual device boundary",
		"Browser QA Host",
		"Werewolves.Client.BrowserQaHost",
		"dotnet run --project Werewolves.Client.BrowserQaHost/Werewolves.Client.BrowserQaHost.csproj",
		"not treat the browser host as a public deployment target",
		"Do not add a required CI gate",
		"CI vs Local Evidence",
		"Source-Test Allowlist",
		"Permanent policy",
		"Deprecated temporary scaffold"
	];

	public static readonly string[] ForbiddenStrategyContent =
	[
		"TBD",
		"placeholder",
		"PRD #",
		"this slice",
		"migration",
		"Existing Source-Test Audit"
	];

	public static readonly string[] RequiredSourceTestAllowlistEntries =
	[
		"`Werewolves.Client.Tests.Documentation.QaStrategyTests.QaStrategy_DefinesClaimFirstEvidenceGuideAndSourceTestAllowlist`",
		"`Werewolves.Client.Tests.Documentation.QaStrategyTests.QaStrategy_SourceTestAllowlistTracksActiveRetainedSourceTests` | Permanent policy | The source-test allowlist tracks active retained source tests and omits retired deleted scaffolds. | Markdown is the policy surface; keep a narrow documentation contract test. |",
		"`Werewolves.Client.Tests.Documentation.QaStrategyTests.NativeDeviceChecklist_DefinesManualOnlyClaimAndEvidenceChecks`",
		"`Werewolves.Client.Tests.Resources.LocalizationPolicyTests.TestProjects_DoNotHardcodeLocalizedProductionCopy`",
		"`Werewolves.Client.Tests.Platform.AndroidManifestTests.AndroidManifest_DeclaresVibratePermissionForHapticFeedback`",
		"`Werewolves.Client.Tests.Resources.ClientStringsTests.ClientStrings_ExposesPortugueseUiCopyThroughGeneratedAccessor`",
		"`Werewolves.Client.Tests.Styling.DarkThemeTokenTests`: `AppCss_ConsumesColorValuesThroughDesignTokens`, `RootDocument_UsesDarkThemeTokensBeforePagesRender`, `MauiHost_UsesDarkChromeAcrossSupportedSurfaces`, `TextTokens_HaveReadableContrastAgainstDarkSurfaces`, `Pages_DoNotUseInlineColorLiterals`",
		"`Werewolves.Client.Tests.Styling.DarkThemeTokenTests.Pages_RenderInsideDarkShells`",
		"`Werewolves.Client.Tests.Styling.InstructionTransitionTests.DesignTokens_DefineInstructionAnimationDurationBetween200And300Ms`",
		"`Werewolves.Client.Tests.Styling.InstructionTransitionTests`: `AppCss_DefinesInstructionEnterKeyframes`, `AppCss_InstructionBlockUsesAnimationToken`",
		"`Werewolves.Client.Tests.Styling.DashboardOverlayLayoutTests`: `ProductionDashboard_FixesTopAndBottomOverlays`, `ProductionDashboard_AddsScrollPaddingForFixedOverlays`, `ProductionDashboard_StatusBarUsesInsetWidthInsteadOfViewportWidth`",
		"`Werewolves.Client.Tests.Styling.HoldButtonTokenTests.DesignTokens_AnimateHoldProgressOverProductionDuration` | Deprecated temporary scaffold | Hold progress CSS timing remains aligned with the production hold duration. | Replace with browser-host computed-style checks for rendered hold progress. |",
		"`Werewolves.Client.Tests.BrowserQaHost.BrowserQaHostCompositionTests.BrowserQaHostProject_ReferencesSharedBoundaryWithoutMaui` | Permanent policy | The browser QA host project stays on the host-agnostic shared boundary without MAUI. | Project XML is the architecture-boundary contract. |",
		"`Werewolves.Client.Tests.Components.InstructionRendererHapticTests.InstructionRenderer_UsesTransitionKeyForAnimationReMount`"
	];

	public static readonly string[] RetiredSourceTestAllowlistEntries =
	[
		"`Werewolves.Client.Tests.Components.HoldButtonMarkupTests`",
		"`Werewolves.Client.Tests.Components.InstructionRendererTests`"
	];

	public static readonly string[] RequiredNativeChecklistContent =
	[
		"Claim",
		"Manual scenario",
		"Expected evidence",
		"real audio output",
		"haptic feel",
		"wake lock behavior",
		"resume/background behavior",
		"platform storage behavior",
		"packaging/install behavior",
		"Native WebView rendering quirks",
		"touch feel",
		"These checks remain manual",
		"one or two tiny device smoke tests",
		"Do not use this checklist to replay Core game rules",
		"Do not use this checklist to repeat Browser QA Host checks"
	];
}
