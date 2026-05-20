using Werewolves.Client.Resources;

namespace Werewolves.Client.Tests.Helpers;

public static partial class ClientTestReferences
{
	public static class FixtureLabels
	{
		public static string CollapsedInstructionPreviewSuffix => ClientStrings.Common_CollapsedTextSuffix;
		public static string UnexpectedRoleGroupDisplayName => nameof(UnexpectedRoleGroupDisplayName);

		public static string RenderedInstructionStep(int step, string instructionType) =>
			$"{nameof(RenderedInstructionStep)}: step={step}; instruction={instructionType}";
	}
}
