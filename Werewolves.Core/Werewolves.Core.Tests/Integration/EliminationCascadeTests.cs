using FluentAssertions;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public class EliminationCascadeTests(ITestOutputHelper output)
	: DiagnosticTestBase(output)
{
	[Fact]
	public void DawnConcurrentVictims_DrainReactionChainBeforeDayNavigation()
	{
		var reaction = new PlayerChainReaction();
		var (builder, players, initialReveal) =
			CreateDawnChainScenario(reaction);

		initialReveal.PlayersForAssignment.Should().BeEquivalentTo(
			[players[2].Id, players[3].Id]);
		initialReveal.RolesForAssignment.Should().Equal(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);

		var reactionReveal = builder.Process(initialReveal.CreateResponse(new()
			{
				[players[2].Id] = MainRoleType.SimpleVillager,
				[players[3].Id] = MainRoleType.SimpleVillager
			}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		builder.GetGameState()!.GetPlayerState(players[2].Id).Health.Should()
			.Be(PlayerHealth.Dead);
		builder.GetGameState()!.GetPlayerState(players[3].Id).Health.Should()
			.Be(PlayerHealth.Dead);
		reactionReveal.PlayersForAssignment.Should().Equal(players[4].Id);

		var chainedReveal = builder.Process(reactionReveal.CreateResponse(new()
			{
				[players[4].Id] = MainRoleType.SimpleVillager
			}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		chainedReveal.PlayersForAssignment.Should().Equal(players[5].Id);

		var afterCascade = builder.Process(chainedReveal.CreateResponse(new()
		{
			[players[5].Id] = MainRoleType.SimpleVillager
		}));

		afterCascade.ModeratorInstruction.Should()
			.NotBeOfType<AssignRolesInstruction>();
		var completed = builder.GetGameState()!;
		completed.GetCurrentPhase().Should().Be(GamePhase.Day);
		var expectedEliminations = new Dictionary<Guid, EliminationReason>
		{
			[players[2].Id] = EliminationReason.WerewolfAttack,
			[players[3].Id] = EliminationReason.WitchKill,
			[players[4].Id] = EliminationReason.EventElimination,
			[players[5].Id] = EliminationReason.EventElimination
		};
		foreach (var (playerId, reason) in expectedEliminations)
		{
			completed.GetPlayerState(playerId).Health.Should().Be(
				PlayerHealth.Dead);
			completed.GameHistoryLog
				.OfType<PlayerEliminatedLogEntry>()
				.Should().ContainSingle(entry =>
					entry.PlayerId == playerId &&
					entry.Reason == reason);
		}

		var reactionFacts = completed.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Where(entry => entry.ReactionId == reaction.ReactionId)
			.ToArray();
		reactionFacts.Should().HaveCount(3);
		reactionFacts[0].TriggeringEliminations.Should().BeEquivalentTo(
			[
				new EliminationCascadeElimination(
					players[2].Id,
					EliminationReason.WerewolfAttack),
				new EliminationCascadeElimination(
					players[3].Id,
					EliminationReason.WitchKill)
			]);
		reactionFacts[0].AdmittedEliminations.Should().ContainSingle(
			elimination =>
				elimination.PlayerId == players[4].Id &&
				elimination.Reason == EliminationReason.EventElimination);

		MarkTestCompleted();
	}

	[Fact]
	public void DawnPubliclyKnownAndUnknownVictims_RevealsOnlyUnknownBeforeBatchCommit()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Werewolf",
				"Witch",
				"Public victim",
				"Unknown victim",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Witch,
				MainRoleType.VillagerVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var publicObservation = builder.ConfirmGameStart()
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		builder.Process(publicObservation.CreateResponse([players[2].Id]));
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction(
			[players[0].Id],
			players[2].Id);
		var witchIdentification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var healing = builder.Process(
				witchIdentification.CreateResponse([players[1].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var poison = builder.Process(healing.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var sleep = builder.Process(
				poison.CreateResponse([players[3].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = builder.Process(sleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var reveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		reveal.PlayersForAssignment.Should().Equal(players[3].Id);
		reveal.PublicAnnouncement.Should().Contain(players[2].Name);
		reveal.PublicAnnouncement.Should().Contain(players[3].Name);
		builder.GetGameState()!.GetPlayerState(players[2].Id).Health
			.Should().Be(PlayerHealth.Alive);
		builder.GetGameState()!.GetPlayerState(players[3].Id).Health
			.Should().Be(PlayerHealth.Alive);

		builder.Process(reveal.CreateResponse(new()
		{
			[players[3].Id] = MainRoleType.SimpleVillager
		}));

		var session = builder.GetGameState()!;
		session.GetPlayerState(players[2].Id).Health.Should().Be(
			PlayerHealth.Dead);
		session.GetPlayerState(players[3].Id).Health.Should().Be(
			PlayerHealth.Dead);
		session.GameHistoryLog
			.OfType<RoleRevealLogEntry>()
			.SelectMany(entry => entry.RevealedRoles.Keys)
			.Should().NotContain(players[2].Id);
		session.GameHistoryLog
			.OfType<RoleRevealLogEntry>()
			.SelectMany(entry => entry.RevealedRoles.Keys)
			.Should().ContainSingle(playerId =>
				playerId == players[3].Id);

		MarkTestCompleted();
	}

	[Fact]
	public void ReactionPublicVictimAnnouncement_RehydratesExactlyOnceBeforeCommit()
	{
		var originalReaction = new SingleWaveReaction();
		var builder = CreateBuilder()
			.WithEliminationCascadeReaction(originalReaction)
			.WithPlayers(
				"Werewolf",
				"Public reaction victim",
				"Initial victim",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.VillagerVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var publicVictimId = players[1].Id;
		var initialVictimId = players[2].Id;
		originalReaction.Configure(initialVictimId, publicVictimId);
		var publicObservation = builder.ConfirmGameStart()
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		builder.Process(publicObservation.CreateResponse([publicVictimId]));
		builder.GetGameState()!.GetPlayerState(publicVictimId)
			.PubliclyRevealedRole.Should().Be(MainRoleType.VillagerVillager);
		builder.ConfirmNightStart();
		var finishNight = builder.CompleteWerewolfNightAction(
				[players[0].Id],
				initialVictimId)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var initialReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var announcement = builder.Process(initialReveal.CreateResponse(new()
			{
				[initialVictimId] = MainRoleType.SimpleVillager
			}))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceEliminationCascadeVictims);
		announcement.AffectedPlayerIds.Should().BeEquivalentTo(
			[publicVictimId]);
		builder.GetGameState()!.GetPlayerState(initialVictimId).Health.Should().Be(
			PlayerHealth.Dead);
		builder.GetGameState()!.GetPlayerState(publicVictimId).Health.Should().Be(
			PlayerHealth.Alive);

		var recoveredReaction = new SingleWaveReaction();
		recoveredReaction.Configure(initialVictimId, publicVictimId);
		var recoveredService = new GameService(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[
				new EliminationCascadeReactionBinding(
					recoveredReaction,
					EliminationCascadeReactionBoundary.Forced)
			]);
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredAnnouncement = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceEliminationCascadeVictims);
		recoveredAnnouncement.InstructionId.Should().Be(
			announcement.InstructionId);
		var afterAnnouncement = recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredAnnouncement.CreateResponse());

		afterAnnouncement.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		recovered.GetCurrentPhase().Should().Be(GamePhase.Day);
		recovered.GetPlayerState(initialVictimId).Health.Should().Be(
			PlayerHealth.Dead);
		recovered.GetPlayerState(publicVictimId).Health.Should().Be(
			PlayerHealth.Dead);
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == initialVictimId &&
				entry.Reason == EliminationReason.WerewolfAttack);
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == publicVictimId &&
				entry.Reason == EliminationReason.EventElimination);
		recovered.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ScopeId.StartsWith("Dawn:"));

		MarkTestCompleted();
	}

	[Fact]
	public void DawnCommittedAndUnknownVictims_RevealOneAtomicBatchWithDistinctDuplicateRoleCards()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Werewolf",
				"Witch victim",
				"Poison victim",
				"Villager A",
				"Villager B",
				"Villager C")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();

		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction(
			[players[0].Id],
			players[1].Id);
		var witchIdentification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var healing = builder.Process(
				witchIdentification.CreateResponse([players[1].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var poison = builder.Process(healing.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var sleep = builder.Process(
				poison.CreateResponse([players[2].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		builder.ArrangeKnownPhysicalRole(
			players[1].Id,
			MainRoleType.SimpleVillager);
		builder.ArrangeKnownRole(players[1].Id, MainRoleType.Witch);
		var committedVictimCardId = builder.GetGameState()!
			.GetPlayerState(players[1].Id)
			.PhysicalCharacterCardId!.Value;
		var finishNight = builder.Process(sleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var reveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		reveal.PlayersForAssignment.Should().Equal(players[2].Id);
		reveal.AffectedPlayerIds.Should().BeEquivalentTo(
			[players[2].Id, players[1].Id]);
		builder.GetGameState()!.GetPlayerState(players[1].Id).Health
			.Should().Be(PlayerHealth.Alive);
		builder.GetGameState()!.GetPlayerState(players[2].Id).Health
			.Should().Be(PlayerHealth.Alive);

		var beforeInvalidReveal = PublicGameSessionSnapshot.Capture(builder);
		var invalidResponse = new ModeratorResponse
		{
			InstructionId = reveal.InstructionId,
			Type = ExpectedInputType.AssignPlayerRoles,
			AssignedPlayerRoles = new Dictionary<Guid, MainRoleType>()
		};
		var invalidReveal = () => builder.Process(invalidResponse);

		invalidReveal.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(builder).Should().BeEquivalentTo(
			beforeInvalidReveal,
			options => options.WithStrictOrdering());
		var matchingUnusedDealPoolCardIds = builder.GetGameState()!
			.GetModeratorPhysicalCharacterCards()
			.Where(state =>
				state.Zone == PhysicalCharacterCardZone.DealPool &&
				state.Card.PrintedRole == MainRoleType.SimpleVillager)
			.Select(state => state.Card.Id)
			.ToHashSet();
		matchingUnusedDealPoolCardIds.Should().HaveCountGreaterThanOrEqualTo(2);
		builder.GetGameState()!.GetPlayerState(players[1].Id).CurrentRole
			.Should().Be(MainRoleType.Witch);
		builder.GetGameState()!.GetPlayerState(players[1].Id).ModeratorKnownRole
			.Should().Be(MainRoleType.Witch);
		builder.GetGameState()!.GetPlayerState(players[1].Id)
			.PhysicalCharacterCardRole.Should()
			.Be(MainRoleType.SimpleVillager);
		builder.GetGameState()!.GetPlayerState(players[2].Id).CurrentRole
			.Should().BeNull();
		builder.GetGameState()!.GetPlayerState(players[2].Id).ModeratorKnownRole
			.Should().BeNull();

		builder.Process(reveal.CreateResponse(new()
		{
			[players[2].Id] = MainRoleType.SimpleVillager
		}));

		var session = builder.GetGameState()!;
		var ownerships = session.GameHistoryLog
			.OfType<PhysicalCharacterCardOwnershipObservedLogEntry>()
			.Where(entry => entry.PlayerId == players[1].Id ||
				entry.PlayerId == players[2].Id)
			.ToArray();
		ownerships.Should().HaveCount(2);
		ownerships.Select(entry => entry.PlayerId).Should().OnlyHaveUniqueItems();
		var ownedCardIds = ownerships.Select(entry => entry.CardId).ToArray();
		ownedCardIds.Should().OnlyHaveUniqueItems();
		ownerships.Single(entry => entry.PlayerId == players[1].Id)
			.CardId.Should().Be(committedVictimCardId);
		matchingUnusedDealPoolCardIds.Should().Contain(
			ownerships.Single(entry => entry.PlayerId == players[2].Id).CardId);
		session.GetPlayerState(players[1].Id).CurrentRole.Should()
			.Be(MainRoleType.Witch);
		session.GetPlayerState(players[1].Id).ModeratorKnownRole.Should()
			.Be(MainRoleType.Witch);
		session.GetPlayerState(players[2].Id).CurrentRole.Should()
			.Be(MainRoleType.SimpleVillager);
		session.GetPlayerState(players[2].Id).ModeratorKnownRole.Should()
			.Be(MainRoleType.SimpleVillager);
		session.GetPlayerState(players[1].Id).PhysicalCharacterCardRole.Should()
			.Be(MainRoleType.SimpleVillager);
		session.GetPlayerState(players[2].Id).PhysicalCharacterCardRole.Should()
			.Be(MainRoleType.SimpleVillager);
		session.GameHistoryLog
			.OfType<RoleRevealLogEntry>()
			.Should().ContainSingle(entry =>
				entry.RevealedRoles.Count == 2 &&
				entry.RevealedRoles.GetValueOrDefault(
					players[1].Id) == MainRoleType.SimpleVillager &&
				entry.RevealedRoles.GetValueOrDefault(
					players[2].Id) == MainRoleType.SimpleVillager);
		session.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.SimpleVillager &&
				entry.PlayerIds.SetEquals(new[] { players[2].Id }));
		session.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == players[1].Id &&
				entry.Reason == EliminationReason.WerewolfAttack);
		session.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == players[2].Id &&
				entry.Reason == EliminationReason.WitchKill);

		MarkTestCompleted();
	}

	[Fact]
	public void CompletedPreRevealReaction_RehydratesPendingRevealWithoutDroppingAdmittedWave()
	{
		var originalReaction = new SingleWaveReaction();
		var (builder, players, initialReveal) = CreateConcurrentDawnScenario(
			originalReaction,
			EliminationCascadeReactionBoundary.PreReveal,
			scenarioPlayers => originalReaction.Configure(
				scenarioPlayers[2].Id,
				scenarioPlayers[4].Id));
		var initialVictimIds = new[] { players[2].Id, players[3].Id };
		var admittedVictimId = players[4].Id;

		var interrupted = builder.GetGameState()!;
		interrupted.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ReactionId == originalReaction.ReactionId &&
				entry.TriggeringEliminations
					.Select(elimination => elimination.PlayerId)
					.ToHashSet()
					.SetEquals(initialVictimIds) &&
				entry.AdmittedEliminations.Count == 1 &&
				entry.AdmittedEliminations.Single().PlayerId ==
					admittedVictimId);

		var recoveredReaction = new SingleWaveReaction();
		recoveredReaction.Configure(players[2].Id, admittedVictimId);
		var recoveredService = new GameService(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[
				new EliminationCascadeReactionBinding(
					recoveredReaction,
					EliminationCascadeReactionBoundary.PreReveal)
			]);
		var recoveredGameId = recoveredService.RehydrateSession(
			interrupted.Serialize());
		var recoveredInitialReveal = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<AssignRolesInstruction>().Subject;

		recoveredInitialReveal.InstructionId.Should().Be(
			initialReveal.InstructionId);
		recoveredInitialReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDawnVictimRoles);
		var admittedReveal = recoveredService.ProcessInstruction(
				recoveredGameId,
				recoveredInitialReveal.CreateResponse(new()
				{
					[players[2].Id] = MainRoleType.SimpleVillager,
					[players[3].Id] = MainRoleType.SimpleVillager
				}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		admittedReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignEliminationCascadeRoles);
		admittedReveal.PlayersForAssignment.Should().Equal(admittedVictimId);
		var beforeAdmittedReveal = recoveredService.GetGameStateView(
			recoveredGameId)!;
		foreach (var initialVictimId in initialVictimIds)
		{
			beforeAdmittedReveal.GetPlayerState(initialVictimId).Health.Should()
				.Be(PlayerHealth.Dead);
		}
		beforeAdmittedReveal.GetPlayerState(admittedVictimId).Health.Should()
			.Be(PlayerHealth.Alive);

		var afterCascade = recoveredService.ProcessInstruction(
			recoveredGameId,
			admittedReveal.CreateResponse(new()
			{
				[admittedVictimId] = MainRoleType.SimpleVillager
			}));

		afterCascade.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		recovered.GetCurrentPhase().Should().Be(GamePhase.Day);
		recovered.GetPlayerState(admittedVictimId).Health.Should().Be(
			PlayerHealth.Dead);
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == admittedVictimId &&
				entry.Reason == EliminationReason.EventElimination);

		MarkTestCompleted();
	}

	[Fact]
	public void PreRevealReactionPause_RetainsEliminationsScheduledByEarlierReaction()
	{
		var scheduledReaction = new SingleWaveReaction();
		var selectionReaction = new InteractiveSelectionReaction();
		var builder = CreateBuilder()
			.WithEliminationCascadeReactions(
				new EliminationCascadeReactionBinding(
					scheduledReaction,
					EliminationCascadeReactionBoundary.PreReveal),
				new EliminationCascadeReactionBinding(
					selectionReaction,
					EliminationCascadeReactionBoundary.PreReveal))
			.WithPlayers(
				"Werewolf",
				"Witch",
				"Attack victim",
				"Poison victim",
				"Scheduled victim",
				"Selected victim",
				"Survivor")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var initialVictimIds = new[] { players[2].Id, players[3].Id };
		var scheduledVictimId = players[4].Id;
		var selectedVictimId = players[5].Id;
		scheduledReaction.Configure(players[2].Id, scheduledVictimId);
		selectionReaction.Configure(initialVictimIds, [selectedVictimId]);

		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction(
			[players[0].Id],
			players[2].Id);
		var witchIdentification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var healing = builder.Process(
				witchIdentification.CreateResponse([players[1].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var poison = builder.Process(healing.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var sleep = builder.Process(poison.CreateResponse([players[3].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = builder.Process(sleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var selection = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		selection.Semantic.Should().Be(
			ModeratorInstructionSemantic.Unspecified);
		selection.AffectedPlayerIds.Should().BeEquivalentTo(initialVictimIds);
		selection.SelectablePlayerIds.Should().Equal(selectedVictimId);
		var initialReveal = builder.Process(
				selection.CreateResponse([selectedVictimId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var serialized = builder.GetGameState()!.Serialize();
		var recoveredScheduledReaction = new SingleWaveReaction();
		recoveredScheduledReaction.Configure(
			players[2].Id,
			scheduledVictimId);
		var recoveredSelectionReaction = new InteractiveSelectionReaction();
		recoveredSelectionReaction.Configure(
			initialVictimIds,
			[selectedVictimId]);
		var recoveredService = new GameService(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[
				new EliminationCascadeReactionBinding(
					recoveredScheduledReaction,
					EliminationCascadeReactionBoundary.PreReveal),
				new EliminationCascadeReactionBinding(
					recoveredSelectionReaction,
					EliminationCascadeReactionBoundary.PreReveal)
			]);
		var recoveredGameId = recoveredService.RehydrateSession(serialized);
		var recoveredInitialReveal = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		recoveredInitialReveal.InstructionId.Should().Be(
			initialReveal.InstructionId);

		var liveReactionReveal = builder.Process(
				initialReveal.CreateResponse(new()
				{
					[players[2].Id] = MainRoleType.SimpleVillager,
					[players[3].Id] = MainRoleType.SimpleVillager
				}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var recoveredReactionReveal = recoveredService.ProcessInstruction(
				recoveredGameId,
				recoveredInitialReveal.CreateResponse(new()
				{
					[players[2].Id] = MainRoleType.SimpleVillager,
					[players[3].Id] = MainRoleType.SimpleVillager
				}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		foreach (var reactionReveal in new[]
			{
				liveReactionReveal,
				recoveredReactionReveal
			})
		{
			reactionReveal.Semantic.Should().Be(
				ModeratorInstructionSemantic.AssignEliminationCascadeRoles);
			reactionReveal.PlayersForAssignment.Should().BeEquivalentTo(
				[scheduledVictimId, selectedVictimId]);
		}
		var afterCascade = recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredReactionReveal.CreateResponse(new()
			{
				[scheduledVictimId] = MainRoleType.SimpleVillager,
				[selectedVictimId] = MainRoleType.SimpleVillager
			}));

		afterCascade.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		var completed = recoveredService.GetGameStateView(recoveredGameId)!;
		var expectedEliminations = new Dictionary<Guid, EliminationReason>
		{
			[players[2].Id] = EliminationReason.WerewolfAttack,
			[players[3].Id] = EliminationReason.WitchKill,
			[scheduledVictimId] = EliminationReason.EventElimination,
			[selectedVictimId] = EliminationReason.EventElimination
		};
		foreach (var (playerId, reason) in expectedEliminations)
		{
			completed.GetPlayerState(playerId).Health.Should().Be(
				PlayerHealth.Dead);
			completed.GameHistoryLog
				.OfType<PlayerEliminatedLogEntry>()
				.Should().ContainSingle(entry =>
					entry.PlayerId == playerId &&
					entry.Reason == reason);
		}

		MarkTestCompleted();
	}

	[Fact]
	public void MixedPreRevealAndForcedDescendants_RehydrateInLiveFifoOrder()
	{
		var parentPreRevealReaction =
			new SingleWaveReaction("parent-pre-reveal");
		var childPreRevealReaction =
			new SingleWaveReaction("child-pre-reveal");
		var parentForcedReaction =
			new SingleWaveReaction("parent-forced");
		var builder = CreateBuilder()
			.WithEliminationCascadeReactions(
				new EliminationCascadeReactionBinding(
					parentPreRevealReaction,
					EliminationCascadeReactionBoundary.PreReveal),
				new EliminationCascadeReactionBinding(
					childPreRevealReaction,
					EliminationCascadeReactionBoundary.PreReveal),
				new EliminationCascadeReactionBinding(
					parentForcedReaction,
					EliminationCascadeReactionBoundary.Forced))
			.WithPlayers(
				"Werewolf",
				"Initial victim",
				"Pre-reveal child",
				"Forced sibling",
				"Pre-reveal grandchild",
				"Survivor A",
				"Survivor B")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var initialVictimId = players[1].Id;
		var preRevealChildId = players[2].Id;
		var forcedSiblingId = players[3].Id;
		var preRevealGrandchildId = players[4].Id;
		parentPreRevealReaction.Configure(
			initialVictimId,
			preRevealChildId);
		childPreRevealReaction.Configure(
			preRevealChildId,
			preRevealGrandchildId);
		parentForcedReaction.Configure(initialVictimId, forcedSiblingId);

		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var finishNight = builder.CompleteWerewolfNightAction(
				[players[0].Id],
				initialVictimId)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var initialReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var preRevealChildReveal = builder.Process(
				initialReveal.CreateResponse(new()
				{
					[initialVictimId] = MainRoleType.SimpleVillager
				}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		preRevealChildReveal.PlayersForAssignment.Should().Equal(
			preRevealChildId);
		var forcedSiblingReveal = builder.Process(
				preRevealChildReveal.CreateResponse(new()
				{
					[preRevealChildId] = MainRoleType.SimpleVillager
				}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		forcedSiblingReveal.PlayersForAssignment.Should().Equal(
			forcedSiblingId);
		var serialized = builder.GetGameState()!.Serialize();

		var recoveredParentPreRevealReaction =
			new SingleWaveReaction("parent-pre-reveal");
		recoveredParentPreRevealReaction.Configure(
			initialVictimId,
			preRevealChildId);
		var recoveredChildPreRevealReaction =
			new SingleWaveReaction("child-pre-reveal");
		recoveredChildPreRevealReaction.Configure(
			preRevealChildId,
			preRevealGrandchildId);
		var recoveredParentForcedReaction =
			new SingleWaveReaction("parent-forced");
		recoveredParentForcedReaction.Configure(
			initialVictimId,
			forcedSiblingId);
		var recoveredService = new GameService(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[
				new EliminationCascadeReactionBinding(
					recoveredParentPreRevealReaction,
					EliminationCascadeReactionBoundary.PreReveal),
				new EliminationCascadeReactionBinding(
					recoveredChildPreRevealReaction,
					EliminationCascadeReactionBoundary.PreReveal),
				new EliminationCascadeReactionBinding(
					recoveredParentForcedReaction,
					EliminationCascadeReactionBoundary.Forced)
			]);
		var recoveredGameId = recoveredService.RehydrateSession(serialized);
		var recoveredForcedSiblingReveal = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<AssignRolesInstruction>().Subject;

		recoveredForcedSiblingReveal.InstructionId.Should().Be(
			forcedSiblingReveal.InstructionId);
		recoveredForcedSiblingReveal.PlayersForAssignment.Should().Equal(
			forcedSiblingId);
		var grandchildReveal = recoveredService.ProcessInstruction(
				recoveredGameId,
				recoveredForcedSiblingReveal.CreateResponse(new()
				{
					[forcedSiblingId] = MainRoleType.SimpleVillager
				}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		grandchildReveal.PlayersForAssignment.Should().Equal(
			preRevealGrandchildId);
		var afterCascade = recoveredService.ProcessInstruction(
			recoveredGameId,
			grandchildReveal.CreateResponse(new()
			{
				[preRevealGrandchildId] = MainRoleType.SimpleVillager
			}));

		afterCascade.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Where(entry =>
				new[]
				{
					initialVictimId,
					preRevealChildId,
					forcedSiblingId,
					preRevealGrandchildId
				}.Contains(entry.PlayerId))
			.Select(entry => entry.PlayerId)
			.Should().Equal(
				initialVictimId,
				preRevealChildId,
				forcedSiblingId,
				preRevealGrandchildId);

		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedChainedReveal_RehydratesExactPendingWaveWithoutReplayingCompletedReactions()
	{
		var originalReaction = new PlayerChainReaction();
		var (builder, players, initialReveal) =
			CreateDawnChainScenario(originalReaction);
		var reactionReveal = builder.Process(initialReveal.CreateResponse(new()
			{
				[players[2].Id] = MainRoleType.SimpleVillager,
				[players[3].Id] = MainRoleType.SimpleVillager
			}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var acceptedReactionResponse = reactionReveal.CreateResponse(new()
		{
			[players[4].Id] = MainRoleType.SimpleVillager
		});
		var chainedReveal = builder.Process(acceptedReactionResponse)
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var serialized = builder.GetGameState()!.Serialize();

		var recoveredReaction = new PlayerChainReaction();
		recoveredReaction.Configure(
			players[2].Id,
			players[3].Id,
			players[4].Id,
			players[5].Id);
		var recoveredService = new GameService(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[
				new EliminationCascadeReactionBinding(
					recoveredReaction,
					EliminationCascadeReactionBoundary.Forced)
			]);
		var recoveredGameId = recoveredService.RehydrateSession(serialized);
		var recoveredReveal = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<AssignRolesInstruction>().Subject;

		recoveredReveal.InstructionId.Should().Be(chainedReveal.InstructionId);
		recoveredReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignEliminationCascadeRoles);
		recoveredReveal.PlayersForAssignment.Should().Equal(players[5].Id);

		var beforeStaleResponse = recoveredService
			.GetGameStateView(recoveredGameId)!.Serialize();
		var processStaleResponse = () => recoveredService.ProcessInstruction(
			recoveredGameId,
			acceptedReactionResponse);

		processStaleResponse.Should().Throw<InvalidOperationException>();
		recoveredService.GetGameStateView(recoveredGameId)!.Serialize()
			.Should().Be(beforeStaleResponse);

		recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredReveal.CreateResponse(new()
			{
				[players[5].Id] = MainRoleType.SimpleVillager
			}));

		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		foreach (var player in players.Skip(2))
		{
			recovered.GameHistoryLog
				.OfType<PlayerEliminatedLogEntry>()
				.Should().ContainSingle(entry =>
					entry.PlayerId == player.Id);
			recovered.GameHistoryLog
				.OfType<RoleRevealLogEntry>()
				.SelectMany(entry => entry.RevealedRoles.Keys)
				.Should().ContainSingle(playerId =>
					playerId == player.Id);
		}

		var completionFacts = recovered.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Where(entry =>
				entry.ReactionId == recoveredReaction.ReactionId)
			.ToArray();
		completionFacts.Should().HaveCount(3);
		completionFacts
			.Select(entry =>
				$"{entry.ScopeId}/{entry.ReactionId}/" +
				string.Join(
					",",
					entry.TriggeringEliminations.Select(elimination =>
						$"{elimination.PlayerId}:{elimination.Reason}")))
			.Should().OnlyHaveUniqueItems();
		recovered.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Should().BeEmpty();

		MarkTestCompleted();
	}

	[Fact]
	public void WildChildTransformation_RehydratesInsideCascadeWithoutRepeatingDurableEffects()
	{
		var originalReaction = new SingleWaveReaction();
		var builder = CreateBuilder()
			.WithEliminationCascadeReaction(originalReaction)
			.WithPlayers(
				"Wild Child",
				"Model",
				"Werewolf",
				"Reaction victim",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.WildChild,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var wildChildId = players[0].Id;
		var modelId = players[1].Id;
		var werewolfId = players[2].Id;
		var reactionVictimId = players[3].Id;
		originalReaction.Configure(modelId, reactionVictimId);

		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wildChildIdentification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var modelSelection = builder.Process(
				wildChildIdentification.CreateResponse([wildChildId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var wildChildSleep = builder.Process(
				modelSelection.CreateResponse([modelId]))
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
				victimSelection.CreateResponse([modelId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = builder.Process(werewolfSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var modelReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var reactionReveal = builder.Process(modelReveal.CreateResponse(new()
			{
				[modelId] = MainRoleType.SimpleVillager
			}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		reactionReveal.PlayersForAssignment.Should().Equal(
			reactionVictimId);
		var interrupted = builder.GetGameState()!;
		interrupted.GetPlayerState(wildChildId).CurrentRole.Should().Be(
			MainRoleType.WildChild);
		interrupted.GetFactionBeneficiaryKnowledge(wildChildId).Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
		interrupted.GetFactionAgentKnowledge(wildChildId, Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		interrupted.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == wildChildId &&
				entry.EffectType == StatusEffectTypes.WildChildChanged &&
				entry.IsActive);
		interrupted.GameHistoryLog
			.OfType<AssignRoleLogEntry>()
			.Should().NotContain(entry => entry.PlayerIds.Contains(wildChildId));

		var recoveredReaction = new SingleWaveReaction();
		recoveredReaction.Configure(modelId, reactionVictimId);
		var recoveredService = new GameService(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[
				new EliminationCascadeReactionBinding(
					recoveredReaction,
					EliminationCascadeReactionBoundary.Forced)
			]);
		var recoveredGameId = recoveredService.RehydrateSession(
			interrupted.Serialize());
		var recoveredReveal = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		recoveredReveal.InstructionId.Should().Be(
			reactionReveal.InstructionId);

		recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredReveal.CreateResponse(new()
			{
				[reactionVictimId] = MainRoleType.SimpleVillager
			}));

		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		recovered.GetPlayerState(wildChildId).CurrentRole.Should().Be(
			MainRoleType.WildChild);
		recovered.GetFactionBeneficiaryKnowledge(wildChildId).Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
		recovered.GetFactionAgentKnowledge(wildChildId, Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		recovered.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == wildChildId &&
				entry.EffectType == StatusEffectTypes.WildChildChanged &&
				entry.IsActive);
		recovered.GameHistoryLog
			.OfType<AssignRoleLogEntry>()
			.Should().NotContain(entry => entry.PlayerIds.Contains(wildChildId));
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == modelId);
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == reactionVictimId);

		MarkTestCompleted();
	}

	[Fact]
	public void VillageIdiotPardon_CommitsDurableVotingConsequencesExactlyOnce()
	{
		var scenario = DayVoteScenario.Start(
			livingTargetRole: MainRoleType.VillageIdiot);
		var builder = scenario.Builder;
		var beforeVote = builder.GetGameState()!
			.GetPlayerState(scenario.LivingTargetId);
		beforeVote.CurrentRole.Should().Be(MainRoleType.VillageIdiot);
		beforeVote.ModeratorKnownRole.Should().Be(MainRoleType.VillageIdiot);
		beforeVote.PubliclyRevealedRole.Should().BeNull();
		beforeVote.DurableVotingPower.Should().Be(1);
		beforeVote.HasVotingRight.Should().BeTrue();

		var revealContinue = builder.Process(
				scenario.Instruction.CreateResponse([scenario.LivingTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		revealContinue.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDayVoteTargetRole);
		revealContinue.AffectedPlayerIds.Should().Equal(
			scenario.LivingTargetId);
		var awaitingReveal = builder.GetGameState()!
			.GetPlayerState(scenario.LivingTargetId);
		awaitingReveal.PubliclyRevealedRole.Should().BeNull();
		awaitingReveal.DurableVotingPower.Should().Be(1);
		awaitingReveal.HasVotingRight.Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();

		var immunityAnnouncement = builder.Process(
				revealContinue.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		immunityAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon);
		var interrupted = builder.GetGameState()!;
		var immunityTurn = interrupted.TurnNumber;
		var pardonedState = interrupted.GetPlayerState(scenario.LivingTargetId);
		pardonedState.Health.Should().Be(PlayerHealth.Alive);
		pardonedState.DurableVotingPower.Should().Be(0);
		pardonedState.HasVotingRight.Should().BeFalse();
		interrupted.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == scenario.LivingTargetId &&
				entry.ResourceIdentity.ActingPlayerId ==
					scenario.LivingTargetId &&
				entry.ResourceIdentity.SourceRole ==
					MainRoleType.VillageIdiot);
		interrupted.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.PlayerId == scenario.LivingTargetId);
		interrupted.GameHistoryLog
			.OfType<EliminationCascadeBatchResolvedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ScopeId == $"Day:{immunityTurn}:Vote:1" &&
				entry.RequestedEliminations.Count == 1 &&
				entry.RequestedEliminations[0].PlayerId ==
					scenario.LivingTargetId &&
				entry.CommittedEliminations.Count == 0);

		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			interrupted.Serialize());
		var recoveredAnnouncement = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredAnnouncement.InstructionId.Should().Be(
			immunityAnnouncement.InstructionId);
		recoveredAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon);
		var afterAnnouncement = recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredAnnouncement.CreateResponse());

		afterAnnouncement.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		var recoveredState = recovered.GetPlayerState(
			scenario.LivingTargetId);
		recoveredState.Health.Should().Be(PlayerHealth.Alive);
		recoveredState.PubliclyRevealedRole.Should().Be(
			MainRoleType.VillageIdiot);
		recoveredState.DurableVotingPower.Should().Be(0);
		recoveredState.HasVotingRight.Should().BeFalse();
		recovered.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == scenario.LivingTargetId);
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.PlayerId == scenario.LivingTargetId);
		recovered.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ScopeId == $"Day:{immunityTurn}:Vote:1");

		MarkTestCompleted();
	}

	[Fact]
	public void DayPublicVoteAnnouncement_RehydratesExactSettledBatchWithoutReplayingVote()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Werewolf",
				"Seer",
				"Public vote target",
				"Night victim",
				"Villager")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.VillagerVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolfId = players[0].Id;
		var seerId = players[1].Id;
		var publicTargetId = players[2].Id;
		var nightVictimId = players[3].Id;
		var publicObservation = builder.ConfirmGameStart()
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		builder.Process(publicObservation.CreateResponse([publicTargetId]));
		builder.CompleteNightPhase(
			[werewolfId],
			nightVictimId,
			seerId,
			players[4].Id);
		builder.CompleteDawnPhase();

		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var announcement = builder.Process(
				vote.CreateResponse([publicTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		var interrupted = builder.GetGameState()!;
		var voteTurn = interrupted.TurnNumber;
		var voteScope = $"Day:{voteTurn}:Vote:1";
		interrupted.GetPlayerState(publicTargetId).Health.Should().Be(
			PlayerHealth.Dead);
		interrupted.GameHistoryLog
			.OfType<EliminationCascadeBatchResolvedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ScopeId == voteScope &&
				entry.RequestedEliminations.Count == 1 &&
				entry.RequestedEliminations[0].PlayerId == publicTargetId &&
				entry.CommittedEliminations.Count == 1 &&
				entry.CommittedEliminations[0].PlayerId == publicTargetId);

		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			interrupted.Serialize());
		var recoveredAnnouncement = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredAnnouncement.InstructionId.Should().Be(
			announcement.InstructionId);
		recoveredAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		var afterAnnouncement = recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredAnnouncement.CreateResponse());

		afterAnnouncement.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		recovered.GetPlayerState(publicTargetId).Health.Should().Be(
			PlayerHealth.Dead);
		recovered.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ReportedOutcomePlayerId == publicTargetId);
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == publicTargetId &&
				entry.Reason == EliminationReason.DayVote);
		recovered.GameHistoryLog
			.OfType<EliminationCascadeBatchResolvedLogEntry>()
			.Should().ContainSingle(entry => entry.ScopeId == voteScope);
		recovered.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().ContainSingle(entry => entry.ScopeId == voteScope);

		MarkTestCompleted();
	}

	[Fact]
	public void InteractiveReactionSelector_RehydratesExactPendingSelectionWithoutSerializedAdapterState()
	{
		var originalReaction = new InteractiveSelectionReaction();
		var (builder, players, initialReveal) =
			CreateConcurrentDawnScenario(
				originalReaction,
				EliminationCascadeReactionBoundary.Interactive,
				scenarioPlayers => originalReaction.Configure(
					[scenarioPlayers[2].Id, scenarioPlayers[3].Id],
					scenarioPlayers.Select(player => player.Id)));
		var initialVictimIds = new[]
		{
			players[2].Id,
			players[3].Id
		};
		var reactionVictimId = players[4].Id;
		var reactionPrompt = builder.Process(
				initialReveal.CreateResponse(new()
				{
					[players[2].Id] = MainRoleType.SimpleVillager,
					[players[3].Id] = MainRoleType.SimpleVillager
				}))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		var recoveredReaction = new InteractiveSelectionReaction();
		recoveredReaction.Configure(
			initialVictimIds,
			players.Select(player => player.Id));
		var recoveredService = new GameService(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[
				new EliminationCascadeReactionBinding(
					recoveredReaction,
					EliminationCascadeReactionBoundary.Interactive)
			]);
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredPrompt = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		recoveredPrompt.InstructionId.Should().Be(
			reactionPrompt.InstructionId);
		recoveredPrompt.Semantic.Should().Be(reactionPrompt.Semantic);
		recoveredPrompt.SelectablePlayerIds.Should().BeEquivalentTo(
			reactionPrompt.SelectablePlayerIds);
		recoveredPrompt.AffectedPlayerIds.Should().BeEquivalentTo(
			reactionPrompt.AffectedPlayerIds);
		var reactionReveal = recoveredService.ProcessInstruction(
				recoveredGameId,
				recoveredPrompt.CreateResponse([reactionVictimId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		recoveredService.ProcessInstruction(
			recoveredGameId,
			reactionReveal.CreateResponse(new()
			{
				[reactionVictimId] = MainRoleType.SimpleVillager
			}));

		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		foreach (var initialVictimId in initialVictimIds)
		{
			recovered.GameHistoryLog
				.OfType<PlayerEliminatedLogEntry>()
				.Should().ContainSingle(entry =>
					entry.PlayerId == initialVictimId);
		}
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == reactionVictimId);
		recovered.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Where(entry =>
				entry.ReactionId == recoveredReaction.ReactionId)
			.Should().HaveCount(2);

		MarkTestCompleted();
	}

	[Fact]
	public void InteractiveReactionResponse_CapturesStableBoundaryAtItsNextWave()
	{
		var originalReaction = new InteractiveSelectionReaction();
		var (builder, players, initialReveal) =
			CreateConcurrentDawnScenario(
				originalReaction,
				EliminationCascadeReactionBoundary.Interactive,
				scenarioPlayers => originalReaction.Configure(
					[scenarioPlayers[2].Id, scenarioPlayers[3].Id],
					scenarioPlayers.Select(player => player.Id)));
		var initialVictimIds = new[]
		{
			players[2].Id,
			players[3].Id
		};
		var reactionVictimId = players[4].Id;
		var reactionPrompt = builder.Process(
				initialReveal.CreateResponse(new()
				{
					[players[2].Id] = MainRoleType.SimpleVillager,
					[players[3].Id] = MainRoleType.SimpleVillager
				}))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		reactionPrompt.Semantic.Should().Be(
			ModeratorInstructionSemantic.Unspecified);
		reactionPrompt.SelectablePlayerIds.Should()
			.NotContain(initialVictimIds);
		reactionPrompt.SelectablePlayerIds.Should().OnlyContain(playerId =>
			builder.GetGameState()!.GetPlayerState(playerId).Health ==
			PlayerHealth.Alive);
		var reactionReveal = builder.Process(
				reactionPrompt.CreateResponse([reactionVictimId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		var recoveredReaction = new InteractiveSelectionReaction();
		recoveredReaction.Configure(
			initialVictimIds,
			players.Select(player => player.Id));
		var recoveredService = new GameService(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[
				new EliminationCascadeReactionBinding(
					recoveredReaction,
					EliminationCascadeReactionBoundary.Interactive)
			]);
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredReveal = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<AssignRolesInstruction>().Subject;

		recoveredReveal.InstructionId.Should().Be(
			reactionReveal.InstructionId);
		recoveredReveal.PlayersForAssignment.Should().Equal(
			reactionVictimId);
		recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredReveal.CreateResponse(new()
			{
				[reactionVictimId] =
					MainRoleType.SimpleVillager
			}));

		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		foreach (var initialVictimId in initialVictimIds)
		{
			recovered.GameHistoryLog
				.OfType<PlayerEliminatedLogEntry>()
				.Should().ContainSingle(entry =>
					entry.PlayerId == initialVictimId);
		}
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == reactionVictimId);
		recovered.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Where(entry =>
				entry.ReactionId == recoveredReaction.ReactionId)
			.Should().HaveCount(2);

		MarkTestCompleted();
	}

	[Fact]
	public void InteractiveReactionWithNoLivingLegalTarget_EmitsNoSyntheticInstruction()
	{
		var reaction = new InteractiveSelectionReaction();
		var (builder, players, initialReveal) =
			CreateConcurrentDawnScenario(
				reaction,
				EliminationCascadeReactionBoundary.Interactive,
				scenarioPlayers => reaction.Configure(
					[scenarioPlayers[2].Id, scenarioPlayers[3].Id],
					[scenarioPlayers[2].Id, scenarioPlayers[3].Id]));

		var afterCascade = builder.Process(
			initialReveal.CreateResponse(new()
			{
				[players[2].Id] = MainRoleType.SimpleVillager,
				[players[3].Id] = MainRoleType.SimpleVillager
			}));

		afterCascade.ModeratorInstruction.Should()
			.NotBeOfType<SelectPlayersInstruction>();
		afterCascade.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		builder.GetGameState()!.GetCurrentPhase().Should().Be(
			GamePhase.Day);

		MarkTestCompleted();
	}

	private (
		GameTestBuilder Builder,
		IPlayer[] Players,
		AssignRolesInstruction InitialReveal)
		CreateDawnChainScenario(PlayerChainReaction reaction) =>
		CreateConcurrentDawnScenario(
			reaction,
			EliminationCascadeReactionBoundary.Forced,
			players => reaction.Configure(
				players[2].Id,
				players[3].Id,
				players[4].Id,
				players[5].Id));

	private (
		GameTestBuilder Builder,
		IPlayer[] Players,
		AssignRolesInstruction InitialReveal)
		CreateConcurrentDawnScenario(
			IEliminationCascadeReaction reaction,
			EliminationCascadeReactionBoundary boundary,
			Action<IPlayer[]> configureReaction)
	{
		var builder = CreateBuilder()
			.WithEliminationCascadeReaction(reaction, boundary)
			.WithPlayers(
				"Werewolf",
				"Witch",
				"Attack victim",
				"Poison victim",
				"Reaction victim",
				"Chained victim")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		configureReaction(players);

		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction(
			[players[0].Id],
			players[2].Id);

		var witchIdentification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var healing = builder.Process(
				witchIdentification.CreateResponse([players[1].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var poison = builder.Process(healing.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var sleep = builder.Process(poison.CreateResponse([players[3].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = builder.Process(sleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var initialReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		return (builder, players, initialReveal);
	}

	private sealed class PlayerChainReaction
		: IEliminationCascadeReaction
	{
		private Guid _attackVictimId;
		private Guid _poisonVictimId;
		private Guid _reactionVictimId;
		private Guid _chainedVictimId;

		public string ReactionId => nameof(PlayerChainReaction);

		internal void Configure(
			Guid attackVictimId,
			Guid poisonVictimId,
			Guid reactionVictimId,
			Guid chainedVictimId)
		{
			_attackVictimId = attackVictimId;
			_poisonVictimId = poisonVictimId;
			_reactionVictimId = reactionVictimId;
			_chainedVictimId = chainedVictimId;
		}

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input)
		{
			if (eliminatedPlayerIds.Contains(_attackVictimId) &&
				eliminatedPlayerIds.Contains(_poisonVictimId))
			{
				return EliminationCascadeReactionResult.Complete(
				[
					new(
						_reactionVictimId,
						EliminationReason.EventElimination),
					new(
						_reactionVictimId,
						EliminationReason.EventElimination)
				]);
			}

			return eliminatedPlayerIds.Contains(_reactionVictimId)
				? EliminationCascadeReactionResult.Complete(
				[
					new(
						_chainedVictimId,
						EliminationReason.EventElimination)
				])
				: EliminationCascadeReactionResult.Complete();
		}
	}

	private sealed class SingleWaveReaction(string? reactionId = null)
		: IEliminationCascadeReaction
	{
		private Guid _triggerPlayerId;
		private Guid _reactionVictimId;

		public string ReactionId { get; } =
			reactionId ?? nameof(SingleWaveReaction);

		internal void Configure(
			Guid triggerPlayerId,
			Guid reactionVictimId)
		{
			_triggerPlayerId = triggerPlayerId;
			_reactionVictimId = reactionVictimId;
		}

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input)
		{
			return eliminatedPlayerIds.Contains(_triggerPlayerId)
				? EliminationCascadeReactionResult.Complete(
					[
						new(
							_reactionVictimId,
							EliminationReason.EventElimination)
					])
				: EliminationCascadeReactionResult.Complete();
		}
	}

	private sealed class InteractiveSelectionReaction
		: IEliminationCascadeReaction
	{
		private const string Prompt = "Choose a living reaction target.";
		private HashSet<Guid> _triggerPlayerIds = [];
		private HashSet<Guid> _legalTargetIds = [];

		public string ReactionId => nameof(InteractiveSelectionReaction);

		internal void Configure(
			IEnumerable<Guid> triggerPlayerIds,
			IEnumerable<Guid> legalTargetIds)
		{
			_triggerPlayerIds = triggerPlayerIds.ToHashSet();
			_legalTargetIds = legalTargetIds.ToHashSet();
		}

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input)
		{
			if (!_triggerPlayerIds.SetEquals(eliminatedPlayerIds))
			{
				return EliminationCascadeReactionResult.Complete();
			}

			var selectablePlayerIds = _legalTargetIds
				.Where(playerId =>
					session.GetPlayerState(playerId).Health ==
					PlayerHealth.Alive)
				.ToHashSet();
			var pendingInstruction = RecoveryPayloadTestDriver
				.Capture(session)
				.PendingInstruction as SelectPlayersInstruction;
			var isAwaitingPendingSelection =
				pendingInstruction is
				{
					Semantic: ModeratorInstructionSemantic.Unspecified,
					CountConstraint: var countConstraint
				} &&
				countConstraint == NumberRangeConstraint.Single &&
				pendingInstruction.AffectedPlayerIds?
					.ToHashSet()
					.SetEquals(eliminatedPlayerIds) == true &&
				pendingInstruction.SelectablePlayerIds.SetEquals(
					selectablePlayerIds);
			if (!isAwaitingPendingSelection)
			{
				if (selectablePlayerIds.Count == 0)
				{
					return EliminationCascadeReactionResult.Complete();
				}

				var instruction = new SelectPlayersInstruction(
					ModeratorInstructionSemantic.Unspecified,
					selectablePlayerIds,
					NumberRangeConstraint.Single,
					publicAnnouncement: Prompt,
					affectedPlayerIds: eliminatedPlayerIds.ToArray());
				return EliminationCascadeReactionResult.NeedInput(
					instruction);
			}

			if (input.InstructionId != pendingInstruction!.InstructionId ||
				input.SelectedPlayerIds is not { Count: 1 })
			{
				throw new InvalidOperationException(
					"Interactive reaction received an uncorrelated response.");
			}

			var selectedPlayerId = input.SelectedPlayerIds.Single();
			if (!_legalTargetIds.Contains(selectedPlayerId) ||
				session.GetPlayerState(selectedPlayerId).Health !=
				PlayerHealth.Alive)
			{
				throw new InvalidOperationException(
					"Interactive reaction received an illegal target.");
			}

			return EliminationCascadeReactionResult.Complete(
				[
					new(
						selectedPlayerId,
						EliminationReason.EventElimination)
				]);
		}
	}
}
