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

public sealed class RoleKnowledgeContractTests : DiagnosticTestBase
{
    public RoleKnowledgeContractTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void LiveGameSession_FromRoleComposition_LeavesEveryPlayerRoleFactUnknown()
    {
        var builder = CreateBuilder()
            .WithPlayers("Alice", "Bob", "Charlie", "Diana", "Eve")
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Seer,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);

        builder.StartGame();

        var session = builder.GetGameState()!;
        session.RoleInPlayCount(MainRoleType.SimpleWerewolf).Should().Be(1);
        session.RoleInPlayCount(MainRoleType.Seer).Should().Be(1);
        session.RoleInPlayCount(MainRoleType.SimpleVillager).Should().Be(3);

        foreach (var player in session.GetPlayers())
        {
            player.State.CurrentRole.Should().BeNull();
            player.State.PhysicalCharacterCardRole.Should().BeNull();
            player.State.ModeratorKnownRole.Should().BeNull();
            player.State.PubliclyRevealedRole.Should().BeNull();
        }

        MarkTestCompleted();
    }

    [Fact]
    public void RoleIdentification_RecordsPrivateCurrentRoleWithoutAssigningOrRevealingCard()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToArray();
        var werewolfAgent = players[0];
        var holder = players[1];
        var werewolfVictim = players[4];
        builder.CompleteWerewolfNightAction(
            [werewolfAgent.Id],
            werewolfVictim.Id);

        var identification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        identification.Semantic.Should()
            .Be(ModeratorInstructionSemantic.IdentifyRoleHolders);
        identification.RoleIdentification.Should().Be(MainRoleType.Seer);

        var result = builder.Process(identification.CreateResponse([holder.Id]));

        result.IsSuccess.Should().BeTrue();
        holder.State.CurrentRole.Should().Be(MainRoleType.Seer);
        holder.State.ModeratorKnownRole.Should().Be(MainRoleType.Seer);
        holder.State.PhysicalCharacterCardRole.Should().BeNull();
        holder.State.PubliclyRevealedRole.Should().BeNull();
        session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
            .Should().ContainSingle(entry =>
                entry.Role == MainRoleType.Seer &&
                entry.PlayerIds.SetEquals(new[] { holder.Id }));
        session.GameHistoryLog.OfType<AssignRoleLogEntry>().Should().BeEmpty();

        MarkTestCompleted();
    }

    [Fact]
    public void RoleIdentification_AcceptedObservation_RehydratesAtExactNextInstruction()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolfAgent = players[0];
        var holder = players[1];
        var werewolfVictim = players[4];
        var target = players[2];
        builder.CompleteWerewolfNightAction(
            [werewolfAgent.Id],
            werewolfVictim.Id);

        var identification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        identification.Semantic.Should()
            .Be(ModeratorInstructionSemantic.IdentifyRoleHolders);
        identification.RoleIdentification.Should().Be(MainRoleType.Seer);
        var acceptedIdentification = identification.CreateResponse([holder.Id]);
        var afterIdentification = builder.Process(acceptedIdentification);
        var expectedNext = afterIdentification.ModeratorInstruction
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        var recoveredService = new GameService();
        var recoveredId = recoveredService.RehydrateSession(builder.GetGameState()!.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredId)!;
        var recoveredNext = recoveredService.GetCurrentInstruction(recoveredId)
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        recovered.GetPlayerState(holder.Id).CurrentRole.Should().Be(MainRoleType.Seer);
        recovered.GetPlayerState(holder.Id).ModeratorKnownRole.Should().Be(MainRoleType.Seer);
        recovered.GetPlayerState(holder.Id).PubliclyRevealedRole.Should().BeNull();
        recovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
            .ContainSingle(entry =>
                entry.Role == MainRoleType.Seer &&
                entry.PlayerIds.SetEquals(new[] { holder.Id }));
        recoveredNext.InstructionId.Should().Be(expectedNext.InstructionId);
        recoveredNext.SelectablePlayerIds.Should().BeEquivalentTo(expectedNext.SelectablePlayerIds);
        recoveredNext.AffectedPlayerIds.Should().Equal(expectedNext.AffectedPlayerIds);

        Action replayAcceptedIdentification = () =>
            recoveredService.ProcessInstruction(recoveredId, acceptedIdentification);
        replayAcceptedIdentification.Should().Throw<InvalidOperationException>();
        recovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should().ContainSingle();

        var continued = recoveredService.ProcessInstruction(
            recoveredId,
            recoveredNext.CreateResponse([target.Id]));

        continued.IsSuccess.Should().BeTrue();
        continued.ModeratorInstruction.Should().BeOfType<ConfirmationInstruction>();
        recovered.GameHistoryLog.OfType<NightActionLogEntry>().Should()
            .ContainSingle(entry =>
                entry.ActionType == NightActionType.SeerCheck &&
                entry.TargetIds!.SequenceEqual(new[] { target.Id }));

        MarkTestCompleted();
    }

    [Fact]
    public void VillagerVillager_AfterPhysicalDeal_RecordsOnePublicHolderBeforeNightPlay()
    {
        var builder = CreateBuilder()
            .WithPlayers("Alice", "Bob", "Charlie", "Diana", "Eve")
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Seer,
                MainRoleType.VillagerVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);

        builder.StartGame();
        var afterStart = builder.ConfirmGameStart();

        var observation = InstructionAssert
            .ExpectSuccessWithType<SelectPlayersInstruction>(afterStart);
        observation.Semantic.Should()
            .Be(ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal);
        observation.CountConstraint.Should().Be(NumberRangeConstraint.Single);
        observation.SelectablePlayerIds.Should()
            .BeEquivalentTo(builder.GetGameState()!.GetPlayers().Select(player => player.Id));

        var holder = builder.GetGameState()!.GetPlayers().ElementAt(2);
        var accepted = builder.Process(observation.CreateResponse([holder.Id]));

        accepted.IsSuccess.Should().BeTrue();
        accepted.ModeratorInstruction!.Semantic.Should().Be(ModeratorInstructionSemantic.StartNight);
        holder.State.CurrentRole.Should().Be(MainRoleType.VillagerVillager);
        holder.State.PhysicalCharacterCardRole.Should().Be(MainRoleType.VillagerVillager);
        holder.State.ModeratorKnownRole.Should().Be(MainRoleType.VillagerVillager);
        holder.State.PubliclyRevealedRole.Should().Be(MainRoleType.VillagerVillager);
        builder.GetGameState()!.GameHistoryLog
            .OfType<VillagerVillagerPublicFromDealLogEntry>()
            .Should().ContainSingle(entry => entry.PlayerId == holder.Id);

        MarkTestCompleted();
    }

    [Fact]
    public void VillagerVillager_AcceptedPublicFromDealObservation_RehydratesExactlyOnceAtNextInstruction()
    {
        var builder = CreateBuilder()
            .WithPlayers("Alice", "Bob", "Charlie", "Diana", "Eve")
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Seer,
                MainRoleType.VillagerVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);

        builder.StartGame();
        var observation = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            builder.ConfirmGameStart());
        var holder = builder.GetGameState()!.GetPlayers().ElementAt(2);
        var afterObservation = builder.Process(observation.CreateResponse([holder.Id]));
        var expectedNextInstruction = afterObservation.ModeratorInstruction!;

        var recoveredService = new GameService();
        var recoveredId = recoveredService.RehydrateSession(builder.GetGameState()!.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredId)!;
        var recoveredHolder = recovered.GetPlayer(holder.Id);

        recoveredHolder.State.CurrentRole.Should().Be(MainRoleType.VillagerVillager);
		recoveredHolder.State.PhysicalCharacterCardId.Should().NotBeNull();
        recoveredHolder.State.PhysicalCharacterCardRole.Should().Be(MainRoleType.VillagerVillager);
        recoveredHolder.State.ModeratorKnownRole.Should().Be(MainRoleType.VillagerVillager);
        recoveredHolder.State.PubliclyRevealedRole.Should().Be(MainRoleType.VillagerVillager);
		var recoveredPhysicalCard = recovered.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.Id ==
				recoveredHolder.State.PhysicalCharacterCardId);
		recoveredPhysicalCard.Zone.Should().Be(
			PhysicalCharacterCardZone.PlayerOwned);
		recoveredPhysicalCard.OwnerPlayerId.Should().Be(holder.Id);
        recovered.GameHistoryLog.OfType<VillagerVillagerPublicFromDealLogEntry>()
            .Should().ContainSingle(entry => entry.PlayerId == holder.Id);
        var recoveredInstruction = recoveredService.GetCurrentInstruction(recoveredId)!;
        recoveredInstruction.GetType().Should().Be(expectedNextInstruction.GetType());
        recoveredInstruction.InstructionId.Should().Be(expectedNextInstruction.InstructionId);
        recoveredInstruction.PublicAnnouncement.Should().Be(expectedNextInstruction.PublicAnnouncement);
        recoveredInstruction.PrivateInstruction.Should().Be(expectedNextInstruction.PrivateInstruction);

        MarkTestCompleted();
    }

    [Fact]
	public void GenericRoleReveal_WhenRoleIsPrivatelyKnown_StillMapsPhysicalRoleAndCommitsPublicReveal()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var seer = players[1];
        var otherVillager = players[2];

        builder.CompleteNightPhase(new NightActionInputs
        {
            WerewolfIds = [werewolf.Id],
            WerewolfVictimId = seer.Id,
            SeerId = seer.Id,
            SeerTargetId = otherVillager.Id
        });

		var reveal = builder.GetCurrentInstruction()
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		reveal.AffectedPlayerIds.Should().Equal(seer.Id);
		reveal.PlayersForAssignment.Should().Equal(seer.Id);
		reveal.RolesForAssignment.Should().Contain(MainRoleType.Seer);
		seer.State.ModeratorKnownRole.Should().Be(MainRoleType.Seer);
		seer.State.PubliclyRevealedRole.Should().BeNull();
		var acceptedReveal = reveal.CreateResponse(new()
		{
			[seer.Id] = MainRoleType.Seer
		});

		var afterReveal = builder.Process(acceptedReveal);

        afterReveal.IsSuccess.Should().BeTrue();
        seer.State.CurrentRole.Should().Be(MainRoleType.Seer);
        seer.State.ModeratorKnownRole.Should().Be(MainRoleType.Seer);
        seer.State.PubliclyRevealedRole.Should().Be(MainRoleType.Seer);
        var revealEntry = builder.GetGameState()!.GameHistoryLog
            .OfType<RoleRevealLogEntry>()
            .Should().ContainSingle().Subject;
        revealEntry.RevealedRoles.Should()
            .Contain(new KeyValuePair<Guid, MainRoleType>(seer.Id, MainRoleType.Seer));

        var recoveredService = new GameService();
        var recoveredId = recoveredService.RehydrateSession(builder.GetGameState()!.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredId)!;
        var recoveredNext = recoveredService.GetCurrentInstruction(recoveredId)!;

        recovered.GetPlayerState(seer.Id).PubliclyRevealedRole.Should().Be(MainRoleType.Seer);
        recovered.GameHistoryLog.OfType<RoleRevealLogEntry>().Should().ContainSingle();
        recoveredNext.GetType().Should().Be(afterReveal.ModeratorInstruction!.GetType());
        recoveredNext.InstructionId.Should().Be(afterReveal.ModeratorInstruction.InstructionId);
		Action replayAcceptedReveal = () =>
			recoveredService.ProcessInstruction(recoveredId, acceptedReveal);
        replayAcceptedReveal.Should().Throw<InvalidOperationException>();
        recovered.GameHistoryLog.OfType<RoleRevealLogEntry>().Should().ContainSingle();

        MarkTestCompleted();
    }

    [Fact]
	public void GenericRoleReveal_UnknownDawnVictim_BindsDealPoolCardAndRecordsInitialCurrentRole()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var seer = players[1];
        var unknownVictim = players[2];

        builder.CompleteNightPhase(new NightActionInputs
        {
            WerewolfIds = [werewolf.Id],
            WerewolfVictimId = unknownVictim.Id,
            SeerId = seer.Id,
            SeerTargetId = players[3].Id
        });

        var reveal = builder.GetCurrentInstruction()
            .Should().BeOfType<AssignRolesInstruction>().Subject;
        reveal.PlayersForAssignment.Should().Equal(unknownVictim.Id);
        reveal.AffectedPlayerIds.Should().Equal(unknownVictim.Id);
		var matchingDealPoolCardIds = builder.GetGameState()!
			.GetModeratorPhysicalCharacterCards()
			.Where(state =>
				state.Zone == PhysicalCharacterCardZone.DealPool &&
				state.Card.PrintedRole == MainRoleType.SimpleVillager)
			.Select(state => state.Card.Id)
			.ToHashSet();
		matchingDealPoolCardIds.Should().NotBeEmpty();
        unknownVictim.State.CurrentRole.Should().BeNull();
		unknownVictim.State.ModeratorKnownRole.Should().BeNull();
		unknownVictim.State.PhysicalCharacterCardId.Should().BeNull();
        unknownVictim.State.PubliclyRevealedRole.Should().BeNull();
        unknownVictim.State.Health.Should().Be(PlayerHealth.Alive);
        builder.GetGameState()!.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
            .Should().NotContain(entry => entry.PlayerId == unknownVictim.Id);

        var afterReveal = builder.Process(reveal.CreateResponse(new()
        {
            [unknownVictim.Id] = MainRoleType.SimpleVillager
        }));

        afterReveal.IsSuccess.Should().BeTrue();
		unknownVictim.State.CurrentRole.Should().Be(MainRoleType.SimpleVillager);
		unknownVictim.State.ModeratorKnownRole.Should().BeNull();
        unknownVictim.State.PubliclyRevealedRole.Should().Be(MainRoleType.SimpleVillager);
		unknownVictim.State.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.SimpleVillager);
		unknownVictim.State.PhysicalCharacterCardId.Should().NotBeNull();
		var ownedCardId = unknownVictim.State.PhysicalCharacterCardId!.Value;
		matchingDealPoolCardIds.Should().Contain(ownedCardId);
		builder.GetGameState()!.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.Id == ownedCardId)
			.Should().Be(new PhysicalCharacterCardState(
				new PhysicalCharacterCard(
					ownedCardId,
					MainRoleType.SimpleVillager),
				PhysicalCharacterCardZone.PlayerOwned,
				unknownVictim.Id));
        unknownVictim.State.Health.Should().Be(PlayerHealth.Dead);

        var history = builder.GetGameState()!.GameHistoryLog.ToList();
		var ownershipIndex = history.FindIndex(entry =>
			entry is PhysicalCharacterCardOwnershipObservedLogEntry ownership &&
			ownership.PlayerId == unknownVictim.Id &&
			ownership.CardId == ownedCardId);
		var assignmentIndex = history.FindIndex(entry =>
			entry is AssignRoleLogEntry assignment &&
			assignment.AssignedMainRole == MainRoleType.SimpleVillager &&
			assignment.PlayerIds.Contains(unknownVictim.Id));
		var revealIndex = history.FindIndex(entry => entry is RoleRevealLogEntry);
		var eliminationIndex = history.FindIndex(entry =>
                entry is PlayerEliminatedLogEntry eliminated &&
				eliminated.PlayerId == unknownVictim.Id);
		history.OfType<PhysicalCharacterCardOwnershipObservedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == unknownVictim.Id &&
				entry.CardId == ownedCardId);
		ownershipIndex.Should().BeLessThan(assignmentIndex);
		assignmentIndex.Should().BeLessThan(revealIndex);
		revealIndex.Should().BeLessThan(eliminationIndex);
		history.OfType<AssignRoleLogEntry>().Should().ContainSingle(entry =>
			entry.AssignedMainRole == MainRoleType.SimpleVillager &&
			entry.PlayerIds.Contains(unknownVictim.Id));
		history.OfType<RoleIdentificationLogEntry>().Should().NotContain(entry =>
			entry.PlayerIds.Contains(unknownVictim.Id));

        var recoveredService = new GameService();
        var recoveredId = recoveredService.RehydrateSession(builder.GetGameState()!.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredId)!;
        var recoveredNext = recoveredService.GetCurrentInstruction(recoveredId)!;

		var recoveredVictim = recovered.GetPlayerState(unknownVictim.Id);
		recoveredVictim.CurrentRole.Should().Be(MainRoleType.SimpleVillager);
		recoveredVictim.ModeratorKnownRole.Should().BeNull();
		recoveredVictim.PhysicalCharacterCardId.Should().Be(ownedCardId);
		recoveredVictim.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.SimpleVillager);
		recoveredVictim.PubliclyRevealedRole.Should().Be(
			MainRoleType.SimpleVillager);
		recoveredVictim.Health.Should().Be(PlayerHealth.Dead);
		recovered.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.Id == ownedCardId)
			.Should().Match<PhysicalCharacterCardState>(state =>
				state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
				state.OwnerPlayerId == unknownVictim.Id);
		recovered.GameHistoryLog
			.OfType<PhysicalCharacterCardOwnershipObservedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == unknownVictim.Id &&
				entry.CardId == ownedCardId);
        recovered.GameHistoryLog.OfType<RoleRevealLogEntry>().Should().ContainSingle();
        recovered.GameHistoryLog.OfType<PlayerEliminatedLogEntry>().Should()
            .ContainSingle(entry => entry.PlayerId == unknownVictim.Id);
        recoveredNext.GetType().Should().Be(afterReveal.ModeratorInstruction!.GetType());
        recoveredNext.InstructionId.Should().Be(afterReveal.ModeratorInstruction.InstructionId);
        Action replayAcceptedReveal = () => recoveredService.ProcessInstruction(
            recoveredId,
            reveal.CreateResponse(new()
            {
                [unknownVictim.Id] = MainRoleType.SimpleVillager
            }));
        replayAcceptedReveal.Should().Throw<InvalidOperationException>();
        recovered.GameHistoryLog.OfType<RoleRevealLogEntry>().Should().ContainSingle();
        recovered.GameHistoryLog.OfType<PlayerEliminatedLogEntry>().Should()
            .ContainSingle(entry => entry.PlayerId == unknownVictim.Id);

        MarkTestCompleted();
    }
}
