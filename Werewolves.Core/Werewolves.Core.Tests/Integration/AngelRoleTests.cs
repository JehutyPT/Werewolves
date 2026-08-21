using FluentAssertions;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class AngelRoleTests : DiagnosticTestBase
{
    public AngelRoleTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void NightOneElimination_AfterOrdinaryReveal_WinsForAngelAtDawn()
    {
        var builder = CreateBuilder()
            .WithPlayers(6)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var angel = players[1];

        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightAction([werewolf.Id], angel.Id);
        var nightEnd = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEnd.CreateResponse());

        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            [angel.Id] = MainRoleType.Angel
        });

        builder.GetCurrentInstruction().Should()
            .BeOfType<FinishedGameConfirmationInstruction>();
        var history = builder.GetGameState()!.GameHistoryLog.ToList();
        var ownership = history
            .OfType<PhysicalCharacterCardOwnershipObservedLogEntry>()
            .Single(entry => entry.PlayerId == angel.Id);
        var reveal = history
            .OfType<RoleRevealLogEntry>()
            .Single(entry => entry.RevealedRoles.GetValueOrDefault(angel.Id) == MainRoleType.Angel);
        var elimination = history
            .OfType<PlayerEliminatedLogEntry>()
            .Single(entry => entry.PlayerId == angel.Id);
        var victory = history
            .OfType<VictoryConditionMetLogEntry>()
            .Single();

        history.IndexOf(ownership).Should().BeLessThan(history.IndexOf(reveal));
        history.IndexOf(reveal).Should().BeLessThan(history.IndexOf(elimination));
        history.IndexOf(elimination).Should().BeLessThan(history.IndexOf(victory));
        victory.GameResult.Should().Be(
            new SingleFactionGameResult(Faction.Angel));
        victory.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
        builder.GetGameState()!.RequireKnownFactionBeneficiary(angel.Id)
            .Should().Be(Faction.Villager);
        history.OfType<IFactionFactBatchLogEntry>()
            .SelectMany(entry => entry.Facts)
            .Should().NotContain(fact => fact.Faction == Faction.Angel);

        MarkTestCompleted();
    }

    [Fact]
    public void NightOneElimination_WhenWerewolvesAlsoQualify_UsesSharedResultSelection()
    {
        var builder = CreateBuilder()
            .WithPlayers(5)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.SimpleWerewolf,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();

        builder.ConfirmNightStart();
        builder.CompleteWerewolfNightAction(
            [players[0].Id, players[1].Id],
            players[2].Id);
        ConfirmNightEnd(builder);
        builder.CompleteDawnPhase(new()
        {
            [players[2].Id] = MainRoleType.Angel
        });

        builder.GetGameState()!.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>()
            .Single().GameResult.Should().Be(
                new SharedVictoryGameResult(
                    [Faction.Werewolf, Faction.Angel]));

        MarkTestCompleted();
    }

    [Fact]
    public void StandardDayOneVoteElimination_WinsForAngelAtPreNight()
    {
        var builder = CreateBuilder()
            .WithPlayers(7)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[2].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: false);

        var debate = builder.GetCurrentInstruction().Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var vote = builder.Process(debate.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        builder.Process(vote.CreateResponse([players[1].Id]));
        CompletePublicEliminationFlow(builder, players[1].Id);

        var victory = builder.GetGameState()!.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>().Single();
        victory.GameResult.Should().Be(
            new SingleFactionGameResult(Faction.Angel));
        victory.VictoryCheckWindow.Should().Be(VictoryCheckWindow.PreNight);

        MarkTestCompleted();
    }

    [Fact]
    public void ConsecutiveDayOneVoteElimination_WinsForAngelAtPreNight()
    {
        var builder = CreateBuilder()
            .WithPlayers(8)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.VillagerVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var publicObservation = builder.ConfirmGameStart()
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        builder.Process(publicObservation.CreateResponse([players[1].Id]));
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[2].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: false);

        var debate = builder.GetCurrentInstruction().Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var firstVote = builder.Process(debate.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        builder.ArrangeDayAction(DayPowerType.JudgeExtraVote);
        var firstAnnouncement = builder.Process(
                firstVote.CreateResponse([players[1].Id]))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var secondVote = builder.Process(firstAnnouncement.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var angelReveal = builder.Process(
                secondVote.CreateResponse([players[3].Id]))
            .ModeratorInstruction.Should()
            .BeOfType<AssignRolesInstruction>().Subject;
        var secondAnnouncement = builder.Process(
                angelReveal.CreateObservedRoleResponse(new()
                {
                    [players[3].Id] = MainRoleType.Angel
                }))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        builder.Process(secondAnnouncement.CreateResponse());

        var session = builder.GetGameState()!;
        session.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
            .Select(entry => entry.ReportedOutcomePlayerId)
            .Should().Equal(players[1].Id, players[3].Id);
        var victory = session.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>().Single();
        victory.GameResult.Should().Be(
            new SingleFactionGameResult(Faction.Angel));
        victory.VictoryCheckWindow.Should().Be(VictoryCheckWindow.PreNight);

        MarkTestCompleted();
    }

    [Fact]
    public void FirstDayOneVoteAngel_WithCommittedConsecutiveVote_WaitsAcrossRecovery()
    {
        var builder = CreateBuilder()
            .WithPlayers(8)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[2].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: false);
        var debate = builder.GetCurrentInstruction().Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var firstVote = builder.Process(debate.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        builder.ArrangeDayAction(DayPowerType.JudgeExtraVote);
        var angelReveal = builder.Process(
                firstVote.CreateResponse([players[1].Id]))
            .ModeratorInstruction.Should()
            .BeOfType<AssignRolesInstruction>().Subject;
        var firstAnnouncement = builder.Process(
                angelReveal.CreateObservedRoleResponse(new()
                {
                    [players[1].Id] = MainRoleType.Angel
                }))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        builder.GetGameState()!.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>().Should().BeEmpty();
        var secondVote = builder.Process(firstAnnouncement.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;

        var recoveredService = new GameService();
        var recoveredId = recoveredService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var recoveredVote = recoveredService.GetCurrentInstruction(recoveredId)
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        recoveredVote.InstructionId.Should().Be(secondVote.InstructionId);
        var secondReveal = recoveredService.ProcessInstruction(
                recoveredId,
                recoveredVote.CreateResponse([players[3].Id]))
            .ModeratorInstruction.Should()
            .BeOfType<AssignRolesInstruction>().Subject;
        var secondAnnouncement = recoveredService.ProcessInstruction(
                recoveredId,
                secondReveal.CreateObservedRoleResponse(new()
                {
                    [players[3].Id] = MainRoleType.SimpleVillager
                }))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        _ = recoveredService.ProcessInstruction(
            recoveredId,
            secondAnnouncement.CreateResponse());

        var recovered = recoveredService.GetGameStateView(recoveredId)!;
        recovered.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
            .Select(entry => entry.ReportedOutcomePlayerId)
            .Should().Equal(players[1].Id, players[3].Id);
        recovered.GameHistoryLog.OfType<VictoryConditionMetLogEntry>()
            .Single().GameResult.Should().Be(
                new SingleFactionGameResult(Faction.Angel));

        MarkTestCompleted();
    }

    [Fact]
    public void DayOneCascadeElimination_WinsForAngelAtPreNight()
    {
        var reaction = new AngelCascadeReaction();
        var builder = CreateBuilder()
            .WithEliminationCascadeReaction(reaction)
            .WithPlayers(8)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        reaction.Configure(players[3].Id, players[1].Id);
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[2].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: false);

        var debate = builder.GetCurrentInstruction().Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var vote = builder.Process(debate.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        builder.Process(vote.CreateResponse([players[3].Id]));
        CompletePublicEliminationFlow(builder, players[1].Id);

        var session = builder.GetGameState()!;
        session.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
            .Should().Contain(entry =>
                entry.PlayerId == players[1].Id &&
                entry.CurrentPhase == GamePhase.Day &&
                entry.TurnNumber == 1);
        session.GameHistoryLog.OfType<VictoryConditionMetLogEntry>()
            .Single().GameResult.Should().Be(
                new SingleFactionGameResult(Faction.Angel));

        MarkTestCompleted();
    }

    [Fact]
    public void NightTwoElimination_WinsAtDawnWithoutExpiringAngel()
    {
        var builder = CreateBuilder()
            .WithPlayers(8)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[2].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: false);
        builder.CompleteDayPhaseWithTie();
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[1].Id,
            MainRoleType.Angel,
            subsequentNight: true);

        var session = builder.GetGameState()!;
        var victory = session.GameHistoryLog
            .OfType<VictoryConditionMetLogEntry>().Single();
        victory.GameResult.Should().Be(
            new SingleFactionGameResult(Faction.Angel));
        victory.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
        session.GameHistoryLog.OfType<AngelExpiredLogEntry>()
            .Should().BeEmpty();

        MarkTestCompleted();
    }

    [Fact]
    public void NightTwoDawnWithoutAngelVictory_ExpiresKnownHolderOnceAndRehydratesIdempotently()
    {
        var builder = CreateBuilder()
            .WithPlayers(8)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        builder.ArrangeKnownPhysicalRole(players[1].Id, MainRoleType.Angel);
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[2].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: false);
        builder.CompleteDayPhaseWithTie();
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[3].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: true);

        AssertExpiredKnownHolder(builder.GetGameState()!, players[1].Id);
        var firstService = new GameService();
        var firstId = firstService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var firstRecovered = firstService.GetGameStateView(firstId)!;
        AssertExpiredKnownHolder(firstRecovered, players[1].Id);
        var secondService = new GameService();
        var secondId = secondService.RehydrateSession(firstRecovered.Serialize());
        AssertExpiredKnownHolder(
            secondService.GetGameStateView(secondId)!,
            players[1].Id);

        MarkTestCompleted();
    }

    [Fact]
    public void NightTwoStart_BeforeFinalDawnWindow_RehydratesWithoutExpiry()
    {
        var builder = CreateBuilder()
            .WithPlayers(7)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[2].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: false);
        builder.CompleteDayPhaseWithTie();

        var recoveredService = new GameService();
        var recoveredId = recoveredService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredId)!;
        recovered.TurnNumber.Should().Be(2);
        recovered.GetCurrentPhase().Should().Be(GamePhase.Night);
        recovered.GameHistoryLog.OfType<AngelExpiredLogEntry>()
            .Should().BeEmpty();

        MarkTestCompleted();
    }

    [Fact]
    public void NightTwoDawnOtherFactionVictory_PreservesTerminalAdjacencyThenExpiresAngel()
    {
        var builder = CreateBuilder()
            .WithPlayers(5)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[2].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: false);
        var debate = builder.GetCurrentInstruction().Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var vote = builder.Process(debate.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var reveal = builder.Process(vote.CreateResponse([players[3].Id]))
            .ModeratorInstruction.Should()
            .BeOfType<AssignRolesInstruction>().Subject;
        var announcement = builder.Process(reveal.CreateObservedRoleResponse(new()
            {
                [players[3].Id] = MainRoleType.SimpleVillager
            }))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        builder.Process(announcement.CreateResponse());
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[4].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: true);

        var history = builder.GetGameState()!.GameHistoryLog.ToList();
        var victoryIndex = history.FindIndex(entry =>
            entry is VictoryConditionMetLogEntry);
        history[victoryIndex - 1].Should().BeOfType<PhaseTransitionLogEntry>();
        history[victoryIndex].Should().BeOfType<VictoryConditionMetLogEntry>()
            .Which.GameResult.Should().Be(
                new SingleFactionGameResult(Faction.Werewolf));
        history[victoryIndex + 1].Should().BeOfType<AngelExpiredLogEntry>();

        MarkTestCompleted();
    }

    [Fact]
    public void Recovery_MissingAngelExpiry_IsRejected()
    {
        var (builder, _) = CreateKnownHolderExpiryScenario();
        var tampered = RecoveryPayloadTestDriver
            .Parse(builder.GetGameState()!.Serialize())
            .RemoveAngelExpiry()
            .Serialize();

        Action rehydrate = () => new GameService().RehydrateSession(tampered);

        rehydrate.Should().Throw<InvalidOperationException>();
        MarkTestCompleted();
    }

    [Fact]
    public void Recovery_DuplicateAngelExpiry_IsRejected()
    {
        var (builder, _) = CreateKnownHolderExpiryScenario();
        var tampered = RecoveryPayloadTestDriver
            .Parse(builder.GetGameState()!.Serialize())
            .DuplicateAngelExpiry()
            .Serialize();

        Action rehydrate = () => new GameService().RehydrateSession(tampered);

        rehydrate.Should().Throw<InvalidOperationException>();
        MarkTestCompleted();
    }

    [Fact]
    public void Recovery_DuplicatePostExpiryProjection_IsRejected()
    {
        var (builder, _) = CreateKnownHolderExpiryScenario();
        var tampered = RecoveryPayloadTestDriver
            .Parse(builder.GetGameState()!.Serialize())
            .DuplicatePostExpirySimpleVillagerProjection()
            .Serialize();

        Action rehydrate = () => new GameService().RehydrateSession(tampered);

        rehydrate.Should().Throw<InvalidOperationException>();
        MarkTestCompleted();
    }

    [Fact]
    public void Recovery_DelayedKnownHolderProjection_IsRejected()
    {
        var (builder, _) = CreateKnownHolderExpiryScenario();
        builder.CompleteDayPhaseWithTie();
        var tampered = RecoveryPayloadTestDriver
            .Parse(builder.GetGameState()!.Serialize())
            .MoveKnownAngelProjectionToHistoryTail()
            .Serialize();

        Action rehydrate = () => new GameService().RehydrateSession(tampered);

        rehydrate.Should().Throw<InvalidOperationException>();
        MarkTestCompleted();
    }

    [Fact]
    public void Recovery_AngelVictoryWithoutQualifyingElimination_IsRejected()
    {
        var builder = CreateBuilder()
            .WithPlayers(6)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[2].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: false);
        var tampered = RecoveryPayloadTestDriver
            .Parse(builder.GetGameState()!.Serialize())
            .AppendAngelVictory(
                turnNumber: 1,
                phase: GamePhase.Day,
                window: VictoryCheckWindow.Dawn)
            .Serialize();

        Action rehydrate = () => new GameService().RehydrateSession(tampered);

        rehydrate.Should().Throw<InvalidOperationException>();
        MarkTestCompleted();
    }

    [Fact]
    public void ReachableThiefOfferAngel_CommitsNoAngelFactionFact()
    {
        var cards = new[]
        {
            new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Thief),
            new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
            new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
            new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
            new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
            new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Angel),
            new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Seer)
        };
        var lockIn = new RoleLockIn(
            version: 1,
            playerCount: 5,
            roleComposition: cards,
            dealPoolCardIds: cards.Take(5).Select(card => card.Id),
            offer1CardId: cards[5].Id,
            offer2CardId: cards[6].Id);
        var service = new GameService();
        var start = service.StartNewGame(new GameSessionConfig(
            ["Player1", "Player2", "Player3", "Player4", "Player5"],
            lockIn));
        var session = service.GetGameStateView(start.GameGuid)!;
        var holder = session.GetPlayers().First();
        var nightStart = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            service.ProcessInstruction(start.GameGuid, start.CreateResponse()));
        var identification = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            service.ProcessInstruction(start.GameGuid, nightStart.CreateResponse()));
        var choice = InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
            service.ProcessInstruction(
                start.GameGuid,
                identification.CreateResponse([holder.Id])));

        _ = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            service.ProcessInstruction(
                start.GameGuid,
                choice.CreateResponse(ThiefOfferOptionIds.Offer1)));

        session.GetPlayerState(holder.Id).CurrentRole.Should()
            .Be(MainRoleType.Angel);
        session.RequireKnownFactionBeneficiary(holder.Id).Should()
            .Be(Faction.Villager);
        session.GetFactionAgentKnowledge(holder.Id, Faction.Angel).Should()
            .Be(FactionAgentKnowledge.Unknown);
        session.GameHistoryLog.OfType<IFactionFactBatchLogEntry>()
            .SelectMany(entry => entry.Facts)
            .Should().NotContain(fact => fact.Faction == Faction.Angel);

        var recoveredService = new GameService();
        var recoveredId = recoveredService.RehydrateSession(session.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredId)!;
        recovered.GetPlayerState(holder.Id).CurrentRole.Should()
            .Be(MainRoleType.Angel);
        recovered.GetFactionAgentKnowledge(holder.Id, Faction.Angel).Should()
            .Be(FactionAgentKnowledge.Unknown);
        recovered.GameHistoryLog.OfType<IFactionFactBatchLogEntry>()
            .SelectMany(entry => entry.Facts)
            .Should().NotContain(fact => fact.Faction == Faction.Angel);
        CompleteOfferedAngelNightOneElimination(
            recoveredService,
            recoveredId,
            holder.Id,
            recovered.GetPlayers().ElementAt(1).Id);
        recovered.GameHistoryLog
            .OfType<PhysicalCharacterCardOwnershipObservedLogEntry>()
            .Should().NotContain(entry =>
                entry.PlayerId == holder.Id &&
                entry.PrintedRole == MainRoleType.Angel);
        recovered.GameHistoryLog.OfType<VictoryConditionMetLogEntry>()
            .Single().GameResult.Should().Be(
                new SingleFactionGameResult(Faction.Angel));

        MarkTestCompleted();
    }

    [Fact]
    public void DayTwoElimination_AfterNightTwoDawnExpiry_DoesNotQualifyAngel()
    {
        var (builder, angelId) = CreateKnownHolderExpiryScenario();
        var debate = builder.GetCurrentInstruction().Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var vote = builder.Process(debate.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var announcement = builder.Process(vote.CreateResponse([angelId]))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        builder.Process(announcement.CreateResponse());

        var history = builder.GetGameState()!.GameHistoryLog.ToList();
        var expiryIndex = history.FindIndex(entry =>
            entry is AngelExpiredLogEntry);
        var eliminationIndex = history.FindIndex(entry =>
            entry is PlayerEliminatedLogEntry elimination &&
            elimination.PlayerId == angelId &&
            elimination is
            {
                TurnNumber: 2,
                CurrentPhase: GamePhase.Day
            });
        expiryIndex.Should().BeGreaterThanOrEqualTo(0);
        eliminationIndex.Should().BeGreaterThan(expiryIndex);
        history.OfType<VictoryConditionMetLogEntry>()
            .Should().NotContain(entry => IncludesAngel(entry.GameResult));

        MarkTestCompleted();
    }

    [Fact]
    public void PostExpiryUnknownHolder_LaterRevealPreservesPhysicalAngelAndProjectsSimpleVillager()
    {
        var builder = CreateBuilder()
            .WithPlayers(9)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[2].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: false);
        builder.CompleteDayPhaseWithTie();
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[3].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: true);

        builder.GetGameState()!.GameHistoryLog
            .OfType<AngelExpiredLogEntry>().Should().ContainSingle();
        var debate = builder.GetCurrentInstruction().Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var vote = builder.Process(debate.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var reveal = builder.Process(vote.CreateResponse([players[1].Id]))
            .ModeratorInstruction.Should()
            .BeOfType<AssignRolesInstruction>().Subject;
        var announcement = builder.Process(reveal.CreateObservedRoleResponse(new()
            {
                [players[1].Id] = MainRoleType.Angel
            }))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;

        var observed = builder.GetGameState()!.GetPlayerState(players[1].Id);
        observed.PhysicalCharacterCardRole.Should().Be(MainRoleType.Angel);
        observed.ModeratorKnownRole.Should().Be(MainRoleType.Angel);
        observed.PubliclyRevealedRole.Should().Be(MainRoleType.Angel);
        observed.CurrentRole.Should().Be(MainRoleType.SimpleVillager);
        builder.Process(announcement.CreateResponse());

        var history = builder.GetGameState()!.GameHistoryLog.ToList();
        var expiryIndex = history.FindIndex(entry => entry is AngelExpiredLogEntry);
        var ownershipIndex = history.FindIndex(entry =>
            entry is PhysicalCharacterCardOwnershipObservedLogEntry ownership &&
            ownership.PlayerId == players[1].Id);
        var revealIndex = history.FindIndex(entry =>
            entry is RoleRevealLogEntry roleReveal &&
            roleReveal.RevealedRoles.ContainsKey(players[1].Id));
        var projectionIndex = history.FindIndex(entry =>
            entry is AssignRoleLogEntry assignment &&
            assignment.AssignedMainRole == MainRoleType.SimpleVillager &&
            assignment.PlayerIds.Contains(players[1].Id));
        expiryIndex.Should().BeLessThan(ownershipIndex);
        ownershipIndex.Should().BeLessThan(revealIndex);
        revealIndex.Should().BeLessThan(projectionIndex);

        var recoveredService = new GameService();
        var recoveredId = recoveredService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredId)!;
        recovered.GameHistoryLog.OfType<AngelExpiredLogEntry>()
            .Should().ContainSingle();
        recovered.GetPlayerState(players[1].Id).CurrentRole.Should()
            .Be(MainRoleType.SimpleVillager);
        recovered.GetPlayerState(players[1].Id).PhysicalCharacterCardRole.Should()
            .Be(MainRoleType.Angel);
        recovered.GameHistoryLog.OfType<VictoryConditionMetLogEntry>()
            .Should().BeEmpty();

        MarkTestCompleted();
    }

    private (GameTestBuilder Builder, Guid AngelId)
        CreateKnownHolderExpiryScenario()
    {
        var builder = CreateBuilder()
            .WithPlayers(8)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Angel,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        builder.ArrangeKnownPhysicalRole(players[1].Id, MainRoleType.Angel);
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[2].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: false);
        builder.CompleteDayPhaseWithTie();
        AdvanceNightAndDawn(
            builder,
            players[0].Id,
            players[3].Id,
            MainRoleType.SimpleVillager,
            subsequentNight: true);
        return (builder, players[1].Id);
    }

    private static void AdvanceNightAndDawn(
        GameTestBuilder builder,
        Guid werewolfId,
        Guid victimId,
        MainRoleType victimRole,
        bool subsequentNight)
    {
        builder.ConfirmNightStart();
        if (subsequentNight)
        {
            builder.CompleteWerewolfNightActionSubsequentNight(victimId);
        }
        else
        {
            builder.CompleteWerewolfNightAction([werewolfId], victimId);
        }

        ConfirmNightEnd(builder);
        builder.CompleteDawnPhase(new()
        {
            [victimId] = victimRole
        });
    }

    private static void ConfirmNightEnd(GameTestBuilder builder)
    {
        var nightEnd = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        builder.Process(nightEnd.CreateResponse());
    }

    private static void CompletePublicEliminationFlow(
        GameTestBuilder builder,
        Guid angelId)
    {
        for (var step = 0; step < 12; step++)
        {
            switch (builder.GetCurrentInstruction())
            {
                case FinishedGameConfirmationInstruction:
                    return;
                case AssignRolesInstruction assignment:
                    builder.Process(assignment.CreateResponse(
                        assignment.PlayersForAssignment.ToDictionary(
                            playerId => playerId,
                            playerId => playerId == angelId
                                ? MainRoleType.Angel
                                : MainRoleType.SimpleVillager)));
                    break;
                case ConfirmationInstruction confirmation:
                    builder.Process(confirmation.CreateResponse());
                    break;
                case var instruction:
                    throw new InvalidOperationException(
                        $"Unexpected Angel cascade instruction: {instruction?.GetType().Name ?? "none"}.");
            }
        }

        throw new InvalidOperationException(
            "Angel cascade did not reach the victory instruction.");
    }

    private static void CompleteOfferedAngelNightOneElimination(
        GameService service,
        Guid gameId,
        Guid angelId,
        Guid werewolfId)
    {
        for (var step = 0; step < 20; step++)
        {
            var instruction = service.GetCurrentInstruction(gameId);
            switch (instruction)
            {
                case FinishedGameConfirmationInstruction:
                    return;
                case SelectPlayersInstruction
                {
                    Semantic:
                        ModeratorInstructionSemantic
                            .ObserveWerewolfFactionAgentGroup
                } observation:
                    service.ProcessInstruction(
                        gameId,
                        observation.CreateResponse([werewolfId]));
                    break;
                case SelectPlayersInstruction
                {
                    Semantic: ModeratorInstructionSemantic.SelectWerewolfVictim
                } victim:
                    service.ProcessInstruction(
                        gameId,
                        victim.CreateResponse([angelId]));
                    break;
                case AssignRolesInstruction assignment:
                    service.ProcessInstruction(
                        gameId,
                        assignment.CreateResponse(
                            assignment.PlayersForAssignment.ToDictionary(
                                playerId => playerId,
                                playerId => playerId == angelId
                                    ? MainRoleType.Angel
                                    : MainRoleType.SimpleVillager)));
                    break;
                case ConfirmationInstruction confirmation:
                    service.ProcessInstruction(
                        gameId,
                        confirmation.CreateResponse());
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected offered-Angel instruction: {instruction?.GetType().Name ?? "none"}.");
            }
        }

        service.GetCurrentInstruction(gameId).Should()
            .BeOfType<FinishedGameConfirmationInstruction>(
                nameof(CompleteOfferedAngelNightOneElimination));
    }

    private static bool IncludesAngel(GameResult result) => result switch
    {
        SingleFactionGameResult single => single.Faction == Faction.Angel,
        SharedVictoryGameResult shared => shared.Factions.Contains(Faction.Angel),
        _ => false
    };

    private static void AssertExpiredKnownHolder(
        IGameSession session,
        Guid angelId)
    {
        session.GameHistoryLog.OfType<AngelExpiredLogEntry>()
            .Should().ContainSingle();
        var history = session.GameHistoryLog.ToList();
        var expiryIndex = history.FindIndex(entry => entry is AngelExpiredLogEntry);
        history[expiryIndex - 1].Should().BeOfType<PhaseTransitionLogEntry>()
            .Which.Should().Match<PhaseTransitionLogEntry>(entry =>
                entry.PreviousPhase == GamePhase.Dawn &&
                entry.CurrentPhase == GamePhase.Day &&
                entry.TurnNumber == 2);
        history[expiryIndex + 1].Should().BeOfType<AssignRoleLogEntry>()
            .Which.PlayerIds.Should().Contain(angelId);
        var state = session.GetPlayerState(angelId);
        state.CurrentRole.Should().Be(MainRoleType.SimpleVillager);
        state.PhysicalCharacterCardRole.Should().Be(MainRoleType.Angel);
        state.ModeratorKnownRole.Should().Be(MainRoleType.Angel);
        state.PubliclyRevealedRole.Should().BeNull();
    }

    private sealed class AngelCascadeReaction : IEliminationCascadeReaction
    {
        private Guid _triggerId;
        private Guid _angelId;

        public string ReactionId => nameof(AngelCascadeReaction);

        internal void Configure(Guid triggerId, Guid angelId)
        {
            _triggerId = triggerId;
            _angelId = angelId;
        }

        public EliminationCascadeReactionResult Advance(
            GameSession session,
            IReadOnlyCollection<Guid> eliminatedPlayerIds,
            ModeratorResponse input) =>
            eliminatedPlayerIds.Contains(_triggerId)
                ? EliminationCascadeReactionResult.Complete(
                    [new(_angelId, EliminationReason.EventElimination)])
                : EliminationCascadeReactionResult.Complete();
    }
}
