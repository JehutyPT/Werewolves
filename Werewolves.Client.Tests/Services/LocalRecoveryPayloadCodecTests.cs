using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public sealed class LocalRecoveryPayloadCodecTests
{
	private static readonly JsonSerializerOptions LegacyJsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	[Fact]
	public void StagedLobbySchema2_RoundTripsExactTypedSetupAggregate()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(Id(1), "Ana"),
			new(Id(2), "Bruno"),
			new(Id(3), "Catarina"),
			new(Id(4), "Diana"),
			new(Id(5), "Eduardo")
		];
		PhysicalCharacterCard[] cards =
		[
			new(Id(101), MainRoleType.Thief),
			new(Id(102), MainRoleType.SimpleWerewolf),
			new(Id(103), MainRoleType.SimpleVillager),
			new(Id(104), MainRoleType.SimpleVillager),
			new(Id(105), MainRoleType.SimpleVillager),
			new(Id(106), MainRoleType.PrejudicedManipulator),
			new(Id(107), MainRoleType.SimpleVillager)
		];
		var roleLockIn = new RoleLockIn(
			version: 7,
			playerCount: roster.Length,
			cards,
			cards.Take(5).Select(card => card.Id),
			offer1CardId: cards[5].Id,
			offer2CardId: cards[6].Id);
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			[roster[0].Id, roster[3].Id],
			[roster[1].Id, roster[2].Id, roster[4].Id]);

		var serialized = LocalRecoveryPayloadCodec.SerializeStagedLobby(
			roster,
			roleLockIn,
			partition);
		var recovered = LocalRecoveryPayloadCodec.Deserialize(serialized)
			.Should().BeOfType<StagedLobbyRecoveryPayload>()
			.Subject;

		recovered.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.Should().Equal(roster.Select(player => (player.Id, player.Name)));
		recovered.RoleLockIn.Version.Should().Be(7);
		recovered.RoleLockIn.PlayerCount.Should().Be(5);
		recovered.RoleLockIn.RoleComposition
			.Select(card => (card.Id, card.PrintedRole))
			.Should().Equal(cards.Select(card => (card.Id, card.PrintedRole)));
		recovered.RoleLockIn.DealPool.Select(card => card.Id)
			.Should().Equal(cards.Take(5).Select(card => card.Id));
		(recovered.RoleLockIn.Offer1!.Id, recovered.RoleLockIn.Offer1.PrintedRole)
			.Should().Be((cards[5].Id, cards[5].PrintedRole));
		(recovered.RoleLockIn.Offer2!.Id, recovered.RoleLockIn.Offer2.PrintedRole)
			.Should().Be((cards[6].Id, cards[6].PrintedRole));
		recovered.PublicGroupPartition.Should().NotBeNull();
		recovered.PublicGroupPartition!.FirstGroupPlayerIds
			.Should().BeEquivalentTo(partition.FirstGroupPlayerIds);
		recovered.PublicGroupPartition.SecondGroupPlayerIds
			.Should().BeEquivalentTo(partition.SecondGroupPlayerIds);
	}

	[Fact]
	public void ActiveGameSchema2_RoundTripsExactSessionJson()
	{
		const string serializedSession = "{\"game\":\"exact-session-json\"}";

		var serialized = LocalRecoveryPayloadCodec.SerializeActiveGame(
			serializedSession);
		var recovered = LocalRecoveryPayloadCodec.Deserialize(serialized)
			.Should().BeOfType<ActiveGameRecoveryPayload>()
			.Subject;
		using var document = System.Text.Json.JsonDocument.Parse(serialized);

		document.RootElement.GetProperty("schemaVersion").GetInt32()
			.Should().Be(2);
		recovered.SerializedSession.Should().Be(serializedSession);
	}

	[Fact]
	public void StagedLobbySchema2_WithNoPartition_RoundTripsNullWithoutDefaulting()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(Id(11), "Ana"),
			new(Id(12), "Bruno"),
			new(Id(13), "Catarina"),
			new(Id(14), "Diana"),
			new(Id(15), "Eduardo")
		];
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 3,
			playerCount: roster.Length,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var serialized = LocalRecoveryPayloadCodec.SerializeStagedLobby(
			roster,
			roleLockIn,
			publicGroupPartition: null);
		var recovered = LocalRecoveryPayloadCodec.Deserialize(serialized)
			.Should().BeOfType<StagedLobbyRecoveryPayload>()
			.Subject;

		recovered.PublicGroupPartition.Should().BeNull();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void OtherwiseValidSchema1Payload_IsRejected(bool stagedLobby)
	{
		var schema1Payload = stagedLobby
			? CreateSchema1StagedLobbyPayload()
			: CreateSchema1ActiveGamePayload("{\"opaque\":\"session-json\"}");
		var act = () => LocalRecoveryPayloadCodec.Deserialize(schema1Payload);

		act.Should().Throw<InvalidOperationException>();
	}

	[Theory]
	[InlineData("StagedLobby", true, true)]
	[InlineData("ActiveGame", true, true)]
	[InlineData("StagedLobby", false, true)]
	[InlineData("ActiveGame", true, false)]
	[InlineData("StagedLobby", false, false)]
	[InlineData("ActiveGame", false, false)]
	public void Schema2Envelope_RequiresExactDiscriminatedUnion(
		string kind,
		bool includeStagedLobby,
		bool includeActiveGame)
	{
		var payload = CreateSchema2Envelope(
			kind,
			includeStagedLobby,
			includeActiveGame);

		var act = () => LocalRecoveryPayloadCodec.Deserialize(payload);

		act.Should().Throw<InvalidOperationException>();
	}

	private static string CreateSchema2Envelope(
		string kind,
		bool includeStagedLobby,
		bool includeActiveGame)
	{
		var stagedEnvelope = JsonNode.Parse(CreateSchema2StagedLobbyPayload())!;
		var activeEnvelope = JsonNode.Parse(
			LocalRecoveryPayloadCodec.SerializeActiveGame(
				"{\"valid\":\"session-json\"}"))!;
		return new JsonObject
		{
			["schemaVersion"] = 2,
			["kind"] = kind,
			["stagedLobby"] = includeStagedLobby
				? stagedEnvelope["stagedLobby"]!.DeepClone()
				: null,
			["activeGame"] = includeActiveGame
				? activeEnvelope["activeGame"]!.DeepClone()
				: null
		}.ToJsonString();
	}

	private static string CreateSchema2StagedLobbyPayload()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(Id(31), "Ana"),
			new(Id(32), "Bruno"),
			new(Id(33), "Catarina"),
			new(Id(34), "Diana"),
			new(Id(35), "Eduardo")
		];
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: roster.Length,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		return LocalRecoveryPayloadCodec.SerializeStagedLobby(
			roster,
			roleLockIn,
			publicGroupPartition: null);
	}

	private static string CreateSchema1StagedLobbyPayload()
	{
		var cards = new[]
		{
			new { Id = Id(201), PrintedRole = MainRoleType.SimpleWerewolf },
			new { Id = Id(202), PrintedRole = MainRoleType.SimpleVillager },
			new { Id = Id(203), PrintedRole = MainRoleType.SimpleVillager },
			new { Id = Id(204), PrintedRole = MainRoleType.SimpleVillager },
			new { Id = Id(205), PrintedRole = MainRoleType.SimpleVillager }
		};
		return JsonSerializer.Serialize(
			new
			{
				SchemaVersion = 1,
				Kind = "StagedLobby",
				StagedLobby = new
				{
					PlayerNames = new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" },
					RoleLockIn = new
					{
						Version = 1L,
						PlayerCount = 5,
						RoleComposition = cards,
						DealPoolCardIds = cards.Select(card => card.Id).ToArray(),
						Offer1CardId = (Guid?)null,
						Offer2CardId = (Guid?)null
					}
				},
				ActiveGame = (object?)null
			},
			LegacyJsonOptions);
	}

	private static string CreateSchema1ActiveGamePayload(string serializedSession) =>
		JsonSerializer.Serialize(
			new
			{
				SchemaVersion = 1,
				Kind = "ActiveGame",
				StagedLobby = (object?)null,
				ActiveGame = new { SerializedSession = serializedSession }
			},
			LegacyJsonOptions);

	private static Guid Id(int value) =>
		Guid.Parse($"30000000-0000-0000-0000-{value:D12}");
}
