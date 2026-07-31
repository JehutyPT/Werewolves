using FluentAssertions;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class HunterRoleTests(ITestOutputHelper output)
	: DiagnosticTestBase(output)
{
	[Fact]
	public void DawnAttack_EliminatedHunterMustChooseExactlyOneFinalShot()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Werewolf",
				"Hunter",
				"Shot target",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var hunterId = players[1].Id;
		var shotTargetId = players[2].Id;

		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var finishNight = builder.CompleteWerewolfNightAction(
				[players[0].Id],
				hunterId)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var hunterReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var finalShot = builder.Process(hunterReveal.CreateResponse(new()
			{
				[hunterId] = MainRoleType.Hunter
			}))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		builder.GetGameState()!.GetPlayerState(hunterId).Health.Should().Be(
			PlayerHealth.Dead);
		finalShot.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		finalShot.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		finalShot.AffectedPlayerIds.Should().Equal(hunterId);
		finalShot.PublicAnnouncement.Should().Be(
			GameStrings.HunterFinalShotSelectionInstruction);
		finalShot.SelectablePlayerIds.Should().BeEquivalentTo(
			players
				.Where(player => player.Id != hunterId)
				.Select(player => player.Id));

		var targetReveal = builder.Process(
				finalShot.CreateResponse([shotTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		targetReveal.PlayersForAssignment.Should().Equal(shotTargetId);
		var afterCascade = builder.Process(targetReveal.CreateResponse(new()
		{
			[shotTargetId] = MainRoleType.SimpleVillager
		}));

		afterCascade.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		var completed = builder.GetGameState()!;
		completed.GetPlayerState(shotTargetId).Health.Should().Be(
			PlayerHealth.Dead);
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == hunterId &&
				entry.Reason == EliminationReason.WerewolfAttack);
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == shotTargetId &&
				entry.Reason == EliminationReason.HunterShot);

		MarkTestCompleted();
	}

	[Fact]
	public void PendingFinalShot_RehydratesExactlyAndNewSuppressionCannotCancelIt()
	{
		var (builder, players, finalShot) = StartDawnHunterFinalShot();
		var hunterId = players[1].Id;
		var shotTargetId = players[2].Id;
		var recoveredService = new GameService(new DenyAllPolicy());
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredShot = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		recoveredShot.InstructionId.Should().Be(finalShot.InstructionId);
		recoveredShot.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		recoveredShot.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		recoveredShot.AffectedPlayerIds.Should().Equal(hunterId);
		recoveredShot.SelectablePlayerIds.Should().BeEquivalentTo(
			finalShot.SelectablePlayerIds);

		var targetReveal = recoveredService.ProcessInstruction(
				recoveredGameId,
				recoveredShot.CreateResponse([shotTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		targetReveal.PlayersForAssignment.Should().Equal(shotTargetId);

		var beforeStaleReplay = recoveredService
			.GetGameStateView(recoveredGameId)!
			.Serialize();
		var replay = () => recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredShot.CreateResponse([shotTargetId]));
		replay.Should().Throw<InvalidOperationException>();
		recoveredService.GetGameStateView(recoveredGameId)!.Serialize()
			.Should().Be(beforeStaleReplay);

		var afterCascade = recoveredService.ProcessInstruction(
			recoveredGameId,
			targetReveal.CreateResponse(new()
			{
				[shotTargetId] = MainRoleType.SimpleVillager
			}));

		afterCascade.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		recoveredService.GetGameStateView(recoveredGameId)!
			.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == shotTargetId &&
				entry.Reason == EliminationReason.HunterShot);

		MarkTestCompleted();
	}

	[Fact]
	public void CommittedFinalShotTarget_RehydratesAtRevealWithoutSuppressionCancellingIt()
	{
		var (builder, players, finalShot) = StartDawnHunterFinalShot();
		var shotTargetId = players[2].Id;
		var targetReveal = builder.Process(
				finalShot.CreateResponse([shotTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var recoveredService = new GameService(new DenyAllPolicy());
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredReveal = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<AssignRolesInstruction>().Subject;

		recoveredReveal.InstructionId.Should().Be(targetReveal.InstructionId);
		recoveredReveal.PlayersForAssignment.Should().Equal(shotTargetId);

		var afterCascade = recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredReveal.CreateResponse(new()
			{
				[shotTargetId] = MainRoleType.SimpleVillager
			}));

		afterCascade.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		recoveredService.GetGameStateView(recoveredGameId)!
			.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == shotTargetId &&
				entry.Reason == EliminationReason.HunterShot);

		MarkTestCompleted();
	}

	[Fact]
	public void ActualHunterElimination_WhenFinalShotIsSuppressed_DoesNotOfferOrCommitShot()
	{
		var policy = new RecordingPolicy(
			RolePowerAvailabilityResult.Denied);
		var (builder, players, hunterReveal) =
			StartDawnHunterElimination(policy);
		var hunterId = players[1].Id;

		var afterReveal = builder.Process(hunterReveal.CreateResponse(new()
		{
			[hunterId] = MainRoleType.Hunter
		}));

		afterReveal.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		var completed = builder.GetGameState()!;
		completed.GetPlayerState(hunterId).Health.Should().Be(PlayerHealth.Dead);
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.Reason == EliminationReason.HunterShot);
		policy.Attempts
			.Where(attempt => attempt.SourceRole == MainRoleType.Hunter)
			.Should().ContainSingle();

		MarkTestCompleted();
	}

	[Fact]
	public void StalePendingFinalShotRoster_IsRejectedWithoutReplacingTheInstruction()
	{
		var (builder, players, finalShot) = StartDawnHunterFinalShot();
		var selectedTargetId = players[2].Id;
		builder.ArrangeEliminatedPlayer(players[4].Id);
		var before = builder.GetGameState()!.Serialize();

		var process = () => builder.Process(
			finalShot.CreateResponse([selectedTargetId]));

		process.Should().Throw<InvalidOperationException>()
			.WithMessage("*no longer matches*");
		builder.GetGameState()!.Serialize().Should().Be(before);
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			finalShot.InstructionId);

		MarkTestCompleted();
	}

	[Fact]
	public void HealedHunter_IsNotEliminatedAndDoesNotReceiveFinalShot()
	{
		var policy = new RecordingPolicy(
			RolePowerAvailabilityResult.Allowed);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(
				"Werewolf",
				"Hunter",
				"Witch",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var hunterId = players[1].Id;
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var identifyWitch = builder.CompleteWerewolfNightAction(
				[players[0].Id],
				hunterId)
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var healing = builder.Process(
				identifyWitch.CreateResponse([players[2].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var poison = builder.Process(healing.CreateResponse([hunterId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var sleep = builder.Process(poison.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = builder.Process(sleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		var afterDawn = builder.Process(finishNight.CreateResponse());

		afterDawn.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		var completed = builder.GetGameState()!;
		completed.GetPlayerState(hunterId).Health.Should().Be(
			PlayerHealth.Alive);
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.PlayerId == hunterId ||
				entry.Reason == EliminationReason.HunterShot);
		policy.Attempts
			.Where(attempt => attempt.SourceRole == MainRoleType.Hunter)
			.Should().BeEmpty();

		MarkTestCompleted();
	}

	[Fact]
	public void ConcurrentDawnEliminations_CommitBeforeHunterChoosesFromRemainingLivingPlayers()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Werewolf",
				"Hunter",
				"Witch",
				"Poison target",
				"Shot target",
				"Villager")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var hunterId = players[1].Id;
		var poisonTargetId = players[3].Id;
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var identifyWitch = builder.CompleteWerewolfNightAction(
				[players[0].Id],
				hunterId)
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var healing = builder.Process(
				identifyWitch.CreateResponse([players[2].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var poison = builder.Process(healing.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var sleep = builder.Process(poison.CreateResponse([poisonTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = builder.Process(sleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var initialReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		initialReveal.PlayersForAssignment.Should().BeEquivalentTo(
			[hunterId, poisonTargetId]);
		var finalShot = builder.Process(initialReveal.CreateResponse(new()
			{
				[hunterId] = MainRoleType.Hunter,
				[poisonTargetId] = MainRoleType.SimpleVillager
			}))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		builder.GetGameState()!.GetPlayerState(hunterId).Health.Should().Be(
			PlayerHealth.Dead);
		builder.GetGameState()!.GetPlayerState(poisonTargetId).Health.Should()
			.Be(PlayerHealth.Dead);
		finalShot.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		finalShot.SelectablePlayerIds.Should().NotContain(hunterId);
		finalShot.SelectablePlayerIds.Should().NotContain(poisonTargetId);
		finalShot.SelectablePlayerIds.Should().OnlyContain(playerId =>
			builder.GetGameState()!.GetPlayerState(playerId).Health ==
			PlayerHealth.Alive);

		MarkTestCompleted();
	}

	[Fact]
	public void DayVoteFinalShotSelector_RehydratesAtExactReactionBoundaryWithoutRecheckingAvailability()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Werewolf",
				"Hunter",
				"Shot target",
				"Villager A",
				"Night victim")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var hunterId = players[1].Id;
		var shotTargetId = players[2].Id;
		var nightVictimId = players[4].Id;
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var finishNight = builder.CompleteWerewolfNightAction(
				[players[0].Id],
				nightVictimId)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var nightVictimReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var debate = builder.Process(
				nightVictimReveal.CreateResponse(new()
				{
					[nightVictimId] = MainRoleType.SimpleVillager
				}))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var hunterReveal = builder.Process(vote.CreateResponse([hunterId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var announcement = builder.Process(
				hunterReveal.CreateResponse(new()
				{
					[hunterId] = MainRoleType.Hunter
				}))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		var finalShot = builder.Process(announcement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		finalShot.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		var throwingPolicy = new ThrowingPolicy();
		var recoveredService = new GameService(throwingPolicy);

		var recoveredGameId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());

		var recoveredShot = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		recoveredShot.InstructionId.Should().Be(finalShot.InstructionId);
		recoveredShot.AffectedPlayerIds.Should().Equal(hunterId);
		recoveredShot.SelectablePlayerIds.Should().BeEquivalentTo(
			finalShot.SelectablePlayerIds);
		throwingPolicy.EvaluationCount.Should().Be(0);

		var targetReveal = recoveredService.ProcessInstruction(
				recoveredGameId,
				recoveredShot.CreateResponse([shotTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		recoveredService.ProcessInstruction(
			recoveredGameId,
			targetReveal.CreateResponse(new()
			{
				[shotTargetId] = MainRoleType.SimpleVillager
			}));
		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == hunterId &&
				entry.Reason == EliminationReason.DayVote);
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == shotTargetId &&
				entry.Reason == EliminationReason.HunterShot);
		throwingPolicy.EvaluationCount.Should().Be(0);

		MarkTestCompleted();
	}

	[Fact]
	public void KnownHunterReveal_RequiresPublicContinueBeforeFinalShotSelection()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Werewolf",
				"Hunter",
				"Shot target",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var hunterId = players[1].Id;
		builder.ArrangeKnownPhysicalRole(hunterId, MainRoleType.Hunter);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var finishNight = builder.CompleteWerewolfNightAction(
				[players[0].Id],
				hunterId)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		var publicReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		publicReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDawnVictimRoles);
		publicReveal.PublicAnnouncement.Should().Contain(
			players[1].Name);
		publicReveal.AffectedPlayerIds.Should().Equal(hunterId);
		var finalShot = builder.Process(publicReveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		finalShot.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		finalShot.AffectedPlayerIds.Should().Equal(hunterId);

		MarkTestCompleted();
	}

	[Fact]
	public void HunterFinalShot_UsesReactiveAvailabilityIdentityExactlyOnce()
	{
		var policy = new RecordingPolicy(
			RolePowerAvailabilityResult.Allowed);
		var (builder, players, finalShot) =
			StartDawnHunterFinalShot(policy);
		var hunterId = players[1].Id;

		policy.Attempts.Should().ContainSingle();
		var attempt = policy.Attempts.Single();
		attempt.ActingPlayer.Id.Should().Be(hunterId);
		attempt.SourceRole.Should().Be(MainRoleType.Hunter);
		attempt.SourcePower.Identifier.Should().Be(
			new RolePowerIdentifier("hunter-final-shot"));
		attempt.SourcePower.Category.Should().Be(RolePowerCategory.Reactive);
		attempt.PowerInstance.Id.Should().Be(hunterId);
		attempt.PowerInstance.SourceRole.Should().Be(MainRoleType.Hunter);
		attempt.PowerInstance.Origin.Should().Be(
			RolePowerInstanceOrigin.Native);
		attempt.OneUseResource.Should().BeNull();

		builder.Process(finalShot.CreateResponse([players[2].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>();
		policy.Attempts.Should().ContainSingle();

		MarkTestCompleted();
	}

	[Fact]
	public void ForcedReactionEliminatesAllCandidatesBeforeHunter_AutoCompletesWithoutSyntheticInput()
	{
		var forcedReaction = new EliminateAllOtherLivingPlayersReaction();
		var builder = CreateBuilder()
			.WithEliminationCascadeReaction(
				forcedReaction,
				EliminationCascadeReactionBoundary.Forced)
			.WithPlayers(
				"Werewolf",
				"Hunter",
				"Villager A",
				"Villager B",
				"Villager C")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var hunterId = players[1].Id;
		forcedReaction.Configure(hunterId);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var finishNight = builder.CompleteWerewolfNightAction(
				[players[0].Id],
				hunterId)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var hunterReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var forcedReveal = builder.Process(
				hunterReveal.CreateResponse(new()
				{
					[hunterId] = MainRoleType.Hunter
				}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		forcedReveal.PlayersForAssignment.Should().BeEquivalentTo(
			players
				.Where(player => player.Id != hunterId)
				.Select(player => player.Id));
		var assignments = forcedReveal.PlayersForAssignment.ToDictionary(
			playerId => playerId,
			playerId => playerId == players[0].Id
				? MainRoleType.SimpleWerewolf
				: MainRoleType.SimpleVillager);
		var recoveredReaction =
			new EliminateAllOtherLivingPlayersReaction();
		recoveredReaction.Configure(hunterId);
		var zeroTargetPolicy = new RecordingPolicy(
			RolePowerAvailabilityResult.Allowed);
		var recoveredService = new GameService(
			zeroTargetPolicy,
			[
				new EliminationCascadeReactionBinding(
					recoveredReaction,
					EliminationCascadeReactionBoundary.Forced)
			]);
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredReveal = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		recoveredReveal.InstructionId.Should().Be(
			forcedReveal.InstructionId);
		recoveredReveal.PlayersForAssignment.Should().BeEquivalentTo(
			forcedReveal.PlayersForAssignment);

		var afterCascade = recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredReveal.CreateResponse(assignments));

		var finished = afterCascade.ModeratorInstruction.Should()
			.BeOfType<FinishedGameConfirmationInstruction>().Subject;
		finished.GameResult.Should().BeOfType<NoWinnerGameResult>();
		finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		zeroTargetPolicy.Attempts.Should().ContainSingle();
		zeroTargetPolicy.Attempts.Single().SourcePower.Identifier.Should().Be(
			new RolePowerIdentifier("hunter-final-shot"));
		var completed = recoveredService.GetGameStateView(recoveredGameId)!;
		completed.GetPlayers().Should().OnlyContain(player =>
			player.State.Health == PlayerHealth.Dead);
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.Reason == EliminationReason.HunterShot);
		completed.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Where(entry =>
				entry.ReactionId ==
					EliminationCascadeReactionIds.HunterFinalShot &&
				entry.TriggeringEliminations.Any(elimination =>
					elimination.PlayerId == hunterId))
			.Should().ContainSingle()
			.Which.AdmittedEliminations.Should().BeEmpty();

		MarkTestCompleted();
	}

	[Fact]
	public void WitchPoison_EliminatedHunterStillCommitsExactlyOneFinalShot()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Werewolf",
				"Witch",
				"Hunter",
				"Attack target",
				"Shot target",
				"Villager")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Witch,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var hunterId = players[2].Id;
		var attackTargetId = players[3].Id;
		var shotTargetId = players[4].Id;
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var identifyWitch = builder.CompleteWerewolfNightAction(
				[players[0].Id],
				attackTargetId)
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var healing = builder.Process(
				identifyWitch.CreateResponse([players[1].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var poison = builder.Process(healing.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var sleep = builder.Process(poison.CreateResponse([hunterId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = builder.Process(sleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var initialReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var finalShot = builder.Process(
				initialReveal.CreateResponse(new()
				{
					[hunterId] = MainRoleType.Hunter,
					[attackTargetId] = MainRoleType.SimpleVillager
				}))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var targetReveal = builder.Process(
				finalShot.CreateResponse([shotTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		builder.Process(targetReveal.CreateResponse(new()
		{
			[shotTargetId] = MainRoleType.SimpleVillager
		}));

		var completed = builder.GetGameState()!;
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == hunterId &&
				entry.Reason == EliminationReason.WitchKill);
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == shotTargetId &&
				entry.Reason == EliminationReason.HunterShot);

		MarkTestCompleted();
	}

	[Fact]
	public void FinalShotVictim_ReentersTheSameCascadeAndDrainsForcedDescendantBeforeNavigation()
	{
		var descendantReaction = new SingleDescendantReaction();
		var builder = CreateBuilder()
			.WithEliminationCascadeReaction(
				descendantReaction,
				EliminationCascadeReactionBoundary.Forced)
			.WithPlayers(
				"Werewolf",
				"Hunter",
				"Shot target",
				"Descendant",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var hunterId = players[1].Id;
		var shotTargetId = players[2].Id;
		var descendantId = players[3].Id;
		descendantReaction.Configure(shotTargetId, descendantId);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var finishNight = builder.CompleteWerewolfNightAction(
				[players[0].Id],
				hunterId)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var hunterReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var finalShot = builder.Process(
				hunterReveal.CreateResponse(new()
				{
					[hunterId] = MainRoleType.Hunter
				}))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var shotTargetReveal = builder.Process(
				finalShot.CreateResponse([shotTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var descendantReveal = builder.Process(
				shotTargetReveal.CreateResponse(new()
				{
					[shotTargetId] = MainRoleType.SimpleVillager
				}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		descendantReveal.PlayersForAssignment.Should().Equal(descendantId);
		builder.GetGameState()!.GetPlayerState(shotTargetId).Health.Should()
			.Be(PlayerHealth.Dead);
		var afterCascade = builder.Process(
			descendantReveal.CreateResponse(new()
			{
				[descendantId] = MainRoleType.SimpleVillager
			}));

		afterCascade.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		var completed = builder.GetGameState()!;
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == shotTargetId &&
				entry.Reason == EliminationReason.HunterShot);
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == descendantId &&
				entry.Reason == EliminationReason.EventElimination);
		var relevantBatches = completed.GameHistoryLog
			.OfType<EliminationCascadeBatchResolvedLogEntry>()
			.Where(entry =>
				entry.CommittedEliminations.Any(elimination =>
					elimination.PlayerId == hunterId ||
					elimination.PlayerId == shotTargetId ||
					elimination.PlayerId == descendantId))
			.ToArray();
		relevantBatches.Should().HaveCount(3);
		relevantBatches.Select(entry => entry.ScopeId)
			.Should().OnlyContain(scopeId =>
				scopeId == relevantBatches[0].ScopeId);

		MarkTestCompleted();
	}

	private (
		GameTestBuilder Builder,
		IPlayer[] Players,
		SelectPlayersInstruction FinalShot) StartDawnHunterFinalShot(
			IRolePowerAvailabilityPolicy? policy = null)
	{
		var (builder, players, hunterReveal) =
			StartDawnHunterElimination(policy);
		var finalShot = builder.Process(hunterReveal.CreateResponse(new()
			{
				[players[1].Id] = MainRoleType.Hunter
			}))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		return (builder, players, finalShot);
	}

	private (
		GameTestBuilder Builder,
		IPlayer[] Players,
		AssignRolesInstruction HunterReveal) StartDawnHunterElimination(
			IRolePowerAvailabilityPolicy? policy = null)
	{
		var builder = CreateBuilder()
			.WithOptionalRolePowerAvailabilityPolicy(policy)
			.WithPlayers(
				"Werewolf",
				"Hunter",
				"Shot target",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var finishNight = builder.CompleteWerewolfNightAction(
				[players[0].Id],
				players[1].Id)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var hunterReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		return (builder, players, hunterReveal);
	}

	private sealed class DenyAllPolicy : IRolePowerAvailabilityPolicy
	{
		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt) =>
			RolePowerAvailabilityResult.Denied;
	}

	private sealed class RecordingPolicy(RolePowerAvailabilityResult result)
		: IRolePowerAvailabilityPolicy
	{
		public List<RolePowerAttempt> Attempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			Attempts.Add(attempt);
			return result;
		}
	}

	private sealed class EliminateAllOtherLivingPlayersReaction
		: IEliminationCascadeReaction
	{
		private Guid _hunterId;

		public string ReactionId =>
			nameof(EliminateAllOtherLivingPlayersReaction);

		internal void Configure(Guid hunterId) => _hunterId = hunterId;

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input) =>
			eliminatedPlayerIds.Contains(_hunterId)
				? EliminationCascadeReactionResult.Complete(
					session.GetPlayers()
						.Where(player =>
							player.Id != _hunterId &&
							player.State.Health == PlayerHealth.Alive)
						.Select(player => new EliminationRequest(
							player.Id,
							EliminationReason.EventElimination))
						.ToArray())
				: EliminationCascadeReactionResult.Complete();
	}

	private sealed class SingleDescendantReaction
		: IEliminationCascadeReaction
	{
		private Guid _triggerId;
		private Guid _descendantId;

		public string ReactionId => nameof(SingleDescendantReaction);

		internal void Configure(Guid triggerId, Guid descendantId)
		{
			_triggerId = triggerId;
			_descendantId = descendantId;
		}

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input) =>
			eliminatedPlayerIds.Contains(_triggerId)
				? EliminationCascadeReactionResult.Complete(
					[
						new EliminationRequest(
							_descendantId,
							EliminationReason.EventElimination)
					])
				: EliminationCascadeReactionResult.Complete();
	}

	private sealed class ThrowingPolicy : IRolePowerAvailabilityPolicy
	{
		public int EvaluationCount { get; private set; }

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			EvaluationCount++;
			throw new InvalidOperationException(
				"A committed Hunter final shot must not recheck availability.");
		}
	}
}
