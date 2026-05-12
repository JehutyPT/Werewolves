using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models.Instructions;

/// <summary>
/// Instruction that requires the moderator to select from a list of text options.
/// Commonly used for event card selections, alignment choices, or other text-based decisions.
/// </summary>
public record SelectOptionsInstruction : ModeratorInstruction
{
    /// <summary>
    /// The list of selectable options.
    /// </summary>
    public HashSet<string> SelectableOptions { get; }

    public NumberRangeConstraint SelectionRange { get; }


    /// <summary>
    /// Initializes a new instance of SelectOptionsInstruction.
    /// </summary>
    /// <param name="selectableOptions">The list of selectable options.</param>
    /// <param name="selectionRange"></param>
    /// <param name="publicAnnouncement">The text to be read aloud to players.</param>
    /// <param name="privateInstruction">Private guidance for the moderator.</param>
    /// <param name="affectedPlayerIds">Optional list of affected player IDs for context.</param>
    [JsonConstructor]
    internal SelectOptionsInstruction(
        HashSet<string> selectableOptions,
        NumberRangeConstraint selectionRange,
        string? publicAnnouncement = null,
        string? privateInstruction = null,
        IReadOnlyList<Guid>? affectedPlayerIds = null)
        : base(publicAnnouncement, privateInstruction, affectedPlayerIds)
    {
        SelectableOptions = selectableOptions ?? throw new ArgumentNullException(nameof(selectableOptions));
        SelectionRange = selectionRange;

        if (selectableOptions.Count == 0)
        {
            throw new ArgumentException("SelectableOptions cannot be empty.", nameof(selectableOptions));
        }

        // Check for duplicate options
        if (selectableOptions.Distinct().Count() != selectableOptions.Count)
        {
            throw new ArgumentException("SelectableOptions contains duplicate entries.", nameof(selectableOptions));
        }
    }

    /// <summary>
    /// Creates a ModeratorResponse with the provided option selection.
    /// Performs contractual validation to ensure the selection is valid.
    /// </summary>
    /// <param name="selectedOptions">The selected option(s).</param>
    /// <returns>A validated ModeratorResponse.</returns>
    /// <exception cref="ArgumentException">Thrown when the selection is invalid.</exception>
    public ModeratorResponse CreateResponse(params string[] selectedOptions) 
        => CreateResponse(new HashSet<string>(selectedOptions));

    /// <summary>
    /// Creates a ModeratorResponse with the provided option selection.
    /// Performs contractual validation to ensure the selection is valid.
    /// </summary>
    /// <param name="selectedOptions">The selected option(s).</param>
    /// <returns>A validated ModeratorResponse.</returns>
    /// <exception cref="ArgumentException">Thrown when the selection is invalid.</exception>
    public ModeratorResponse CreateResponse(HashSet<string> selectedOptions)
    {
        if (!selectedOptions.IsSubsetOf(SelectableOptions))
        {
            throw new ArgumentException("Selected options are not valid.");
        }

        SelectionRange.Enforce(selectedOptions.ToList());

        return new ModeratorResponse
        {
            Type = ExpectedInputType.OptionSelection,
            SelectedOption = selectedOptions
        };
    }
}
