namespace Werewolves.Client.Tests.Helpers;

public static partial class ClientTestReferences
{
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
}
