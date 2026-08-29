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
	IReadOnlyList<GameSessionPlayerConfig> PlayerRoster,
	RoleLockIn RoleLockIn,
	ActorSetupCards ActorSetupCards,
	PublicGroupPartition? PublicGroupPartition) : LocalRecoveryPayload;

internal sealed record ActiveGameRecoveryPayload(
	string SerializedSession) : LocalRecoveryPayload;

internal static class LocalRecoveryPayloadCodec
{
	private const int CurrentSchemaVersion = 3;
	private const string StagedLobbyKind = "StagedLobby";
	private const string ActiveGameKind = "ActiveGame";
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static string SerializeStagedLobby(
		LobbySetupAggregate aggregate)
	{
		ArgumentNullException.ThrowIfNull(aggregate);
		var roleLockIn = aggregate.AcceptedRoleLockIn ??
			throw new InvalidOperationException(
				"A staged Lobby aggregate requires an accepted Role Lock-In.");
		return SerializeStagedLobby(
			aggregate.PlayerRoster,
			roleLockIn,
			aggregate.AcceptedActorSetupCards,
			aggregate.AcceptedPublicGroupPartition);
	}

	public static string SerializeStagedLobby(
		IReadOnlyList<GameSessionPlayerConfig> playerRoster,
		RoleLockIn roleLockIn,
		ActorSetupCards actorSetupCards,
		PublicGroupPartition? publicGroupPartition)
	{
		ArgumentNullException.ThrowIfNull(playerRoster);
		ArgumentNullException.ThrowIfNull(roleLockIn);
		ArgumentNullException.ThrowIfNull(actorSetupCards);
		return JsonSerializer.Serialize(
			new RecoveryEnvelopeDto(
				CurrentSchemaVersion,
				StagedLobbyKind,
				new StagedLobbyDto(
					playerRoster
						.Select(GameSessionPlayerConfigDto.FromValue)
						.ToArray(),
					RoleLockInDto.FromRoleLockIn(roleLockIn),
					ActorSetupCardsDto.FromValue(actorSetupCards),
					publicGroupPartition is null
						? null
						: PublicGroupPartitionDto.FromValue(publicGroupPartition)),
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
		var envelope = JsonSerializer.Deserialize<RecoveryEnvelopeDto>(
			payload,
			JsonOptions);
		if (envelope is
		{
			SchemaVersion: CurrentSchemaVersion,
				Kind: StagedLobbyKind,
				StagedLobby: { ActorSetupCards: not null },
				ActiveGame: null
			})
		{
			var playerRoster = envelope.StagedLobby.PlayerRoster
				.Select(player => player.ToValue())
				.ToArray();
			return new StagedLobbyRecoveryPayload(
				playerRoster,
				envelope.StagedLobby.RoleLockIn.ToRoleLockIn(),
				envelope.StagedLobby.ActorSetupCards.ToValue(),
				envelope.StagedLobby.PublicGroupPartition?.ToValue(
					playerRoster.Select(player => player.Id)));
		}

		if (envelope is
		{
			SchemaVersion: CurrentSchemaVersion,
				Kind: ActiveGameKind,
				StagedLobby: null,
				ActiveGame: not null
			})
		{
			return new ActiveGameRecoveryPayload(envelope.ActiveGame.SerializedSession);
		}

		throw new InvalidOperationException(
			"The local recovery payload is invalid or unsupported.");
	}

	private sealed record RecoveryEnvelopeDto(
		int SchemaVersion,
		string Kind,
		StagedLobbyDto? StagedLobby,
		ActiveGameDto? ActiveGame);

	private sealed record StagedLobbyDto(
		IReadOnlyList<GameSessionPlayerConfigDto> PlayerRoster,
		RoleLockInDto RoleLockIn,
		ActorSetupCardsDto ActorSetupCards,
		PublicGroupPartitionDto? PublicGroupPartition);

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

	private sealed record ActorSetupCardsDto(
		long Version,
		IReadOnlyList<PhysicalCharacterCardDto> Cards)
	{
		public static ActorSetupCardsDto FromValue(ActorSetupCards actorSetupCards) =>
			new(
				actorSetupCards.Version,
				actorSetupCards.Cards
					.Select(card => new PhysicalCharacterCardDto(
						card.Id,
						card.PrintedRole))
					.ToArray());

		public ActorSetupCards ToValue() =>
			new(
				Version,
				Cards.Select(card =>
					new PhysicalCharacterCard(card.Id, card.PrintedRole)));
	}

	private sealed record GameSessionPlayerConfigDto(Guid Id, string Name)
	{
		public static GameSessionPlayerConfigDto FromValue(
			GameSessionPlayerConfig player) =>
			new(player.Id, player.Name);

		public GameSessionPlayerConfig ToValue() => new(Id, Name);
	}

	private sealed record PublicGroupPartitionDto(
		IReadOnlyList<Guid> FirstGroupPlayerIds,
		IReadOnlyList<Guid> SecondGroupPlayerIds)
	{
		public static PublicGroupPartitionDto FromValue(
			PublicGroupPartition partition) =>
			new(
				partition.FirstGroupPlayerIds.ToArray(),
				partition.SecondGroupPlayerIds.ToArray());

		public PublicGroupPartition ToValue(IEnumerable<Guid> rosterPlayerIds) =>
			PublicGroupPartition.Create(
				rosterPlayerIds,
				FirstGroupPlayerIds,
				SecondGroupPlayerIds);
	}
}
