using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Data structure for communication FROM the moderator.
/// Represents the moderator's response to a ModeratorInstruction.
/// </summary>
public class ModeratorResponse
{
    public ExpectedInputType Type { get; internal init; }

    // Optional fields, presence depends on Type
    public HashSet<Guid>? SelectedPlayerIds { get; internal init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Dictionary<Guid, MainRoleType>? AssignedPlayerRoles { get; internal init; }
    public HashSet<string>? SelectedOption { get; internal init; }
    public bool? Confirmation { get; internal init; }

    //internal so only ModeratorInputs can create instances, not external consumers
    internal ModeratorResponse(){}
}
