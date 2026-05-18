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
