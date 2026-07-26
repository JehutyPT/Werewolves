using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Data structure for communication FROM the moderator.
/// Represents the moderator's response to a ModeratorInstruction.
/// </summary>
public class ModeratorResponse
{
    public Guid InstructionId { get; internal init; }
    public ExpectedInputType Type { get; internal init; }

    // Optional fields, presence depends on Type
    public IReadOnlySet<Guid>? SelectedPlayerIds { get; internal init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IReadOnlyDictionary<Guid, MainRoleType>? AssignedPlayerRoles { get; internal init; }
    public IReadOnlyList<string>? SelectedOptionIds { get; internal init; }

    //internal so only ModeratorInputs can create instances, not external consumers
    internal ModeratorResponse(){}
}
