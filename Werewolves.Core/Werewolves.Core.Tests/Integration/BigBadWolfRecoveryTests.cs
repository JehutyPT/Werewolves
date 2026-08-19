using FluentAssertions;
using FluentAssertions.Execution;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class BigBadWolfRecoveryTests
{
    [Fact]
    public void AcceptedIdentification_FreshServiceRestoresExactTargetSelectionWithoutReopeningIdentification()
    {
        var (builder, holderId, _, additionalVictimId, identification) =
            CreateGameAtIdentification();
        var acceptedIdentification =
            identification.CreateResponse([holderId]);
        var expectedTargetSelection = builder.Process(acceptedIdentification)
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var serializedSession = builder.GetGameState()!.Serialize();
        var freshService = new GameService();

        var recoveredGameId =
            freshService.RehydrateSession(serializedSession);
        var recoveredSession =
            freshService.GetGameStateView(recoveredGameId)!;
        var recoveredTargetSelection = freshService
            .GetCurrentInstruction(recoveredGameId)
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        using (new AssertionScope())
        {
            recoveredSession.GameHistoryLog
                .OfType<RoleIdentificationLogEntry>()
                .Should().ContainSingle(entry =>
                    entry.Role == MainRoleType.BigBadWolf &&
                    entry.PlayerIds.SetEquals(new[] { holderId }));
            AssertEquivalentInstruction(
                recoveredTargetSelection,
                expectedTargetSelection);
        }

        var beforeReplay =
            PublicGameSessionSnapshot.Capture(
                freshService,
                recoveredGameId);
        Action replayIdentification = () =>
            freshService.ProcessInstruction(
                recoveredGameId,
                acceptedIdentification);

        replayIdentification.Should().Throw<InvalidOperationException>();
        PublicGameSessionSnapshot.Capture(freshService, recoveredGameId)
            .Should().BeEquivalentTo(
                beforeReplay,
                options => options.WithStrictOrdering());

        var recoveredSleep = freshService.ProcessInstruction(
                recoveredGameId,
                recoveredTargetSelection.CreateResponse(
                    [additionalVictimId]))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        recoveredSleep.Semantic.Should().Be(
            ModeratorInstructionSemantic.PutRoleToSleep);
        recoveredSession.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Should().ContainSingle(entry =>
                entry.ActionType ==
                NightActionType.BigBadWolfVictimSelection &&
                entry.TargetIds!.SequenceEqual(
                    new[] { additionalVictimId }));
    }

    [Fact]
    public void CommittedTarget_FreshServiceRestoresExactSleepWithoutDuplicatingIntent()
    {
        var (
            builder,
            holderId,
            _,
            additionalVictimId,
            identification) = CreateGameAtIdentification();
        var targetSelection = builder.Process(
                identification.CreateResponse([holderId]))
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var acceptedTarget =
            targetSelection.CreateResponse([additionalVictimId]);
        var expectedSleep = builder.Process(acceptedTarget)
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var serializedSession = builder.GetGameState()!.Serialize();
        var freshService = new GameService();

        var recoveredGameId =
            freshService.RehydrateSession(serializedSession);
        var recoveredSession =
            freshService.GetGameStateView(recoveredGameId)!;
        var recoveredSleep = freshService
            .GetCurrentInstruction(recoveredGameId)
            .Should().BeOfType<ConfirmationInstruction>().Subject;

        AssertEquivalentInstruction(recoveredSleep, expectedSleep);
        recoveredSession.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Should().ContainSingle(entry =>
                entry.ActionType ==
                NightActionType.BigBadWolfVictimSelection &&
                entry.TargetIds!.SequenceEqual(
                    new[] { additionalVictimId }));

        var beforeReplay =
            PublicGameSessionSnapshot.Capture(
                freshService,
                recoveredGameId);
        Action replayTarget = () =>
            freshService.ProcessInstruction(
                recoveredGameId,
                acceptedTarget);

        replayTarget.Should().Throw<InvalidOperationException>();
        PublicGameSessionSnapshot.Capture(freshService, recoveredGameId)
            .Should().BeEquivalentTo(
                beforeReplay,
                options => options.WithStrictOrdering());
        recoveredSession.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Count(entry =>
                entry.ActionType ==
                NightActionType.BigBadWolfVictimSelection)
            .Should().Be(1);

        freshService.ProcessInstruction(
                recoveredGameId,
                recoveredSleep.CreateResponse())
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void LegacyVersionOneCommittedTarget_FreshServiceRejectsBeforeRegisteringSession()
    {
        var (
            builder,
            holderId,
            _,
            additionalVictimId,
            identification) = CreateGameAtIdentification();
        var targetSelection = builder.Process(
                identification.CreateResponse([holderId]))
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        _ = builder.Process(
                targetSelection.CreateResponse([additionalVictimId]))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var legacyPayload = RecoveryPayloadTestDriver
            .Parse(builder.GetGameState()!.Serialize())
            .DowngradeLatestRecurringCommitToLegacyNightAction()
            .Serialize();
        var freshService = new GameService();

        Action rehydrate = () => freshService.RehydrateSession(legacyPayload);

        rehydrate.Should().Throw<InvalidOperationException>();
        freshService.GetGameStateView(builder.GameId).Should().BeNull();
    }

    [Fact]
    public void LoneWerewolfAgentCommittedTarget_FreshServiceRestoresExactSleepWithoutAmbiguousListenerResolution()
    {
        var (
            builder,
            holderId,
            _,
            additionalVictimId,
            identification) = CreateGameAtIdentification(
                loneWerewolfAgent: true);
        var targetSelection = builder.Process(
                identification.CreateResponse([holderId]))
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var expectedSleep = builder.Process(
                targetSelection.CreateResponse([additionalVictimId]))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var freshService = new GameService();

        var recoveredGameId = freshService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var recoveredSleep = freshService
            .GetCurrentInstruction(recoveredGameId)
            .Should().BeOfType<ConfirmationInstruction>().Subject;

        AssertEquivalentInstruction(recoveredSleep, expectedSleep);
        freshService.GetGameStateView(recoveredGameId)!
            .GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Should().ContainSingle(entry =>
                entry.ActionType ==
                    NightActionType.BigBadWolfVictimSelection &&
                entry.TargetIds!.SequenceEqual(
                    new[] { additionalVictimId }));
        freshService.ProcessInstruction(
                recoveredGameId,
                recoveredSleep.CreateResponse())
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CommittedTarget_FreshServiceRejectsCursorAndActionRetargetedToCollectiveVictim()
    {
        var (
            builder,
            holderId,
            collectiveVictimId,
            additionalVictimId,
            identification) = CreateGameAtIdentification();
        var targetSelection = builder.Process(
                identification.CreateResponse([holderId]))
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        builder.Process(
                targetSelection.CreateResponse([additionalVictimId]))
            .IsSuccess.Should().BeTrue();
        var tampered = RecoveryPayloadTestDriver
            .Parse(builder.GetGameState()!.Serialize())
            .RetargetLatestRecurringNightActionAndCursor(
                collectiveVictimId)
            .Serialize();
        var freshService = new GameService();

        Action rehydrate = () => freshService.RehydrateSession(tampered);

        rehydrate.Should().Throw<InvalidOperationException>()
            .WithMessage("*collective victim*");
    }

    [Fact]
    public void CommittedTarget_SemanticallyWrongRecurringCursorIsRejectedAgainstOwnedCommit()
    {
        var (
            builder,
            holderId,
            _,
            additionalVictimId,
            identification) = CreateGameAtIdentification();
        var targetSelection = builder.Process(
                identification.CreateResponse([holderId]))
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        builder.Process(
                targetSelection.CreateResponse([additionalVictimId]))
            .IsSuccess.Should().BeTrue();
        var tampered = RecoveryPayloadTestDriver
            .Parse(builder.GetGameState()!.Serialize())
            .RewriteRecurringCursorSourceRole(MainRoleType.Seer)
            .Serialize();

        Action deserializeStateModels = () => _ = new GameSession(tampered);

        deserializeStateModels.Should().Throw<InvalidOperationException>()
            .WithMessage("*latest recurring native Role Power action*");
    }

    [Fact]
    public void CommittedTarget_CursorlessSleepBoundaryIsRejected()
    {
        var (
            builder,
            holderId,
            _,
            additionalVictimId,
            identification) = CreateGameAtIdentification();
        var targetSelection = builder.Process(
                identification.CreateResponse([holderId]))
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        builder.Process(
                targetSelection.CreateResponse([additionalVictimId]))
            .IsSuccess.Should().BeTrue();
        var tampered = RecoveryPayloadTestDriver
            .Parse(builder.GetGameState()!.Serialize())
            .RemoveDomainRecoveryCursor()
            .Serialize();
        var freshService = new GameService();

        Action rehydrate = () => freshService.RehydrateSession(tampered);

        rehydrate.Should().Throw<InvalidOperationException>();
        freshService.GetGameStateView(builder.GameId).Should().BeNull();
    }

    [Fact]
    public void AcceptedIdentification_TamperedTargetSelectionRosterIsRejected()
    {
        var (builder, holderId, _, _, identification) =
            CreateGameAtIdentification();
        var targetSelection = builder.Process(
                identification.CreateResponse([holderId]))
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var tampered = RecoveryPayloadTestDriver
            .Parse(builder.GetGameState()!.Serialize())
            .RewritePendingPlayerSelectionSelectablePlayerIds(
                targetSelection.SelectablePlayerIds.Skip(1))
            .Serialize();

        Action rehydrate = () => new GameService().RehydrateSession(tampered);

        rehydrate.Should().Throw<InvalidOperationException>()
            .WithMessage("*Big Bad Wolf target selection*");
    }

    [Fact]
    public void EliminatedAgentHistory_FreshServiceKeepsLaterSwappedHolderPowerDisabled()
    {
        var builder = GameTestBuilder.Create()
            .WithPlayers(9)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.BigBadWolf,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var originalHolder = players[1];
        var eliminatedAgent = players[2];
        var laterHolder = players[3];
        var secondCollectiveVictim = players[4];
        var firstCollectiveVictim = players[5];
        var firstAdditionalVictim = players[6];
        builder.ArrangeKnownRole(
            originalHolder.Id,
            MainRoleType.BigBadWolf);
        builder.ArrangeKnownWerewolfFactionAgentGroup(
            [players[0].Id, originalHolder.Id, eliminatedAgent.Id]);
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var firstWake = builder.CompleteWerewolfNightAction(
                [players[0].Id, originalHolder.Id, eliminatedAgent.Id],
                firstCollectiveVictim.Id)
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var firstTargetSelection = builder.Process(
                firstWake.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var firstSleep = builder.Process(
                firstTargetSelection.CreateResponse(
                    [firstAdditionalVictim.Id]))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var firstFinishNight = builder.Process(firstSleep.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        builder.Process(firstFinishNight.CreateResponse())
            .IsSuccess.Should().BeTrue();
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            [firstCollectiveVictim.Id] = MainRoleType.SimpleVillager,
            [firstAdditionalVictim.Id] = MainRoleType.SimpleVillager
        }).IsSuccess.Should().BeTrue();

        builder.ArrangeEliminatedPlayer(eliminatedAgent.Id);
        builder.ArrangeCurrentRole(
            originalHolder.Id,
            MainRoleType.SimpleVillager);
        builder.ArrangeKnownRole(laterHolder.Id, MainRoleType.BigBadWolf);
        builder.ArrangeKnownWerewolfFactionAgentGroup(
            [players[0].Id, eliminatedAgent.Id, laterHolder.Id]);
        builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();
        var freshService = new GameService();
        var recoveredGameId = freshService.RehydrateSession(
            builder.GetGameState()!.Serialize());

        var recoveredNightStart = freshService
            .GetCurrentInstruction(recoveredGameId)
            .Should().BeOfType<ConfirmationInstruction>().Subject;
        var recoveredCollectiveWake = freshService.ProcessInstruction(
                recoveredGameId,
                recoveredNightStart.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var recoveredVictimSelection = freshService.ProcessInstruction(
                recoveredGameId,
                recoveredCollectiveWake.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var recoveredCollectiveSleep = freshService.ProcessInstruction(
                recoveredGameId,
                recoveredVictimSelection.CreateResponse(
                    [secondCollectiveVictim.Id]))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var finishNight = freshService.ProcessInstruction(
                recoveredGameId,
                recoveredCollectiveSleep.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        finishNight.Semantic.Should().Be(
            ModeratorInstructionSemantic.FinishNightActions);
        freshService.GetGameStateView(recoveredGameId)!.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Should().ContainSingle(entry =>
                entry.ActionType ==
                    NightActionType.BigBadWolfVictimSelection &&
                entry.TargetIds!.SequenceEqual(
                    new[] { firstAdditionalVictim.Id }));
    }

    private static (
        GameTestBuilder Builder,
        Guid HolderId,
        Guid CollectiveVictimId,
        Guid AdditionalVictimId,
        SelectPlayersInstruction Identification)
        CreateGameAtIdentification(bool loneWerewolfAgent = false)
    {
        MainRoleType[] roles = loneWerewolfAgent
            ?
            [
                MainRoleType.BigBadWolf,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager
            ]
            :
            [
                MainRoleType.SimpleWerewolf,
                MainRoleType.BigBadWolf,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager
            ];
        var builder = GameTestBuilder.Create()
            .WithPlayers(7)
            .WithRoles(roles);
        builder.StartGame();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var holderId = players[loneWerewolfAgent ? 0 : 1].Id;
        var collectiveVictimId = players[4].Id;
        var additionalVictimId = players[5].Id;
        HashSet<Guid> werewolfAgentIds = loneWerewolfAgent
            ? [holderId]
            : [players[0].Id, holderId];
        builder.ArrangeKnownWerewolfFactionAgentGroup(
            [.. werewolfAgentIds]);
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var identification =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.CompleteWerewolfNightAction(
                    werewolfAgentIds,
                    collectiveVictimId));

        identification.Semantic.Should().Be(
            ModeratorInstructionSemantic.IdentifyRoleHolders);
        identification.RoleIdentification.Should().Be(
            MainRoleType.BigBadWolf);
        return (
            builder,
            holderId,
            collectiveVictimId,
            additionalVictimId,
            identification);
    }

    private static void AssertEquivalentInstruction(
        SelectPlayersInstruction actual,
        SelectPlayersInstruction expected)
    {
        actual.InstructionId.Should().Be(expected.InstructionId);
        actual.Semantic.Should().Be(expected.Semantic);
        actual.PublicAnnouncement.Should().Be(expected.PublicAnnouncement);
        actual.PrivateInstruction.Should().Be(expected.PrivateInstruction);
        actual.AffectedPlayerIds.Should().Equal(expected.AffectedPlayerIds);
        actual.SoundEffects.Should().Equal(expected.SoundEffects);
        actual.SelectablePlayerIds.Should().BeEquivalentTo(
            expected.SelectablePlayerIds);
        actual.CountConstraint.Should().Be(expected.CountConstraint);
        actual.RoleIdentification.Should().Be(expected.RoleIdentification);
    }

    private static void AssertEquivalentInstruction(
        ConfirmationInstruction actual,
        ConfirmationInstruction expected)
    {
        actual.InstructionId.Should().Be(expected.InstructionId);
        actual.Semantic.Should().Be(expected.Semantic);
        actual.PublicAnnouncement.Should().Be(expected.PublicAnnouncement);
        actual.PrivateInstruction.Should().Be(expected.PrivateInstruction);
        actual.AffectedPlayerIds.Should().Equal(expected.AffectedPlayerIds);
        actual.SoundEffects.Should().Equal(expected.SoundEffects);
    }
}
