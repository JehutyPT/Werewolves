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
    /// Players whose Role must be supplied explicitly by the Moderator.
    /// </summary>
    public ImmutableHashSet<Guid> PlayersForAssignment { get; }

	/// <summary>
	/// Duplicate-preserving possible printed Roles for each unknown Player.
	/// </summary>
	public IReadOnlyDictionary<Guid, IReadOnlyList<MainRoleType>>
		SelectableRolesForPlayers { get; }

    /// <summary>
    /// Initializes a new instance of AssignRolesInstruction.
    /// </summary>
    /// <param name="selectableRolesForPlayers">Dictionary mapping player IDs to their assignable roles.</param>
    /// <param name="publicAnnouncement">The text to be read aloud to players.</param>
    /// <param name="privateInstruction">Private guidance for the moderator.</param>
    /// <param name="affectedPlayerIds">Optional list of affected player IDs for context.</param>
    internal AssignRolesInstruction(
        ImmutableHashSet<Guid> playersForAssignment,
        IReadOnlyList<MainRoleType> rolesForAssignment,
		string? publicAnnouncement = null,
        string? privateInstruction = null,
        IReadOnlyList<Guid>? affectedPlayerIds = null,
        Guid instructionId = default)
			: this(
				ModeratorInstructionSemantic.Unspecified,
				playersForAssignment,
				CreateSharedRoleOptions(
					playersForAssignment,
					rolesForAssignment),
				publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			instructionId)
	{
	}

	internal AssignRolesInstruction(
			ModeratorInstructionSemantic semantic,
			ImmutableHashSet<Guid> playersForAssignment,
			IReadOnlyList<MainRoleType> rolesForAssignment,
		string? publicAnnouncement = null,
		string? privateInstruction = null,
		IReadOnlyList<Guid>? affectedPlayerIds = null,
		Guid instructionId = default)
			: this(
				semantic,
				playersForAssignment,
				CreateSharedRoleOptions(
					playersForAssignment,
					rolesForAssignment),
				publicAnnouncement,
				privateInstruction,
				affectedPlayerIds,
				instructionId)
		{
		}

	[JsonConstructor]
	internal AssignRolesInstruction(
		ModeratorInstructionSemantic semantic,
		ImmutableHashSet<Guid> playersForAssignment,
		IReadOnlyDictionary<Guid, IReadOnlyList<MainRoleType>>
			selectableRolesForPlayers,
		string? publicAnnouncement = null,
		string? privateInstruction = null,
		IReadOnlyList<Guid>? affectedPlayerIds = null,
		Guid instructionId = default)
		: base(
				publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			instructionId: instructionId,
			semantic: semantic)
    {
		ArgumentNullException.ThrowIfNull(playersForAssignment);
		ArgumentNullException.ThrowIfNull(selectableRolesForPlayers);
		if (selectableRolesForPlayers.Count == 0)
		{
			throw new ArgumentException(
				"SelectableRolesForPlayers cannot be empty.",
				nameof(selectableRolesForPlayers));
		}

		var roleOptions = selectableRolesForPlayers.ToImmutableDictionary(
			entry => entry.Key,
			entry => (IReadOnlyList<MainRoleType>)(entry.Value ??
				throw new ArgumentException(
					"Every Player must have a possible-Role multiset.",
					nameof(selectableRolesForPlayers)))
				.ToImmutableArray());
		if (roleOptions.Any(entry => entry.Value.Count == 0))
		{
			throw new ArgumentException(
				"Every Player must have at least one possible Role.",
				nameof(selectableRolesForPlayers));
		}
		if (!playersForAssignment.IsSubsetOf(roleOptions.Keys))
		{
			throw new ArgumentException(
				"Every Player requiring assignment must have Role options.",
				nameof(playersForAssignment));
		}

		PlayersForAssignment = playersForAssignment;
		SelectableRolesForPlayers = roleOptions;
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

		return PlayersForAssignment.Count == 0
			? new ModeratorResponse
			{
				InstructionId = InstructionId,
				Type = ExpectedInputType.Continue
			}
			: new ModeratorResponse
			{
				InstructionId = InstructionId,
				Type = ExpectedInputType.AssignPlayerRoles,
				AssignedPlayerRoles = assignments.ToImmutableDictionary()
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

        if (assignments.Count != PlayersForAssignment.Count ||
            !PlayersForAssignment.SetEquals(assignments.Keys))
        {
            throw new ArgumentException(
                "Assignments must contain exactly every Player requested by the instruction.",
                nameof(assignments));
        }

		foreach (var assignment in assignments)
		{
			if (!SelectableRolesForPlayers[assignment.Key]
				.Contains(assignment.Value))
			{
				throw new ArgumentException(
					$"MainRole {assignment.Value} is not in the list of assignable roles for player {assignment.Key}.");
			}
		}
	}

	private static IReadOnlyDictionary<Guid, IReadOnlyList<MainRoleType>>
		CreateSharedRoleOptions(
			IReadOnlySet<Guid> playerIds,
			IReadOnlyList<MainRoleType> roles)
	{
		ArgumentNullException.ThrowIfNull(playerIds);
		ArgumentNullException.ThrowIfNull(roles);
		return playerIds.ToDictionary(
			playerId => playerId,
			_ => roles);
	}
}
