using Microsoft.Playwright;

namespace Werewolves.Client.BrowserQa.Tests;

internal static class BrowserQaInstructionAnimations
{
	private const string EvidenceStoreName = "__wwInstructionAnimations";
	private const string AnimationStartEventName = "animationstart";
	private const string DataTestIdAttributeName = "data-testid";
	private static readonly string TestIdEvidenceProperty = nameof(InstructionAnimationEvidence.TestId);
	private static readonly string AnimationNameEvidenceProperty = nameof(InstructionAnimationEvidence.AnimationName);
	private static readonly string ComputedAnimationNameEvidenceProperty = nameof(InstructionAnimationEvidence.ComputedAnimationName);
	private static readonly string DurationMsEvidenceProperty = nameof(InstructionAnimationEvidence.DurationMs);
	private static readonly string ResolvedTokenDurationMsEvidenceProperty = nameof(InstructionAnimationEvidence.ResolvedTokenDurationMs);
	private static readonly string TimingFunctionEvidenceProperty = nameof(InstructionAnimationEvidence.TimingFunction);
	private static readonly string WebAnimationsDurationMsEvidenceProperty = nameof(InstructionAnimationEvidence.WebAnimationsDurationMs);
	private static readonly string KeyframeCountEvidenceProperty = nameof(InstructionAnimationEvidence.KeyframeCount);
	private static readonly string FirstOpacityEvidenceProperty = nameof(InstructionAnimationEvidence.FirstOpacity);
	private static readonly string LastOpacityEvidenceProperty = nameof(InstructionAnimationEvidence.LastOpacity);
	private static readonly string FirstTransformEvidenceProperty = nameof(InstructionAnimationEvidence.FirstTransform);
	private static readonly string LastTransformEvidenceProperty = nameof(InstructionAnimationEvidence.LastTransform);

	private static readonly string HasInstructionAnimationEvidenceScript =
		$"testId => window.{EvidenceStoreName}?.some(animation => animation.{TestIdEvidenceProperty} === testId)";

	private static readonly string ReadInstructionAnimationEvidenceScript =
		$"testId => window.{EvidenceStoreName}.find(animation => animation.{TestIdEvidenceProperty} === testId)";

	public static Task InstallRecorderAsync(IPage page, string instructionBlockTestId) =>
		page.AddInitScriptAsync($$"""
			(() => {
				window.{{EvidenceStoreName}} = [];

				const instructionTestId = "{{instructionBlockTestId}}";
				const animationToken = "{{BrowserQaCss.InstructionAnimationDuration}}";
				const toMilliseconds = value => {
					const firstValue = value.split("{{BrowserQaCss.ValueListSeparator}}")[0].trim();
					if (firstValue.endsWith("{{BrowserQaCss.MillisecondUnit}}")) {
						return Number.parseFloat(firstValue.slice(0, -{{BrowserQaCss.MillisecondUnit.Length}}));
					}

					if (firstValue.endsWith("{{BrowserQaCss.SecondUnit}}")) {
						return Number.parseFloat(firstValue.slice(0, -{{BrowserQaCss.SecondUnit.Length}})) * {{BrowserQaCss.MillisecondsPerSecond}};
					}

					return Number.NaN;
				};

				document.addEventListener("{{AnimationStartEventName}}", event => {
					const target = event.target;
					if (!(target instanceof HTMLElement) ||
						target.getAttribute("{{DataTestIdAttributeName}}") !== instructionTestId) {
						return;
					}

					const styles = getComputedStyle(target);
					const runtimeAnimation = target.getAnimations()
						.find(animation => animation.animationName === event.animationName);
					const runtimeTiming = runtimeAnimation?.effect?.getTiming();
					const runtimeDuration = runtimeTiming?.duration;
					const runtimeKeyframes = runtimeAnimation?.effect?.getKeyframes() ?? [];
					const firstKeyframe = runtimeKeyframes[0] ?? {};
					const lastKeyframe = runtimeKeyframes[runtimeKeyframes.length - 1] ?? {};

					window.{{EvidenceStoreName}}.push({
						{{TestIdEvidenceProperty}}: instructionTestId,
						{{AnimationNameEvidenceProperty}}: event.animationName,
						{{ComputedAnimationNameEvidenceProperty}}: styles.animationName,
						{{DurationMsEvidenceProperty}}: toMilliseconds(styles.animationDuration),
						{{ResolvedTokenDurationMsEvidenceProperty}}: toMilliseconds(styles.getPropertyValue(animationToken)),
						{{TimingFunctionEvidenceProperty}}: styles.animationTimingFunction,
						{{WebAnimationsDurationMsEvidenceProperty}}: typeof runtimeDuration === "number" ? runtimeDuration : null,
						{{KeyframeCountEvidenceProperty}}: runtimeKeyframes.length,
						{{FirstOpacityEvidenceProperty}}: String(firstKeyframe.opacity ?? ""),
						{{LastOpacityEvidenceProperty}}: String(lastKeyframe.opacity ?? ""),
						{{FirstTransformEvidenceProperty}}: String(firstKeyframe.transform ?? ""),
						{{LastTransformEvidenceProperty}}: String(lastKeyframe.transform ?? "")
					});
				}, true);
			})();
			""");

	public static Task WaitForRecordedEvidenceAsync(IPage page, string instructionBlockTestId) =>
		page.WaitForFunctionAsync(HasInstructionAnimationEvidenceScript, instructionBlockTestId);

	public static Task<InstructionAnimationEvidence> ReadRecordedEvidenceAsync(
		IPage page,
		string instructionBlockTestId) =>
		page.EvaluateAsync<InstructionAnimationEvidence>(
			ReadInstructionAnimationEvidenceScript,
			instructionBlockTestId);
}

internal sealed class InstructionAnimationEvidence
{
	public string TestId { get; init; } = string.Empty;
	public string AnimationName { get; init; } = string.Empty;
	public string ComputedAnimationName { get; init; } = string.Empty;
	public double DurationMs { get; init; }
	public double ResolvedTokenDurationMs { get; init; }
	public string TimingFunction { get; init; } = string.Empty;
	public double? WebAnimationsDurationMs { get; init; }
	public int KeyframeCount { get; init; }
	public string FirstOpacity { get; init; } = string.Empty;
	public string LastOpacity { get; init; } = string.Empty;
	public string FirstTransform { get; init; } = string.Empty;
	public string LastTransform { get; init; } = string.Empty;
}
