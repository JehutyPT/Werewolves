using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models.Instructions;

/// <summary>
/// Instruction that requires the moderator to assign roles to specific players.
/// Each player can be assigned from a specific list of available roles.
/// </summary>
public record AssignRolesInstruction : ModeratorInstruction
{
    /// <summary>
    /// Dictionary mapping player IDs to the list of roles that can be assigned to that player.
    /// </summary>
    public ImmutableHashSet<Guid> PlayersForAssignment { get; }
    public IReadOnlyList<MainRoleType> RolesForAssignment { get; }

    /// <summary>
    /// Initializes a new instance of AssignRolesInstruction.
    /// </summary>
    /// <param name="selectableRolesForPlayers">Dictionary mapping player IDs to their assignable roles.</param>
    /// <param name="publicAnnouncement">The text to be read aloud to players.</param>
    /// <param name="privateInstruction">Private guidance for the moderator.</param>
    /// <param name="affectedPlayerIds">Optional list of affected player IDs for context.</param>
    [JsonConstructor]
    internal AssignRolesInstruction(
        ImmutableHashSet<Guid> playersForAssignment,
        IReadOnlyList<MainRoleType> rolesForAssignment,
		string? publicAnnouncement = null,
        string? privateInstruction = null,
        IReadOnlyList<Guid>? affectedPlayerIds = null)
        : base(publicAnnouncement, privateInstruction, affectedPlayerIds)
    {
        PlayersForAssignment = playersForAssignment ?? throw new ArgumentNullException(nameof(playersForAssignment));

        if (playersForAssignment.Count == 0)
        {
            throw new ArgumentException("PlayersForAssignment cannot be empty.", nameof(playersForAssignment));
        }

        RolesForAssignment = rolesForAssignment ?? throw new ArgumentNullException(nameof(rolesForAssignment));

        if (rolesForAssignment.Count == 0)
        {
            throw new ArgumentException("RolesForAssignment cannot be empty.", nameof(rolesForAssignment));
		}

        if (playersForAssignment.Count > rolesForAssignment.Count)
        {
            throw new InvalidOperationException("Not enough roles available for assignment.");
        }
    }

    /// <summary>
    /// Creates a ModeratorResponse with the provided role assignments.
    /// Performs contractual validation to ensure assignments are valid.
    /// </summary>
    /// <param name="assignments">Dictionary mapping player IDs to their assigned roles.</param>
    /// <returns>A validated ModeratorResponse.</returns>
    /// <exception cref="ArgumentException">Thrown when assignments are invalid.</exception>
    public ModeratorResponse CreateResponse(Dictionary<Guid, MainRoleType> assignments)
    {
        ValidateAssignments(assignments);

        return new ModeratorResponse
        {
            Type = ExpectedInputType.AssignPlayerRoles,
            AssignedPlayerRoles = assignments
        };
    }

    /// <summary>
    /// Validates that the provided assignments are valid according to the selectable roles.
    /// </summary>
    /// <param name="assignments">The assignments to validate.</param>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    private void ValidateAssignments(Dictionary<Guid, MainRoleType> assignments)
    {
        if (assignments == null)
        {
            throw new ArgumentNullException(nameof(assignments));
        }

        var assignedRoles = assignments.Values.ToList();

		// Check that the assigned role count does not exceed the allowed quota for each role
        foreach (var role in RolesForAssignment.Distinct())
        {
            int allowedCount = RolesForAssignment.Count(r => r == role);
            int assignedCount = assignedRoles.Count(r => r == role);
            if (assignedCount > allowedCount)
            {
                throw new ArgumentException($"Role {role} has been assigned {assignedCount} times, exceeding the allowed count of {allowedCount}.");
            }
		}


		// Check that all assigned players are in the selectable list
		foreach (var assignment in assignments)
        {
            if (!PlayersForAssignment.Contains(assignment.Key))
            {
                throw new ArgumentException($"Player {assignment.Key} is not in the list of players that can be assigned roles.");
            }

            if (!RolesForAssignment.Contains(assignment.Value))
            {
                throw new ArgumentException($"MainRole {assignment.Value} is not in the list of assignable roles for player {assignment.Key}.");
            }
        }
    }
}
