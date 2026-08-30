using System.Text.Json.Nodes;
using FluentAssertions;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Serialization;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class RoleFactionRecoveryValidationTests(ITestOutputHelper output)
	: DiagnosticTestBase(output)
{
	public enum AcceptedRoleIdentificationHolderRecordTamper
	{
		StaleTurn,
		NonNight,
		Empty,
		Subset,
		Superset
	}

	[Theory]
	[InlineData(AcceptedRoleIdentificationHolderRecordTamper.StaleTurn)]
	[InlineData(AcceptedRoleIdentificationHolderRecordTamper.NonNight)]
	[InlineData(AcceptedRoleIdentificationHolderRecordTamper.Empty)]
	[InlineData(AcceptedRoleIdentificationHolderRecordTamper.Subset)]
	[InlineData(AcceptedRoleIdentificationHolderRecordTamper.Superset)]
	public void AcceptedRoleIdentificationMismatch_FailsBeforeReactionConfiguration(
		AcceptedRoleIdentificationHolderRecordTamper tamper)
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Sister A",
				"Sister B",
				"Werewolf",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.TwoSisters,
				MainRoleType.TwoSisters,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);

		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var sisterIds = players.Take(2).Select(player => player.Id).ToArray();
		var nonSister = players[2];
		var identificationInstruction = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		builder.Process(identificationInstruction.CreateResponse(sisterIds.ToHashSet()));
		var gameId = builder.GetGameState()!.Id;
		var payload = JsonNode.Parse(builder.GetGameState()!.Serialize())!.AsObject();
		var history = payload[nameof(GameSessionDto.GameHistoryLog)]!.AsArray();
		var entailment = history
			.Select(node => node!.AsObject())
			.Single(entry =>
				entry["$type"]!.GetValue<string>() ==
					nameof(FactionFactsCommittedLogEntry) &&
				entry[nameof(FactionFactsCommittedLogEntry.Source)]!
					[nameof(FactionFactSource.Identifier)]!.GetValue<string>() ==
					FactionFactSource
						.RoleIdentificationWerewolfFactionAgencyEntailmentIdentifier);
		history.Remove(entailment);
		foreach (var sister in payload[nameof(GameSessionDto.Players)]!
			         .AsArray()
			         .Select(node => node!.AsObject())
			         .Where(player => sisterIds.Contains(
				         player[nameof(PlayerDto.Id)]!.GetValue<Guid>())))
		{
			sister[nameof(PlayerDto.FactionAgentKnowledge)]!
				[Faction.Werewolf.ToString()] =
				FactionAgentKnowledge.Unknown.ToString();
		}

		var identification = history
			.Select(node => node!.AsObject())
			.Single(entry =>
				entry["$type"]!.GetValue<string>() ==
					nameof(RoleIdentificationLogEntry) &&
				entry[nameof(RoleIdentificationLogEntry.Role)]!.GetValue<string>() ==
					MainRoleType.TwoSisters.ToString());

		switch (tamper)
		{
			case AcceptedRoleIdentificationHolderRecordTamper.StaleTurn:
				identification[nameof(RoleIdentificationLogEntry.TurnNumber)] = 0;
				break;
			case AcceptedRoleIdentificationHolderRecordTamper.NonNight:
				identification[nameof(RoleIdentificationLogEntry.CurrentPhase)] =
					GamePhase.Day.ToString();
				break;
			case AcceptedRoleIdentificationHolderRecordTamper.Empty:
				identification[nameof(RoleIdentificationLogEntry.PlayerIds)] =
					new JsonArray();
				break;
			case AcceptedRoleIdentificationHolderRecordTamper.Subset:
				identification[nameof(RoleIdentificationLogEntry.PlayerIds)] =
					new JsonArray(JsonValue.Create(sisterIds[0]));
				break;
			case AcceptedRoleIdentificationHolderRecordTamper.Superset:
				identification[nameof(RoleIdentificationLogEntry.PlayerIds)] =
					new JsonArray(
						JsonValue.Create(sisterIds[0]),
						JsonValue.Create(sisterIds[1]),
						JsonValue.Create(nonSister.Id));
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(tamper), tamper, null);
		}

		var service = CreateServiceWithDuplicateReactionIds();
		Action rehydrate = () => service.RehydrateSession(payload.ToJsonString());

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*accepted observation recovery cursor*");
		service.GetGameStateView(gameId).Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedWerewolfAgentGroupMismatch_FailsBeforeReactionConfiguration()
	{
		var builder = CreateBuilder()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var observedAgent = players[0];
		var mismatchedAffectedPlayer = players[1];
		var observation = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		builder.Process(observation.CreateResponse([observedAgent.Id]));
		var gameId = builder.GetGameState()!.Id;
		var payload = JsonNode.Parse(builder.GetGameState()!.Serialize())!.AsObject();
		payload[nameof(GameSessionDto.PendingInstruction)]!
			.AsObject()[nameof(ModeratorInstruction.AffectedPlayerIds)] =
			new JsonArray(JsonValue.Create(mismatchedAffectedPlayer.Id));
		var service = CreateServiceWithDuplicateReactionIds();

		Action rehydrate = () => service.RehydrateSession(payload.ToJsonString());

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*accepted observation recovery cursor*");
		service.GetGameStateView(gameId).Should().BeNull();
		MarkTestCompleted();
	}

	private static GameService CreateServiceWithDuplicateReactionIds()
	{
		var reaction = new RecoveryOrderingSentinelReaction();
		var binding = new EliminationCascadeReactionBinding(
			reaction,
			EliminationCascadeReactionBoundary.Forced);
		return new GameService(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[binding, binding]);
	}

	private sealed class RecoveryOrderingSentinelReaction
		: IEliminationCascadeReaction
	{
		public string ReactionId => "role-faction-recovery-order-sentinel";

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input) =>
			EliminationCascadeReactionResult.NotApplicable();
	}
}
