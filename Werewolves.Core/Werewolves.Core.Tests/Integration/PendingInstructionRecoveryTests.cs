using FluentAssertions;
using FluentAssertions.Execution;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class PendingInstructionRecoveryTests
{
    [Fact]
    public void StableNightBoundary_AfterRehydration_AllBaselineRolesContinueRepresentativeBehavior()
    {
        var builder = GameTestBuilder.Create()
            .WithPlayers(
                "Wild Child",
                "Role Model",
                "Werewolf",
                "Seer",
                "Villager A",
                "Villager B")
            .WithRoles(
                MainRoleType.WildChild,
                MainRoleType.SimpleWerewolf,
                MainRoleType.Seer,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);

        builder.StartGame();
        builder.ConfirmGameStart().IsSuccess.Should().BeTrue();

        var committedNightStart = builder.GetCurrentInstruction()
            .Should().BeOfType<ConfirmationInstruction>().Subject;
        var recoveredService = new GameService();
        var recoveredGameId = recoveredService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var recoveredSession = recoveredService.GetGameStateView(recoveredGameId)!;
        var playersByName = recoveredSession.GetPlayers()
            .ToDictionary(player => player.Name);
        var wildChildId = playersByName["Wild Child"].Id;
        var roleModelId = playersByName["Role Model"].Id;
        var werewolfId = playersByName["Werewolf"].Id;
        var seerId = playersByName["Seer"].Id;
        var passiveVillagerId = playersByName["Villager A"].Id;

        var recoveredNightStart = recoveredService.GetCurrentInstruction(recoveredGameId)
            .Should().BeOfType<ConfirmationInstruction>().Subject;
        recoveredNightStart.InstructionId.Should().Be(committedNightStart.InstructionId);

        var wildChildIdentification = ProcessAndExpect<SelectPlayersInstruction>(
            recoveredService,
            recoveredGameId,
            recoveredNightStart.CreateResponse());
        wildChildIdentification.RoleIdentification.Should().Be(MainRoleType.WildChild);

        var modelSelection = ProcessAndExpect<SelectPlayersInstruction>(
            recoveredService,
            recoveredGameId,
            wildChildIdentification.CreateResponse([wildChildId]));
        modelSelection.RoleIdentification.Should().BeNull();
        modelSelection.AffectedPlayerIds.Should().Equal(wildChildId);
        modelSelection.SelectablePlayerIds.Should().NotContain(wildChildId);

        var wildChildSleep = ProcessAndExpect<ConfirmationInstruction>(
            recoveredService,
            recoveredGameId,
            modelSelection.CreateResponse([roleModelId]));
        var werewolfIdentification = ProcessAndExpect<SelectPlayersInstruction>(
            recoveredService,
            recoveredGameId,
            wildChildSleep.CreateResponse());
        werewolfIdentification.RoleIdentification.Should().Be(MainRoleType.SimpleWerewolf);

        var victimSelection = ProcessAndExpect<SelectPlayersInstruction>(
            recoveredService,
            recoveredGameId,
            werewolfIdentification.CreateResponse([werewolfId]));
        victimSelection.RoleIdentification.Should().BeNull();
        victimSelection.AffectedPlayerIds.Should().Equal(werewolfId);

        var werewolfSleep = ProcessAndExpect<ConfirmationInstruction>(
            recoveredService,
            recoveredGameId,
            victimSelection.CreateResponse([roleModelId]));
        var seerIdentification = ProcessAndExpect<SelectPlayersInstruction>(
            recoveredService,
            recoveredGameId,
            werewolfSleep.CreateResponse());
        seerIdentification.RoleIdentification.Should().Be(MainRoleType.Seer);

        var seerTargetSelection = ProcessAndExpect<SelectPlayersInstruction>(
            recoveredService,
            recoveredGameId,
            seerIdentification.CreateResponse([seerId]));
        seerTargetSelection.RoleIdentification.Should().BeNull();
        seerTargetSelection.AffectedPlayerIds.Should().Equal(seerId);

        var seerFeedback = ProcessAndExpect<ConfirmationInstruction>(
            recoveredService,
            recoveredGameId,
            seerTargetSelection.CreateResponse([werewolfId]));
        var seerSleep = ProcessAndExpect<ConfirmationInstruction>(
            recoveredService,
            recoveredGameId,
            seerFeedback.CreateResponse());
        var nightEnd = ProcessAndExpect<ConfirmationInstruction>(
            recoveredService,
            recoveredGameId,
            seerSleep.CreateResponse());
        var roleReveal = ProcessAndExpect<ConfirmationInstruction>(
            recoveredService,
            recoveredGameId,
            nightEnd.CreateResponse());

        recoveredService.ProcessInstruction(
            recoveredGameId,
            roleReveal.CreateResponse()).IsSuccess.Should().BeTrue();

        using (new AssertionScope())
        {
            recoveredSession.GetPlayerState(wildChildId).MainRole.Should()
                .Be(MainRoleType.SimpleWerewolf);
            recoveredSession.GetPlayerState(wildChildId)
                .HasStatusEffect(StatusEffectTypes.WildChildChanged).Should().BeTrue();
            recoveredSession.GetPlayerState(werewolfId).MainRole.Should()
                .Be(MainRoleType.SimpleWerewolf);
            recoveredSession.GetPlayerState(seerId).MainRole.Should()
                .Be(MainRoleType.Seer);
            recoveredSession.GetPlayerState(roleModelId).MainRole.Should()
                .Be(MainRoleType.SimpleVillager);
            recoveredSession.GetPlayerState(roleModelId).Health.Should()
                .Be(PlayerHealth.Dead);
            recoveredSession.GetPlayerState(passiveVillagerId).MainRole.Should().BeNull();

            recoveredSession.GameHistoryLog.OfType<NightActionLogEntry>()
                .Should().ContainSingle(entry =>
                    entry.ActionType == NightActionType.WildChildModel &&
                    entry.TargetIds!.SequenceEqual(new[] { roleModelId }));
            recoveredSession.GameHistoryLog.OfType<NightActionLogEntry>()
                .Should().ContainSingle(entry =>
                    entry.ActionType == NightActionType.WerewolfVictimSelection &&
                    entry.TargetIds!.SequenceEqual(new[] { roleModelId }));
            recoveredSession.GameHistoryLog.OfType<NightActionLogEntry>()
                .Should().ContainSingle(entry =>
                    entry.ActionType == NightActionType.SeerCheck &&
                    entry.TargetIds!.SequenceEqual(new[] { werewolfId }));
            recoveredSession.GameHistoryLog.OfType<AssignRoleLogEntry>()
                .Should().NotContain(entry => entry.PlayerIds.Contains(passiveVillagerId));
        }
    }

    [Fact]
    public void StableBoundary_RestoresCommittedVoteAndExactNextInstruction_OnAFreshService()
    {
        var scenario = DayVoteScenario.Start();
        var committedVoteResponse =
            scenario.Instruction.CreateResponse([scenario.LivingTargetId]);

        scenario.Builder.Process(committedVoteResponse).IsSuccess.Should().BeTrue();
        AdvanceToNextNight(scenario.Builder);

        var originalSession = scenario.Builder.GetGameState()!;
        var originalNextInstruction = scenario.Builder.GetCurrentInstruction()!;
        var serializedSession = originalSession.Serialize();
        var freshService = new GameService();

        var rehydratedGameId = freshService.RehydrateSession(serializedSession);
        var rehydratedSession = freshService.GetGameStateView(rehydratedGameId)!;
        var rehydratedNextInstruction =
            freshService.GetCurrentInstruction(rehydratedGameId)!;

        using (new AssertionScope())
        {
            rehydratedGameId.Should().Be(originalSession.Id);
            rehydratedSession.GetCurrentPhase().Should().Be(GamePhase.Night);
            rehydratedNextInstruction.GetType().Should()
                .Be(originalNextInstruction.GetType());
            rehydratedNextInstruction.InstructionId.Should()
                .Be(originalNextInstruction.InstructionId);
            rehydratedSession.GetPlayerState(scenario.LivingTargetId).Health.Should()
                .Be(PlayerHealth.Dead);
            rehydratedSession.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
                .Should().ContainSingle(entry =>
                    entry.ReportedOutcomePlayerId == scenario.LivingTargetId);
        }

        var beforeReplay =
            PublicGameSessionSnapshot.Capture(freshService, rehydratedGameId);
        var replay = () =>
            freshService.ProcessInstruction(rehydratedGameId, committedVoteResponse);

        replay.Should().Throw<InvalidOperationException>();
        PublicGameSessionSnapshot.Capture(freshService, rehydratedGameId).Should()
            .BeEquivalentTo(beforeReplay, options => options.WithStrictOrdering());

        var continueResponse = rehydratedNextInstruction
            .Should().BeOfType<ConfirmationInstruction>().Subject
            .CreateResponse();
        var continued = freshService.ProcessInstruction(
            rehydratedGameId,
            continueResponse);

        continued.IsSuccess.Should().BeTrue();
        freshService.GetCurrentInstruction(rehydratedGameId)!.InstructionId.Should()
            .NotBe(rehydratedNextInstruction.InstructionId);
    }

    [Fact]
    public void InterruptedDayTail_ReplaysFromStableInstruction_AndCommitsVoteOnce()
    {
        var scenario = DayVoteScenario.Start();

        scenario.Builder.Process(
            scenario.Instruction.CreateResponse([scenario.LivingTargetId]));
        scenario.Builder.GetGameState()!.GameHistoryLog
            .OfType<VoteOutcomeReportedLogEntry>()
            .Should().ContainSingle();

        var interruptedPayload = scenario.Builder.GetGameState()!.Serialize();
        var replayService = new GameService();
        var replayGameId = replayService.RehydrateSession(interruptedPayload);
        var replaySession = replayService.GetGameStateView(replayGameId)!;
        var stableInstruction = replayService.GetCurrentInstruction(replayGameId)
            .Should().BeOfType<ConfirmationInstruction>().Subject;

        using (new AssertionScope())
        {
            replaySession.GetCurrentPhase().Should().Be(GamePhase.Day);
            stableInstruction.InstructionId.Should()
                .Be(scenario.StableDayBoundaryInstruction.InstructionId);
            replaySession.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
                .Should().BeEmpty();
        }

        var replayedDebate = replayService.ProcessInstruction(
            replayGameId,
            stableInstruction.CreateResponse());
        var replayedVoteInstruction = replayedDebate.ModeratorInstruction
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        using (new AssertionScope())
        {
            replayedVoteInstruction.InstructionId.Should()
                .NotBe(scenario.Instruction.InstructionId);
            replayedVoteInstruction.SelectablePlayerIds.Should()
                .BeEquivalentTo(scenario.Instruction.SelectablePlayerIds);
        }

        var replayedVote = replayService.ProcessInstruction(
            replayGameId,
            replayedVoteInstruction.CreateResponse([scenario.LivingTargetId]));

        replayedVote.IsSuccess.Should().BeTrue();
        replaySession.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
            .Should().ContainSingle(entry =>
                entry.ReportedOutcomePlayerId == scenario.LivingTargetId);
    }

    private static void AdvanceToNextNight(GameTestBuilder builder)
    {
        for (var step = 0; step < 10; step++)
        {
            if (builder.GetGameState()!.GetCurrentPhase() == GamePhase.Night)
            {
                return;
            }

            var instruction = builder.GetCurrentInstruction()
                .Should().BeOfType<ConfirmationInstruction>().Subject;
            builder.Process(instruction.CreateResponse()).IsSuccess.Should().BeTrue();
        }

        throw new InvalidOperationException(
            "Day phase did not reach the next stable Night boundary.");
    }

    private static TInstruction ProcessAndExpect<TInstruction>(
        GameService service,
        Guid gameId,
        ModeratorResponse response)
        where TInstruction : ModeratorInstruction
    {
        var result = service.ProcessInstruction(gameId, response);

        result.IsSuccess.Should().BeTrue();
        return result.ModeratorInstruction.Should().BeOfType<TInstruction>().Subject;
    }
}
