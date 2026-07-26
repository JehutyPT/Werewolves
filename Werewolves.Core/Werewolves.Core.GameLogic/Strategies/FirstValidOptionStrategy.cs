using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;

namespace Werewolves.Core.GameLogic.Strategies;

public sealed class FirstValidOptionStrategy : IModeratorDecisionStrategy
{
	public ModeratorResponse CreateResponse(ModeratorInstruction instruction, IGameSession session)
	{
		return instruction switch
		{
			ConfirmationInstruction confirmation => confirmation.CreateResponse(),
			SelectPlayersInstruction selectPlayers => CreatePlayerSelectionResponse(selectPlayers, session),
			AssignRolesInstruction assignRoles => CreateRoleAssignmentResponse(assignRoles, session),
			SelectOptionsInstruction selectOptions => CreateOptionSelectionResponse(selectOptions),
			_ => throw new NotSupportedException($"Unsupported moderator instruction type: {instruction.GetType().Name}")
		};
	}

	private static ModeratorResponse CreatePlayerSelectionResponse(
		SelectPlayersInstruction instruction,
		IGameSession session)
	{
		var selectedPlayerIds = session.GetPlayers()
			.Select(player => player.Id)
			.Where(instruction.SelectablePlayerIds.Contains)
			.Take(instruction.CountConstraint.Minimum)
			.ToHashSet();

		return instruction.CreateResponse(selectedPlayerIds);
	}

	private static ModeratorResponse CreateRoleAssignmentResponse(
		AssignRolesInstruction instruction,
		IGameSession session)
	{
		var playersForAssignment = session.GetPlayers()
			.Select(player => player.Id)
			.Where(instruction.PlayersForAssignment.Contains)
			.ToList();

		var assignments = playersForAssignment
			.Zip(instruction.RolesForAssignment, (playerId, role) => new KeyValuePair<Guid, MainRoleType>(playerId, role))
			.ToDictionary(pair => pair.Key, pair => pair.Value);

		return instruction.CreateResponse(assignments);
	}

	private static ModeratorResponse CreateOptionSelectionResponse(SelectOptionsInstruction instruction)
	{
		var selectionCount = Math.Max(1, instruction.SelectionRange.Minimum);
		var selectedOptions = instruction.Options
			.Select(option => option.Id)
			.Take(selectionCount)
			.ToArray();

		return instruction.CreateResponse(selectedOptions);
	}
}
