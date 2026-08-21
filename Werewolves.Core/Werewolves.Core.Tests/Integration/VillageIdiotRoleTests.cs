using FluentAssertions;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class VillageIdiotRoleTests
{
	[Fact]
	public void PreRevealRoleChange_IsReReadBeforePardonAttempt()
	{
		var scenario = StartWithPreRevealRoleChange();

		var reveal = scenario.Builder.Process(
				scenario.Instruction.CreateResponse(
					[scenario.LivingTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		reveal.AffectedPlayerIds.Should().Equal(
			scenario.LivingTargetId);
		var preRevealState = scenario.Builder.GetGameState()!
			.GetPlayerState(scenario.LivingTargetId);
		preRevealState.CurrentRole.Should().Be(
			MainRoleType.VillageIdiot);
		preRevealState.PubliclyRevealedRole.Should().BeNull();
		preRevealState.DurableVotingPower.Should().Be(1);
		scenario.Builder.GetGameState()!.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();

		var pardon = scenario.Builder.Process(
				reveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		pardon.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon);
		var state = scenario.Builder.GetGameState()!
			.GetPlayerState(scenario.LivingTargetId);
		state.CurrentRole.Should().Be(MainRoleType.VillageIdiot);
		state.PubliclyRevealedRole.Should().Be(
			MainRoleType.VillageIdiot);
		state.DurableVotingPower.Should().Be(0);
	}

	[Fact]
	public void UnknownRole_MapsPublicRevealBeforePardon()
	{
		var scenario = DayVoteScenario.Start(
			livingTargetRole: MainRoleType.VillageIdiot,
			arrangeKnownPhysicalRole: false);

		var reveal = scenario.Builder.Process(
				scenario.Instruction.CreateResponse(
					[scenario.LivingTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		scenario.Builder.GetGameState()!
			.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();

		var pardon = scenario.Builder.Process(
				reveal.CreateResponse(new Dictionary<Guid, MainRoleType>
				{
					[scenario.LivingTargetId] =
						MainRoleType.VillageIdiot
				}))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		pardon.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon);
		var state = scenario.Builder.GetGameState()!
			.GetPlayerState(scenario.LivingTargetId);
		state.PubliclyRevealedRole.Should().Be(
			MainRoleType.VillageIdiot);
		state.DurableVotingPower.Should().Be(0);
	}

	[Fact]
	public void AlreadyPublicRole_PardonsWithoutAnotherRevealInstruction()
	{
		var scenario = DayVoteScenario.Start(
			livingTargetRole: MainRoleType.VillageIdiot);
		scenario.Builder
			.ArrangeKnownPhysicalRole(
				scenario.LivingTargetId,
				MainRoleType.VillageIdiot)
			.ArrangePubliclyRevealedRole(
				scenario.LivingTargetId,
				MainRoleType.VillageIdiot);

		var pardon = scenario.Builder.Process(
				scenario.Instruction.CreateResponse(
					[scenario.LivingTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		pardon.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon);
		scenario.Builder.GetGameState()!.GameHistoryLog
			.OfType<RoleRevealLogEntry>()
			.Should().ContainSingle(entry =>
				entry.RevealedRoles.ContainsKey(
					scenario.LivingTargetId));
		scenario.Builder.GetGameState()!.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().ContainSingle();
	}

	[Fact]
	public void DawnElimination_BypassesPardonAndEliminatesVillageIdiot()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.VillageIdiot,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var villageIdiot = players[2];
		builder.ArrangeKnownPhysicalRole(
			villageIdiot.Id,
			MainRoleType.VillageIdiot);

		builder.CompleteNightPhase(
			werewolfIds: [players[0].Id],
			victimId: villageIdiot.Id,
			seerId: players[1].Id,
			seerTargetId: players[3].Id);
		builder.CompleteDawnPhase();

		var session = builder.GetGameState()!;
		var state = session.GetPlayerState(villageIdiot.Id);
		state.Health.Should().Be(PlayerHealth.Dead);
		state.DurableVotingPower.Should().Be(1);
		state.HasVotingRight.Should().BeTrue();
		session.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == villageIdiot.Id &&
				entry.Reason == EliminationReason.WerewolfAttack);
	}

	[Fact]
	public void AvailabilityDenied_RevealsThenCommitsOrdinaryVoteElimination()
	{
		var scenario = DayVoteScenario.Start(
			new DenyVillageIdiotPardonPolicy(),
			livingTargetRole: MainRoleType.VillageIdiot);
		scenario.Builder.ArrangeKnownPhysicalRole(
			scenario.LivingTargetId,
			MainRoleType.VillageIdiot);

		var reveal = scenario.Builder.Process(
				scenario.Instruction.CreateResponse(
					[scenario.LivingTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		reveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDayVoteTargetRole);

		var elimination = scenario.Builder.Process(
				reveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		elimination.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		var session = scenario.Builder.GetGameState()!;
		var state = session.GetPlayerState(scenario.LivingTargetId);
		state.PubliclyRevealedRole.Should().Be(
			MainRoleType.VillageIdiot);
		state.Health.Should().Be(PlayerHealth.Dead);
		state.DurableVotingPower.Should().Be(1);
		state.HasVotingRight.Should().BeTrue();
		session.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == scenario.LivingTargetId &&
				entry.Reason == EliminationReason.DayVote);
	}

	[Fact]
	public void TemporaryVotingRestriction_DoesNotSuppressFreshPardon()
	{
		var scenario = DayVoteScenario.Start(
			livingTargetRole: MainRoleType.VillageIdiot);
		scenario.Builder
			.ArrangeKnownPhysicalRole(
				scenario.LivingTargetId,
				MainRoleType.VillageIdiot)
			.ArrangeVotingRight(
				scenario.LivingTargetId,
				hasVotingRight: false);

		var reveal = scenario.Builder.Process(
				scenario.Instruction.CreateResponse(
					[scenario.LivingTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var pardon = scenario.Builder.Process(reveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		pardon.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon);
		var session = scenario.Builder.GetGameState()!;
		var state = session.GetPlayerState(scenario.LivingTargetId);
		state.Health.Should().Be(PlayerHealth.Alive);
		state.DurableVotingPower.Should().Be(0);
		state.HasVotingRight.Should().BeFalse();
		session.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().ContainSingle();
		session.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.PlayerId == scenario.LivingTargetId &&
				entry.Reason == EliminationReason.DayVote);
	}

	[Fact]
	public void TiedVote_BypassesRevealAvailabilityAndPardon()
	{
		var policy = new DenyVillageIdiotPardonPolicy();
		var scenario = DayVoteScenario.Start(
			policy,
			livingTargetRole: MainRoleType.VillageIdiot);
		scenario.Builder.ArrangeKnownPhysicalRole(
			scenario.LivingTargetId,
			MainRoleType.VillageIdiot);

		scenario.Builder.Process(
			scenario.Instruction.CreateResponse([]));

		var session = scenario.Builder.GetGameState()!;
		var state = session.GetPlayerState(scenario.LivingTargetId);
		state.PubliclyRevealedRole.Should().BeNull();
		state.Health.Should().Be(PlayerHealth.Alive);
		state.DurableVotingPower.Should().Be(1);
		state.HasVotingRight.Should().BeTrue();
		policy.VillageIdiotEvaluationCount.Should().Be(0);
		session.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void SpentPardon_ConsecutiveVoteUsesFreshRosterAndEliminatesNormally()
	{
		var scenario = DayVoteScenario.Start(
			livingTargetRole: MainRoleType.VillageIdiot);
		scenario.Builder
			.ArrangeKnownPhysicalRole(
				scenario.LivingTargetId,
				MainRoleType.VillageIdiot)
			.ArrangeDayAction(DayPowerType.JudgeExtraVote);

		var reveal = scenario.Builder.Process(
				scenario.Instruction.CreateResponse(
					[scenario.LivingTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var pardon = scenario.Builder.Process(
				reveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		pardon.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon);

		var consecutiveVote = scenario.Builder.Process(
				pardon.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		consecutiveVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		consecutiveVote.SelectablePlayerIds.Should().Contain(
			scenario.LivingTargetId);

		var elimination = scenario.Builder.Process(
				consecutiveVote.CreateResponse(
					[scenario.LivingTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		elimination.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		var session = scenario.Builder.GetGameState()!;
		session.GetPlayerState(scenario.LivingTargetId).Health.Should()
			.Be(PlayerHealth.Dead);
		session.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().ContainSingle();
		session.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == scenario.LivingTargetId &&
				entry.Reason == EliminationReason.DayVote);
		session.GameHistoryLog
			.OfType<EliminationCascadeBatchResolvedLogEntry>()
			.Where(entry =>
				entry.ScopeId.StartsWith(
					"Day:1:Vote:",
					StringComparison.Ordinal))
			.Select(entry =>
				(entry.ScopeId, entry.CommittedEliminations.Count))
			.Should().Equal(
				("Day:1:Vote:1", 0),
				("Day:1:Vote:2", 1));
	}

	[Fact]
	public void PendingReveal_ReplaysSemanticEquivalentRevealWithFreshCorrelation()
	{
		var scenario = DayVoteScenario.Start(
			livingTargetRole: MainRoleType.VillageIdiot);
		var reveal = scenario.Builder.Process(
				scenario.Instruction.CreateResponse(
					[scenario.LivingTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		var recoveredService = new GameLogic.Services.GameService();
		var gameId = recoveredService.RehydrateSession(
			scenario.Builder.GetGameState()!.Serialize());
		var stableDayBoundary = recoveredService.GetCurrentInstruction(gameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		stableDayBoundary.InstructionId.Should().Be(
			scenario.StableDayBoundaryInstruction.InstructionId);
		var replayedVote = recoveredService.ProcessInstruction(
				gameId,
				stableDayBoundary.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var recoveredReveal = recoveredService.ProcessInstruction(
				gameId,
				replayedVote.CreateResponse([scenario.LivingTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		recoveredReveal.InstructionId.Should().NotBe(reveal.InstructionId);
		recoveredReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDayVoteTargetRole);
		var recovered = recoveredService.GetGameStateView(gameId)!;
		recovered.GetPlayerState(scenario.LivingTargetId)
			.DurableVotingPower.Should().Be(1);
		recovered.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();

		Action staleResponse = () => recoveredService.ProcessInstruction(
			gameId,
			reveal.CreateResponse());

		staleResponse.Should().Throw<InvalidOperationException>();
		recovered.GetPlayerState(scenario.LivingTargetId)
			.DurableVotingPower.Should().Be(1);
		recovered.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();
		recoveredService.GetCurrentInstruction(gameId)!.InstructionId.Should()
			.Be(recoveredReveal.InstructionId);

		var pardon = recoveredService.ProcessInstruction(
				gameId,
				recoveredReveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		pardon.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon);
		recovered.GetPlayerState(scenario.LivingTargetId)
			.DurableVotingPower.Should().Be(0);
		recovered.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().ContainSingle();
	}

	[Fact]
	public void AcknowledgedConsequence_RehydratesNextBoundaryWithoutDuplicatePardon()
	{
		var scenario = DayVoteScenario.Start(
			livingTargetRole: MainRoleType.VillageIdiot);
		scenario.Builder.ArrangeKnownPhysicalRole(
			scenario.LivingTargetId,
			MainRoleType.VillageIdiot);
		var reveal = scenario.Builder.Process(
				scenario.Instruction.CreateResponse(
					[scenario.LivingTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var pardon = scenario.Builder.Process(reveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var next = scenario.Builder.Process(pardon.CreateResponse())
			.ModeratorInstruction!;

		var recoveredService = new GameLogic.Services.GameService();
		var gameId = recoveredService.RehydrateSession(
			scenario.Builder.GetGameState()!.Serialize());
		var recoveredNext = recoveredService.GetCurrentInstruction(gameId)!;

		recoveredNext.InstructionId.Should().Be(next.InstructionId);
		recoveredNext.Semantic.Should().Be(next.Semantic);
		var recovered = recoveredService.GetGameStateView(gameId)!;
		recovered.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().ContainSingle();
		recovered.GetPlayerState(scenario.LivingTargetId)
			.DurableVotingPower.Should().Be(0);
	}

	[Fact]
	public void PardonCreatesWerewolfControlAtPreNightVictoryWindow()
	{
		var scenario = DayVoteScenario.Start(
			livingTargetRole: MainRoleType.VillageIdiot);
		var remainingPlayers = scenario.Builder.GetGameState()!
			.GetPlayers()
			.Where(player =>
				player.Id != scenario.EliminatedPlayerId &&
				player.Id != scenario.LivingTargetId)
			.ToArray();
		var werewolf = remainingPlayers[0];
		var ordinaryVillager = remainingPlayers[1];
		var extraVillager = remainingPlayers[2];
		scenario.Builder
			.ArrangeKnownPhysicalRole(
				scenario.LivingTargetId,
				MainRoleType.VillageIdiot)
			.ArrangeEliminatedPlayer(extraVillager.Id);

		var reveal = scenario.Builder.Process(
				scenario.Instruction.CreateResponse(
					[scenario.LivingTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var pardon = scenario.Builder.Process(reveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var result = scenario.Builder.Process(pardon.CreateResponse());

		var finished = result.ModeratorInstruction.Should()
			.BeOfType<FinishedGameConfirmationInstruction>().Subject;
		finished.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.Werewolf));
		finished.VictoryCheckWindow.Should().Be(
			VictoryCheckWindow.PreNight);
		var session = scenario.Builder.GetGameState()!;
		session.GetPlayerState(werewolf.Id).DurableVotingPower.Should()
			.Be(1);
		session.GetPlayerState(ordinaryVillager.Id)
			.DurableVotingPower.Should().Be(1);
		session.GetPlayerState(scenario.LivingTargetId)
			.DurableVotingPower.Should().Be(0);
		var victoryEntry = session.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().ContainSingle().Subject;
		victoryEntry.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.Werewolf));
		victoryEntry.VictoryCheckWindow.Should().Be(
			VictoryCheckWindow.PreNight);
	}

	private sealed class DenyVillageIdiotPardonPolicy
		: IRolePowerAvailabilityPolicy
	{
		internal int VillageIdiotEvaluationCount { get; private set; }

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			if (attempt.SourceRole != MainRoleType.VillageIdiot)
				return RolePowerAvailabilityResult.Allowed;

			VillageIdiotEvaluationCount++;
			return RolePowerAvailabilityResult.Denied;
		}
	}

	private static DayVoteScenario StartWithPreRevealRoleChange()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.VillageIdiot,
				MainRoleType.SimpleVillager)
			.WithEliminationCascadeReaction(
				new ChangeVoteTargetRoleReaction(),
				EliminationCascadeReactionBoundary.PreReveal);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.CompleteNightPhase(
			werewolfIds: [players[0].Id],
			victimId: players[2].Id,
			seerId: players[1].Id,
			seerTargetId: players[3].Id);
		builder.CompleteDawnPhase();
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		return new DayVoteScenario(
			builder,
			vote,
			debate,
			players[3].Id,
			players[2].Id);
	}

	private sealed class ChangeVoteTargetRoleReaction
		: IEliminationCascadeReaction
	{
		public string ReactionId => "test-pre-reveal-role-change";

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input)
		{
			if (session.GetCurrentPhase() != GamePhase.Day)
				return EliminationCascadeReactionResult.Complete();

			session.AssignRole(
				eliminatedPlayerIds.Single(),
				MainRoleType.VillageIdiot);
			return EliminationCascadeReactionResult.Complete();
		}
	}
}
