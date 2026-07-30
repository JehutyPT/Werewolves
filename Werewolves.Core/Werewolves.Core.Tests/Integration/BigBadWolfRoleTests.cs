using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
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

public sealed class BigBadWolfRoleTests : DiagnosticTestBase
{
    public BigBadWolfRoleTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void FirstNight_UnknownHolder_IdentifiesThenRequiresOneEligibleAdditionalVictim()
    {
        var builder = CreateBuilder()
            .WithPlayers(7)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.BigBadWolf,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var bigBadWolf = players[1];
        var collectiveVictim = players[4];
        var additionalVictim = players[5];
        builder.ArrangeKnownWerewolfFactionAgentGroup(
            [players[0].Id, bigBadWolf.Id]);
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var identification =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.CompleteWerewolfNightAction(
                    [players[0].Id, bigBadWolf.Id],
                    collectiveVictim.Id));

        identification.RoleIdentification.Should().Be(MainRoleType.BigBadWolf);

        var targetSelection =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.Process(
                    identification.CreateResponse([bigBadWolf.Id])));
        targetSelection.Semantic.Should().Be(
            ModeratorInstructionSemantic.SelectBigBadWolfTarget);
        targetSelection.CountConstraint.Should().BeEquivalentTo(
            NumberRangeConstraint.Single);
        targetSelection.SelectablePlayerIds.Should()
            .Contain(additionalVictim.Id)
            .And.NotContain(collectiveVictim.Id)
            .And.NotContain(players[0].Id)
            .And.NotContain(bigBadWolf.Id);
        targetSelection.AffectedPlayerIds.Should().Equal(bigBadWolf.Id);
        targetSelection.PublicAnnouncement.Should().BeNull();
        targetSelection.PrivateInstruction.Should().NotBeNullOrWhiteSpace();
        MarkTestCompleted();
    }

    [Fact]
    public void CompleteNightPhase_BigBadWolfInputUsesPublicFlowAndResolvesDawn()
    {
        var (builder, players) = CreateStartedGame();
        var bigBadWolf = players[1];
        var collectiveVictim = players[4];
        var additionalVictim = players[5];
        builder.ConfirmGameStart();

        var result = builder.CompleteNightPhase(new NightActionInputs
        {
            WerewolfIds = [players[0].Id, bigBadWolf.Id],
            WerewolfVictimId = collectiveVictim.Id,
            BigBadWolfId = bigBadWolf.Id,
            BigBadWolfTargetId = additionalVictim.Id
        });

        result.IsSuccess.Should().BeTrue();
        builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Dawn);
        builder.GetGameState()!.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Should().ContainSingle(entry =>
                entry.ActionType ==
                NightActionType.BigBadWolfVictimSelection &&
                entry.TargetIds!.SequenceEqual(
                    new[] { additionalVictim.Id }));

        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            [collectiveVictim.Id] = MainRoleType.SimpleVillager,
            [additionalVictim.Id] = MainRoleType.SimpleVillager
        }).IsSuccess.Should().BeTrue();
        builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Day);
        MarkTestCompleted();
    }

    [Fact]
    public void KnownLivingHolder_WakesSelectsAdditionalVictimAndReturnsPublicSleep()
    {
        var (builder, players, targetSelection) = StartKnownTargetSelection();
        var bigBadWolf = players[1];
        var additionalVictim = players[5];

        var sleep =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(
                    targetSelection.CreateResponse([additionalVictim.Id])));

        sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
        sleep.PublicAnnouncement.Should().Be(
            GameStrings.RoleGoesToSleepSingle.Format(
                GameStrings.BigBadWolfRoleName));
        sleep.PrivateInstruction.Should().BeNull();
        sleep.AffectedPlayerIds.Should().Equal(bigBadWolf.Id);
        builder.GetGameState()!.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Should().ContainSingle(entry =>
                entry.ActionType ==
                NightActionType.BigBadWolfVictimSelection &&
                entry.TargetIds!.SequenceEqual(
                    new[] { additionalVictim.Id }));
        MarkTestCompleted();
    }

    [Fact]
    public void NoLegalAdditionalTarget_AfterAvailabilityGoesStraightToPublicSleep()
    {
        var (builder, players) = CreateStartedGame();
        var bigBadWolf = players[1];
        var collectiveVictim = players[4];
        builder.ArrangeKnownRole(bigBadWolf.Id, MainRoleType.BigBadWolf);
        builder.ConfirmGameStart();
        foreach (var player in new[]
                 {
                     players[2],
                     players[3],
                     players[5],
                     players[6]
                 })
        {
            builder.ArrangeEliminatedPlayer(player.Id);
        }
        builder.ArrangeKnownWerewolfFactionAgentGroup(
            [players[0].Id, bigBadWolf.Id]);
        builder.ConfirmNightStart();
        var wake =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.CompleteWerewolfNightAction(
                    [players[0].Id, bigBadWolf.Id],
                    collectiveVictim.Id));

        var sleep =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(wake.CreateResponse()));

        sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
        sleep.AffectedPlayerIds.Should().Equal(bigBadWolf.Id);
        builder.GetGameState()!.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Should().NotContain(entry =>
                entry.ActionType ==
                NightActionType.BigBadWolfVictimSelection);
        MarkTestCompleted();
    }

    [Fact]
    public void MissingRetainedCollectiveVictim_OmitsEntireIndividualCall()
    {
        var (builder, players) = CreateStartedGame();
        var bigBadWolf = players[1];
        builder.ArrangeKnownRole(bigBadWolf.Id, MainRoleType.BigBadWolf);
        builder.ConfirmGameStart();
        builder.ArrangeKnownWerewolfFactionAgentGroup();

        var finishNight =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.ConfirmNightStart());

        finishNight.Semantic.Should().Be(
            ModeratorInstructionSemantic.FinishNightActions);
        builder.GetGameState()!.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Should().NotContain(entry =>
                entry.ActionType ==
                NightActionType.BigBadWolfVictimSelection);
        MarkTestCompleted();
    }

    [Fact]
    public void EliminatedFinalKnownWerewolfAgent_DisablesEntireIndividualCall()
    {
        var policy = new SequenceAvailabilityPolicy();
        var (builder, players) = CreateStartedGame(policy);
        var bigBadWolf = players[1];
        var eliminatedAgent = players[2];
        var collectiveVictim = players[4];
        builder.ArrangeKnownRole(bigBadWolf.Id, MainRoleType.BigBadWolf);
        builder.ConfirmGameStart();
        builder.ArrangeKnownWerewolfFactionAgentGroup(
            [players[0].Id, bigBadWolf.Id, eliminatedAgent.Id]);
        builder.ArrangeEliminatedPlayer(
            eliminatedAgent.Id,
            EliminationReason.EventElimination);
        builder.ConfirmNightStart();

        var finishNight =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.CompleteWerewolfNightAction(
                    [players[0].Id, bigBadWolf.Id],
                    collectiveVictim.Id));

        finishNight.Semantic.Should().Be(
            ModeratorInstructionSemantic.FinishNightActions);
        policy.Attempts.Should().BeEmpty();
        builder.GetGameState()!.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Should().NotContain(entry =>
                entry.ActionType ==
                NightActionType.BigBadWolfVictimSelection);
        MarkTestCompleted();
    }

    [Fact]
    public void EliminatedKnownAgent_DisablesPowerForLaterSwappedHolder()
    {
        var policy = new SequenceAvailabilityPolicy();
        var (builder, players) = CreateStartedGame(policy);
        var originalHolder = players[1];
        var eliminatedAgent = players[2];
        var laterHolder = players[3];
        var collectiveVictim = players[4];
        builder.ArrangeCurrentRole(
            originalHolder.Id,
            MainRoleType.SimpleVillager);
        builder.ArrangeKnownRole(laterHolder.Id, MainRoleType.BigBadWolf);
        builder.ArrangeKnownWerewolfFactionAgentGroup(
            [players[0].Id, eliminatedAgent.Id, laterHolder.Id]);
        builder.ArrangeEliminatedPlayer(eliminatedAgent.Id);
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var finishNight =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.CompleteWerewolfNightAction(
                    [players[0].Id, laterHolder.Id],
                    collectiveVictim.Id));

        finishNight.Semantic.Should().Be(
            ModeratorInstructionSemantic.FinishNightActions);
        policy.Attempts.Should().BeEmpty();
        builder.GetGameState()!.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Should().NotContain(entry =>
                entry.ActionType ==
                NightActionType.BigBadWolfVictimSelection);
        MarkTestCompleted();
    }

    [Fact]
    public void SupersededHistoricalAgentFact_DoesNotDisableCurrentPower()
    {
        var (builder, players) = CreateStartedGame();
        var bigBadWolf = players[1];
        var formerlyKnownAgent = players[2];
        var collectiveVictim = players[4];
        builder.ArrangeKnownRole(bigBadWolf.Id, MainRoleType.BigBadWolf);
        builder.ConfirmGameStart();
        builder.ArrangeKnownWerewolfFactionAgentGroup(
            [players[0].Id, bigBadWolf.Id, formerlyKnownAgent.Id]);
        builder.ArrangeEliminatedPlayer(formerlyKnownAgent.Id);
        builder.ArrangeKnownWerewolfFactionAgentGroup(
            [players[0].Id, bigBadWolf.Id]);
        builder.ConfirmNightStart();

        var wake =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.CompleteWerewolfNightAction(
                    [players[0].Id, bigBadWolf.Id],
                    collectiveVictim.Id));

        wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
        wake.AffectedPlayerIds.Should().Equal(bigBadWolf.Id);
        MarkTestCompleted();
    }

    [Fact]
    public void AvailabilityDenial_IsEvaluatedOnceWithoutResource_ThenSleeps()
    {
        var policy = new SequenceAvailabilityPolicy(false);
        var (builder, players) = CreateStartedGame(policy);
        var bigBadWolf = players[1];
        var collectiveVictim = players[4];
        builder.ArrangeKnownRole(bigBadWolf.Id, MainRoleType.BigBadWolf);
        builder.ArrangeKnownWerewolfFactionAgentGroup(
            [players[0].Id, bigBadWolf.Id]);
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var wake =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.CompleteWerewolfNightAction(
                    [players[0].Id, bigBadWolf.Id],
                    collectiveVictim.Id));

        var sleep =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(wake.CreateResponse()));

        sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
        policy.Attempts.Should().ContainSingle();
        var attempt = policy.Attempts.Single();
        attempt.ActingPlayer.Id.Should().Be(bigBadWolf.Id);
        attempt.SourceRole.Should().Be(MainRoleType.BigBadWolf);
        attempt.SourcePower.Identifier.Value.Should().Be(
            "big-bad-wolf-additional-victim");
        attempt.PowerInstance.Id.Should().Be(bigBadWolf.Id);
        attempt.PowerInstance.Origin.Should().Be(
            RolePowerInstanceOrigin.Native);
        attempt.OneUseResource.Should().BeNull();
        MarkTestCompleted();
    }

    [Fact]
    public void InvalidTargetResponse_IsSideEffectFreeAndCanBeRetried()
    {
        var (builder, players, targetSelection) = StartKnownTargetSelection();
        var collectiveVictim = players[4];
        var additionalVictim = players[5];
        var historyCount = builder.GetGameState()!.GameHistoryLog.Count();

        var invalidResponse = new ModeratorResponse
        {
            InstructionId = targetSelection.InstructionId,
            Type = ExpectedInputType.PlayerSelection,
            SelectedPlayerIds = new HashSet<Guid> { collectiveVictim.Id }
        };

        Action processInvalid = () => builder.Process(invalidResponse);

        processInvalid.Should().ThrowExactly<InvalidOperationException>();
        builder.GetGameState()!.GameHistoryLog.Should().HaveCount(historyCount);
        builder.GetGameState()!.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Should().NotContain(entry =>
                entry.ActionType ==
                NightActionType.BigBadWolfVictimSelection);

        var sleep =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(
                    targetSelection.CreateResponse([additionalVictim.Id])));
        sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
        MarkTestCompleted();
    }

    [Fact]
    public void AdditionalVictimIntent_ResolvesThroughCanonicalDawnAttack()
    {
        var (builder, players, targetSelection) = StartKnownTargetSelection();
        var additionalVictim = players[5];
        var sleep =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(
                    targetSelection.CreateResponse([additionalVictim.Id])));
        var finishNight =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(sleep.CreateResponse()));
        builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();

        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            [players[4].Id] = MainRoleType.SimpleVillager,
            [additionalVictim.Id] = MainRoleType.SimpleVillager
        }).IsSuccess.Should().BeTrue();

        additionalVictim.State.Health.Should().Be(PlayerHealth.Dead);
        MarkTestCompleted();
    }

    [Fact]
    public void PowerIsRecurring_WhenNoAgentWasEliminated()
    {
        var (builder, players, targetSelection) = StartKnownTargetSelection();
        var firstAdditionalVictim = players[5];
        var firstSleep =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(
                    targetSelection.CreateResponse(
                        [firstAdditionalVictim.Id])));
        var firstFinishNight =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(firstSleep.CreateResponse()));
        builder.Process(firstFinishNight.CreateResponse()).IsSuccess
            .Should().BeTrue();
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            [players[4].Id] = MainRoleType.SimpleVillager,
            [firstAdditionalVictim.Id] = MainRoleType.SimpleVillager
        }).IsSuccess.Should().BeTrue();
        builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();

        builder.ConfirmNightStart();
        var secondCollectiveVictim = players[3];
        var secondWake =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.CompleteWerewolfNightActionSubsequentNight(
                    secondCollectiveVictim.Id));
        var secondTargetSelection =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.Process(secondWake.CreateResponse()));
        var secondAdditionalVictim = players[6];
        builder.Process(
                secondTargetSelection.CreateResponse(
                    [secondAdditionalVictim.Id])).IsSuccess.Should().BeTrue();

        builder.GetGameState()!.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Where(entry =>
                entry.ActionType ==
                NightActionType.BigBadWolfVictimSelection)
            .Should().HaveCount(2);
        builder.GetGameState()!.GameHistoryLog
            .OfType<IOneUseRolePowerCommittedLogEntry>()
            .Should().BeEmpty();
        MarkTestCompleted();
    }

    private (
        GameTestBuilder Builder,
        IPlayer[] Players,
        SelectPlayersInstruction TargetSelection)
        StartKnownTargetSelection()
    {
        var (builder, players) = CreateStartedGame();
        var bigBadWolf = players[1];
        builder.ArrangeKnownRole(bigBadWolf.Id, MainRoleType.BigBadWolf);
        builder.ArrangeKnownWerewolfFactionAgentGroup(
            [players[0].Id, bigBadWolf.Id]);
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var wake =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.CompleteWerewolfNightAction(
                    [players[0].Id, bigBadWolf.Id],
                    players[4].Id));
        wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
        wake.PublicAnnouncement.Should().Be(
            GameStrings.RoleWakesUp.Format(
                GameStrings.BigBadWolfRoleName));
        wake.PrivateInstruction.Should().BeNull();
        wake.AffectedPlayerIds.Should().Equal(bigBadWolf.Id);
        var targetSelection =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.Process(wake.CreateResponse()));
        return (builder, players, targetSelection);
    }

    private (GameTestBuilder Builder, IPlayer[] Players) CreateStartedGame(
        IRolePowerAvailabilityPolicy? policy = null)
    {
        var builder = CreateBuilder()
            .WithOptionalRolePowerAvailabilityPolicy(policy)
            .WithPlayers(7)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.BigBadWolf,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        return (builder, builder.GetGameState()!.GetPlayers().ToArray());
    }

    private sealed class SequenceAvailabilityPolicy(params bool[] decisions)
        : IRolePowerAvailabilityPolicy
    {
        private int _nextDecision;

        public List<RolePowerAttempt> Attempts { get; } = [];

        public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
        {
            Attempts.Add(attempt);
            if (_nextDecision >= decisions.Length)
            {
                throw new InvalidOperationException(
                    "The availability policy was evaluated more often than expected.");
            }

            return decisions[_nextDecision++]
                ? RolePowerAvailabilityResult.Allowed
                : RolePowerAvailabilityResult.Denied;
        }
    }
}
