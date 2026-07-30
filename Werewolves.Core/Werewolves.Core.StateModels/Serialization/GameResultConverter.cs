using System.Text.Json;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.StateModels.Serialization;

public sealed class GameResultConverter : JsonConverter<GameResult>
{
	public override GameResult Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		var root = document.RootElement;
		var kind = root.GetProperty("$type").GetString();
		return kind switch
		{
			"NoWinner" => new NoWinnerGameResult(),
			"SingleFaction" => new SingleFactionGameResult(
				root.GetProperty("faction").Deserialize<Faction>(options)),
			"SharedVictory" => new SharedVictoryGameResult(
				root.GetProperty("factions").Deserialize<Faction[]>(options)
				?? throw new JsonException("Shared victory factions are required.")),
			_ => throw new JsonException($"Unknown Game Result type: {kind}")
		};
	}

	public override void Write(
		Utf8JsonWriter writer,
		GameResult value,
		JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		switch (value)
		{
			case NoWinnerGameResult:
				writer.WriteString("$type", "NoWinner");
				break;
			case SingleFactionGameResult single:
				writer.WriteString("$type", "SingleFaction");
				writer.WritePropertyName("faction");
				JsonSerializer.Serialize(writer, single.Faction, options);
				break;
			case SharedVictoryGameResult shared:
				writer.WriteString("$type", "SharedVictory");
				writer.WritePropertyName("factions");
				JsonSerializer.Serialize(writer, shared.Factions, options);
				break;
			default:
				throw new JsonException($"Unknown Game Result type: {value.GetType().Name}");
		}
		writer.WriteEndObject();
	}
}
