using System.Text.Json;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Log;

namespace Werewolves.Core.StateModels.Serialization;

/// <summary>
/// Polymorphic JSON converter for GameLogEntryBase and its derived types.
/// Uses a discriminator pattern to properly serialize/deserialize the correct types.
/// </summary>
public class GameLogEntryConverter : JsonConverter<GameLogEntryBase>
{
    private const string TypeDiscriminator = "$type";

    private static readonly Dictionary<string, Type> TypeMap = new()
    {
	        ["ActorBorrowedRolePowerActivationExpiredLogEntry"] =
		        typeof(ActorBorrowedRolePowerActivationExpiredLogEntry),
	        ["ActorSetupCardSpendCommittedLogEntry"] =
		        typeof(ActorSetupCardSpendCommittedLogEntry),
	        ["AngelExpiredLogEntry"] = typeof(AngelExpiredLogEntry),
	        ["AssignRoleLogEntry"] = typeof(AssignRoleLogEntry),
	        ["BearTamerGrowlOccurredLogEntry"] = typeof(BearTamerGrowlOccurredLogEntry),
	        ["DayActionLogEntry"] = typeof(DayActionLogEntry),
		["DevotedServantPublicSelfRevealCommittedLogEntry"] =
			typeof(DevotedServantPublicSelfRevealCommittedLogEntry),
		["DevotedServantRoleTakenCommittedLogEntry"] =
			typeof(DevotedServantRoleTakenCommittedLogEntry),
        ["DawnVictimDeterminedLogEntry"] = typeof(DawnVictimDeterminedLogEntry),
        ["EliminationCascadeBatchResolvedLogEntry"] = typeof(EliminationCascadeBatchResolvedLogEntry),
        ["EliminationCascadeCompletedLogEntry"] = typeof(EliminationCascadeCompletedLogEntry),
        ["EliminationCascadeReactionCompletedLogEntry"] = typeof(EliminationCascadeReactionCompletedLogEntry),
        ["FactionFactsCommittedLogEntry"] = typeof(FactionFactsCommittedLogEntry),
        ["LoversPairCommittedLogEntry"] = typeof(LoversPairCommittedLogEntry),
        ["NightActionLogEntry"] = typeof(NightActionLogEntry),
        ["OneUseRolePowerCommittedLogEntry"] = typeof(OneUseRolePowerCommittedLogEntry),
		["PermanentRoleSwapCommittedLogEntry"] = typeof(PermanentRoleSwapCommittedLogEntry),
		["ThiefOfferDeclinedLogEntry"] = typeof(ThiefOfferDeclinedLogEntry),
        ["RecurringRolePowerCommittedLogEntry"] = typeof(RecurringRolePowerCommittedLogEntry),
        ["TargetPrivateRolePowerCommittedLogEntry"] = typeof(TargetPrivateRolePowerCommittedLogEntry),
	        ["PhaseTransitionLogEntry"] = typeof(PhaseTransitionLogEntry),
			["PhysicalCharacterCardOwnershipObservedLogEntry"] = typeof(PhysicalCharacterCardOwnershipObservedLogEntry),
        ["PlayerEliminatedLogEntry"] = typeof(PlayerEliminatedLogEntry),
        ["RoleIdentificationLogEntry"] = typeof(RoleIdentificationLogEntry),
	        ["RoleRevealLogEntry"] = typeof(RoleRevealLogEntry),
	        ["ScapegoatTieReplacementLogEntry"] = typeof(ScapegoatTieReplacementLogEntry),
	        ["VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry"] = typeof(VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry),
	        ["VoterEligibilityRestrictionCommittedLogEntry"] = typeof(VoterEligibilityRestrictionCommittedLogEntry),
	        ["VoterEligibilityRestrictionExpiredLogEntry"] = typeof(VoterEligibilityRestrictionExpiredLogEntry),
	        ["StatusEffectLogEntry"] = typeof(StatusEffectLogEntry),
	        ["OneUseRolePowerDayActionCommittedLogEntry"] = typeof(OneUseRolePowerDayActionCommittedLogEntry),
	        ["StutteringJudgeSignalDidNotOccurLogEntry"] = typeof(StutteringJudgeSignalDidNotOccurLogEntry),
	        ["StutteringJudgeSignalEstablishedLogEntry"] = typeof(StutteringJudgeSignalEstablishedLogEntry),
	        ["VillagerVillagerPublicFromDealLogEntry"] = typeof(VillagerVillagerPublicFromDealLogEntry),
	        ["VictoryConditionMetLogEntry"] = typeof(VictoryConditionMetLogEntry),
	        ["VoteOutcomeReportedLogEntry"] = typeof(VoteOutcomeReportedLogEntry),
	        ["VotingRightChangedLogEntry"] = typeof(VotingRightChangedLogEntry),
	        ["VillageIdiotPardonCommittedLogEntry"] = typeof(VillageIdiotPardonCommittedLogEntry),
	        ["VillagerRolePowerSuppressionCommittedLogEntry"] =
		        typeof(VillagerRolePowerSuppressionCommittedLogEntry),
	        ["VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry"] =
		        typeof(
			        VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry),
	    };

    private static readonly Dictionary<Type, string> ReverseTypeMap =
        TypeMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    public override GameLogEntryBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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

        return (GameLogEntryBase?)JsonSerializer.Deserialize(
            root.GetRawText(),
            targetType,
            innerOptions);
    }

    public override void Write(Utf8JsonWriter writer, GameLogEntryBase value, JsonSerializerOptions options)
    {
        var type = value.GetType();
        
        if (!ReverseTypeMap.TryGetValue(type, out var typeName))
        {
            throw new JsonException($"Unknown GameLogEntryBase type: {type.Name}");
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
            if (newOptions.Converters[i] is GameLogEntryConverter)
            {
                newOptions.Converters.RemoveAt(i);
            }
        }
        
        return newOptions;
    }
}
