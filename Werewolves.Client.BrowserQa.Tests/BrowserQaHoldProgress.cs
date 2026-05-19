using Microsoft.Playwright;

namespace Werewolves.Client.BrowserQa.Tests;

internal static class BrowserQaHoldProgress
{
	private static readonly object ReadTransitionEvidenceOptions = new
	{
		buttonSelector = BrowserQaCss.HoldButtonSelector,
		fillSelector = BrowserQaCss.HoldFillSelector,
		edgeSelector = BrowserQaCss.HoldEdgeSelector,
		holdingClass = BrowserQaCss.HoldingClass,
		pointerDownEventName = BrowserQaDom.PointerDownEventName,
		pointerUpEventName = BrowserQaDom.PointerUpEventName,
		pointerId = BrowserQaDom.PrimaryPointerId,
		pointerType = BrowserQaDom.MousePointerType,
		primaryButton = BrowserQaDom.PrimaryMouseButton,
		pressedButtons = BrowserQaDom.PrimaryMouseButtonsPressed,
		releasedButtons = BrowserQaDom.ReleasedMouseButtons,
		missingProgressMessage = BrowserQaDom.MissingHoldProgressMessage,
		holdingStateTimeoutMessage = BrowserQaDom.WaitForHoldingStateTimeoutMessage,
		holdingStateTimeoutMs = BrowserQaDom.HoldingStateTimeoutMs
	};

	private const string ReadTransitionEvidenceScript =
		"""
		async (zone, options) => {
			const button = zone.querySelector(options.buttonSelector);
			const fill = zone.querySelector(options.fillSelector);
			const edge = zone.querySelector(options.edgeSelector);

			if (!button || !fill || !edge) {
				throw new Error(options.missingProgressMessage);
			}

			const pointerDown = new PointerEvent(options.pointerDownEventName, {
				bubbles: true,
				pointerId: options.pointerId,
				pointerType: options.pointerType,
				isPrimary: true,
				button: options.primaryButton,
				buttons: options.pressedButtons
			});
			const pointerUp = new PointerEvent(options.pointerUpEventName, {
				bubbles: true,
				pointerId: options.pointerId,
				pointerType: options.pointerType,
				isPrimary: true,
				button: options.primaryButton,
				buttons: options.releasedButtons
			});

			button.dispatchEvent(pointerDown);
			try {
				await new Promise((resolve, reject) => {
					const timeout = window.setTimeout(
						() => reject(new Error(options.holdingStateTimeoutMessage)),
						options.holdingStateTimeoutMs);
					const observe = () => {
						if (zone.classList.contains(options.holdingClass)) {
							window.clearTimeout(timeout);
							resolve();
							return;
						}

						window.requestAnimationFrame(observe);
					};

					observe();
				});

				const fillStyle = getComputedStyle(fill);
				const edgeStyle = getComputedStyle(edge);

				return {
					FillTransitionProperty: fillStyle.transitionProperty,
					FillTransitionDuration: fillStyle.transitionDuration,
					FillTransitionTimingFunction: fillStyle.transitionTimingFunction,
					EdgeTransitionProperty: edgeStyle.transitionProperty,
					EdgeTransitionDuration: edgeStyle.transitionDuration,
					EdgeTransitionTimingFunction: edgeStyle.transitionTimingFunction
				};
			} finally {
				button.dispatchEvent(pointerUp);
			}
		}
		""";

	public static ILocator HoldZoneIn(ILocator actionZone) =>
		actionZone.Locator(BrowserQaCss.HoldZoneSelector).First;

	public static Task<HoldProgressTransitionEvidence> ReadTransitionEvidenceWhileHoldingAsync(ILocator holdZone) =>
		holdZone.EvaluateAsync<HoldProgressTransitionEvidence>(
			ReadTransitionEvidenceScript,
			ReadTransitionEvidenceOptions);
}

internal sealed class HoldProgressTransitionEvidence
{
	public string FillTransitionProperty { get; set; } = string.Empty;
	public string FillTransitionDuration { get; set; } = string.Empty;
	public string FillTransitionTimingFunction { get; set; } = string.Empty;
	public string EdgeTransitionProperty { get; set; } = string.Empty;
	public string EdgeTransitionDuration { get; set; } = string.Empty;
	public string EdgeTransitionTimingFunction { get; set; } = string.Empty;
}
