using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class KnightWithTheRustySwordRoleTests : DiagnosticTestBase
{
	public KnightWithTheRustySwordRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void Dawn_QualifyingWerewolfAttackSchedulesDiseaseOnFirstClockwiseEligibleAgentAfterCascade()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Knight",
				"Clockwise Werewolf",
				"Villager A",
				"Villager B",
				"Villager C")
			.WithRoles(
				MainRoleType.KnightWithRustySword,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(
			players[0].Id,
			MainRoleType.KnightWithRustySword);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[1].Id],
				players[0].Id));
		builder.Process(nightEnd.CreateResponse());

		builder.CompleteDawnPhase().IsSuccess.Should().BeTrue();

		players[0].State.Health.Should().Be(PlayerHealth.Dead);
		players[1].State
			.HasStatusEffect(StatusEffectTypes.RustySwordDisease)
			.Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == players[1].Id &&
				entry.EffectType == StatusEffectTypes.RustySwordDisease &&
				entry.IsActive &&
				entry.TurnNumber == 1);
		builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Day);
		MarkTestCompleted();
	}

	[Fact]
	public void FollowingNight_DueDiseaseBecomesOneRustySwordActionAndAnnouncesCauseBeforeElimination()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Knight",
				"Diseased Werewolf",
				"Villager A",
				"Villager B",
				"Villager C",
				"Villager D")
			.WithRoles(
				MainRoleType.KnightWithRustySword,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(
			players[0].Id,
			MainRoleType.KnightWithRustySword);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var firstNightEnd =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[1].Id],
					players[0].Id));
		builder.Process(firstNightEnd.CreateResponse());
		builder.CompleteDawnPhase().IsSuccess.Should().BeTrue();

		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		builder.Process(vote.CreateResponse([])).IsSuccess.Should().BeTrue();
		builder.ConfirmNightStart();
		var secondNightEnd =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[1].Id],
					players[2].Id));

		var session = builder.GetGameState()!;
		session.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().ContainSingle(entry =>
				entry.TurnNumber == 2 &&
				entry.CurrentPhase == GamePhase.Night &&
				entry.ActionType == NightActionType.RustySword &&
				entry.TargetIds != null &&
				entry.TargetIds.Count == 1 &&
				entry.TargetIds[0] == players[1].Id);
		players[1].State
			.HasStatusEffect(StatusEffectTypes.RustySwordDisease)
			.Should().BeFalse();

		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			session.Serialize());
		var recoveredNightEnd = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredNightEnd.InstructionId.Should().Be(secondNightEnd.InstructionId);
		var reveal = recoveredService
			.ProcessInstruction(
				recoveredGameId,
				recoveredNightEnd.CreateResponse())
			.ModeratorInstruction
			.Should().BeOfType<AssignRolesInstruction>().Subject;

		reveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDawnVictimRoles);
		reveal.PublicAnnouncement.Should().Contain(
			GameStrings.RustySwordDiseaseEliminationAnnouncement.Format(
				players[1].Name));

		CompleteRecoveredDawn(
			recoveredService,
			recoveredGameId,
			new Dictionary<Guid, MainRoleType>
		{
			[players[1].Id] = MainRoleType.SimpleWerewolf,
			[players[2].Id] = MainRoleType.SimpleVillager
		});
		var recoveredState =
			recoveredService.GetGameStateView(recoveredGameId)!;
		recoveredState.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == players[1].Id &&
				entry.Reason == EliminationReason.RustySword);
		recoveredState.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().ContainSingle(entry =>
				entry.TurnNumber == 2 &&
				entry.CurrentPhase == GamePhase.Night &&
				entry.ActionType == NightActionType.RustySword &&
				entry.TargetIds != null &&
				entry.TargetIds.Count == 1 &&
				entry.TargetIds[0] == players[1].Id);
		var afterCompletion = recoveredState.Serialize();
		recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredNightEnd.CreateResponse());

		recoveredService.GetGameStateView(recoveredGameId)!
			.Serialize().Should().Be(afterCompletion);
		MarkTestCompleted();
	}

	[Fact]
	public void Dawn_DefendedWerewolfAttackDoesNotEliminateKnightOrAttemptDisease()
	{
		var policy = new RecordingKnightAvailabilityPolicy(isAvailable: true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(
				"Knight",
				"Werewolf",
				"Villager A",
				"Villager B",
				"Villager C")
			.WithRoles(
				MainRoleType.KnightWithRustySword,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(
			players[0].Id,
			MainRoleType.KnightWithRustySword);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.ArrangeNightAction(
			NightActionType.DefenderProtect,
			players[0].Id);
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[1].Id],
				players[0].Id));
		builder.Process(nightEnd.CreateResponse());

		builder.CompleteDawnPhase().IsSuccess.Should().BeTrue();

		players[0].State.Health.Should().Be(PlayerHealth.Alive);
		policy.KnightAttempts.Should().BeEmpty();
		players.Should().OnlyContain(player =>
			!player.State.HasStatusEffect(
				StatusEffectTypes.RustySwordDisease));
		builder.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == players[0].Id);
		MarkTestCompleted();
	}

	[Fact]
	public void Dawn_QualifyingEliminationWithDeniedAutomaticPowerEvaluatesOnceAndSkips()
	{
		var policy = new RecordingKnightAvailabilityPolicy(isAvailable: false);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(
				"Knight",
				"Werewolf",
				"Villager A",
				"Villager B",
				"Villager C")
			.WithRoles(
				MainRoleType.KnightWithRustySword,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(
			players[0].Id,
			MainRoleType.KnightWithRustySword);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[1].Id],
				players[0].Id));
		builder.Process(nightEnd.CreateResponse());

		builder.CompleteDawnPhase().IsSuccess.Should().BeTrue();

		players[0].State.Health.Should().Be(PlayerHealth.Dead);
		policy.KnightAttempts.Should().ContainSingle();
		var attempt = policy.KnightAttempts.Single();
		attempt.ActingPlayer.Id.Should().Be(players[0].Id);
		attempt.SourceRole.Should().Be(MainRoleType.KnightWithRustySword);
		attempt.SourcePower.Identifier.Value.Should().Be(
			"knight-rusty-sword-disease");
		attempt.SourcePower.Category.Should().Be(RolePowerCategory.Automatic);
		attempt.PowerInstance.Id.Should().Be(players[0].Id);
		attempt.PowerInstance.Origin.Should().Be(
			RolePowerInstanceOrigin.Native);
		attempt.OneUseResource.Should().BeNull();
		players.Should().OnlyContain(player =>
			!player.State.HasStatusEffect(
				StatusEffectTypes.RustySwordDisease));
		MarkTestCompleted();
	}

	[Fact]
	public void Dawn_ClockwiseScanAfterCascadeSkipsDeadAgentAndWrapsToLivingAgent()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Knight",
				"Clockwise Werewolf",
				"Villager A",
				"Villager B",
				"Villager C",
				"Wrapped Werewolf")
			.WithRoles(
				MainRoleType.KnightWithRustySword,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleWerewolf);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(
			players[0].Id,
			MainRoleType.KnightWithRustySword);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			players[1].Id,
			players[5].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.ArrangeNightAction(
			NightActionType.WitchKill,
			players[1].Id);
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[1].Id, players[5].Id],
				players[0].Id));
		builder.Process(nightEnd.CreateResponse());

		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[players[0].Id] = MainRoleType.KnightWithRustySword,
			[players[1].Id] = MainRoleType.SimpleWerewolf
		}).IsSuccess.Should().BeTrue();

		players[1].State.Health.Should().Be(PlayerHealth.Dead);
		players[1].State.HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeFalse();
		players[5].State.HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == players[5].Id &&
				entry.EffectType == StatusEffectTypes.RustySwordDisease &&
				entry.IsActive);
		MarkTestCompleted();
	}

	[Fact]
	public void Dawn_NoLivingEligibleAgentEvaluatesOnceAndCompletesSilently()
	{
		var policy = new RecordingKnightAvailabilityPolicy(isAvailable: true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(
				"Knight",
				"Werewolf A",
				"Villager A",
				"Werewolf B",
				"Villager B",
				"Villager C")
			.WithRoles(
				MainRoleType.KnightWithRustySword,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(
			players[0].Id,
			MainRoleType.KnightWithRustySword);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			players[1].Id,
			players[3].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder
			.ArrangeNightAction(NightActionType.WitchKill, players[1].Id)
			.ArrangeNightAction(NightActionType.WitchKill, players[3].Id);
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[1].Id, players[3].Id],
				players[0].Id));
		builder.Process(nightEnd.CreateResponse());

		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[players[0].Id] = MainRoleType.KnightWithRustySword,
			[players[1].Id] = MainRoleType.SimpleWerewolf,
			[players[3].Id] = MainRoleType.SimpleWerewolf
		}).IsSuccess.Should().BeTrue();

		players[1].State.Health.Should().Be(PlayerHealth.Dead);
		players[3].State.Health.Should().Be(PlayerHealth.Dead);
		policy.KnightAttempts.Should().ContainSingle();
		players.Should().OnlyContain(player =>
			!player.State.HasStatusEffect(
				StatusEffectTypes.RustySwordDisease));
		builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry =>
				entry.EffectType == StatusEffectTypes.RustySwordDisease);
		MarkTestCompleted();
	}

	[Fact]
	public void Dawn_DiseaseIsScheduledBeforeBearGrowlAndImmediateWerewolfVictory()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Knight",
				"Werewolf A",
				"Werewolf B",
				"Werewolf C",
				"Bear Tamer",
				"Villager")
			.WithRoles(
				MainRoleType.KnightWithRustySword,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.BearTamer,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(
			players[0].Id,
			MainRoleType.KnightWithRustySword);
		builder.ArrangeKnownRole(players[4].Id, MainRoleType.BearTamer);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			players[1].Id,
			players[2].Id,
			players[3].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[1].Id, players[2].Id, players[3].Id],
				players[0].Id));
		builder.Process(nightEnd.CreateResponse());

		var growl = AdvanceDawnToBearTamerGrowl(
			builder,
			new Dictionary<Guid, MainRoleType>
			{
				[players[0].Id] = MainRoleType.KnightWithRustySword
			});

		players[1].State.HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeTrue();
		growl.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceBearTamerGrowl);
		builder.Process(growl.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<FinishedGameConfirmationInstruction>();
		MarkTestCompleted();
	}

	[Fact]
	public void ScheduledDisease_IsPlayerBoundAcrossLaterAgentFactsAndFreshServiceRecovery()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Knight",
				"Original Agent",
				"Villager A",
				"Later Agent",
				"Villager B",
				"Villager C")
			.WithRoles(
				MainRoleType.KnightWithRustySword,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(
			players[0].Id,
			MainRoleType.KnightWithRustySword);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[1].Id],
				players[0].Id));
		builder.Process(nightEnd.CreateResponse());
		builder.CompleteDawnPhase().IsSuccess.Should().BeTrue();

		builder.ArrangeKnownWerewolfFactionAgentGroup(players[3].Id);

		players[1].State.GetFactionAgentKnowledge(Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		players[3].State.GetFactionAgentKnowledge(Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		players[1].State.HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeTrue();
		players[3].State.HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeFalse();

		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredPlayers = recoveredService
			.GetGameStateView(recoveredGameId)!
			.GetPlayers()
			.ToDictionary(player => player.Id);

		recoveredPlayers[players[1].Id].State.HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeTrue();
		recoveredPlayers[players[3].Id].State.HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeFalse();
		recoveredService.GetGameStateView(recoveredGameId)!.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == players[1].Id &&
				entry.EffectType == StatusEffectTypes.RustySwordDisease &&
				entry.IsActive);
		MarkTestCompleted();
	}

	[Fact]
	public void FollowingNight_DeadDiseasedTargetExpiresWithoutRustySwordAction()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Knight",
				"Diseased Werewolf",
				"Villager A",
				"Surviving Werewolf",
				"Villager B",
				"Villager C")
			.WithRoles(
				MainRoleType.KnightWithRustySword,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(
			players[0].Id,
			MainRoleType.KnightWithRustySword);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			players[1].Id,
			players[3].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var firstNightEnd =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[1].Id, players[3].Id],
					players[0].Id));
		builder.Process(firstNightEnd.CreateResponse());
		builder.CompleteDawnPhase().IsSuccess.Should().BeTrue();
		players[1].State.HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeTrue();

		builder.ArrangeEliminatedPlayer(players[1].Id);
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();
		builder.ConfirmNightStart();
		InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[3].Id],
				players[2].Id));

		players[1].State.Health.Should().Be(PlayerHealth.Dead);
		players[1].State.HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeFalse();
		builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().NotContain(entry =>
				entry.TurnNumber == 2 &&
				entry.CurrentPhase == GamePhase.Night &&
				entry.ActionType == NightActionType.RustySword);
		builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Where(entry =>
				entry.PlayerId == players[1].Id &&
				entry.EffectType == StatusEffectTypes.RustySwordDisease)
			.Select(entry => entry.IsActive)
			.Should().Equal(true, false);
		MarkTestCompleted();
	}

	[Fact]
	public void FollowingDawn_RustySwordAndExistingCauseOnSameTargetEliminateOnce()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Knight",
				"Diseased Werewolf",
				"Surviving Werewolf",
				"Villager A",
				"Villager B",
				"Villager C",
				"Villager D")
			.WithRoles(
				MainRoleType.KnightWithRustySword,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(
			players[0].Id,
			MainRoleType.KnightWithRustySword);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			players[1].Id,
			players[2].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var firstNightEnd =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[1].Id, players[2].Id],
					players[0].Id));
		builder.Process(firstNightEnd.CreateResponse());
		builder.CompleteDawnPhase().IsSuccess.Should().BeTrue();
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();
		builder.ConfirmNightStart();
		builder.ArrangeNightAction(
			NightActionType.WitchKill,
			players[1].Id);
		var secondNightEnd =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[1].Id, players[2].Id],
					players[3].Id));
		builder.Process(secondNightEnd.CreateResponse());

		builder.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.TurnNumber == 2 &&
				entry.PlayerId == players[1].Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[players[1].Id] = MainRoleType.SimpleWerewolf,
			[players[3].Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.TurnNumber == 2 &&
				entry.CurrentPhase == GamePhase.Dawn &&
				entry.PlayerId == players[1].Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Where(entry =>
				entry.TurnNumber == 2 &&
				entry.TargetIds != null &&
				entry.TargetIds.Contains(players[1].Id))
			.Select(entry => entry.ActionType)
			.Should().Contain([
				NightActionType.WitchKill,
				NightActionType.RustySword
			]);
		MarkTestCompleted();
	}

	private static ConfirmationInstruction AdvanceDawnToBearTamerGrowl(
		GameTestBuilder builder,
		IReadOnlyDictionary<Guid, MainRoleType> roleAssignments)
	{
		for (var step = 0; step < 20; step++)
		{
			switch (builder.GetCurrentInstruction())
			{
				case ConfirmationInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.AnnounceBearTamerGrowl
				} growl:
					return growl;
				case ConfirmationInstruction confirmation:
					builder.Process(confirmation.CreateResponse());
					break;
				case AssignRolesInstruction assignRoles:
					builder.Process(assignRoles.CreateResponse(
						assignRoles.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							playerId => roleAssignments.GetValueOrDefault(
								playerId,
								MainRoleType.SimpleVillager))));
					break;
				case null:
					throw new InvalidOperationException(
						"Dawn did not expose a pending Moderator Instruction.");
				case var instruction:
					throw new InvalidOperationException(
						$"Unexpected Dawn instruction {instruction.GetType().Name}.");
			}
		}

		throw new InvalidOperationException(
			"Dawn did not reach the Bear Tamer growl.");
	}

	private static void CompleteRecoveredDawn(
		GameService service,
		Guid gameId,
		IReadOnlyDictionary<Guid, MainRoleType> roleAssignments)
	{
		for (var step = 0; step < 20; step++)
		{
			var state = service.GetGameStateView(gameId)
				?? throw new InvalidOperationException(
					"The recovered Game Session is unavailable.");
			var instruction = service.GetCurrentInstruction(gameId);
			if (state.GetCurrentPhase() == GamePhase.Day ||
			    instruction is FinishedGameConfirmationInstruction)
			{
				return;
			}

			switch (instruction)
			{
				case ConfirmationInstruction confirmation:
					service.ProcessInstruction(
						gameId,
						confirmation.CreateResponse());
					break;
				case AssignRolesInstruction assignRoles:
					service.ProcessInstruction(
						gameId,
						assignRoles.CreateResponse(
							assignRoles.PlayersForAssignment.ToDictionary(
								playerId => playerId,
								playerId => roleAssignments.GetValueOrDefault(
									playerId,
									MainRoleType.SimpleVillager))));
					break;
				case null:
					throw new InvalidOperationException(
						"Recovered Dawn has no pending Moderator Instruction.");
				default:
					throw new InvalidOperationException(
						$"Unexpected recovered Dawn instruction {instruction.GetType().Name}.");
			}
		}

		throw new InvalidOperationException(
			"Recovered Dawn did not reach a stable boundary.");
	}

	private sealed class RecordingKnightAvailabilityPolicy(bool isAvailable)
		: IRolePowerAvailabilityPolicy
	{
		internal List<RolePowerAttempt> KnightAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			if (attempt.SourceRole != MainRoleType.KnightWithRustySword)
			{
				return RolePowerAvailabilityResult.Allowed;
			}

			KnightAttempts.Add(attempt);
			return isAvailable
				? RolePowerAvailabilityResult.Allowed
				: RolePowerAvailabilityResult.Denied;
		}
	}
}
