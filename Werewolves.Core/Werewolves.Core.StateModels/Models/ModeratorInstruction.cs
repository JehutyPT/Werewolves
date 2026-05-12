using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Abstract base class for all moderator instructions.
/// Provides the foundation for a polymorphic instruction hierarchy with dual text fields.
/// </summary>
public abstract record ModeratorInstruction
{
    /// <summary>
    /// The text to be read aloud or displayed publicly to all players.
    /// </summary>
    public string? PublicAnnouncement { get; protected set; }

    /// <summary>
    /// The text for the moderator's eyes only, containing reminders, rules, or guidance.
    /// </summary>
    public string? PrivateInstruction { get; protected set; }

    /// <summary>
    /// Optional: A list of player IDs that this instruction primarily relates to.
    /// This provides context, for example, indicating which player needs their role revealed
    /// or who is the target of a specific action being prompted.
    /// </summary>
    public IReadOnlyList<Guid>? AffectedPlayerIds { get; protected set; }

	/// <summary>
	/// Placeholder for future sound effects associated with the instruction.
	/// Allows for multiple sound effects to be specified.
	/// Only the sound effects in this list should be played. All others should be stopped if playing.
	/// </summary>
	public List<SoundEffectsEnum> SoundEffects { get; protected set; }

    /// <summary>
    /// Initializes a new instance of ModeratorInstruction.
    /// Validates that at least one text field is provided.
    /// </summary>
    [JsonConstructor]
    protected ModeratorInstruction(
        string? publicAnnouncement = null,
        string? privateInstruction = null,
        IReadOnlyList<Guid>? affectedPlayerIds = null,
        List<SoundEffectsEnum>? soundEffects = null)
    {
        PublicAnnouncement = publicAnnouncement;
        PrivateInstruction = privateInstruction;
        AffectedPlayerIds = affectedPlayerIds;
        SoundEffects = soundEffects ?? new List<SoundEffectsEnum>();

        // Validate that at least one text field is provided
        if (string.IsNullOrWhiteSpace(publicAnnouncement) && string.IsNullOrWhiteSpace(privateInstruction))
        {
            throw new ArgumentException("At least one of PublicAnnouncement or PrivateInstruction must be provided.");
        }
    }
}
