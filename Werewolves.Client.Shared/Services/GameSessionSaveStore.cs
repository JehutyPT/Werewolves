using System.Text.Json;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Client.Services;

public interface IGameSessionSaveStore
{
	string? Load();
	void Save(string serializedSession);
	void Clear();
}

public sealed class DisabledGameSessionSaveStore : IGameSessionSaveStore
{
	public static DisabledGameSessionSaveStore Instance { get; } = new();

	private DisabledGameSessionSaveStore()
	{
	}

	public string? Load() => null;

	public void Save(string serializedSession)
	{
	}

	public void Clear()
	{
	}
}

internal abstract record LocalRecoveryPayload;

internal sealed record StagedLobbyRecoveryPayload(
	IReadOnlyList<string> PlayerNames,
	RoleLockIn RoleLockIn) : LocalRecoveryPayload;

internal sealed record ActiveGameRecoveryPayload(
	string SerializedSession) : LocalRecoveryPayload;

internal static class LocalRecoveryPayloadCodec
{
	private const int CurrentSchemaVersion = 1;
	private const string StagedLobbyKind = "StagedLobby";
	private const string ActiveGameKind = "ActiveGame";
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static string SerializeStagedLobby(
		IReadOnlyList<string> playerNames,
		RoleLockIn roleLockIn)
	{
		ArgumentNullException.ThrowIfNull(playerNames);
		ArgumentNullException.ThrowIfNull(roleLockIn);
		return JsonSerializer.Serialize(
			new RecoveryEnvelopeDto(
				CurrentSchemaVersion,
				StagedLobbyKind,
				new StagedLobbyDto(
					playerNames.ToArray(),
					RoleLockInDto.FromRoleLockIn(roleLockIn)),
				ActiveGame: null),
			JsonOptions);
	}

	public static string SerializeActiveGame(string serializedSession)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(serializedSession);
		return JsonSerializer.Serialize(
			new RecoveryEnvelopeDto(
				CurrentSchemaVersion,
				ActiveGameKind,
				StagedLobby: null,
				new ActiveGameDto(serializedSession)),
			JsonOptions);
	}

	public static LocalRecoveryPayload Deserialize(string payload)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(payload);
		try
		{
			var envelope = JsonSerializer.Deserialize<RecoveryEnvelopeDto>(payload, JsonOptions);
			if (envelope is { SchemaVersion: CurrentSchemaVersion, Kind: StagedLobbyKind, StagedLobby: not null })
			{
				return new StagedLobbyRecoveryPayload(
					envelope.StagedLobby.PlayerNames.ToArray(),
					envelope.StagedLobby.RoleLockIn.ToRoleLockIn());
			}

			if (envelope is { SchemaVersion: CurrentSchemaVersion, Kind: ActiveGameKind, ActiveGame: not null })
			{
				return new ActiveGameRecoveryPayload(envelope.ActiveGame.SerializedSession);
			}
		}
		catch (JsonException)
		{
			// A pre-discriminator save is handled by the compatibility path below.
		}

		// Existing installations may still have a raw Core session payload. Treat it
		// as ActiveGame and let Core perform the authoritative validation.
		return new ActiveGameRecoveryPayload(payload);
	}

	private sealed record RecoveryEnvelopeDto(
		int SchemaVersion,
		string Kind,
		StagedLobbyDto? StagedLobby,
		ActiveGameDto? ActiveGame);

	private sealed record StagedLobbyDto(
		IReadOnlyList<string> PlayerNames,
		RoleLockInDto RoleLockIn);

	private sealed record ActiveGameDto(string SerializedSession);

	private sealed record RoleLockInDto(
		long Version,
		int PlayerCount,
		IReadOnlyList<PhysicalCharacterCardDto> RoleComposition,
		IReadOnlyList<Guid> DealPoolCardIds,
		Guid? Offer1CardId,
		Guid? Offer2CardId)
	{
		public static RoleLockInDto FromRoleLockIn(RoleLockIn roleLockIn) =>
			new(
				roleLockIn.Version,
				roleLockIn.PlayerCount,
				roleLockIn.RoleComposition
					.Select(card => new PhysicalCharacterCardDto(card.Id, card.PrintedRole))
					.ToArray(),
				roleLockIn.DealPool.Select(card => card.Id).ToArray(),
				roleLockIn.Offer1?.Id,
				roleLockIn.Offer2?.Id);

		public RoleLockIn ToRoleLockIn() =>
			new(
				Version,
				PlayerCount,
				RoleComposition.Select(card =>
					new PhysicalCharacterCard(card.Id, card.PrintedRole)),
				DealPoolCardIds,
				Offer1CardId,
				Offer2CardId);
	}

	private sealed record PhysicalCharacterCardDto(
		Guid Id,
		MainRoleType PrintedRole);
}
