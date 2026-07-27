using System.Text.Json;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.Tests.Helpers;

/// <summary>
/// Narrow typed seam for constructing contradictory recovery payloads.
/// Integration tests still enter through opaque Serialize/Rehydrate behavior.
/// </summary>
internal sealed class RecoveryPayloadTestDriver
{
	private static readonly JsonSerializerOptions SerializationOptions = new()
	{
		Converters =
		{
			new GameLogEntryConverter(),
			new ModeratorInstructionConverter(),
			new JsonStringEnumConverter()
		}
	};

	private readonly GameSessionDto _payload;

	private RecoveryPayloadTestDriver(GameSessionDto payload)
	{
		_payload = payload;
	}

	internal static RecoveryPayloadTestDriver Parse(string serializedSession)
	{
		var payload = JsonSerializer.Deserialize<GameSessionDto>(
			serializedSession,
			SerializationOptions)
			?? throw new InvalidOperationException(
				"The recovery test payload could not be deserialized.");
		return new RecoveryPayloadTestDriver(payload);
	}

	internal RecoveryPayloadTestDriver MismatchOneUseResource(
		Guid cursorResourceId)
	{
		RequireDomainCursor().OneUseResourceId = cursorResourceId;
		return this;
	}

	internal RecoveryPayloadTestDriver OmitSourceRole()
	{
		RequireDomainCursor().SourceRole = null;
		return this;
	}

	internal RecoveryPayloadTestDriver OmitPowerInstanceOrigin()
	{
		RequireDomainCursor().PowerInstanceOrigin = null;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestOneUseAction(
		NightActionType actionType)
	{
		RequireDomainCursor().CommittedActionType = actionType;
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is OneUseRolePowerCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    OneUseRolePowerCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed One-Use Resource action.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			ActionType = actionType
		};
		return this;
	}

	internal RecoveryPayloadTestDriver ReplacePendingInstructionWithConfirmation()
	{
		var pending = _payload.PendingInstruction
			?? throw new InvalidOperationException(
				"The recovery test payload has no pending instruction.");
		_payload.PendingInstruction = new ConfirmationInstruction(
			pending.Semantic,
			pending.PublicAnnouncement,
			pending.PrivateInstruction,
			pending.AffectedPlayerIds,
			pending.InstructionId);
		return this;
	}

	internal string Serialize() =>
		JsonSerializer.Serialize(_payload, SerializationOptions);

	private DomainRecoveryCursor RequireDomainCursor() =>
		_payload.DomainRecoveryCursor
		?? throw new InvalidOperationException(
			"The recovery test payload has no committed domain continuation.");
}
