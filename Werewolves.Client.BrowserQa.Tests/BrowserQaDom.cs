namespace Werewolves.Client.BrowserQa.Tests;

internal static class BrowserQaDom
{
	public const int HoldingStateTimeoutMs = 350;
	public const int PrimaryMouseButton = 0;
	public const int PrimaryMouseButtonsPressed = 1;
	public const int PrimaryPointerId = 1;
	public const int ReleasedMouseButtons = 0;
	public const string MissingHoldProgressMessage =
		"The rendered hold button did not expose the progress fill and edge.";
	public const string MousePointerType = "mouse";
	public const string PointerDownEventName = "pointerdown";
	public const string PointerUpEventName = "pointerup";
	public const string WaitForHoldingStateTimeoutMessage =
		"The rendered hold button never entered its holding state.";
}
