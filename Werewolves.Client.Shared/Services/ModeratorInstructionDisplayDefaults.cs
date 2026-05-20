using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;

namespace Werewolves.Client.Services;

internal static class ModeratorInstructionDisplayDefaults
{
	public static bool RequiresModeratorDataEntryForDisplay(ModeratorInstruction instruction) =>
		instruction is SelectPlayersInstruction or SelectOptionsInstruction or AssignRolesInstruction;
}
