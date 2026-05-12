using System.Text.Json;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;

namespace Werewolves.Core.StateModels.Serialization;

/// <summary>
/// Polymorphic JSON converter for ModeratorInstruction and its derived types.
/// Uses a discriminator pattern to properly serialize/deserialize the correct types.
/// </summary>
public class ModeratorInstructionConverter : JsonConverter<ModeratorInstruction>
{
    private const string TypeDiscriminator = "$type";

    private static readonly Dictionary<string, Type> TypeMap = new()
    {
        ["ConfirmationInstruction"] = typeof(ConfirmationInstruction),
        ["StartGameConfirmationInstruction"] = typeof(StartGameConfirmationInstruction),
        ["FinishedGameConfirmationInstruction"] = typeof(FinishedGameConfirmationInstruction),
        ["SelectPlayersInstruction"] = typeof(SelectPlayersInstruction),
        ["AssignRolesInstruction"] = typeof(AssignRolesInstruction),
        ["SelectOptionsInstruction"] = typeof(SelectOptionsInstruction),
    };

    private static readonly Dictionary<Type, string> ReverseTypeMap = 
        TypeMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    public override ModeratorInstruction? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object");
        }

        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        var root = jsonDoc.RootElement;

        if (!root.TryGetProperty(TypeDiscriminator, out var typeProperty))
        {
            throw new JsonException($"Missing type discriminator '{TypeDiscriminator}'");
        }

        var typeName = typeProperty.GetString();
        if (typeName == null || !TypeMap.TryGetValue(typeName, out var targetType))
        {
            throw new JsonException($"Unknown type discriminator: {typeName}");
        }

        // Create a new options instance without this converter to avoid infinite recursion
        var innerOptions = CreateOptionsWithoutThisConverter(options);
        
        return (ModeratorInstruction?)JsonSerializer.Deserialize(root.GetRawText(), targetType, innerOptions);
    }

    public override void Write(Utf8JsonWriter writer, ModeratorInstruction value, JsonSerializerOptions options)
    {
        var type = value.GetType();
        
        if (!ReverseTypeMap.TryGetValue(type, out var typeName))
        {
            throw new JsonException($"Unknown ModeratorInstruction type: {type.Name}");
        }

        writer.WriteStartObject();
        
        // Write the type discriminator first
        writer.WriteString(TypeDiscriminator, typeName);
        
        // Create options without this converter to serialize the rest
        var innerOptions = CreateOptionsWithoutThisConverter(options);
        
        // Serialize the object as a JsonDocument to extract its properties
        using var doc = JsonSerializer.SerializeToDocument(value, type, innerOptions);
        
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            property.WriteTo(writer);
        }
        
        writer.WriteEndObject();
    }

    private static JsonSerializerOptions CreateOptionsWithoutThisConverter(JsonSerializerOptions options)
    {
        var newOptions = new JsonSerializerOptions(options);
        
        // Remove this converter to avoid recursion
        for (int i = newOptions.Converters.Count - 1; i >= 0; i--)
        {
            if (newOptions.Converters[i] is ModeratorInstructionConverter)
            {
                newOptions.Converters.RemoveAt(i);
            }
        }
        
        return newOptions;
    }
}
