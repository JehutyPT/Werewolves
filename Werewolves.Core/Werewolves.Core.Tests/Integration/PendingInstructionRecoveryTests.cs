using FluentAssertions;
using FluentAssertions.Execution;
using System.Text.Json.Nodes;
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
        var werewolfObservation = ProcessAndExpect<SelectPlayersInstruction>(
            recoveredService,
            recoveredGameId,
            wildChildSleep.CreateResponse());
        werewolfObservation.Semantic.Should().Be(
            ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
        werewolfObservation.RoleIdentification.Should().BeNull();

        var victimSelection = ProcessAndExpect<SelectPlayersInstruction>(
            recoveredService,
            recoveredGameId,
            werewolfObservation.CreateResponse([werewolfId]));
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
        var roleReveal = ProcessAndExpect<AssignRolesInstruction>(
            recoveredService,
            recoveredGameId,
            nightEnd.CreateResponse());

        recoveredService.ProcessInstruction(
            recoveredGameId,
            roleReveal.CreateResponse(new()
            {
                [roleModelId] = MainRoleType.SimpleVillager
            })).IsSuccess.Should().BeTrue();

        using (new AssertionScope())
        {
            recoveredSession.GetPlayerState(wildChildId).MainRole.Should()
                .Be(MainRoleType.WildChild);
            recoveredSession.GetPlayerState(wildChildId)
                .HasStatusEffect(StatusEffectTypes.WildChildChanged).Should().BeTrue();
            recoveredSession.RequireKnownFactionBeneficiary(wildChildId)
                .Should().Be(Faction.Werewolf);
            recoveredSession.GetFactionAgentKnowledge(
                    wildChildId,
                    Faction.Werewolf)
                .Should().Be(FactionAgentKnowledge.KnownAgent);
            recoveredSession.GetPlayerState(werewolfId).MainRole.Should()
                .BeNull();
            recoveredSession.GetFactionAgentKnowledge(
                    werewolfId,
                    Faction.Werewolf)
                .Should().Be(FactionAgentKnowledge.KnownAgent);
            recoveredSession.RequireKnownFactionBeneficiary(werewolfId)
                .Should().Be(Faction.Werewolf);
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
    public void AcceptedWerewolfAgentGroupObservation_DoubleRehydration_PreservesFactsAndExactVictimSelection()
    {
        var builder = GameTestBuilder.Create()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var observedAgent = players[0];
        var victim = players[2];
        var observation = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        observation.Semantic.Should().Be(
            ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
        var acceptedObservation = observation.CreateResponse([observedAgent.Id]);
        var expectedNext = builder.Process(acceptedObservation).ModeratorInstruction
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        var firstService = new GameService();
        var firstGameId = firstService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var firstRecovered = firstService.GetGameStateView(firstGameId)!;
        var secondService = new GameService();
        var secondGameId = secondService.RehydrateSession(firstRecovered.Serialize());
        var secondRecovered = secondService.GetGameStateView(secondGameId)!;
        var secondNext = secondService.GetCurrentInstruction(secondGameId)
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        using (new AssertionScope())
        {
            var observationEntry = secondRecovered.GameHistoryLog
                .OfType<FactionFactsCommittedLogEntry>()
                .Single(entry =>
                    entry.Source.Kind ==
                    FactionFactSourceKind.ScheduledObservation);
            observationEntry.Facts.Should().HaveCount(players.Length);
            observationEntry.Facts.Should().OnlyContain(fact =>
                fact.Type == FactionFactType.Agent &&
                fact.Faction == Faction.Werewolf);
            observationEntry.Facts.Should().ContainSingle(fact =>
                fact.PlayerId == observedAgent.Id &&
                fact.AgentKnowledge == FactionAgentKnowledge.KnownAgent);
            secondRecovered.GetFactionAgentKnowledge(
                    observedAgent.Id,
                    Faction.Werewolf)
                .Should().Be(FactionAgentKnowledge.KnownAgent);
            foreach (var nonAgent in players.Skip(1))
            {
                secondRecovered.GetFactionAgentKnowledge(
                        nonAgent.Id,
                        Faction.Werewolf)
                    .Should().Be(FactionAgentKnowledge.KnownNonAgent);
            }

            secondNext.InstructionId.Should().Be(expectedNext.InstructionId);
            secondNext.Semantic.Should().Be(expectedNext.Semantic);
            secondNext.PublicAnnouncement.Should().Be(expectedNext.PublicAnnouncement);
            secondNext.PrivateInstruction.Should().Be(expectedNext.PrivateInstruction);
            secondNext.AffectedPlayerIds.Should().Equal(expectedNext.AffectedPlayerIds);
            secondNext.SoundEffects.Should().Equal(expectedNext.SoundEffects);
            secondNext.SelectablePlayerIds.Should()
                .BeEquivalentTo(expectedNext.SelectablePlayerIds);
            secondNext.CountConstraint.Should().BeEquivalentTo(expectedNext.CountConstraint);
            secondNext.RoleIdentification.Should().Be(expectedNext.RoleIdentification);
            secondNext.EmptySelectionOptionLabel.Should()
                .Be(expectedNext.EmptySelectionOptionLabel);
        }

        Action replayAcceptedObservation = () =>
            secondService.ProcessInstruction(secondGameId, acceptedObservation);
        replayAcceptedObservation.Should().Throw<InvalidOperationException>();
        secondRecovered.GameHistoryLog.OfType<FactionFactsCommittedLogEntry>()
            .Count(entry =>
                entry.Source.Kind ==
                FactionFactSourceKind.ScheduledObservation)
            .Should().Be(1);

        var continued = secondService.ProcessInstruction(
            secondGameId,
            secondNext.CreateResponse([victim.Id]));

        continued.IsSuccess.Should().BeTrue();
        continued.ModeratorInstruction.Should().BeOfType<ConfirmationInstruction>();
        secondRecovered.GameHistoryLog.OfType<FactionFactsCommittedLogEntry>()
            .Count(entry =>
                entry.Source.Kind ==
                FactionFactSourceKind.ScheduledObservation)
            .Should().Be(1);
        secondRecovered.GameHistoryLog.OfType<NightActionLogEntry>().Should()
            .ContainSingle(entry =>
                entry.ActionType == NightActionType.WerewolfVictimSelection &&
                entry.TargetIds!.SequenceEqual(new[] { victim.Id }));
    }

    [Fact]
    public void AcceptedWerewolfAgentGroupObservation_WithoutLegalVictim_DoubleRehydrationContinuesAtSleep()
    {
        var builder = GameTestBuilder.Create()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var observation = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        var acceptedObservation = observation.CreateResponse(
            players.Select(player => player.Id).ToHashSet());
        var expectedSleep = builder.Process(acceptedObservation)
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        expectedSleep.Semantic.Should().Be(
            ModeratorInstructionSemantic.PutRoleToSleep);

        var firstService = new GameService();
        var firstGameId = firstService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var firstRecovered = firstService.GetGameStateView(firstGameId)!;
        var secondService = new GameService();
        var secondGameId = secondService.RehydrateSession(firstRecovered.Serialize());
        var secondRecovered = secondService.GetGameStateView(secondGameId)!;
        var secondSleep = secondService.GetCurrentInstruction(secondGameId)
            .Should().BeOfType<ConfirmationInstruction>().Subject;

        using (new AssertionScope())
        {
            secondSleep.InstructionId.Should().Be(expectedSleep.InstructionId);
            secondSleep.Semantic.Should().Be(expectedSleep.Semantic);
            secondSleep.AffectedPlayerIds.Should()
                .BeEquivalentTo(expectedSleep.AffectedPlayerIds);
            players.Should().AllSatisfy(player =>
                secondRecovered.GetFactionAgentKnowledge(
                        player.Id,
                        Faction.Werewolf)
                    .Should().Be(FactionAgentKnowledge.KnownAgent));
            secondRecovered.GameHistoryLog
                .OfType<FactionFactsCommittedLogEntry>()
                .Count(entry =>
                    entry.Source.Kind ==
                    FactionFactSourceKind.ScheduledObservation)
                .Should().Be(1);
            secondRecovered.GameHistoryLog.OfType<NightActionLogEntry>()
                .Should().NotContain(entry =>
                    entry.ActionType ==
                    NightActionType.WerewolfVictimSelection);
        }

        Action replayAcceptedObservation = () =>
            secondService.ProcessInstruction(secondGameId, acceptedObservation);
        replayAcceptedObservation.Should().Throw<InvalidOperationException>();

        var continued = secondService.ProcessInstruction(
            secondGameId,
            secondSleep.CreateResponse());

        continued.IsSuccess.Should().BeTrue();
        continued.ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>();
        secondRecovered.GameHistoryLog
            .OfType<FactionFactsCommittedLogEntry>()
            .Count(entry =>
                entry.Source.Kind ==
                FactionFactSourceKind.ScheduledObservation)
            .Should().Be(1);
        secondRecovered.GameHistoryLog.OfType<NightActionLogEntry>()
            .Should().NotContain(entry =>
                entry.ActionType ==
                NightActionType.WerewolfVictimSelection);
    }

    [Fact]
    public void KnownWerewolfAgentWake_WithoutLegalVictim_DoubleRehydrationResumesAtSleep()
    {
        var builder = GameTestBuilder.Create()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);

        builder.StartGame();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var finishNight = builder.CompleteWerewolfNightAction(
                [players[0].Id],
                players[1].Id)
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        finishNight.Semantic.Should().Be(
            ModeratorInstructionSemantic.FinishNightActions);
        builder.Process(finishNight.CreateResponse())
            .IsSuccess.Should().BeTrue();
        builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
        {
            [players[1].Id] = MainRoleType.SimpleVillager
        }).IsSuccess.Should().BeTrue();

        var session = builder.GetGameState()!;
        var transitionBoundary = new FactionFactEffectiveBoundary(
            session.TurnNumber,
            session.GetCurrentPhase(),
            session.GameHistoryLog.Count());
        builder.ArrangeExplicitFactionTransition(
            "test-all-living-players-become-werewolf-agents",
            session.GetPlayers()
                .Where(player => player.State.Health == PlayerHealth.Alive)
                .Select(player => FactionFact.Agent(
                    player.Id,
                    Faction.Werewolf,
                    FactionAgentKnowledge.KnownAgent,
                    transitionBoundary))
                .ToArray());
        var expectedNightStart = builder.CompleteDayPhaseWithTie()
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        expectedNightStart.Semantic.Should().Be(
            ModeratorInstructionSemantic.StartNight);
        var wake = builder.Process(expectedNightStart.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
        var expectedSleep = builder.Process(wake.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        expectedSleep.Semantic.Should().Be(
            ModeratorInstructionSemantic.PutRoleToSleep);

        var firstService = new GameService();
        var firstGameId = firstService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var firstRecovered = firstService.GetGameStateView(firstGameId)!;
        var secondService = new GameService();
        var secondGameId = secondService.RehydrateSession(firstRecovered.Serialize());
        var secondRecovered = secondService.GetGameStateView(secondGameId)!;
        var secondSleep = secondService.GetCurrentInstruction(secondGameId)
            .Should().BeOfType<ConfirmationInstruction>().Subject;

        using (new AssertionScope())
        {
            secondSleep.InstructionId.Should().Be(expectedSleep.InstructionId);
            secondSleep.Semantic.Should().Be(expectedSleep.Semantic);
            secondSleep.PublicAnnouncement.Should().Be(
                expectedSleep.PublicAnnouncement);
            secondSleep.AffectedPlayerIds.Should()
                .BeEquivalentTo(expectedSleep.AffectedPlayerIds);
            secondRecovered.GameHistoryLog
                .OfType<FactionFactsCommittedLogEntry>()
                .Count(entry =>
                    entry.Source.Kind ==
                    FactionFactSourceKind.ExplicitTransition)
                .Should().Be(1);
            secondRecovered.GameHistoryLog
                .OfType<FactionFactsCommittedLogEntry>()
                .Count(entry =>
                    entry.Source.Kind ==
                    FactionFactSourceKind.InitialBeneficiaryClosure)
                .Should().Be(1);
            secondRecovered.GameHistoryLog
                .OfType<FactionFactsCommittedLogEntry>()
                .Count(entry =>
                    entry.Source.Kind ==
                    FactionFactSourceKind.ScheduledObservation)
                .Should().Be(1);
            secondRecovered.GameHistoryLog.OfType<NightActionLogEntry>()
                .Count(entry =>
                    entry.ActionType ==
                    NightActionType.WerewolfVictimSelection)
                .Should().Be(1);
        }

        var continued = secondService.ProcessInstruction(
            secondGameId,
            secondSleep.CreateResponse());

        continued.IsSuccess.Should().BeTrue();
        continued.ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Which.Semantic.Should().Be(
                ModeratorInstructionSemantic.FinishNightActions);
        secondRecovered.GameHistoryLog
            .OfType<FactionFactsCommittedLogEntry>()
            .Count(entry =>
                entry.Source.Kind ==
                FactionFactSourceKind.InitialBeneficiaryClosure)
            .Should().Be(1);
        secondRecovered.GameHistoryLog.OfType<NightActionLogEntry>()
            .Count(entry =>
                entry.ActionType ==
                NightActionType.WerewolfVictimSelection)
            .Should().Be(1);
    }

    [Fact]
    public void AcceptedWildChildIdentification_DoubleRehydration_ContinuesAtModelSelection()
    {
        var builder = GameTestBuilder.Create()
            .WithPlayers("Wild Child", "Werewolf", "Seer", "Villager A", "Villager B")
            .WithRoles(
                MainRoleType.WildChild,
                MainRoleType.SimpleWerewolf,
                MainRoleType.Seer,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var wildChild = players[0];
        var model = players[3];
        var identification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        var acceptedIdentification = identification.CreateResponse([wildChild.Id]);
        var expectedNext = builder.Process(acceptedIdentification).ModeratorInstruction
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        var firstService = new GameService();
        var firstGameId = firstService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var firstRecovered = firstService.GetGameStateView(firstGameId)!;
        var secondService = new GameService();
        var secondGameId = secondService.RehydrateSession(firstRecovered.Serialize());
        var secondRecovered = secondService.GetGameStateView(secondGameId)!;
        var secondNext = secondService.GetCurrentInstruction(secondGameId)
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        using (new AssertionScope())
        {
            secondRecovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
                .ContainSingle(entry =>
                    entry.Role == MainRoleType.WildChild &&
                    entry.PlayerIds.SetEquals(new[] { wildChild.Id }));
            secondNext.InstructionId.Should().Be(expectedNext.InstructionId);
            secondNext.Semantic.Should().Be(expectedNext.Semantic);
            secondNext.AffectedPlayerIds.Should().Equal(expectedNext.AffectedPlayerIds);
            secondNext.SelectablePlayerIds.Should()
                .BeEquivalentTo(expectedNext.SelectablePlayerIds);
            secondNext.CountConstraint.Should().BeEquivalentTo(expectedNext.CountConstraint);
        }

        var continued = secondService.ProcessInstruction(
            secondGameId,
            secondNext.CreateResponse([model.Id]));

        continued.IsSuccess.Should().BeTrue();
        continued.ModeratorInstruction.Should().BeOfType<ConfirmationInstruction>();
        secondRecovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
            .Count(entry => entry.Role == MainRoleType.WildChild).Should().Be(1);
        secondRecovered.GameHistoryLog.OfType<NightActionLogEntry>().Should()
            .ContainSingle(entry =>
                entry.ActionType == NightActionType.WildChildModel &&
                entry.TargetIds!.SequenceEqual(new[] { model.Id }));
    }

    [Fact]
    public void AcceptedSeerIdentification_DoubleRehydration_ContinuesAtTargetSelection()
    {
        var builder = GameTestBuilder.Create()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var seer = players[1];
        var werewolfVictim = players[2];
        var seerTarget = players[3];
        var werewolfIdentification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        var werewolfTarget = builder.Process(
                werewolfIdentification.CreateResponse([werewolf.Id]))
            .ModeratorInstruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
        var werewolfSleep = builder.Process(
                werewolfTarget.CreateResponse([werewolfVictim.Id]))
            .ModeratorInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;
        var seerIdentification = builder.Process(werewolfSleep.CreateResponse())
            .ModeratorInstruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
        var acceptedIdentification = seerIdentification.CreateResponse([seer.Id]);
        var expectedNext = builder.Process(acceptedIdentification).ModeratorInstruction
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        var firstService = new GameService();
        var firstGameId = firstService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var firstRecovered = firstService.GetGameStateView(firstGameId)!;
        var secondService = new GameService();
        var secondGameId = secondService.RehydrateSession(firstRecovered.Serialize());
        var secondRecovered = secondService.GetGameStateView(secondGameId)!;
        var secondNext = secondService.GetCurrentInstruction(secondGameId)
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        using (new AssertionScope())
        {
            secondRecovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
                .ContainSingle(entry =>
                    entry.Role == MainRoleType.Seer &&
                    entry.PlayerIds.SetEquals(new[] { seer.Id }));
            secondRecovered.GetPlayerState(seer.Id).CurrentRole.Should()
                .Be(MainRoleType.Seer);
            secondNext.InstructionId.Should().Be(expectedNext.InstructionId);
            secondNext.Semantic.Should().Be(expectedNext.Semantic);
            secondNext.PublicAnnouncement.Should().Be(expectedNext.PublicAnnouncement);
            secondNext.PrivateInstruction.Should().Be(expectedNext.PrivateInstruction);
            secondNext.AffectedPlayerIds.Should().Equal(expectedNext.AffectedPlayerIds);
            secondNext.SelectablePlayerIds.Should()
                .BeEquivalentTo(expectedNext.SelectablePlayerIds);
            secondNext.CountConstraint.Should().BeEquivalentTo(expectedNext.CountConstraint);
        }

        Action replayAcceptedIdentification = () =>
            secondService.ProcessInstruction(secondGameId, acceptedIdentification);
        replayAcceptedIdentification.Should().Throw<InvalidOperationException>();
        secondRecovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
            .Count(entry => entry.Role == MainRoleType.Seer).Should().Be(1);

        var continued = secondService.ProcessInstruction(
            secondGameId,
            secondNext.CreateResponse([seerTarget.Id]));

        continued.IsSuccess.Should().BeTrue();
        continued.ModeratorInstruction.Should().BeOfType<ConfirmationInstruction>();
        secondRecovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
            .Count(entry => entry.Role == MainRoleType.Seer).Should().Be(1);
        secondRecovered.GameHistoryLog.OfType<NightActionLogEntry>().Should()
            .ContainSingle(entry =>
                entry.ActionType == NightActionType.SeerCheck &&
                entry.TargetIds!.SequenceEqual(new[] { seerTarget.Id }));
    }

    [Fact]
    public void AcceptedWerewolfAgentGroupObservation_UnknownSemanticCursorVersion_IsRejected()
    {
        var builder = GameTestBuilder.Create()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var werewolf = builder.GetGameState()!.GetPlayers().First();
        var identification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        builder.Process(identification.CreateResponse([werewolf.Id]));
        var payload = JsonNode.Parse(builder.GetGameState()!.Serialize())!.AsObject();
        payload["AcceptedObservationRecoveryCursor"]!["Version"] = int.MaxValue;
        var service = new GameService();

        Action rehydrate = () => service.RehydrateSession(payload.ToJsonString());

        rehydrate.Should().Throw<InvalidOperationException>()
            .WithMessage("*cursor version*");
    }

    [Fact]
    public void AcceptedWerewolfAgentGroupObservation_MismatchedPendingInstructionSemantic_IsRejected()
    {
        var builder = GameTestBuilder.Create()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var werewolf = builder.GetGameState()!.GetPlayers().First();
        var identification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        builder.Process(identification.CreateResponse([werewolf.Id]));
        var payload = JsonNode.Parse(builder.GetGameState()!.Serialize())!.AsObject();
        payload["PendingInstructionSemantic"] =
            ModeratorInstructionSemantic.SelectSeerTarget.ToString();
        var service = new GameService();

        Action rehydrate = () => service.RehydrateSession(payload.ToJsonString());

        rehydrate.Should().Throw<InvalidOperationException>()
            .WithMessage("*Pending Instruction Semantic*");
    }

    [Fact]
    public void AcceptedWerewolfAgentGroupObservation_ForeignScheduledObservationSource_IsRejected()
    {
        var builder = GameTestBuilder.Create()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var observedAgent = builder.GetGameState()!.GetPlayers().First();
        var observation = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        builder.Process(observation.CreateResponse([observedAgent.Id]));
        var payload = JsonNode.Parse(
            builder.GetGameState()!.Serialize())!.AsObject();
        var scheduledObservation = payload["GameHistoryLog"]!.AsArray()
            .Select(entry => entry!.AsObject())
            .Single(entry =>
                entry["Source"]?["Kind"]?.GetValue<string>() ==
                FactionFactSourceKind.ScheduledObservation.ToString());
        scheduledObservation["Source"]!["Identifier"] =
            "foreign-scheduled-observation";
        var service = new GameService();

        Action rehydrate = () =>
            service.RehydrateSession(payload.ToJsonString());

        rehydrate.Should().Throw<InvalidOperationException>()
            .WithMessage("*committed observation*");
    }

    [Fact]
    public void AcceptedWerewolfAgentGroupObservation_MismatchedNightSubPhase_IsRejected()
    {
        var builder = GameTestBuilder.Create()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var werewolf = builder.GetGameState()!.GetPlayers().First();
        var identification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        builder.Process(identification.CreateResponse([werewolf.Id]));
        var payload = JsonNode.Parse(builder.GetGameState()!.Serialize())!.AsObject();
        payload["PhaseStateCache"]!["SubPhase"] =
            DawnSubPhases.CalculateVictims.ToString();
        var service = new GameService();

        Action rehydrate = () => service.RehydrateSession(payload.ToJsonString());

        rehydrate.Should().Throw<InvalidOperationException>()
            .WithMessage("*accepted observation continuation*");
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

            var instruction = builder.GetCurrentInstruction()!;
            var response = instruction switch
            {
                ConfirmationInstruction confirmation => confirmation.CreateResponse(),
                AssignRolesInstruction reveal => reveal.CreateResponse(
                    reveal.PlayersForAssignment.ToDictionary(
                        playerId => playerId,
                        _ => MainRoleType.SimpleVillager)),
                _ => throw new InvalidOperationException(
                    $"Unexpected instruction {instruction.GetType().Name} while advancing to Night.")
            };
            builder.Process(response).IsSuccess.Should().BeTrue();
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
