using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models.Instructions;

/// <summary>
/// Instruction that requires the moderator to select one or more players from a list.
/// Uses a flexible constraint system to define selection requirements.
/// </summary>
public record SelectPlayersInstruction : ModeratorInstruction
{
    /// <summary>
    /// The list of player IDs that can be selected from.
    /// </summary>
    public ImmutableHashSet<Guid> SelectablePlayerIds { get; }

    /// <summary>
    /// The constraint defining how many players must be selected.
    /// </summary>
    public NumberRangeConstraint CountConstraint { get; }

	/// <summary>
	/// Machine-stable context for first-night Role identification.
	/// Null for ordinary target and vote selections.
	/// </summary>
	public MainRoleType? RoleIdentification { get; }

    /// <summary>
    /// Optional label for an explicit empty-selection choice when the constraint allows no players.
    /// </summary>
    public string? EmptySelectionOptionLabel { get; init; }

    /// <summary>
    /// Initializes a new instance of SelectPlayersInstruction.
    /// </summary>
    /// <param name="selectablePlayerIds">The list of player IDs that can be selected.</param>
    /// <param name="countConstraint">The constraint defining selection requirements.</param>
    /// <param name="publicAnnouncement">The text to be read aloud to players.</param>
    /// <param name="privateInstruction">Private guidance for the moderator.</param>
    /// <param name="affectedPlayerIds">Optional list of affected player IDs for context.</param>
    internal SelectPlayersInstruction(
        HashSet<Guid> selectablePlayerIds,
        NumberRangeConstraint countConstraint,
        string? publicAnnouncement = null,
        string? privateInstruction = null,
        IReadOnlyList<Guid>? affectedPlayerIds = null,
        Guid instructionId = default)
		: this(
			selectablePlayerIds.ToImmutableHashSet(),
			countConstraint,
			publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			roleIdentification: null,
			instructionId: instructionId)
	{
	}

	/// <summary>
	/// Initializes a new instance of SelectPlayersInstruction with machine-stable
	/// first-night Role identification context.
	/// </summary>
	internal SelectPlayersInstruction(
		HashSet<Guid> selectablePlayerIds,
		NumberRangeConstraint countConstraint,
		string? publicAnnouncement,
		string? privateInstruction,
		IReadOnlyList<Guid>? affectedPlayerIds,
		MainRoleType? roleIdentification,
		Guid instructionId = default)
		: this(
			ModeratorInstructionSemantic.Unspecified,
			selectablePlayerIds,
			countConstraint,
			publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			roleIdentification,
			instructionId)
	{
	}

	/// <summary>
	/// Initializes a new instance from its durable representation.
	/// </summary>
    [JsonConstructor]
	internal SelectPlayersInstruction(
		ImmutableHashSet<Guid> selectablePlayerIds,
		NumberRangeConstraint countConstraint,
		string? publicAnnouncement,
		string? privateInstruction,
		IReadOnlyList<Guid>? affectedPlayerIds,
		MainRoleType? roleIdentification,
        Guid instructionId = default)
		: this(
			ModeratorInstructionSemantic.Unspecified,
			selectablePlayerIds,
			countConstraint,
			publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			roleIdentification,
			instructionId)
	{
	}

	internal SelectPlayersInstruction(
		ModeratorInstructionSemantic semantic,
		HashSet<Guid> selectablePlayerIds,
		NumberRangeConstraint countConstraint,
		string? publicAnnouncement = null,
		string? privateInstruction = null,
		IReadOnlyList<Guid>? affectedPlayerIds = null,
		MainRoleType? roleIdentification = null,
		Guid instructionId = default)
		: this(
			semantic,
			selectablePlayerIds?.ToImmutableHashSet()
				?? throw new ArgumentNullException(nameof(selectablePlayerIds)),
			countConstraint,
			publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			roleIdentification,
			instructionId)
	{
	}

	private SelectPlayersInstruction(
		ModeratorInstructionSemantic semantic,
		ImmutableHashSet<Guid> selectablePlayerIds,
		NumberRangeConstraint countConstraint,
		string? publicAnnouncement,
		string? privateInstruction,
		IReadOnlyList<Guid>? affectedPlayerIds,
		MainRoleType? roleIdentification,
		Guid instructionId)
        : base(
			publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			instructionId: instructionId,
			semantic: semantic)
    {
        SelectablePlayerIds = selectablePlayerIds;
        CountConstraint = countConstraint;
		if (roleIdentification.HasValue && !Enum.IsDefined(roleIdentification.Value))
		{
			throw new ArgumentOutOfRangeException(nameof(roleIdentification));
		}

		RoleIdentification = roleIdentification;

        if (selectablePlayerIds.Count == 0)
        {
            throw new ArgumentException("SelectablePlayerIds cannot be empty.", nameof(selectablePlayerIds));
        }
    }

    /// <summary>
    /// Creates a ModeratorResponse with the provided player selection.
    /// Performs contractual validation to ensure the selection meets the constraint requirements.
    /// </summary>
    /// <param name="selectedPlayerIds">The list of selected player IDs.</param>
    /// <returns>A validated ModeratorResponse.</returns>
    /// <exception cref="ArgumentException">Thrown when the selection violates the constraint.</exception>
    public ModeratorResponse CreateResponse(HashSet<Guid> selectedPlayerIds)
    {
        ValidateSelection(selectedPlayerIds);

        return new ModeratorResponse
        {
            InstructionId = InstructionId,
            Type = ExpectedInputType.PlayerSelection,
            SelectedPlayerIds = selectedPlayerIds.ToImmutableHashSet()
        };
    }

    /// <summary>
    /// Validates that the provided selection meets the constraint requirements.
    /// </summary>
    /// <param name="selectedPlayerIds">The selection to validate.</param>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    private void ValidateSelection(HashSet<Guid> selectedPlayerIds)
    {
        if (selectedPlayerIds == null)
        {
            throw new ArgumentNullException(nameof(selectedPlayerIds));
        }

        var count = selectedPlayerIds.Count;

        CountConstraint.Enforce(selectedPlayerIds.ToList());

        // Check that all selected players are in the selectable list
        if(!selectedPlayerIds.IsSubsetOf(SelectablePlayerIds))
        {
            throw new ArgumentException("Selected player IDs are not valid.");
        }
    }
}
