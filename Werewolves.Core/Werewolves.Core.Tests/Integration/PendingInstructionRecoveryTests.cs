using FluentAssertions;
using FluentAssertions.Execution;
using System.Text.Json.Nodes;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
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
    public void AcceptedActorSetupCardSpend_RehydratesAtSleep_RejectsReplayWithoutMutation_AndContinuesToWerewolfObservation()
    {
        var fixture = StartActorGameAtPendingSleep();

        var recoveredService = new GameService();
        var recoveredGameId = recoveredService.RehydrateSession(
            fixture.Service.GetGameStateView(fixture.GameId)!.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
        var recoveredSleep = recoveredService.GetCurrentInstruction(recoveredGameId)
            .Should().BeOfType<ConfirmationInstruction>().Subject;
        var recoveredActivation = recovered
            .GetModeratorActiveActorBorrowedRolePowerActivation();

        using (new AssertionScope())
        {
            recoveredSleep.InstructionId.Should().Be(
                fixture.PendingActorSleep.InstructionId);
            recoveredSleep.Semantic.Should().Be(
                ModeratorInstructionSemantic.PutRoleToSleep);
            recovered.GetModeratorSpentActorSetupCards().Should()
                .ContainSingle(card =>
                    card.Id == fixture.HunterCard.Id &&
                    card.PrintedRole == MainRoleType.Hunter);
            recovered.GetModeratorRemainingActorSetupCards().Should().HaveCount(2);
            recoveredActivation.Should().NotBeNull();
            recoveredActivation!.ActingPlayerId.Should().Be(fixture.ActorId);
            recoveredActivation.SelectedCardId.Should().Be(fixture.HunterCard.Id);
            recoveredActivation.SourceRole.Should().Be(MainRoleType.Hunter);
            recoveredActivation.Origin.Should().Be(RolePowerInstanceOrigin.Borrowed);
            recovered.GameHistoryLog.OfType<ActorSetupCardSpendCommittedLogEntry>()
                .Should().ContainSingle();
        }

        AssertResponseReplayIsRejectedWithoutPublicMutation(
            recoveredService, recoveredGameId, fixture.AcceptedActorChoice);

        var werewolfObservation = ProcessAndExpect<SelectPlayersInstruction>(
            recoveredService, recoveredGameId, recoveredSleep.CreateResponse());

        werewolfObservation.Semantic.Should().Be(
            ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
        recovered.GetModeratorSpentActorSetupCards().Should()
            .ContainSingle(card => card.Id == fixture.HunterCard.Id);
        recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
            .Be(recoveredActivation);
        recovered.GameHistoryLog.OfType<ActorSetupCardSpendCommittedLogEntry>()
            .Should().ContainSingle();
    }

    [Fact]
    public void SuppressedActorOpening_AfterRecovery_ExpiresOnceRejectsStaleChoiceAndContinuesToWerewolves()
    {
        var fixture = StartActorGameAtPendingSleep();
        var service = fixture.Service;
        var gameId = fixture.GameId;
        var werewolfObservation = ProcessAndExpect<SelectPlayersInstruction>(
            service, gameId, fixture.PendingActorSleep.CreateResponse());
        werewolfObservation.Semantic.Should().Be(
            ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
        var victimSelection = ProcessAndExpect<SelectPlayersInstruction>(
            service, gameId,
            werewolfObservation.CreateResponse([fixture.WerewolfId]));
        victimSelection.Semantic.Should().Be(
            ModeratorInstructionSemantic.SelectWerewolfVictim);
        var werewolfSleep = ProcessAndExpect<ConfirmationInstruction>(
            service, gameId,
            victimSelection.CreateResponse([fixture.NightVictimId]));
        var nightEnd = ProcessAndExpect<ConfirmationInstruction>(
            service, gameId, werewolfSleep.CreateResponse());
        var elderIdentification = ProcessAndExpect<SelectPlayersInstruction>(
            service, gameId, nightEnd.CreateResponse());
        elderIdentification.RoleIdentification.Should().Be(MainRoleType.Elder);
        var dawnRoleAssignment = ProcessAndExpect<AssignRolesInstruction>(
            service, gameId,
            elderIdentification.CreateResponse([fixture.ElderId]));
        service.ProcessInstruction(
            gameId,
            dawnRoleAssignment.CreateResponse(new()
            {
                [fixture.NightVictimId] = MainRoleType.SimpleVillager
            })).IsSuccess.Should().BeTrue();

        for (var step = 0;
             step < 10 &&
             service.GetGameStateView(gameId)!.GetCurrentPhase() != GamePhase.Day;
             step++)
        {
            var dawnConfirmation = service.GetCurrentInstruction(gameId)
                .Should().BeOfType<ConfirmationInstruction>().Subject;
            service.ProcessInstruction(gameId, dawnConfirmation.CreateResponse())
                .IsSuccess.Should().BeTrue();
        }

        service.GetGameStateView(gameId)!.GetCurrentPhase().Should()
            .Be(GamePhase.Day);
        var debate = service.GetCurrentInstruction(gameId)
            .Should().BeOfType<ConfirmationInstruction>().Subject;
        debate.Semantic.Should().Be(ModeratorInstructionSemantic.StartDayDebate);
        var vote = ProcessAndExpect<SelectPlayersInstruction>(
            service, gameId, debate.CreateResponse());
        var dayRoleAssignment = ProcessAndExpect<AssignRolesInstruction>(
            service, gameId, vote.CreateResponse([fixture.ElderId]));
        dayRoleAssignment.Semantic.Should().Be(
            ModeratorInstructionSemantic.AssignDayVoteTargetRole);
        dayRoleAssignment.PlayersForAssignment.Should().Equal(fixture.ElderId);
        dayRoleAssignment.RolesForAssignment.Should().Contain(MainRoleType.Elder);
        var reveal = ProcessAndExpect<ConfirmationInstruction>(
            service, gameId,
            dayRoleAssignment.CreateResponse(new()
            {
                [fixture.ElderId] = MainRoleType.Elder
            }));
        var dayTail = ProcessAndExpect<ConfirmationInstruction>(
            service, gameId, reveal.CreateResponse());
        var secondNightStart = ProcessAndExpect<ConfirmationInstruction>(
            service, gameId, dayTail.CreateResponse());
        secondNightStart.Semantic.Should().Be(
            ModeratorInstructionSemantic.StartNight);
        service.GetGameStateView(gameId)!.GetCurrentPhase().Should()
            .Be(GamePhase.Night);
        service.GetGameStateView(gameId)!.GameHistoryLog
            .OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
            .Should().ContainSingle();

        var recoveredService = new GameService();
        var recoveredGameId = recoveredService.RehydrateSession(
            service.GetGameStateView(gameId)!.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
        var recoveredNightStart = recoveredService
            .GetCurrentInstruction(recoveredGameId)
            .Should().BeOfType<ConfirmationInstruction>().Subject;
        recoveredNightStart.InstructionId.Should().Be(secondNightStart.InstructionId);
        recoveredNightStart.Semantic.Should().Be(
            ModeratorInstructionSemantic.StartNight);
        recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
            .NotBeNull();
        recovered.GameHistoryLog
            .OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
            .Should().BeEmpty();

        var werewolfWake = ProcessAndExpect<ConfirmationInstruction>(
            recoveredService, recoveredGameId,
            recoveredNightStart.CreateResponse());

        using (new AssertionScope())
        {
            werewolfWake.Semantic.Should().Be(
                ModeratorInstructionSemantic.WakeRole);
            werewolfWake.AffectedPlayerIds.Should().Equal(fixture.WerewolfId);
            recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
                .BeNull();
            recovered.GetModeratorSpentActorSetupCards().Should()
                .ContainSingle(card => card.Id == fixture.HunterCard.Id);
            recovered.GameHistoryLog
                .OfType<ActorSetupCardSpendCommittedLogEntry>()
                .Should().ContainSingle();
            recovered.GameHistoryLog
                .OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
                .Should().ContainSingle();
        }

        AssertResponseReplayIsRejectedWithoutPublicMutation(
            recoveredService, recoveredGameId, fixture.AcceptedActorChoice);

        var secondVictimSelection = ProcessAndExpect<SelectPlayersInstruction>(
            recoveredService, recoveredGameId, werewolfWake.CreateResponse());

        secondVictimSelection.Semantic.Should().Be(
            ModeratorInstructionSemantic.SelectWerewolfVictim);
        recovered.GameHistoryLog
            .OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
            .Should().ContainSingle();
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

    private static (GameService Service, Guid GameId, Guid ActorId,
        Guid WerewolfId, Guid ElderId, Guid NightVictimId,
        PhysicalCharacterCard HunterCard, ModeratorResponse AcceptedActorChoice,
        ConfirmationInstruction PendingActorSleep) StartActorGameAtPendingSleep()
    {
        var actorSetup = new ActorSetupCards([
            MainRoleType.Hunter,
            MainRoleType.VillageIdiot,
            MainRoleType.Scapegoat
        ]);
        var service = new GameService();
        var start = service.StartNewGame(new GameSessionConfig(
            [GameStrings.ActorRoleName, "Werewolf", "Elder", "Villager A", "Villager B"],
            [
                MainRoleType.Actor,
                MainRoleType.SimpleWerewolf,
                MainRoleType.Elder,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager
            ],
            actorSetup));
        var gameId = start.GameGuid;
        var players = service.GetGameStateView(gameId)!.GetPlayers()
            .ToDictionary(player => player.Name);
        var actorId = players[GameStrings.ActorRoleName].Id;
        var nightStart = ProcessAndExpect<ConfirmationInstruction>(
            service, gameId, start.CreateResponse());
        var actorIdentification = ProcessAndExpect<SelectPlayersInstruction>(
            service, gameId, nightStart.CreateResponse());
        actorIdentification.RoleIdentification.Should().Be(MainRoleType.Actor);
        var actorChoice = ProcessAndExpect<SelectOptionsInstruction>(
            service, gameId, actorIdentification.CreateResponse([actorId]));
        actorChoice.Semantic.Should().Be(
            ModeratorInstructionSemantic.ChooseActorSetupCard);
        var hunterCard = actorSetup.Cards.Single(card =>
            card.PrintedRole == MainRoleType.Hunter);
        var acceptedActorChoice = actorChoice.CreateResponse(hunterCard.Id.ToString("D"));
        var actorSleep = ProcessAndExpect<ConfirmationInstruction>(
            service, gameId, acceptedActorChoice);
        actorSleep.Semantic.Should().Be(
            ModeratorInstructionSemantic.PutRoleToSleep);

        return (service, gameId, actorId, players["Werewolf"].Id,
            players["Elder"].Id, players["Villager A"].Id, hunterCard,
            acceptedActorChoice, actorSleep);
    }

    private static void AssertResponseReplayIsRejectedWithoutPublicMutation(
        GameService service,
        Guid gameId,
        ModeratorResponse response)
    {
        var beforeReplay = PublicGameSessionSnapshot.Capture(service, gameId);
        Action replay = () => service.ProcessInstruction(gameId, response);

        replay.Should().Throw<InvalidOperationException>();
        PublicGameSessionSnapshot.Capture(service, gameId).Should().BeEquivalentTo(
            beforeReplay,
            options => options.WithStrictOrdering());
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
