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
		var remainingRoleCopies = new Dictionary<MainRoleType, int>();
		foreach (var playerId in playersForAssignment)
		{
			foreach (var roleCopies in instruction
				.SelectableRolesForPlayers[playerId]
				.GroupBy(role => role))
			{
				remainingRoleCopies[roleCopies.Key] = Math.Max(
					remainingRoleCopies.GetValueOrDefault(roleCopies.Key),
					roleCopies.Count());
			}
		}

		var assignments = new Dictionary<Guid, MainRoleType>();
		if (!TryAssignRoles(
				playersForAssignment,
				instruction.SelectableRolesForPlayers,
				remainingRoleCopies,
				assignments,
				playerIndex: 0))
		{
			throw new InvalidOperationException(
				"No complete first-valid Role assignment exists for the instruction.");
		}

		return instruction.CreateResponse(assignments);
	}

	private static bool TryAssignRoles(
		IReadOnlyList<Guid> playerIds,
		IReadOnlyDictionary<Guid, IReadOnlyList<MainRoleType>> roleOptions,
		Dictionary<MainRoleType, int> remainingRoleCopies,
		Dictionary<Guid, MainRoleType> assignments,
		int playerIndex)
	{
		if (playerIndex == playerIds.Count)
		{
			return true;
		}

		var playerId = playerIds[playerIndex];
		foreach (var role in roleOptions[playerId].Distinct())
		{
			var copies = remainingRoleCopies[role];
			if (copies == 0)
			{
				continue;
			}

			assignments[playerId] = role;
			remainingRoleCopies[role] = copies - 1;
			if (TryAssignRoles(
					playerIds,
					roleOptions,
					remainingRoleCopies,
					assignments,
					playerIndex + 1))
			{
				return true;
			}

			remainingRoleCopies[role] = copies;
			assignments.Remove(playerId);
		}

		return false;
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
