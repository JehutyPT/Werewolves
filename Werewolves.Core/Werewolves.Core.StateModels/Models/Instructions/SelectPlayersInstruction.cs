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
    public HashSet<Guid> SelectablePlayerIds { get; }

    /// <summary>
    /// The constraint defining how many players must be selected.
    /// </summary>
    public NumberRangeConstraint CountConstraint { get; }

    /// <summary>
    /// Initializes a new instance of SelectPlayersInstruction.
    /// </summary>
    /// <param name="selectablePlayerIds">The list of player IDs that can be selected.</param>
    /// <param name="countConstraint">The constraint defining selection requirements.</param>
    /// <param name="publicAnnouncement">The text to be read aloud to players.</param>
    /// <param name="privateInstruction">Private guidance for the moderator.</param>
    /// <param name="affectedPlayerIds">Optional list of affected player IDs for context.</param>
    [JsonConstructor]
    internal SelectPlayersInstruction(
        HashSet<Guid> selectablePlayerIds,
        NumberRangeConstraint countConstraint,
        string? publicAnnouncement = null,
        string? privateInstruction = null,
        IReadOnlyList<Guid>? affectedPlayerIds = null)
        : base(publicAnnouncement, privateInstruction, affectedPlayerIds)
    {
        SelectablePlayerIds = selectablePlayerIds ?? throw new ArgumentNullException(nameof(selectablePlayerIds));
        CountConstraint = countConstraint;

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
            Type = ExpectedInputType.PlayerSelection,
            SelectedPlayerIds = selectedPlayerIds
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
