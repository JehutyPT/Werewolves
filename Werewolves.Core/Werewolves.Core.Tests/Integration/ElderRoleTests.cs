using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class ElderRoleTests : DiagnosticTestBase
{
	public ElderRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void UnknownHolder_KnownNonElderAttackTarget_DoesNotRequestIdentification()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var knownVillagerTarget = players[2];
		builder.ArrangeKnownPhysicalRole(
			knownVillagerTarget.Id,
			MainRoleType.SimpleVillager);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					knownVillagerTarget.Id));

		var afterNight = builder.Process(finishNight.CreateResponse());

		afterNight.IsSuccess.Should().BeTrue();
		afterNight.ModeratorInstruction.Should().NotBeOfType<
			SelectPlayersInstruction>(because:
			"the only unblocked attack target is already known not to be Elder");
		builder.GetGameState()!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry => entry.Role == MainRoleType.Elder);
		builder.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == knownVillagerTarget.Id &&
				entry.Reason == EliminationReason.WerewolfAttack);
		MarkTestCompleted();
	}

	[Fact]
	public void UnknownHolder_EmptyExactObservation_CommitsKnownEmptyAndResolvesAttackNormally()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var attackedPlayer = players[2];
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					attackedPlayer.Id));
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(finishNight.CreateResponse()));

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.Elder);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.SingleOptional);

		var afterEmptyObservation = builder.Process(
			identification.CreateResponse([]));

		afterEmptyObservation.IsSuccess.Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.Elder &&
				entry.PlayerIds.Count == 0);
		builder.GetGameState()!.GetPlayers()
			.Should().OnlyContain(player =>
				!player.State.HasStatusEffect(
					StatusEffectTypes.ElderProtectionLost));
		builder.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == attackedPlayer.Id &&
				entry.Reason == EliminationReason.WerewolfAttack);
		MarkTestCompleted();
	}

	[Fact]
	public void UnknownHolder_FirstUnblockedAttackIdentifiesJustInTime_WhileDefenderBlockDoesNot()
	{
		var unblocked = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		unblocked.StartGame();
		var unblockedPlayers = unblocked.GetGameState()!.GetPlayers().ToArray();
		var werewolf = unblockedPlayers[0];
		var elder = unblockedPlayers[1];
		unblocked.ConfirmGameStart();
		unblocked.ConfirmNightStart();
		var finishNight = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			unblocked.CompleteWerewolfNightAction([werewolf.Id], elder.Id));

		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				unblocked.Process(finishNight.CreateResponse()));

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.Elder);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.SingleOptional);
		identification.PublicAnnouncement.Should().BeNull();

		unblocked.Process(identification.CreateResponse([elder.Id]))
			.IsSuccess.Should().BeTrue();
		elder.State.Health.Should().Be(PlayerHealth.Alive);
		elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeTrue();
		unblocked.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == elder.Id);
		unblocked.GetGameState()!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.Elder &&
				entry.PlayerIds.SetEquals(new[] { elder.Id }));

		var blocked = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		blocked.StartGame();
		var blockedPlayers = blocked.GetGameState()!.GetPlayers().ToArray();
		var defender = blockedPlayers[0];
		var blockedWerewolf = blockedPlayers[1];
		var protectedElder = blockedPlayers[2];
		blocked.ConfirmGameStart();
		var defenderIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				blocked.ConfirmNightStart());
		var defenderTarget =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				blocked.Process(
					defenderIdentification.CreateResponse([defender.Id])));
		var defenderSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				blocked.Process(
					defenderTarget.CreateResponse([protectedElder.Id])));
		blocked.Process(defenderSleep.CreateResponse()).IsSuccess.Should().BeTrue();
		var blockedFinishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				blocked.CompleteWerewolfNightAction(
					[blockedWerewolf.Id],
					protectedElder.Id));

		var afterBlockedAttack = blocked.Process(
			blockedFinishNight.CreateResponse());

		afterBlockedAttack.ModeratorInstruction.Should().NotBeOfType<
			SelectPlayersInstruction>(because:
			"Defender resolves before an Elder resistance check");
		protectedElder.State.HasStatusEffect(
			StatusEffectTypes.ElderProtectionLost).Should().BeFalse();
		blocked.GetGameState()!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry => entry.Role == MainRoleType.Elder);
		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedResistanceIdentification_RecoversExactContinuationAndRejectsInvalidOrStaleResponses()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var elder = players[1];
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					elder.Id));
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(finishNight.CreateResponse()));

		var beforeInvalid = PublicGameSessionSnapshot.Capture(
			builder.GameService,
			builder.GameId);
		Action invalid = () => builder.Process(
			identification.CreateResponse([elder.Id, players[2].Id]));

		invalid.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(builder.GameService, builder.GameId)
			.Should().BeEquivalentTo(
				beforeInvalid,
				options => options.WithStrictOrdering());

		var acceptedIdentification = identification.CreateResponse([elder.Id]);
		var acceptedResult = builder.Process(acceptedIdentification);
		acceptedResult.ModeratorInstruction.Should().NotBeNull();
		var expectedContinuation = acceptedResult.ModeratorInstruction!;
		var session = builder.GetGameState()!;
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.Elder &&
				entry.PlayerIds.SetEquals(new[] { elder.Id }));
		session.GameHistoryLog.OfType<StatusEffectLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == elder.Id &&
				entry.EffectType == StatusEffectTypes.ElderProtectionLost &&
				entry.IsActive);

		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.SerializeSession());
		var recoveredContinuation = recoveredService.GetCurrentInstruction(
			recoveredGameId);
		recoveredContinuation.Should().NotBeNull();
		var recoveredInstruction = recoveredContinuation!;

		recoveredInstruction.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		PendingInstructionSnapshot.Capture(recoveredInstruction)
			.Should().BeEquivalentTo(
				PendingInstructionSnapshot.Capture(expectedContinuation),
				options => options.WithStrictOrdering());
		var beforeStale = PublicGameSessionSnapshot.Capture(
			recoveredService,
			recoveredGameId);
		Action replayAccepted = () => recoveredService.ProcessInstruction(
			recoveredGameId,
			acceptedIdentification);

		replayAccepted.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(recoveredService, recoveredGameId)
			.Should().BeEquivalentTo(
				beforeStale,
				options => options.WithStrictOrdering());
		var recoveredSession = recoveredService.GetGameStateView(
			recoveredGameId)!;
		recoveredSession.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry => entry.Role == MainRoleType.Elder);
		recoveredSession.GameHistoryLog.OfType<StatusEffectLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == elder.Id &&
				entry.EffectType == StatusEffectTypes.ElderProtectionLost &&
				entry.IsActive);
		MarkTestCompleted();
	}

	[Fact]
	public void WhiteWerewolf_PublicSoloAttackCanSpendFreshElderResistance()
	{
		var builder = CreateBuilder()
			.WithPlayers(8)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var elderAgent = players[0];
		var whiteWerewolf = players[1];
		builder
			.ArrangeKnownRole(elderAgent.Id, MainRoleType.Elder)
			.ArrangeKnownRole(whiteWerewolf.Id, MainRoleType.WhiteWerewolf)
			.ArrangeKnownRole(players[2].Id, MainRoleType.SimpleVillager)
			.ArrangeKnownWerewolfFactionAgentGroup(
				elderAgent.Id,
				whiteWerewolf.Id);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(
			[elderAgent.Id, whiteWerewolf.Id],
			players[4].Id);
		builder.CompleteDawnPhase(new()
		{
			[players[4].Id] = MainRoleType.SimpleVillager
		});
		builder.CompleteDayPhaseWithTie();
		builder.ConfirmNightStart();
		var whiteWake = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[elderAgent.Id, whiteWerewolf.Id],
					players[5].Id));
		var whiteTarget = InstructionAssert
			.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(whiteWake.CreateResponse()));
		whiteTarget.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWhiteWerewolfTarget);
		var whiteSleep = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					whiteTarget.CreateResponse([elderAgent.Id])));
		var finishNight = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(whiteSleep.CreateResponse()));

		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();

		elderAgent.State.HasStatusEffect(
			StatusEffectTypes.ElderProtectionLost).Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == elderAgent.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void BigBadWolf_PublicAdditionalAttackCanSpendFreshElderResistance()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.BigBadWolf,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var bigBadWolf = players[1];
		var elder = players[2];
		builder
			.ArrangeKnownRole(bigBadWolf.Id, MainRoleType.BigBadWolf)
			.ArrangeKnownPhysicalRole(elder.Id, MainRoleType.Elder)
			.ArrangeKnownWerewolfFactionAgentGroup(
				players[0].Id,
				bigBadWolf.Id);
		builder.ConfirmGameStart();

		var afterNight = builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [players[0].Id, bigBadWolf.Id],
			WerewolfVictimId = players[4].Id,
			BigBadWolfId = bigBadWolf.Id,
			BigBadWolfTargetId = elder.Id
		});

		afterNight.IsSuccess.Should().BeTrue();
		elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == elder.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void PublicAttacks_AfterResistanceSpent_LaterBigBadWolfAttackIsLethal()
	{
		var builder = CreateBuilder()
			.WithPlayers(8)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.BigBadWolf,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var bigBadWolf = players[1];
		var elder = players[2];
		builder
			.ArrangeKnownRole(bigBadWolf.Id, MainRoleType.BigBadWolf)
			.ArrangeKnownPhysicalRole(elder.Id, MainRoleType.Elder)
			.ArrangeKnownWerewolfFactionAgentGroup(
				players[0].Id,
				bigBadWolf.Id);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [players[0].Id, bigBadWolf.Id],
			WerewolfVictimId = elder.Id,
			BigBadWolfId = bigBadWolf.Id,
			BigBadWolfTargetId = players[4].Id
		});

		elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == elder.Id);
		builder.CompleteDawnPhase(new()
		{
			[players[4].Id] = MainRoleType.SimpleVillager
		});
		builder.CompleteDayPhaseWithTie();
		builder.ConfirmNightStart();
		var bigBadWake = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id, bigBadWolf.Id],
					players[5].Id));
		bigBadWake.AffectedPlayerIds.Should().Equal(bigBadWolf.Id);
		var finishNight = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteBigBadWolfNightAction(
					bigBadWolf.Id,
					elder.Id));

		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();

		builder.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == elder.Id &&
				entry.Reason == EliminationReason.WerewolfAttack);
		MarkTestCompleted();
	}

	[Fact]
	public void VillageVote_SpentElderCompletesCascadeThenAnnouncesSuppressionBeforeNavigation()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var elder = players[1];
		builder.ArrangeKnownPhysicalRole(elder.Id, MainRoleType.Elder);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase([werewolf.Id], elder.Id);
		builder.CompleteDawnPhase();

		elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeTrue();
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var reveal = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(vote.CreateResponse([elder.Id])));
		var elimination =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(reveal.CreateResponse()));

		var suppression =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(elimination.CreateResponse()));

		elder.State.Health.Should().Be(PlayerHealth.Dead);
		suppression.Semantic.ToString().Should().Be(
			"AnnounceVillagerRolePowerSuppression");
		suppression.PublicAnnouncement.Should().NotBeNullOrWhiteSpace();
		builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Day);

		var afterAcknowledgment = builder.Process(suppression.CreateResponse());
		afterAcknowledgment.IsSuccess.Should().BeTrue();
		afterAcknowledgment.ModeratorInstruction?.Semantic.ToString()
			.Should().NotBe("AnnounceVillagerRolePowerSuppression");
		MarkTestCompleted();
	}
}
