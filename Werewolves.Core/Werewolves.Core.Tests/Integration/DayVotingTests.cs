using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for day phase voting: vote outcomes, tie handling, elimination flow.
/// Test IDs: DV-001 through DV-011
/// </summary>
public class DayVotingTests : DiagnosticTestBase
{
    public DayVotingTests(ITestOutputHelper output) : base(output) { }

    #region DV-001: Debate Phase Transitions to Voting

    /// <summary>
    /// DV-001: Debate sub-phase transitions to voting.
    /// After dawn phase completes, game enters Day.Debate. Confirming debate leads to voting.
    /// </summary>
    [Fact]
    public void DebatePhase_TransitionsToVoting()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1 = players[2].Id;
        var villager2 = players[3].Id;

        // Complete night phase (werewolf kills a villager)
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1,
            seerId: seerId,
            seerTargetId: villager2);

        // Complete dawn phase
        builder.CompleteDawnPhase(new()
        {
            [villager1] = MainRoleType.SimpleVillager
        });

        // Assert: We're in Day phase, and should have a confirmation instruction for debate
        var gameState = builder.GetGameState()!;
        gameState.GetCurrentPhase().Should().Be(GamePhase.Day);

        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmationInstruction);

        // Act: Confirm debate is complete
        var afterDebate = builder.Process(debateInstruction.CreateResponse());
        afterDebate.IsSuccess.Should().BeTrue();

        // After DetermineVoteType (silent transition), we should get a voting instruction
        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingSelectionInstruction);

        // Verify it's a voting instruction with appropriate constraints
        // Uses SingleOptional which has Minimum=1, Maximum=1, IsOptional=true
        // The IsOptional flag allows 0 selections for tie votes
        votingInstruction.CountConstraint.Should().NotBeNull();
        votingInstruction.CountConstraint!.IsOptional.Should().BeTrue(CoreTestReferences.AssertionReasons.TieVotesAllowNoSelection);
        votingInstruction.CountConstraint!.Maximum.Should().Be(1, CoreTestReferences.AssertionReasons.SinglePlayerCanBeLynched);
        votingInstruction.EmptySelectionOptionLabel.Should().Be(GameStrings.DayVoteNoEliminationOption);

        MarkTestCompleted();
    }

    #endregion

    #region DV-002 to DV-006: Normal Vote Flow

    /// <summary>
    /// DV-002: Vote outcome publicly reveals the target before announcing elimination.
    /// A sole remaining role type is not inferred.
    /// </summary>
    [Fact]
    public void VoteOutcome_SinglePlayer_WithSinglePossibleRole_AnnouncesElimination()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2 = players[3];

        // Complete night and dawn phases
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2.Id);
        builder.CompleteDawnPhase(new()
        {
            [villager1Id] = MainRoleType.SimpleVillager
        });

        // Confirm debate
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse());

        // Get voting instruction
        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        // Act: Vote to lynch villager2 (who is still alive)
        var voteResponse = votingInstruction.CreateResponse([villager2.Id]);
        var afterVote = builder.Process(voteResponse);

        var reveal = InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
            afterVote,
            CoreTestReferences.InstructionContexts.RoleAssignmentAfterLynch);
        reveal.PlayersForAssignment.Should().Equal(villager2.Id);
        builder.GetGameState()!.GetPlayer(villager2.Id).State.MainRole.Should().BeNull();
        builder.GetGameState()!.GetPlayer(villager2.Id).State.Health.Should().Be(PlayerHealth.Alive);

        var afterReveal = builder.Process(reveal.CreateResponse(new()
        {
            [villager2.Id] = MainRoleType.SimpleVillager
        }));
        var deathAnnouncement = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterReveal,
            CoreTestReferences.InstructionContexts.DeathAnnouncementConfirmation);

        deathAnnouncement.PublicAnnouncement.Should().Contain(villager2.Name);
        builder.GetGameState()!.GetPlayer(villager2.Id).State.MainRole
            .Should().Be(MainRoleType.SimpleVillager);
        builder.GetGameState()!.GetPlayer(villager2.Id).State.PubliclyRevealedRole
            .Should().Be(MainRoleType.SimpleVillager);

        MarkTestCompleted();
    }

    /// <summary>
    /// DV-005: Vote outcome with a sole remaining role type still requires public reveal mapping.
    /// </summary>
    [Fact]
    public void VoteOutcome_SinglePossibleRole_RequiresRevealMappingAndAnnouncesElimination()
    {
        // Arrange: 1 Werewolf and 4 Villagers leaves only Villager roles unknown after night.
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolf = players[0];
        var dawnVictim = players[1];
        var lynchedPlayer = players[2];

        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: dawnVictim.Id);
        builder.CompleteDawnPhase(new()
        {
            [dawnVictim.Id] = MainRoleType.SimpleVillager
        });

        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse());

        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        // Act: Vote to lynch a player whose only possible role type is SimpleVillager.
        var afterVote = builder.Process(votingInstruction.CreateResponse([lynchedPlayer.Id]));

        // Assert: The engine does not infer the sole remaining role type.
        var reveal = InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
            afterVote,
            CoreTestReferences.InstructionContexts.RoleAssignmentAfterLynch);
        reveal.PlayersForAssignment.Should().Equal(lynchedPlayer.Id);
        lynchedPlayer.State.MainRole.Should().BeNull();
        lynchedPlayer.State.Health.Should().Be(PlayerHealth.Alive);

        var afterReveal = builder.Process(reveal.CreateResponse(new()
        {
            [lynchedPlayer.Id] = MainRoleType.SimpleVillager
        }));
        var announcement = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterReveal,
            CoreTestReferences.InstructionContexts.DeathAnnouncementConfirmation);
        announcement.PublicAnnouncement.Should().Contain(lynchedPlayer.Name);

        var gameState = builder.GetGameState()!;
        gameState.GetPlayer(lynchedPlayer.Id).State.MainRole.Should().Be(MainRoleType.SimpleVillager);
        gameState.GetPlayer(lynchedPlayer.Id).State.Health.Should().Be(PlayerHealth.Dead);

        var roleLog = gameState.GameHistoryLog
            .OfType<RoleRevealLogEntry>()
            .Single(entry => entry.RevealedRoles.ContainsKey(lynchedPlayer.Id));
        roleLog.RevealedRoles[lynchedPlayer.Id].Should().Be(MainRoleType.SimpleVillager);
        roleLog.CurrentPhase.Should().Be(GamePhase.Day);

        gameState.GameHistoryLog
            .OfType<PlayerEliminatedLogEntry>()
            .Should()
            .ContainSingle(entry =>
                entry.PlayerId == lynchedPlayer.Id &&
                entry.Reason == EliminationReason.DayVote);

        MarkTestCompleted();
    }

    [Fact]
    public void VoteOutcome_DifferentPhysicalReveal_PreservesCommittedCurrentRole()
    {
        var scenario = DayVoteScenario.Start();
        var builder = scenario.Builder
            .ArrangeKnownPhysicalRole(
                scenario.LivingTargetId,
                MainRoleType.SimpleVillager)
            .ArrangeCurrentRole(
                scenario.LivingTargetId,
                MainRoleType.Seer);
        var afterVote = builder.Process(
            scenario.Instruction.CreateResponse([scenario.LivingTargetId]));
        var reveal = afterVote.ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;

        reveal.AffectedPlayerIds.Should().Equal(
            scenario.LivingTargetId);
        var playerState = builder.GetGameState()!.GetPlayerState(
            scenario.LivingTargetId);
        playerState.CurrentRole.Should().Be(MainRoleType.Seer);
        playerState.ModeratorKnownRole.Should().Be(
            MainRoleType.SimpleVillager);
        playerState.PhysicalCharacterCardRole.Should().Be(
            MainRoleType.SimpleVillager);
        playerState.Health.Should().Be(PlayerHealth.Alive);

        var afterReveal = builder.Process(reveal.CreateResponse());

        afterReveal.ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>();
        playerState.CurrentRole.Should().Be(MainRoleType.Seer);
		playerState.ModeratorKnownRole.Should().Be(
			MainRoleType.SimpleVillager);
        playerState.PubliclyRevealedRole.Should().Be(
			MainRoleType.SimpleVillager);
        playerState.Health.Should().Be(PlayerHealth.Dead);
        builder.GetGameState()!.GameHistoryLog
            .OfType<RoleRevealLogEntry>()
            .Should()
            .ContainSingle(entry =>
                entry.RevealedRoles.Count == 1 &&
                entry.RevealedRoles.GetValueOrDefault(
					scenario.LivingTargetId) ==
					MainRoleType.SimpleVillager);
        builder.GetGameState()!.GameHistoryLog
            .OfType<PlayerEliminatedLogEntry>()
            .Should().ContainSingle(entry =>
                entry.PlayerId == scenario.LivingTargetId &&
                entry.Reason == EliminationReason.DayVote);

        MarkTestCompleted();
    }

    [Fact]
    public void VoteOutcome_WildChildFactionTransition_RevealsPreservedRoleAtomically()
    {
        var builder = CreateBuilder()
            .WithPlayers(
                "Wild Child",
                "Role Model",
                "Werewolf",
                "Villager A",
                "Villager B",
                "Villager C",
                "Villager D")
            .WithRoles(
                MainRoleType.WildChild,
                MainRoleType.SimpleWerewolf,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var wildChildId = players[0].Id;
        var roleModelId = players[1].Id;
        var werewolfId = players[2].Id;

        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var wildChildIdentification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        var modelSelection = builder.Process(
                wildChildIdentification.CreateResponse([wildChildId]))
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var wildChildSleep = builder.Process(
                modelSelection.CreateResponse([roleModelId]))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var werewolfIdentification = builder.Process(
                wildChildSleep.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var victimSelection = builder.Process(
                werewolfIdentification.CreateResponse([werewolfId]))
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        var werewolfSleep = builder.Process(
                victimSelection.CreateResponse([roleModelId]))
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var finishNight = builder.Process(werewolfSleep.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var roleModelReveal = builder.Process(finishNight.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<AssignRolesInstruction>().Subject;
        var afterModelReveal = builder.Process(
            roleModelReveal.CreateResponse(new()
            {
                [roleModelId] = MainRoleType.SimpleVillager
            }));

        var session = builder.GetGameState()!;
        var wildChildState = session.GetPlayerState(wildChildId);
        wildChildState.CurrentRole.Should().Be(MainRoleType.WildChild);
        wildChildState.ModeratorKnownRole.Should().Be(MainRoleType.WildChild);
        session.GameHistoryLog
            .OfType<StatusEffectLogEntry>()
            .Should().ContainSingle(entry =>
                entry.PlayerId == wildChildId &&
                entry.EffectType == StatusEffectTypes.WildChildChanged &&
                entry.IsActive);
        session.GameHistoryLog
            .OfType<AssignRoleLogEntry>()
            .Should().NotContain(entry =>
                entry.PlayerIds.SetEquals(new[] { wildChildId }) &&
                entry.AssignedMainRole == MainRoleType.SimpleWerewolf);
        session.RequireKnownFactionBeneficiary(wildChildId).Should()
            .Be(Faction.Werewolf);
        session.GetFactionAgentKnowledge(wildChildId, Faction.Werewolf)
            .Should().Be(FactionAgentKnowledge.KnownAgent);

        var debate = afterModelReveal.ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>().Subject;
        var vote = builder.Process(debate.CreateResponse())
            .ModeratorInstruction.Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
		var reveal = builder.Process(
				vote.CreateResponse([wildChildId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		reveal.AffectedPlayerIds.Should().Equal(wildChildId);
        wildChildState.Health.Should().Be(PlayerHealth.Alive);

        session.GameHistoryLog
            .OfType<StatusEffectLogEntry>()
            .Should().ContainSingle(entry =>
                entry.PlayerId == wildChildId &&
                entry.EffectType == StatusEffectTypes.WildChildChanged &&
                entry.IsActive);
        session.GameHistoryLog
            .OfType<AssignRoleLogEntry>()
            .Should().NotContain(entry =>
                entry.PlayerIds.SetEquals(new[] { wildChildId }) &&
                entry.AssignedMainRole == MainRoleType.SimpleWerewolf);

		var afterReveal = builder.Process(reveal.CreateResponse());

        afterReveal.ModeratorInstruction.Should()
            .BeOfType<ConfirmationInstruction>();
        wildChildState.CurrentRole.Should().Be(MainRoleType.WildChild);
        wildChildState.ModeratorKnownRole.Should().Be(
            MainRoleType.WildChild);
        wildChildState.PubliclyRevealedRole.Should().Be(
            MainRoleType.WildChild);
        wildChildState.Health.Should().Be(PlayerHealth.Dead);
        session.GameHistoryLog
            .OfType<RoleRevealLogEntry>()
            .Should()
            .ContainSingle(entry =>
                entry.RevealedRoles.Count == 1 &&
                entry.RevealedRoles.ContainsKey(wildChildId) &&
                    entry.RevealedRoles.GetValueOrDefault(
                    wildChildId) == MainRoleType.WildChild);
        session.GameHistoryLog
            .OfType<PlayerEliminatedLogEntry>()
            .Should().ContainSingle(entry =>
                entry.PlayerId == wildChildId &&
                entry.Reason == EliminationReason.DayVote);

        MarkTestCompleted();
    }

    /// <summary>
    /// DV-003: Vote elimination creates VoteOutcomeReportedLogEntry.
    /// </summary>
    [Fact]
    public void VoteElimination_CreatesVoteOutcomeLogEntry()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2Id = players[3].Id;

        // Complete night and dawn phases
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2Id);
        builder.CompleteDawnPhase(new()
        {
            [villager1Id] = MainRoleType.SimpleVillager
        });

        // Confirm debate
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse());

        // Get voting instruction and vote
        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        // Act: Vote to lynch villager2
        var voteResponse = votingInstruction.CreateResponse([villager2Id]);
        builder.Process(voteResponse);

        // Assert: VoteOutcomeReportedLogEntry should exist with correct player
        var gameState = builder.GetGameState()!;
        var voteLogs = gameState.GameHistoryLog
            .OfType<VoteOutcomeReportedLogEntry>()
            .ToList();

        voteLogs.Should().HaveCount(1);
        voteLogs[0].ReportedOutcomePlayerId.Should().Be(villager2Id);

        MarkTestCompleted();
    }

    /// <summary>
    /// DV-004: Vote elimination sets player health to Dead.
    /// </summary>
    [Fact]
    public void VoteElimination_PlayerHealthSetToDead()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2Id = players[3].Id;

        // Complete night and dawn phases
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2Id);
        builder.CompleteDawnPhase(new()
        {
            [villager1Id] = MainRoleType.SimpleVillager
        });

        // Act: Complete day phase with lynch
        builder.CompleteDayPhaseWithLynch(villager2Id, new()
        {
            [villager2Id] = MainRoleType.SimpleVillager
        });

        // Assert: Lynched player should be dead
        var gameState = builder.GetGameState()!;
        var lynchedPlayer = gameState.GetPlayers().First(p => p.Id == villager2Id);
        lynchedPlayer.State.Health.Should().Be(PlayerHealth.Dead);

        // Also verify PlayerEliminatedLogEntry was created
        var eliminationLogs = gameState.GameHistoryLog
            .OfType<PlayerEliminatedLogEntry>()
            .Where(e => e.PlayerId == villager2Id && e.Reason == EliminationReason.DayVote)
            .ToList();

        eliminationLogs.Should().HaveCount(1);

        MarkTestCompleted();
    }

    #endregion

    #region DV-010 to DV-011: Tie Votes

    /// <summary>
    /// DV-010: Tie vote (no player selected) results in no elimination.
    /// </summary>
    [Fact]
    public void TieVote_NoPlayerSelected_NoElimination()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2Id = players[3].Id;

        // Complete night and dawn phases
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2Id);
        builder.CompleteDawnPhase(new()
        {
            [villager1Id] = MainRoleType.SimpleVillager
        });

        // Get the count of living players before voting
        var livingPlayersBefore = builder.GetGameState()!.GetPlayers()
            .Count(p => p.State.Health == PlayerHealth.Alive);

        // Confirm debate
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse());

        // Get voting instruction
        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        // Act: Vote with no player selected (tie)
        var tieResponse = votingInstruction.CreateResponse([]);
        builder.Process(tieResponse);

        // Complete remaining day phase
        builder.CompleteDayPhaseWithTie();

        // Assert: No player eliminated during day voting (villager1 was killed at dawn)
        var gameState = builder.GetGameState()!;
        var dayEliminationLogs = gameState.GameHistoryLog
            .OfType<PlayerEliminatedLogEntry>()
            .Where(e => e.Reason == EliminationReason.DayVote)
            .ToList();

        dayEliminationLogs.Should().BeEmpty(CoreTestReferences.AssertionReasons.TieVoteDoesNotEliminatePlayer);

        // Living player count should be same as before voting
        var livingPlayersAfter = gameState.GetPlayers()
            .Count(p => p.State.Health == PlayerHealth.Alive);
        livingPlayersAfter.Should().Be(livingPlayersBefore);

        MarkTestCompleted();
    }

    /// <summary>
    /// DV-011: Tie vote creates correct log entry with Empty playerId.
    /// </summary>
    [Fact]
    public void TieVote_LogsCorrectOutcome()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2Id = players[3].Id;

        // Complete night and dawn phases
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2Id);
        builder.CompleteDawnPhase(new()
        {
            [villager1Id] = MainRoleType.SimpleVillager
        });

        // Confirm debate
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse());

        // Get voting instruction
        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        // Act: Vote with no player selected (tie)
        var tieResponse = votingInstruction.CreateResponse([]);
        builder.Process(tieResponse);

        // Assert: VoteOutcomeReportedLogEntry should exist with Guid.Empty
        var gameState = builder.GetGameState()!;
        var voteLogs = gameState.GameHistoryLog
            .OfType<VoteOutcomeReportedLogEntry>()
            .ToList();

        voteLogs.Should().HaveCount(1);
        voteLogs[0].ReportedOutcomePlayerId.Should().Be(Guid.Empty,
            CoreTestReferences.AssertionReasons.TieVoteLoggedWithEmptyPlayerId);

        MarkTestCompleted();
    }

    #endregion

    #region DV-020: Vote Target Validation

    /// <summary>
    /// DV-020: Vote cannot select a dead player.
    /// After dawn elimination, the dead player's ID should be excluded from selectable targets.
    /// </summary>
    [Fact]
    public void Vote_CannotSelectDeadPlayer()
    {
        // Arrange: Game with 5 players (1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2Id = players[3].Id;
        var villager3Id = players[4].Id;

        // Complete Night 1: werewolf kills villager1
        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2Id);

        // Complete Dawn 1: victim eliminated
        builder.CompleteDawnPhase(new()
        {
            [villager1Id] = MainRoleType.SimpleVillager
        });

        // Verify villager1 is now dead
        var deadPlayer = builder.GetGameState()!.GetPlayers().First(p => p.Id == villager1Id);
        deadPlayer.State.Health.Should().Be(PlayerHealth.Dead);

        // Confirm debate to get to voting
        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse());

        // Act: Get the vote selection instruction
        var votingInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        // Assert: Dead player's ID should NOT be in selectable targets
        votingInstruction.SelectablePlayerIds.Should().NotContain(villager1Id,
            CoreTestReferences.AssertionReasons.DeadPlayerInvalidVoteTarget);

        // Living players should be selectable
        votingInstruction.SelectablePlayerIds.Should().Contain(werewolfId);
        votingInstruction.SelectablePlayerIds.Should().Contain(seerId);
        votingInstruction.SelectablePlayerIds.Should().Contain(villager2Id);
        votingInstruction.SelectablePlayerIds.Should().Contain(villager3Id);

        MarkTestCompleted();
    }

    #endregion
}
