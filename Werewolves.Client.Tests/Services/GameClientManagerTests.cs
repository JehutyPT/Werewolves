using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Services;

public class GameClientManagerTests
{
	[Fact]
	public void StagedRoleLockIn_ReplacementRecoveryAndLobbyExit_KeepOneDiscriminatedArtifact()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var lobby = CreateThiefLobby();
		var first = CreateThiefRoleLockIn(version: 1, rotateOffer1IntoDealPool: false);
		var second = CreateThiefRoleLockIn(version: 2, rotateOffer1IntoDealPool: true);
		var manager = new GameClientManager(new GameService(), saveStore: saveStore);
		var scenarioChanges = 0;
		lobby.SimulationScenarioChanged += (_, _) => scenarioChanges++;

		manager.TryReplaceStagedRoleLockIn(lobby, expectedCurrentVersion: 0, first)
			.Should().BeTrue();
		var firstPayload = saveStore.Load();
		ReadRecoveryKind(firstPayload).Should().Be("StagedLobby");
		lobby.AcceptedRoleLockIn.Should().BeSameAs(first);
		scenarioChanges.Should().Be(1);

		manager.TryReplaceStagedRoleLockIn(lobby, expectedCurrentVersion: 1, second)
			.Should().BeTrue();
		var secondPayload = saveStore.Load();
		secondPayload.Should().NotBe(firstPayload);
		ReadRecoveryKind(secondPayload).Should().Be("StagedLobby");
		lobby.AcceptedRoleLockIn.Should().BeSameAs(second);
		scenarioChanges.Should().Be(2);

		manager.TryReplaceStagedRoleLockIn(lobby, expectedCurrentVersion: 0, first)
			.Should().BeFalse();
		lobby.AcceptedRoleLockIn.Should().BeSameAs(second);
		saveStore.Load().Should().Be(secondPayload);
		scenarioChanges.Should().Be(2);

		var recoveredLobby = CreateThiefLobby(withPlayers: false);
		var recovered = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path),
			lobbySetupState: recoveredLobby);
		recovered.HasActiveSession.Should().BeFalse();
		recoveredLobby.PlayerNames.Should().Equal(PlayerNames.DefaultFive);
		recoveredLobby.AcceptedRoleLockIn.Should().NotBeNull();
		recoveredLobby.AcceptedRoleLockIn!.Version.Should().Be(2);
		recoveredLobby.AcceptedRoleLockIn.RoleComposition.Select(card => card.Id)
			.Should().Equal(second.RoleComposition.Select(card => card.Id));
		var finalSupportedLockIn = CreateSupportedRoleLockIn(version: 3);
		recovered.TryReplaceStagedRoleLockIn(
			recoveredLobby,
			expectedCurrentVersion: 2,
			finalSupportedLockIn).Should().BeTrue();

		recovered.StartGame(recoveredLobby);

		recovered.HasActiveSession.Should().BeTrue();
		ReadRecoveryKind(saveStore.Load()).Should().Be("ActiveGame");
		recovered.TryReplaceStagedRoleLockIn(
			recoveredLobby,
			expectedCurrentVersion: 3,
			CreateSupportedRoleLockIn(version: 4))
			.Should().BeFalse("Lobby Exit finalizes the latest Role Lock-In");
		ReadRecoveryKind(saveStore.Load()).Should().Be("ActiveGame");
	}

	[Fact]
	public void ActorSetupCards_ReplacementRecoveryAndStaleAttempt_KeepLatestExactArtifact()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var lobby = CreateActorLobby();
		var manager = new GameClientManager(new GameService(), saveStore: saveStore);
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var scenarioChanges = 0;
		lobby.SimulationScenarioChanged += (_, _) => scenarioChanges++;

		manager.TryReplaceStagedActorSetupCards(
			lobby,
			expectedCurrentVersion: 0,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]).Should().BeTrue();
		var first = lobby.AcceptedActorSetupCards;
		first.Version.Should().Be(1);
		first.Cards.Should().OnlyContain(card => card.Id != Guid.Empty);
		first.Cards.Select(card => card.Id).Should().OnlyHaveUniqueItems();
		var firstPayload = saveStore.Load();
		scenarioChanges.Should().Be(1);

		manager.TryReplaceStagedActorSetupCards(
			lobby,
			expectedCurrentVersion: 1,
			[
				MainRoleType.Seer,
				MainRoleType.Defender,
				MainRoleType.Elder
			]).Should().BeTrue();
		var latest = lobby.AcceptedActorSetupCards;
		latest.Version.Should().Be(2);
		latest.PrintedRoles.Should().Equal(
			MainRoleType.Seer,
			MainRoleType.Defender,
			MainRoleType.Elder);
		latest.Cards.Select(card => card.Id).Should().OnlyHaveUniqueItems();
		latest.Cards.Select(card => card.Id)
			.Intersect(first.Cards.Select(card => card.Id))
			.Should().BeEmpty();
		var latestPayload = saveStore.Load();
		latestPayload.Should().NotBe(firstPayload);
		scenarioChanges.Should().Be(2);

		manager.TryReplaceStagedActorSetupCards(
			lobby,
			expectedCurrentVersion: 0,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]).Should().BeFalse();
		lobby.AcceptedActorSetupCards.Should().BeSameAs(latest);
		saveStore.Load().Should().Be(latestPayload);
		scenarioChanges.Should().Be(2);

		var recoveredLobby = CreateActorLobby(withPlayers: false);
		var recovered = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path),
			lobbySetupState: recoveredLobby);

		recovered.HasActiveSession.Should().BeFalse();
		recoveredLobby.PlayerNames.Should().Equal(PlayerNames.DefaultFive);
		recoveredLobby.AcceptedActorSetupCards.Version.Should().Be(2);
		recoveredLobby.AcceptedActorSetupCards.Cards
			.Select(card => (card.Id, card.PrintedRole))
			.Should().Equal(latest.Cards
				.Select(card => (card.Id, card.PrintedRole)));
		recoveredLobby.RequiresActorSetupCards.Should().BeFalse();
		recoveredLobby.TryCreateSimulationScenario(out var scenario)
			.Should().BeTrue();
		scenario.ActorSetupCards.Should().Be(
			recoveredLobby.AcceptedActorSetupCards);
	}

	[Fact]
	public void Restart_ResumesActorThenManipulatorAtEachIncompleteAggregateGate()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var lobby = CreateActorAndPrejudicedManipulatorLobby();
		var manager = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var acceptedRoleLockIn = lobby.AcceptedRoleLockIn!;

		var actorGateLobby = CreateActorAndPrejudicedManipulatorLobby(
			withPlayers: false);
		var actorGateManager = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path),
			lobbySetupState: actorGateLobby);

		actorGateLobby.AcceptedRoleLockIn!.RoleComposition
			.Select(card => (card.Id, card.PrintedRole))
			.Should().Equal(acceptedRoleLockIn.RoleComposition
				.Select(card => (card.Id, card.PrintedRole)));
		actorGateLobby.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
		actorGateLobby.AcceptedPublicGroupPartition.Should().BeNull();
		actorGateLobby.RequiresActorSetupCards.Should().BeTrue();
		actorGateLobby.RequiresPublicGroupPartition.Should().BeFalse();
		actorGateLobby.TryCreateSimulationScenario(out _).Should().BeFalse();

		actorGateManager.TryReplaceStagedActorSetupCards(
			actorGateLobby,
			expectedCurrentVersion: 0,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]).Should().BeTrue();
		var acceptedActorSetupCards = actorGateLobby.AcceptedActorSetupCards;

		var manipulatorGateLobby = CreateActorAndPrejudicedManipulatorLobby(
			withPlayers: false);
		_ = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path),
			lobbySetupState: manipulatorGateLobby);

		manipulatorGateLobby.AcceptedActorSetupCards.Cards
			.Select(card => (card.Id, card.PrintedRole))
			.Should().Equal(acceptedActorSetupCards.Cards
				.Select(card => (card.Id, card.PrintedRole)));
		manipulatorGateLobby.AcceptedPublicGroupPartition.Should().BeNull();
		manipulatorGateLobby.RequiresActorSetupCards.Should().BeFalse();
		manipulatorGateLobby.RequiresPublicGroupPartition.Should().BeTrue();
		manipulatorGateLobby.TryCreateSimulationScenario(out _).Should().BeFalse();
	}

	[Fact]
	public void ActorSetupCards_WhenPersistenceFails_ChangesNeitherLobbyStoredBytesNorEvaluation()
	{
		var store = new ToggleThrowSaveStore();
		var lobby = CreateActorLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var stagedBytes = store.Load();
		var scenarioChanges = 0;
		lobby.SimulationScenarioChanged += (_, _) => scenarioChanges++;
		store.ThrowOnSave = true;

		manager.TryReplaceStagedActorSetupCards(
			lobby,
			expectedCurrentVersion: 0,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]).Should().BeFalse();

		lobby.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
		lobby.RequiresActorSetupCards.Should().BeTrue();
		store.Load().Should().Be(stagedBytes);
		scenarioChanges.Should().Be(0);
	}

	[Fact]
	public void RoleLockInReplacement_PreservesValidActorArtifactAndClearsOverlappingArtifact()
	{
		var store = new RecordingSaveStore();
		var lobby = CreateActorLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		manager.TryReplaceStagedActorSetupCards(
			lobby,
			expectedCurrentVersion: 0,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]).Should().BeTrue();
		var acceptedActorSetupCards = lobby.AcceptedActorSetupCards;
		var sameCompositionReplacement = RoleLockIn.CreateFromPrintedRoles(
			version: 2,
			playerCount: lobby.PlayerRoster.Count,
			lobby.GetSelectedRoles());

		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 1,
			sameCompositionReplacement).Should().BeTrue();

		lobby.AcceptedActorSetupCards.Should().BeSameAs(
			acceptedActorSetupCards);
		LocalRecoveryPayloadCodec.Deserialize(store.Load()!)
			.Should().BeOfType<StagedLobbyRecoveryPayload>()
			.Which.ActorSetupCards.Should().Be(acceptedActorSetupCards);

		lobby.DecrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.Cupid);
		var overlappingReplacement = RoleLockIn.CreateFromPrintedRoles(
			version: 3,
			playerCount: lobby.PlayerRoster.Count,
			lobby.GetSelectedRoles());

		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 2,
			overlappingReplacement).Should().BeTrue();

		lobby.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
		lobby.RequiresActorSetupCards.Should().BeTrue();
		LocalRecoveryPayloadCodec.Deserialize(store.Load()!)
			.Should().BeOfType<StagedLobbyRecoveryPayload>()
			.Which.ActorSetupCards.Should().Be(ActorSetupCards.None);
	}

	[Fact]
	public void RoleLockInReplacement_AddingActorClearsPartitionAndRecoversAtActorGate()
	{
		var store = new ToggleThrowSaveStore();
		var lobby = CreateActorAndPrejudicedManipulatorLobby();
		lobby.DecrementRole(MainRoleType.Actor);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		var manager = new GameClientManager(new GameService(), saveStore: store);
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var partition = PublicGroupPartition.Create(
			lobby.PlayerRoster.Select(player => player.Id),
			lobby.PlayerRoster.Take(2).Select(player => player.Id),
			lobby.PlayerRoster.Skip(2).Select(player => player.Id));
		manager.TryReplaceStagedPublicGroupPartition(lobby, partition)
			.Should().BeTrue();
		lobby.DecrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.Actor);
		var replacement = RoleLockIn.CreateFromPrintedRoles(
			version: 2,
			playerCount: lobby.PlayerRoster.Count,
			lobby.GetSelectedRoles());
		var scenarioChanges = 0;
		lobby.SimulationScenarioChanged += (_, _) => scenarioChanges++;

		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 1,
			replacement).Should().BeTrue();

		lobby.AcceptedRoleLockIn.Should().BeSameAs(replacement);
		lobby.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
		lobby.AcceptedPublicGroupPartition.Should().BeNull();
		lobby.RequiresActorSetupCards.Should().BeTrue();
		lobby.RequiresPublicGroupPartition.Should().BeFalse();
		scenarioChanges.Should().Be(1);
		var persisted = LocalRecoveryPayloadCodec.Deserialize(store.Load()!)
			.Should().BeOfType<StagedLobbyRecoveryPayload>().Which;
		persisted.RoleLockIn.Version.Should().Be(2);
		persisted.ActorSetupCards.Should().Be(ActorSetupCards.None);
		persisted.PublicGroupPartition.Should().BeNull();

		var recoveredLobby = CreateActorAndPrejudicedManipulatorLobby(withPlayers: false);
		var recovered = new GameClientManager(
			new GameService(),
			saveStore: store,
			lobbySetupState: recoveredLobby);

		recovered.HasActiveSession.Should().BeFalse();
		recoveredLobby.AcceptedRoleLockIn!.Version.Should().Be(2);
		recoveredLobby.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
		recoveredLobby.AcceptedPublicGroupPartition.Should().BeNull();
		recoveredLobby.RequiresActorSetupCards.Should().BeTrue();
		recoveredLobby.RequiresPublicGroupPartition.Should().BeFalse();
	}

	[Fact]
	public void RoleLockInReplacement_InvalidatingActorCardsClearsPartitionAtomicallyAndRecovers()
	{
		var store = new ToggleThrowSaveStore();
		var lobby = CreateActorAndPrejudicedManipulatorLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		manager.TryReplaceStagedActorSetupCards(
			lobby,
			expectedCurrentVersion: 0,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]).Should().BeTrue();
		var partition = PublicGroupPartition.Create(
			lobby.PlayerRoster.Select(player => player.Id),
			lobby.PlayerRoster.Take(2).Select(player => player.Id),
			lobby.PlayerRoster.Skip(2).Select(player => player.Id));
		manager.TryReplaceStagedPublicGroupPartition(lobby, partition)
			.Should().BeTrue();
		var acceptedRoleLockIn = lobby.AcceptedRoleLockIn;
		var acceptedActorSetupCards = lobby.AcceptedActorSetupCards;
		var acceptedPartition = lobby.AcceptedPublicGroupPartition;
		var acceptedBytes = store.Load();

		lobby.DecrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.Cupid);
		var replacement = RoleLockIn.CreateFromPrintedRoles(
			version: 2,
			playerCount: lobby.PlayerRoster.Count,
			lobby.GetSelectedRoles());
		var scenarioChanges = 0;
		lobby.SimulationScenarioChanged += (_, _) => scenarioChanges++;
		store.ThrowOnSave = true;

		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 1,
			replacement).Should().BeFalse();

		lobby.AcceptedRoleLockIn.Should().BeSameAs(acceptedRoleLockIn);
		lobby.AcceptedActorSetupCards.Should().BeSameAs(acceptedActorSetupCards);
		lobby.AcceptedPublicGroupPartition.Should().BeSameAs(acceptedPartition);
		store.Load().Should().Be(acceptedBytes);
		scenarioChanges.Should().Be(0);

		store.ThrowOnSave = false;
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 1,
			replacement).Should().BeTrue();

		lobby.AcceptedRoleLockIn.Should().BeSameAs(replacement);
		lobby.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
		lobby.AcceptedPublicGroupPartition.Should().BeNull();
		lobby.RequiresActorSetupCards.Should().BeTrue();
		lobby.RequiresPublicGroupPartition.Should().BeFalse();
		scenarioChanges.Should().Be(1);
		var persisted = LocalRecoveryPayloadCodec.Deserialize(store.Load()!)
			.Should().BeOfType<StagedLobbyRecoveryPayload>().Which;
		persisted.RoleLockIn.Version.Should().Be(2);
		persisted.ActorSetupCards.Should().Be(ActorSetupCards.None);
		persisted.PublicGroupPartition.Should().BeNull();

		var recoveredLobby = CreateActorAndPrejudicedManipulatorLobby(withPlayers: false);
		_ = new GameClientManager(
			new GameService(),
			saveStore: store,
			lobbySetupState: recoveredLobby);

		recoveredLobby.AcceptedRoleLockIn!.Version.Should().Be(2);
		recoveredLobby.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
		recoveredLobby.AcceptedPublicGroupPartition.Should().BeNull();
		recoveredLobby.RequiresActorSetupCards.Should().BeTrue();
		recoveredLobby.RequiresPublicGroupPartition.Should().BeFalse();
	}

	[Fact]
	public void PrintedRoleOffers_StageRecoverAndStartTheExactAcceptedPartition()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var lobby = CreateThiefLobby();
		lobby.IncrementRole(MainRoleType.Thief);
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 5; index++)
		{
			lobby.IncrementRole(MainRoleType.SimpleVillager);
		}
		var manager = new GameClientManager(new GameService(), saveStore: saveStore);

		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			offer1: MainRoleType.SimpleVillager,
			offer2: MainRoleType.SimpleVillager).Should().BeTrue();
		lobby.AcceptedRoleLockIn.Should().NotBeNull();
		var staged = lobby.AcceptedRoleLockIn!;
		staged.Offer1!.PrintedRole.Should().Be(MainRoleType.SimpleVillager);
		staged.Offer2!.PrintedRole.Should().Be(MainRoleType.SimpleVillager);
		staged.Offer1.Id.Should().NotBe(staged.Offer2.Id);

		var recoveredLobby = CreateThiefLobby(withPlayers: false);
		var recovered = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path),
			lobbySetupState: recoveredLobby);
		recoveredLobby.AcceptedRoleLockIn.Should().NotBeNull();
		var recoveredLockIn = recoveredLobby.AcceptedRoleLockIn!;
		recoveredLockIn.RoleComposition.Select(card => card.Id)
			.Should().Equal(staged.RoleComposition.Select(card => card.Id));

		recovered.StartGame(recoveredLobby);

		var published = recovered.CurrentSession!.RoleLockIn;
		published.RoleComposition.Select(card => card.Id)
			.Should().Equal(staged.RoleComposition.Select(card => card.Id));
		published.DealPool.Select(card => card.PrintedRole).Should().BeEquivalentTo(
			staged.DealPool.Select(card => card.PrintedRole));
		published.Offer1!.PrintedRole.Should().Be(MainRoleType.SimpleVillager);
		published.Offer2!.PrintedRole.Should().Be(MainRoleType.SimpleVillager);
		recovered.CurrentSession.GetModeratorPhysicalCharacterCards().Should().OnlyContain(
			card => card.OwnerPlayerId == null &&
				(card.Zone == PhysicalCharacterCardZone.DealPool ||
					card.Zone == PhysicalCharacterCardZone.Offer1 ||
					card.Zone == PhysicalCharacterCardZone.Offer2));
	}

	[Fact]
	public void EnsureStagedRoleLockIn_NonThiefManipulator_PersistsBeforePartitionSelection()
	{
		var store = new RecordingSaveStore();
		var lobby = CreatePrejudicedManipulatorLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);

		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();

		lobby.AcceptedRoleLockIn.Should().NotBeNull();
		lobby.AcceptedRoleLockIn!.Version.Should().Be(1);
		lobby.AcceptedRoleLockIn.RoleComposition.Should().Contain(
			card => card.PrintedRole == MainRoleType.PrejudicedManipulator);
		lobby.RequiresPublicGroupPartition.Should().BeTrue();
		lobby.AcceptedPublicGroupPartition.Should().BeNull();
		store.SavedPayloads.Select(ReadRecoveryKind)
			.Should().Equal("StagedLobby");
		manager.HasActiveSession.Should().BeFalse();
	}

	[Fact]
	public void EnsureStagedRoleLockIn_AfterStaleThiefBecomesNonThief_UsesNextVersion()
	{
		var store = new RecordingSaveStore();
		var lobby = CreateThiefLobby();
		lobby.IncrementRole(MainRoleType.Thief);
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 5; index++)
		{
			lobby.IncrementRole(MainRoleType.SimpleVillager);
		}
		var manager = new GameClientManager(new GameService(), saveStore: store);
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			offer1: MainRoleType.SimpleVillager,
			offer2: MainRoleType.SimpleVillager).Should().BeTrue();

		lobby.DecrementRole(MainRoleType.Thief);
		lobby.DecrementRole(MainRoleType.SimpleVillager);

		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();

		lobby.AcceptedRoleLockIn!.Version.Should().Be(2);
		lobby.AcceptedRoleLockIn.Offer1.Should().BeNull();
		lobby.AcceptedRoleLockIn.Offer2.Should().BeNull();
		store.SavedPayloads.Select(ReadRecoveryKind).Should().Equal(
			"StagedLobby",
			"StagedLobby");
	}

	[Fact]
	public void StartGame_AfterAcceptedThiefCompositionBecomesNonThief_ReplacesImplicitlyAtNextVersion()
	{
		var store = new RecordingSaveStore();
		var lobby = CreateThiefLobby();
		lobby.IncrementRole(MainRoleType.Thief);
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 5; index++)
		{
			lobby.IncrementRole(MainRoleType.SimpleVillager);
		}
		var manager = new GameClientManager(new GameService(), saveStore: store);
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			offer1: MainRoleType.SimpleVillager,
			offer2: MainRoleType.SimpleVillager).Should().BeTrue();
		var acceptedThiefLockIn = lobby.AcceptedRoleLockIn!;

		lobby.DecrementRole(MainRoleType.Thief);
		lobby.DecrementRole(MainRoleType.SimpleVillager);
		lobby.RequiresRoleLockIn.Should().BeTrue();

		manager.StartGame(lobby);

		manager.HasActiveSession.Should().BeTrue();
		store.SavedPayloads.Select(ReadRecoveryKind).Should().Equal(
			"StagedLobby",
			"StagedLobby",
			"ActiveGame");
		var replacement = lobby.AcceptedRoleLockIn!;
		replacement.Should().NotBeSameAs(acceptedThiefLockIn);
		replacement.Version.Should().Be(2);
		replacement.Offer1.Should().BeNull();
		replacement.Offer2.Should().BeNull();
		replacement.RoleComposition.Select(card => card.PrintedRole).Should().BeEquivalentTo(new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		});
		manager.CurrentSession!.RoleLockIn.RoleComposition.Select(card => card.Id)
			.Should().Equal(replacement.RoleComposition.Select(card => card.Id));
	}

	[Fact]
	public void PrintedRoleOffers_WhenStagedPersistenceFails_CommitNothing()
	{
		var lobby = CreateThiefLobby();
		lobby.IncrementRole(MainRoleType.Thief);
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 5; index++)
		{
			lobby.IncrementRole(MainRoleType.SimpleVillager);
		}
		var manager = new GameClientManager(
			new GameService(),
			saveStore: new ThrowingSaveStore());

		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			offer1: MainRoleType.SimpleVillager,
			offer2: MainRoleType.SimpleVillager).Should().BeFalse();

		lobby.AcceptedRoleLockIn.Should().BeNull();
		manager.StagedRoleLockIn.Should().BeNull();
		manager.HasActiveSession.Should().BeFalse();
	}

	[Fact]
	public void PublicGroupPartition_WhenPersistenceFails_ChangesNeitherLobbyNorStoredBytes()
	{
		var store = new ToggleThrowSaveStore();
		var lobby = CreatePrejudicedManipulatorLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: lobby.PlayerRoster.Count,
			lobby.GetSelectedRoles());
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			roleLockIn).Should().BeTrue();
		var stagedBytes = store.Load();
		var partition = PublicGroupPartition.Create(
			lobby.PlayerRoster.Select(player => player.Id),
			lobby.PlayerRoster.Take(2).Select(player => player.Id),
			lobby.PlayerRoster.Skip(2).Select(player => player.Id));
		store.ThrowOnSave = true;

		manager.TryReplaceStagedPublicGroupPartition(lobby, partition)
			.Should().BeFalse();

		lobby.AcceptedPublicGroupPartition.Should().BeNull();
		store.Load().Should().Be(stagedBytes);
	}

	[Fact]
	public void MoveStagedPlayerDown_WithAcceptedAggregate_PersistsExactReorderedRoster()
	{
		var store = new RecordingSaveStore();
		var lobby = CreatePrejudicedManipulatorLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: lobby.PlayerRoster.Count,
			lobby.GetSelectedRoles());
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			roleLockIn).Should().BeTrue();
		var partition = PublicGroupPartition.Create(
			lobby.PlayerRoster.Select(player => player.Id),
			lobby.PlayerRoster.Take(2).Select(player => player.Id),
			lobby.PlayerRoster.Skip(2).Select(player => player.Id));
		manager.TryReplaceStagedPublicGroupPartition(lobby, partition)
			.Should().BeTrue();
		var original = lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.ToArray();
		var expected = new[]
		{
			original[1],
			original[0],
			original[2],
			original[3],
			original[4]
		};
		var scenarioChanges = 0;
		lobby.SimulationScenarioChanged += (_, _) => scenarioChanges++;

		manager.TryMoveStagedPlayerDown(lobby, index: 0).Should().BeTrue();

		lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.Should().Equal(expected);
		lobby.AcceptedRoleLockIn.Should().BeSameAs(roleLockIn);
		lobby.AcceptedPublicGroupPartition.Should().BeSameAs(partition);
		scenarioChanges.Should().Be(1);
		var persisted = LocalRecoveryPayloadCodec.Deserialize(store.Load()!)
			.Should().BeOfType<StagedLobbyRecoveryPayload>()
			.Subject;
		persisted.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.Should().Equal(expected);
		persisted.RoleLockIn.RoleComposition.Select(card => card.Id)
			.Should().Equal(roleLockIn.RoleComposition.Select(card => card.Id));
		persisted.PublicGroupPartition.Should().Be(partition);
	}

	[Fact]
	public void MoveStagedPlayerUp_WhenPersistenceFails_ChangesNothing()
	{
		var store = new ToggleThrowSaveStore();
		var lobby = CreatePrejudicedManipulatorLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: lobby.PlayerRoster.Count,
			lobby.GetSelectedRoles());
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			roleLockIn).Should().BeTrue();
		var partition = PublicGroupPartition.Create(
			lobby.PlayerRoster.Select(player => player.Id),
			lobby.PlayerRoster.Take(2).Select(player => player.Id),
			lobby.PlayerRoster.Skip(2).Select(player => player.Id));
		manager.TryReplaceStagedPublicGroupPartition(lobby, partition)
			.Should().BeTrue();
		var originalRoster = lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.ToArray();
		var storedBytes = store.Load();
		store.ThrowOnSave = true;

		manager.TryMoveStagedPlayerUp(lobby, index: 1).Should().BeFalse();

		lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.Should().Equal(originalRoster);
		lobby.AcceptedRoleLockIn.Should().BeSameAs(roleLockIn);
		lobby.AcceptedPublicGroupPartition.Should().BeSameAs(partition);
		store.Load().Should().Be(storedBytes);
	}

	[Fact]
	public void MoveStagedPlayer_WithoutAcceptedLock_DelegatesAndRejectsInvalidMoves()
	{
		var store = new RecordingSaveStore();
		var lobby = CreateSupportedLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		var originalRoster = lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.ToArray();

		manager.TryMoveStagedPlayerDown(lobby, index: 0).Should().BeTrue();
		manager.TryMoveStagedPlayerUp(lobby, index: 1).Should().BeTrue();
		manager.TryMoveStagedPlayerUp(lobby, index: 0).Should().BeFalse();
		manager.TryMoveStagedPlayerDown(
			lobby,
			index: lobby.PlayerRoster.Count - 1).Should().BeFalse();

		lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.Should().Equal(originalRoster);
		store.SavedPayloads.Should().BeEmpty();
	}

	[Fact]
	public void AddStagedPlayer_AfterAcceptedAggregate_ClearsObsoleteRecovery()
	{
		var store = new RecordingSaveStore();
		var lobby = CreatePrejudicedManipulatorLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: lobby.PlayerRoster.Count,
			lobby.GetSelectedRoles());
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			roleLockIn).Should().BeTrue();
		var partition = PublicGroupPartition.Create(
			lobby.PlayerRoster.Select(player => player.Id),
			lobby.PlayerRoster.Take(2).Select(player => player.Id),
			lobby.PlayerRoster.Skip(2).Select(player => player.Id));
		manager.TryReplaceStagedPublicGroupPartition(lobby, partition)
			.Should().BeTrue();
		var originalIds = lobby.PlayerRoster.Select(player => player.Id).ToArray();

		manager.TryAddStagedPlayer(lobby, "Fátima", out var result)
			.Should().BeTrue();

		result.Should().Be(AddPlayerResult.Success);
		lobby.PlayerRoster.Select(player => player.Id).Take(originalIds.Length)
			.Should().Equal(originalIds);
		lobby.PlayerRoster.Should().HaveCount(6);
		var addedPlayer = lobby.PlayerRoster[^1];
		addedPlayer.Name.Should().Be("Fátima");
		addedPlayer.Id.Should().NotBeEmpty();
		originalIds.Should().NotContain(addedPlayer.Id);
		lobby.RequiresRoleLockIn.Should().BeTrue();
		lobby.AcceptedPublicGroupPartition.Should().BeNull();
		manager.StagedRoleLockIn.Should().BeNull();
		store.Load().Should().BeNull();

		var recoveredLobby = CreatePrejudicedManipulatorLobby(withPlayers: false);
		_ = new GameClientManager(
			new GameService(),
			saveStore: store,
			lobbySetupState: recoveredLobby);
		recoveredLobby.PlayerRoster.Should().BeEmpty();
		recoveredLobby.AcceptedRoleLockIn.Should().BeNull();
	}

	[Fact]
	public void RemoveStagedPlayer_AfterAcceptedAggregate_ClearsObsoleteRecovery()
	{
		var store = new RecordingSaveStore();
		var lobby = CreatePrejudicedManipulatorLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: lobby.PlayerRoster.Count,
			lobby.GetSelectedRoles());
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			roleLockIn).Should().BeTrue();
		var partition = PublicGroupPartition.Create(
			lobby.PlayerRoster.Select(player => player.Id),
			lobby.PlayerRoster.Take(2).Select(player => player.Id),
			lobby.PlayerRoster.Skip(2).Select(player => player.Id));
		manager.TryReplaceStagedPublicGroupPartition(lobby, partition)
			.Should().BeTrue();
		var originalIds = lobby.PlayerRoster.Select(player => player.Id).ToArray();
		var removedId = originalIds[2];

		manager.TryRemoveStagedPlayer(lobby, index: 2).Should().BeTrue();

		lobby.PlayerRoster.Select(player => player.Id).Should().Equal(
			originalIds.Where(playerId => playerId != removedId));
		lobby.PlayerRoster.Select(player => player.Id).Should().NotContain(removedId);
		lobby.RequiresRoleLockIn.Should().BeTrue();
		lobby.AcceptedPublicGroupPartition.Should().BeNull();
		manager.StagedRoleLockIn.Should().BeNull();
		store.Load().Should().BeNull();

		var recoveredLobby = CreatePrejudicedManipulatorLobby(withPlayers: false);
		_ = new GameClientManager(
			new GameService(),
			saveStore: store,
			lobbySetupState: recoveredLobby);
		recoveredLobby.PlayerRoster.Should().BeEmpty();
		recoveredLobby.AcceptedRoleLockIn.Should().BeNull();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void RosterMembershipEdit_WhenRecoveryClearFails_ChangesNothing(
		bool addPlayer)
	{
		var store = new ToggleThrowSaveStore();
		var lobby = CreatePrejudicedManipulatorLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: lobby.PlayerRoster.Count,
			lobby.GetSelectedRoles());
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			roleLockIn).Should().BeTrue();
		var partition = PublicGroupPartition.Create(
			lobby.PlayerRoster.Select(player => player.Id),
			lobby.PlayerRoster.Take(2).Select(player => player.Id),
			lobby.PlayerRoster.Skip(2).Select(player => player.Id));
		manager.TryReplaceStagedPublicGroupPartition(lobby, partition)
			.Should().BeTrue();
		var originalRoster = lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.ToArray();
		var storedBytes = store.Load();
		store.ThrowOnClear = true;

		var changed = addPlayer
			? manager.TryAddStagedPlayer(lobby, "Fátima", out _)
			: manager.TryRemoveStagedPlayer(lobby, index: 2);

		changed.Should().BeFalse();
		lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.Should().Equal(originalRoster);
		lobby.AcceptedRoleLockIn.Should().BeSameAs(roleLockIn);
		lobby.AcceptedPublicGroupPartition.Should().BeSameAs(partition);
		manager.StagedRoleLockIn.Should().BeSameAs(roleLockIn);
		store.Load().Should().Be(storedBytes);
	}

	[Fact]
	public void InvalidRosterMembershipEdits_ClearNothingAndChangeNothing()
	{
		var store = new RecordingSaveStore();
		var lobby = CreateSupportedLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var originalRoster = lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.ToArray();
		var storedBytes = store.Load();

		manager.TryAddStagedPlayer(lobby, "  ", out var emptyResult)
			.Should().BeFalse();
		manager.TryAddStagedPlayer(lobby, "ana", out var duplicateResult)
			.Should().BeFalse();
		manager.TryRemoveStagedPlayer(lobby, index: -1).Should().BeFalse();
		manager.TryRemoveStagedPlayer(lobby, lobby.PlayerRoster.Count)
			.Should().BeFalse();

		emptyResult.Should().Be(AddPlayerResult.EmptyName);
		duplicateResult.Should().Be(AddPlayerResult.DuplicateName);
		lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.Should().Equal(originalRoster);
		store.Load().Should().Be(storedBytes);
	}

	[Fact]
	public void Restart_RestoresExactRosterRoleLockInAndPublicGroupPartition()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var lobby = CreatePrejudicedManipulatorLobby();
		var originalRoster = lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.ToArray();
		var manager = new GameClientManager(new GameService(), saveStore: saveStore);
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: lobby.PlayerRoster.Count,
			lobby.GetSelectedRoles());
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			roleLockIn).Should().BeTrue();
		var partition = PublicGroupPartition.Create(
			lobby.PlayerRoster.Select(player => player.Id),
			lobby.PlayerRoster.Take(2).Select(player => player.Id),
			lobby.PlayerRoster.Skip(2).Select(player => player.Id));
		manager.TryReplaceStagedPublicGroupPartition(lobby, partition)
			.Should().BeTrue();

		var recoveredLobby = CreatePrejudicedManipulatorLobby(withPlayers: false);
		var recovered = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path),
			lobbySetupState: recoveredLobby);
		recoveredLobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.Should().Equal(originalRoster);
		recoveredLobby.AcceptedPublicGroupPartition.Should().Be(partition);

		recoveredLobby.AcceptedRoleLockIn!.RoleComposition
			.Select(card => (card.Id, card.PrintedRole))
			.Should().Equal(roleLockIn.RoleComposition
				.Select(card => (card.Id, card.PrintedRole)));
	}

	[Fact]
	public void RestartThenLobbyExit_PreservesExactActivePlayerIdentities()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var lobby = CreateSupportedLobby();
		var originalRoster = lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.ToArray();
		var manager = new GameClientManager(new GameService(), saveStore: saveStore);
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();

		var recoveredLobby = CreateSupportedLobby(withPlayers: false);
		var recovered = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path),
			lobbySetupState: recoveredLobby);

		recovered.StartGame(recoveredLobby);

		recovered.CurrentSession!.GetPlayers()
			.Select(player => (player.Id, player.Name))
			.Should().Equal(originalRoster);
	}

	[Fact]
	public void PartialRoleEdit_BlocksExitButPreservesLastAcceptedStagedBytes()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var lobby = CreateThiefLobby();
		var staged = CreateThiefRoleLockIn(
			version: 1,
			rotateOffer1IntoDealPool: false);
		var manager = new GameClientManager(new GameService(), saveStore: saveStore);
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			staged).Should().BeTrue();
		var stagedPayload = saveStore.Load();

		lobby.DecrementRole(MainRoleType.SimpleVillager);

		lobby.AcceptedRoleLockIn.Should().BeSameAs(staged);
		saveStore.Load().Should().Be(stagedPayload);
		var exit = () => manager.StartGame(lobby);
		exit.Should().Throw<InvalidOperationException>()
			.WithMessage("*fresh accepted Role Lock-In*");
		manager.HasActiveSession.Should().BeFalse();
		saveStore.Load().Should().Be(stagedPayload);
	}

	[Fact]
	public void StartGame_WhenActiveGameOverwriteFails_RetainsStagedLobbyAndPublishesNoSession()
	{
		var store = new ToggleThrowSaveStore();
		var lobby = CreateThiefLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		var staged = CreateSupportedRoleLockIn(version: 1);
		manager.TryReplaceStagedRoleLockIn(lobby, expectedCurrentVersion: 0, staged)
			.Should().BeTrue();
		var stagedPayload = store.Load();
		store.ThrowOnSave = true;

		var act = () => manager.StartGame(lobby);

		act.Should().Throw<IOException>();
		manager.HasActiveSession.Should().BeFalse();
		manager.ActiveGameId.Should().BeNull();
		manager.CurrentSession.Should().BeNull();
		store.Load().Should().Be(stagedPayload);
		ReadRecoveryKind(store.Load()).Should().Be("StagedLobby");
		store.ThrowOnSave = false;
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 1,
			CreateSupportedRoleLockIn(version: 2)).Should().BeTrue(
				"a failed Lobby Exit must not finalize the staged Role Lock-In");
	}

	[Fact]
	public void StartGame_FromUnstagedLobby_PersistsFinalRoleLockInBeforeActiveGame()
	{
		var store = new RecordingSaveStore();
		var lobby = CreateSupportedLobby();
		var manager = new GameClientManager(new GameService(), saveStore: store);

		manager.StartGame(lobby);

		store.SavedPayloads.Select(ReadRecoveryKind).Should().Equal(
			"StagedLobby",
			"ActiveGame");
		lobby.AcceptedRoleLockIn.Should().NotBeNull();
		manager.CurrentSession!.RoleLockIn.RoleComposition
			.Select(card => card.Id)
			.Should().Equal(lobby.AcceptedRoleLockIn!.RoleComposition
				.Select(card => card.Id));
	}

	[Fact]
	public void StartGame_FromLobbyConfiguration_CreatesCoreSessionAndExposesInstruction()
	{
		var manager = new GameClientManager();
		var players = PlayerNames.DefaultFive;
		var roles = new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};

		var instruction = manager.StartGame(players, roles);

		instruction.Should().BeOfType<StartGameConfirmationInstruction>();
		manager.HasActiveSession.Should().BeTrue();
		manager.ActiveGameId.Should().Be(instruction.GameGuid);
		manager.CurrentInstruction.Should().Be(instruction);
		manager.CurrentSession.Should().NotBeNull();
		manager.CurrentSession!.GetPlayers().Select(p => p.Name).Should().Equal(players);
		manager.CurrentSession.RoleInPlayCount(MainRoleType.SimpleWerewolf).Should().Be(1);
		manager.CurrentSession.RoleInPlayCount(MainRoleType.Seer).Should().Be(1);
		manager.CurrentSession.RoleInPlayCount(MainRoleType.SimpleVillager).Should().Be(3);
	}

	[Fact]
	public void ProcessInput_ForCurrentInstruction_AdvancesCurrentInstruction()
	{
		var manager = new GameClientManager();
		var startInstruction = StartSimpleGame(manager);

		var result = manager.ProcessInput(startInstruction.CreateResponse());

		result.IsSuccess.Should().BeTrue();
		result.ModeratorInstruction.Should().NotBeNull();
		manager.CurrentInstruction.Should().Be(result.ModeratorInstruction);
		manager.CurrentInstruction.Should().NotBe(startInstruction);
		manager.CurrentPhase.Should().Be(GamePhase.Night);
		manager.TurnNumber.Should().Be(1);
	}

	[Fact]
	public void ProcessInput_WhenVictoryFactsAreNotReady_PreservesPublishedSessionReference()
	{
		var service = new GameService();
		var manager = new GameClientManager(service);
		var startInstruction = manager.StartGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var publishedSession = manager.CurrentSession!;
		var mutableSession = (GameSession)publishedSession;
		var players = publishedSession.GetPlayers().ToArray();
		mutableSession.EliminatePlayer(
			players[0].Id,
			EliminationReason.EventElimination);
		var boundary = new FactionFactEffectiveBoundary(
			publishedSession.TurnNumber,
			publishedSession.GetCurrentPhase(),
			publishedSession.GameHistoryLog.Count());
		mutableSession.CommitFactionFactBatch(context =>
			new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ExplicitTransition,
					"test-client-incomplete-closure"),
				Facts = players
					.Skip(1)
					.Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						FactionAgentKnowledge.KnownNonAgent,
						boundary))
					.ToImmutableArray()
			});
		manager.ProcessInput(startInstruction.CreateResponse());
		var startNight = manager.CurrentInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = manager.ProcessInput(startNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		var gameId = manager.ActiveGameId!.Value;
		var stableBefore = publishedSession.Serialize();
		var phaseBefore = publishedSession.GetCurrentPhase();
		var historyCountBefore = publishedSession.GameHistoryLog.Count();
		var transitionCountBefore = publishedSession.GameHistoryLog
			.OfType<PhaseTransitionLogEntry>()
			.Count();
		var response = finishNight.CreateResponse();
		var reachVictoryCheckWindow = () => manager.ProcessInput(response);

		reachVictoryCheckWindow.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("Required Faction facts are not ready.");
		AssertPublishedSessionRemainsCoherent();
		reachVictoryCheckWindow.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("Required Faction facts are not ready.");
		AssertPublishedSessionRemainsCoherent();

		void AssertPublishedSessionRemainsCoherent()
		{
			manager.CurrentSession.Should().BeSameAs(publishedSession);
			service.GetGameStateView(gameId).Should().BeSameAs(publishedSession);
			manager.CurrentPhase.Should().Be(phaseBefore);
			publishedSession.Serialize().Should().Be(stableBefore);
			publishedSession.GameHistoryLog.Should().HaveCount(historyCountBefore);
			publishedSession.GameHistoryLog.OfType<PhaseTransitionLogEntry>()
				.Should().HaveCount(transitionCountBefore);
			service.GetCurrentInstruction(gameId)!.InstructionId
				.Should().Be(finishNight.InstructionId);
			manager.CurrentInstruction.Should().BeSameAs(finishNight);
		}
	}

	[Fact]
	public void ProcessInput_SuccessfulInput_WritesSingleActiveGameSaveFile()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var manager = new GameClientManager(new GameService(), saveStore: saveStore);
		var startInstruction = StartSimpleGame(manager);

		manager.ProcessInput(startInstruction.CreateResponse());

		var saveFiles = Directory.GetFiles(saveDirectory.Path);
		saveFiles.Should().ContainSingle();
		var payload = File.ReadAllText(saveFiles.Single());
		ReadRecoveryKind(payload).Should().Be("ActiveGame");
		ReadActiveGameSerializedSession(payload).Should().Be(
			manager.CurrentSession!.Serialize());
	}

	[Fact]
	public void ProcessInput_SuccessfulInput_OverwritesExistingSaveFile()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var manager = new GameClientManager(new GameService(), saveStore: saveStore);
		var startInstruction = StartSimpleGame(manager);
		var saveFilePath = Path.Combine(saveDirectory.Path, FileGameSessionSaveStore.SaveFileName);
		File.WriteAllText(saveFilePath, "stale save data");

		manager.ProcessInput(startInstruction.CreateResponse());

		Directory.GetFiles(saveDirectory.Path).Should().ContainSingle();
		var payload = File.ReadAllText(saveFilePath);
		ReadRecoveryKind(payload).Should().Be("ActiveGame");
		ReadActiveGameSerializedSession(payload).Should().Be(
			manager.CurrentSession!.Serialize());
	}

	[Fact]
	public void Save_OverwritesExistingSaveFileAndRemovesTemporaryWriteArtifacts()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var saveFilePath = Path.Combine(saveDirectory.Path, FileGameSessionSaveStore.SaveFileName);
		var staleTempPath = Path.Combine(
			saveDirectory.Path,
			$"{FileGameSessionSaveStore.SaveFileName}.stale.tmp");
		File.WriteAllText(saveFilePath, "stale save data");
		File.WriteAllText(staleTempPath, "left over from interrupted write");

		saveStore.Save("fresh save data");

		File.ReadAllText(saveFilePath).Should().Be("fresh save data");
		Directory.GetFiles(saveDirectory.Path).Should().ContainSingle(path => path == saveFilePath);
		Directory.GetFiles(saveDirectory.Path, $"{FileGameSessionSaveStore.SaveFileName}.*.tmp")
			.Should().BeEmpty();
	}

	[Fact]
	public void Clear_RemovesSaveFileAndTemporaryWriteArtifacts()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var saveFilePath = Path.Combine(saveDirectory.Path, FileGameSessionSaveStore.SaveFileName);
		var staleTempPath = Path.Combine(
			saveDirectory.Path,
			$"{FileGameSessionSaveStore.SaveFileName}.stale.tmp");
		File.WriteAllText(saveFilePath, "stale save data");
		File.WriteAllText(staleTempPath, "left over from interrupted write");

		saveStore.Clear();

		Directory.GetFiles(saveDirectory.Path).Should().BeEmpty();
	}

	[Fact]
	public void Constructor_WhenSaveFileExists_RehydratesSessionAndCurrentInstruction()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var manager = new GameClientManager(new GameService(), saveStore: saveStore);
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse());
		var savedGameId = manager.ActiveGameId;
		var savedPhase = manager.CurrentPhase;

		var resumed = new GameClientManager(new GameService(), saveStore: new FileGameSessionSaveStore(saveDirectory.Path));

		resumed.HasActiveSession.Should().BeTrue();
		resumed.ActiveGameId.Should().Be(savedGameId);
		resumed.CurrentSession.Should().NotBeNull();
		resumed.CurrentInstruction.Should().NotBeNull();
		resumed.CurrentPhase.Should().Be(savedPhase);
	}

	[Fact]
	public void ProcessInput_AfterResume_ContinuesFromRestoredInstruction()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var manager = new GameClientManager(new GameService(), saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse());
		var resumed = new GameClientManager(new GameService(), saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		var restoredInstruction = resumed.CurrentInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;

		var result = resumed.ProcessInput(restoredInstruction.CreateResponse());

		result.IsSuccess.Should().BeTrue();
		resumed.CurrentInstruction.Should().Be(result.ModeratorInstruction);
		resumed.CurrentInstruction.Should().NotBe(restoredInstruction);
		resumed.HasActiveSession.Should().BeTrue();
	}

	[Fact]
	public void Constructor_WhenSaveFileIsCorrupted_DoesNotThrowAndClearsSave()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveFilePath = Path.Combine(saveDirectory.Path, FileGameSessionSaveStore.SaveFileName);
		File.WriteAllText(saveFilePath, "not valid session json");

		var act = () => new GameClientManager(new GameService(), saveStore: new FileGameSessionSaveStore(saveDirectory.Path));

		var manager = act.Should().NotThrow().Subject;
		manager.HasActiveSession.Should().BeFalse();
		File.Exists(saveFilePath).Should().BeFalse();
	}

	[Fact]
	public void Constructor_WhenSaveFileIsRawCoreSession_DoesNotThrowAndClearsSave()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveFilePath = Path.Combine(
			saveDirectory.Path,
			FileGameSessionSaveStore.SaveFileName);
		var manager = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		StartSimpleGame(manager);
		File.WriteAllText(saveFilePath, manager.CurrentSession!.Serialize());

		var act = () => new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path));

		var resumed = act.Should().NotThrow().Subject;
		resumed.HasActiveSession.Should().BeFalse();
		File.Exists(saveFilePath).Should().BeFalse();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Constructor_WhenRecoveryEnvelopeUsesSchema1_ClearsObsoleteArtifact(
		bool stagedLobbyPayload)
	{
		var store = new RecordingSaveStore();
		var payload = stagedLobbyPayload
			? CreateSchema1StagedLobbyPayload()
			: CreateSchema1ActiveGamePayload(CreateValidSerializedCoreSession());
		store.Save(payload);
		var lobby = CreateSupportedLobby(withPlayers: false);

		var manager = new GameClientManager(
			new GameService(),
			saveStore: store,
			lobbySetupState: lobby);

		manager.HasActiveSession.Should().BeFalse();
		manager.StagedRoleLockIn.Should().BeNull();
		lobby.PlayerRoster.Should().BeEmpty();
		lobby.AcceptedRoleLockIn.Should().BeNull();
		store.Load().Should().BeNull();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Constructor_WhenRecoveryEnvelopeUsesSchema2_ClearsObsoleteArtifact(
		bool stagedLobbyPayload)
	{
		var store = new RecordingSaveStore();
		var sourceLobby = CreateSupportedLobby();
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: sourceLobby.PlayerRoster.Count,
			sourceLobby.GetSelectedRoles());
		var schema3Payload = stagedLobbyPayload
			? LocalRecoveryPayloadCodec.SerializeStagedLobby(
				sourceLobby.PlayerRoster,
				roleLockIn,
				ActorSetupCards.None,
				publicGroupPartition: null)
			: LocalRecoveryPayloadCodec.SerializeActiveGame(
				CreateValidSerializedCoreSession());
		var obsoleteEnvelope = JsonNode.Parse(schema3Payload)!;
		obsoleteEnvelope["schemaVersion"] = 2;
		if (stagedLobbyPayload)
		{
			obsoleteEnvelope["stagedLobby"]!.AsObject()
				.Remove("actorSetupCards");
		}
		store.Save(obsoleteEnvelope.ToJsonString());
		var lobby = CreateSupportedLobby(withPlayers: false);

		var manager = new GameClientManager(
			new GameService(),
			saveStore: store,
			lobbySetupState: lobby);

		manager.HasActiveSession.Should().BeFalse();
		manager.StagedRoleLockIn.Should().BeNull();
		lobby.PlayerRoster.Should().BeEmpty();
		lobby.AcceptedRoleLockIn.Should().BeNull();
		lobby.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
		store.Load().Should().BeNull();
	}

	[Fact]
	public void Constructor_WhenStagedRecoveryFailsSemanticValidation_PublishesNothing()
	{
		var store = new RecordingSaveStore();
		var sourceLobby = CreateSupportedLobby();
		var sourceManager = new GameClientManager(
			new GameService(),
			saveStore: store);
		sourceManager.TryEnsureStagedRoleLockIn(sourceLobby).Should().BeTrue();
		var incompatibleLobby = CreateThiefLobby(withPlayers: false);

		var manager = new GameClientManager(
			new GameService(),
			saveStore: store,
			lobbySetupState: incompatibleLobby);

		manager.StagedRoleLockIn.Should().BeNull();
		manager.HasActiveSession.Should().BeFalse();
		incompatibleLobby.PlayerRoster.Should().BeEmpty();
		incompatibleLobby.AcceptedRoleLockIn.Should().BeNull();
		store.Load().Should().BeNull();
	}

	[Theory]
	[InlineData("StagedLobby", true, true)]
	[InlineData("ActiveGame", true, true)]
	[InlineData("StagedLobby", false, true)]
	[InlineData("ActiveGame", true, false)]
	[InlineData("StagedLobby", false, false)]
	[InlineData("ActiveGame", false, false)]
	public void Constructor_WhenSchema3EnvelopeIsNotExactUnion_ClearsArtifact(
		string kind,
		bool includeStagedLobby,
		bool includeActiveGame)
	{
		var store = new RecordingSaveStore();
		store.Save(CreateInvalidSchema3UnionPayload(
			kind,
			includeStagedLobby,
			includeActiveGame));
		var lobby = CreateSupportedLobby(withPlayers: false);

		var manager = new GameClientManager(
			new GameService(),
			saveStore: store,
			lobbySetupState: lobby);

		manager.StagedRoleLockIn.Should().BeNull();
		manager.HasActiveSession.Should().BeFalse();
		lobby.PlayerRoster.Should().BeEmpty();
		lobby.AcceptedRoleLockIn.Should().BeNull();
		store.Load().Should().BeNull();
	}

	[Fact]
	public void StartGame_WhenSaveFileExists_ReplacesItWithActiveGamePayload()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveFilePath = Path.Combine(saveDirectory.Path, FileGameSessionSaveStore.SaveFileName);
		var manager = new GameClientManager(new GameService(), saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse());
		File.Exists(saveFilePath).Should().BeTrue();

		StartSimpleGame(manager);

		File.Exists(saveFilePath).Should().BeTrue();
		ReadRecoveryKind(File.ReadAllText(saveFilePath)).Should().Be("ActiveGame");
	}

	[Fact]
	public void ProcessInput_WhenVictoryInstructionIsReached_RetainsSaveUntilLocalDismissal()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveFilePath = Path.Combine(saveDirectory.Path, FileGameSessionSaveStore.SaveFileName);
		var manager = new GameClientManager(new GameService(), saveStore: new FileGameSessionSaveStore(saveDirectory.Path));

		PlayToWerewolfVictoryAtDawn(manager);

		var finished = manager.CurrentInstruction.Should()
			.BeOfType<FinishedGameConfirmationInstruction>().Subject;
		File.Exists(saveFilePath).Should().BeTrue();

		var resumedService = new GameService();
		var resumed = new GameClientManager(
			resumedService,
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		var resumedFinished = resumed.CurrentInstruction.Should()
			.BeOfType<FinishedGameConfirmationInstruction>().Subject;
		resumedFinished.GameResult.Should().Be(finished.GameResult);
		resumedFinished.VictoryCheckWindow.Should().Be(finished.VictoryCheckWindow);
		resumed.ActiveGameId.Should().HaveValue();
		var resumedGameId = resumed.ActiveGameId!.Value;
		var terminalSession = resumed.CurrentSession!;
		var terminalSnapshot = terminalSession.Serialize();
		resumedService.GetGameStateView(resumedGameId).Should().BeSameAs(terminalSession);

		resumed.ClearSession();

		resumedService.GetGameStateView(resumedGameId).Should().BeNull();
		terminalSession.Serialize().Should().Be(terminalSnapshot);
		File.Exists(saveFilePath).Should().BeFalse();
	}

	[Fact]
	public void PlayToDawn_DeterministicRoleReveal_ProjectsVictimAsPublic()
	{
		var manager = new GameClientManager();
		var startInstruction = manager.StartGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		manager.ProcessInput(startInstruction.CreateResponse());
		var players = manager.CurrentSession!.GetPlayers().ToList();
		var werewolfIds = players.Take(2).Select(p => p.Id).ToHashSet();
		var victimId = players[2].Id;

		ConfirmCurrentInstruction(manager);
		SelectCurrentPlayers(manager, werewolfIds);
		SelectCurrentPlayers(manager, [victimId]);
		ConfirmCurrentInstruction(manager);
		ConfirmCurrentInstruction(manager);
		AssignCurrentRoles(manager, MainRoleType.SimpleVillager);

		var roster = manager.CurrentRoster;
		var victimEntry = roster.Should().ContainSingle(r => r.PlayerId == victimId).Which;
		victimEntry.Name.Should().Be(players[2].Name);
		victimEntry.RoleLabel.Should().Be(MainRoleType.SimpleVillager.GetPublicName());
		victimEntry.RoleVisibility.Should().Be(DashboardRoleVisibility.Public);
	}

	[Fact]
	public void ProcessInput_WhenSaveFails_DoesNotThrowAndKeepsGameProgress()
	{
		var manager = new GameClientManager(new GameService(), saveStore: new ThrowingSaveStore());
		var startInstruction = StartSimpleGame(manager);

		var act = () => manager.ProcessInput(startInstruction.CreateResponse());

		act.Should().NotThrow();
		manager.HasActiveSession.Should().BeTrue();
		manager.CurrentInstruction.Should().NotBe(startInstruction);
	}

	[Fact]
	public void ProcessInput_AcceptedWerewolfAgentObservation_PersistsFactWithoutLaterNightAction()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var manager = new GameClientManager(new GameService(), saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse());
		ConfirmCurrentInstruction(manager);
		var players = manager.CurrentSession!.GetPlayers().ToList();

		SelectCurrentPlayers(manager, [players[0].Id]);
		var instructionAfterAcceptedObservation = manager.CurrentInstruction!;
		SelectCurrentPlayers(manager, [players[4].Id]);

		manager.CurrentSession.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Source.Kind == FactionFactSourceKind.ScheduledObservation);
		manager.CurrentSession.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().BeEmpty();
		manager.CurrentSession.GameHistoryLog.OfType<NightActionLogEntry>().Should().NotBeEmpty();
		var resumed = new GameClientManager(new GameService(), saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		var savedSession = resumed.CurrentSession!;
		savedSession.GetCurrentPhase().Should().Be(GamePhase.Night);
		savedSession.TurnNumber.Should().Be(1);
		var observationEntry = savedSession.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Source.Kind == FactionFactSourceKind.ScheduledObservation)
			.Subject;
		observationEntry.Facts.Should().ContainSingle(fact =>
			fact.PlayerId == players[0].Id &&
			fact.Type == FactionFactType.Agent &&
			fact.Faction == Faction.Werewolf &&
			fact.AgentKnowledge == FactionAgentKnowledge.KnownAgent);
		savedSession.GameHistoryLog.OfType<NightActionLogEntry>().Should().BeEmpty();
		savedSession.GetPlayer(players[0].Id).State.ModeratorKnownRole
			.Should().BeNull();
		savedSession.GetFactionAgentKnowledge(players[0].Id, Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		savedSession.GetFactionAgentKnowledge(players[1].Id, Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		resumed.CurrentInstruction!.GetType().Should().Be(instructionAfterAcceptedObservation.GetType());
		resumed.CurrentInstruction.InstructionId.Should().Be(instructionAfterAcceptedObservation.InstructionId);
		resumed.CurrentInstruction.PublicAnnouncement.Should().Be(instructionAfterAcceptedObservation.PublicAnnouncement);
		resumed.CurrentInstruction.PrivateInstruction.Should().Be(instructionAfterAcceptedObservation.PrivateInstruction);

		resumed.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>();
		resumed.CurrentSession!.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void ProcessInput_AcceptedPublicRoleReveal_PersistsFactAndNextInstruction()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var manager = new GameClientManager(new GameService(), saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		var startInstruction = manager.StartGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(startInstruction.CreateResponse());
		var players = manager.CurrentSession!.GetPlayers().ToList();
		var victim = players[2];

		ConfirmCurrentInstruction(manager);
		SelectCurrentPlayers(manager, [players[0].Id]);
		SelectCurrentPlayers(manager, [victim.Id]);
		ConfirmCurrentInstruction(manager);

		manager.CurrentPhase.Should().Be(GamePhase.Dawn);
		ConfirmCurrentInstruction(manager);
		AssignCurrentRoles(manager, MainRoleType.SimpleVillager);
		var instructionAfterAcceptedReveal = manager.CurrentInstruction!;

		victim.State.PubliclyRevealedRole.Should().Be(MainRoleType.SimpleVillager);
		manager.CurrentSession.GameHistoryLog.OfType<RoleRevealLogEntry>()
			.Should().ContainSingle(entry =>
				entry.RevealedRoles.Contains(
					new KeyValuePair<Guid, MainRoleType>(victim.Id, MainRoleType.SimpleVillager)));
		var resumed = ResumeFromSave(saveDirectory.Path);
		var savedSession = resumed.CurrentSession!;
		savedSession.GetPlayer(victim.Id).State.PubliclyRevealedRole
			.Should().Be(MainRoleType.SimpleVillager);
		savedSession.GameHistoryLog.OfType<RoleRevealLogEntry>()
			.Should().ContainSingle();
		resumed.CurrentInstruction!.GetType().Should().Be(instructionAfterAcceptedReveal.GetType());
		resumed.CurrentInstruction.InstructionId.Should().Be(instructionAfterAcceptedReveal.InstructionId);
		resumed.CurrentInstruction.PublicAnnouncement.Should().Be(instructionAfterAcceptedReveal.PublicAnnouncement);
		resumed.CurrentInstruction.PrivateInstruction.Should().Be(instructionAfterAcceptedReveal.PrivateInstruction);
	}

	[Fact]
	public void ProcessInput_DuringDayVote_PersistsStableDayBoundaryWithoutVoteTailEntries()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var manager = new GameClientManager(new GameService(), saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		AdvanceToDebate(manager);
		var stableDayTurn = manager.TurnNumber;

		ConfirmCurrentInstruction(manager);
		var voteInstruction = manager.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(voteInstruction.CreateResponse([voteInstruction.SelectablePlayerIds.First()]));

		manager.CurrentSession!.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>().Should().NotBeEmpty();
		var resumed = ResumeFromSave(saveDirectory.Path);
		var savedSession = resumed.CurrentSession!;
		savedSession.GetCurrentPhase().Should().Be(GamePhase.Day);
		savedSession.TurnNumber.Should().Be(stableDayTurn);
		savedSession.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>().Should().BeEmpty();
		savedSession.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Where(entry => entry.CurrentPhase == GamePhase.Day)
			.Should().BeEmpty();
		savedSession.GameHistoryLog.OfType<AssignRoleLogEntry>()
			.Where(entry => entry.CurrentPhase == GamePhase.Day)
			.Should().BeEmpty();
		savedSession.GameHistoryLog.OfType<PhaseTransitionLogEntry>()
			.Should().Contain(entry => entry.CurrentPhase == GamePhase.Day);
		resumed.CurrentInstruction.Should().BeOfType<ConfirmationInstruction>()
			.Subject.PublicAnnouncement.Should().Be(GameStrings.DebateStartsPrompt);

		ConfirmCurrentInstruction(resumed);

		resumed.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>();
	}

	[Fact]
	public void ProcessInput_DayToNightRecoveryPayload_HasPostTransitionTurnWithoutDoubleIncrement()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var manager = new GameClientManager(new GameService(), saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		AdvanceToDebate(manager);

		ConfirmCurrentInstruction(manager);
		var voteInstruction = manager.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(voteInstruction.CreateResponse([]));

		manager.CurrentPhase.Should().Be(GamePhase.Night);
		manager.TurnNumber.Should().Be(2);
		var resumed = ResumeFromSave(saveDirectory.Path);
		var savedSession = resumed.CurrentSession!;
		savedSession.GetCurrentPhase().Should().Be(GamePhase.Night);
		savedSession.TurnNumber.Should().Be(2);
		savedSession.GameHistoryLog.OfType<PhaseTransitionLogEntry>()
			.Where(entry => entry.CurrentPhase == GamePhase.Night)
			.Should().ContainSingle()
			.Which.TurnNumber.Should().Be(2);

		ConfirmCurrentInstruction(resumed);

		resumed.TurnNumber.Should().Be(2);
	}

	[Fact]
	public void DisplayFlow_ForInstructionWithPublicAndPrivateText_ShowsAnnouncementBeforeInput()
	{
		var manager = new GameClientManager();
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse());
		var instruction = manager.CurrentInstruction!;

		instruction.PublicAnnouncement.Should().NotBeNullOrWhiteSpace();
		instruction.PrivateInstruction.Should().NotBeNullOrWhiteSpace();

		var flow = new InstructionDisplayFlow(instruction);

		flow.CurrentText.Should().Be(instruction.PublicAnnouncement);
		flow.IsShowingInput.Should().BeFalse();

		flow.Advance();

		flow.CurrentText.Should().Be(instruction.PrivateInstruction);
		flow.IsShowingInput.Should().BeTrue();
	}

	[Fact]
	public void DisplayFlow_ForSinglePartInstruction_ShowsTextAndInputImmediately()
	{
		var manager = new GameClientManager();
		var instruction = StartSimpleGame(manager);

		var flow = new InstructionDisplayFlow(instruction);

		flow.CurrentText.Should().Be(instruction.PublicAnnouncement);
		flow.IsShowingInput.Should().BeTrue();
	}

	[Fact]
	public void ProcessInput_AcrossNightBoundary_UsesResourceBackedConfirmationText()
	{
		var manager = new GameClientManager();
		var startInstruction = StartSimpleGame(manager);

		manager.ProcessInput(startInstruction.CreateResponse());
		var nightStartInstruction = manager.CurrentInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;
		nightStartInstruction.PublicAnnouncement.Should().Be(GameStrings.NightStartsPrompt);
		nightStartInstruction.PrivateInstruction.Should().Be(GameStrings.ConfirmNightStarted);
		manager.CurrentPhase.Should().Be(GamePhase.Night);
		manager.TurnNumber.Should().Be(1);

		manager.ProcessInput(nightStartInstruction.CreateResponse());

		for (var step = 0; step < 20; step++)
		{
			if (manager.CurrentInstruction is ConfirmationInstruction confirmation
				&& confirmation.PublicAnnouncement == GameStrings.NightActionsCompletePrompt)
			{
				break;
			}

			switch (manager.CurrentInstruction)
			{
				case ConfirmationInstruction currentConfirmation:
					manager.ProcessInput(currentConfirmation.CreateResponse());
					break;
				case SelectPlayersInstruction selectPlayers:
					manager.ProcessInput(selectPlayers.CreateResponse(
						[selectPlayers.SelectablePlayerIds.First()]));
					break;
				default:
					throw new InvalidOperationException(
						ClientTestReferences.ExceptionMessages.UnexpectedInstruction(
							manager.CurrentInstruction?.GetType().Name));
			}
		}

		var dawnInstruction = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		dawnInstruction.PublicAnnouncement.Should().Be(GameStrings.NightActionsCompletePrompt);
		manager.CurrentPhase.Should().Be(GamePhase.Dawn);
		manager.TurnNumber.Should().Be(1);
	}

	[Fact]
	public void StartGame_RaisesStateChangedOnceAfterSessionCreation()
	{
		var manager = new GameClientManager();
		var eventCount = 0;
		manager.StateChanged += (_, _) => eventCount++;

		StartSimpleGame(manager);

		eventCount.Should().Be(1);
	}

	[Fact]
	public void ProcessInput_RaisesStateChangedAfterSuccessfulProcessing()
	{
		var manager = new GameClientManager();
		var startInstruction = StartSimpleGame(manager);
		var eventCount = 0;
		manager.StateChanged += (_, _) => eventCount++;

		manager.ProcessInput(startInstruction.CreateResponse());

		eventCount.Should().Be(1);
	}

	[Fact]
	public void CurrentRoster_PreservesUnknownExactRoleWhenFactionAgentKnowledgeChangesDuringNight()
	{
		var manager = new GameClientManager();
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse());
		var nightStartInstruction = manager.CurrentInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(nightStartInstruction.CreateResponse());
		var observeWerewolfAgentsInstruction = manager.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var werewolf = manager.CurrentSession!.GetPlayers().First();
		var eventRosterSnapshots = new List<IReadOnlyList<DashboardRosterEntry>>();
		manager.StateChanged += (_, _) => eventRosterSnapshots.Add(manager.CurrentRoster);

		manager.ProcessInput(observeWerewolfAgentsInstruction.CreateResponse([werewolf.Id]));

		var werewolfEntry = manager.CurrentRoster
			.Single(entry => entry.PlayerId == werewolf.Id);
		werewolfEntry.RoleLabel.Should().Be(DashboardRoster.UnknownRoleLabel);
		werewolfEntry.IsRoleKnown.Should().BeFalse();
		manager.CurrentSession.GetFactionAgentKnowledge(werewolf.Id, Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		eventRosterSnapshots.Should().ContainSingle();
		var eventWerewolfEntry = eventRosterSnapshots[0]
			.Single(entry => entry.PlayerId == werewolf.Id);
		eventWerewolfEntry.RoleLabel.Should().Be(DashboardRoster.UnknownRoleLabel);
		eventWerewolfEntry.IsRoleKnown.Should().BeFalse();
	}

	[Fact]
	public void ProcessInput_WithoutActiveSession_ThrowsInvalidOperationException()
	{
		var manager = new GameClientManager();
		var response = StartSimpleGame(new GameClientManager()).CreateResponse();

		var act = () => manager.ProcessInput(response);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage(ClientTestReferences.ExceptionPatterns.MissingActiveGameSession);
	}

	[Fact]
	public async Task StartGame_ReconcilesAudioForDisplayedInstruction()
	{
		var audioPlayback = new FakeInstructionAudioPlayback();
		var manager = new GameClientManager(new GameService(), audioPlayback);

		var instruction = StartSimpleGame(manager);
		await manager.PendingAudioReconciliation;

		audioPlayback.ReconciledInstructions.Should().Equal(instruction);
	}

	[Fact]
	public async Task ProcessInput_ReconcilesAudioForNewInstruction()
	{
		var audioPlayback = new FakeInstructionAudioPlayback();
		var manager = new GameClientManager(new GameService(), audioPlayback);
		var startInstruction = StartSimpleGame(manager);
		await manager.PendingAudioReconciliation;
		audioPlayback.ReconciledInstructions.Clear();

		var result = manager.ProcessInput(startInstruction.CreateResponse());
		await manager.PendingAudioReconciliation;

		audioPlayback.ReconciledInstructions.Should().Equal(result.ModeratorInstruction);
	}

	[Fact]
	public async Task ToggleAudioMuteAsync_UpdatesMuteStateAndRaisesStateChanged()
	{
		var audioPlayback = new FakeInstructionAudioPlayback();
		var manager = new GameClientManager(new GameService(), audioPlayback);
		StartSimpleGame(manager);
		var eventCount = 0;
		manager.StateChanged += (_, _) => eventCount++;

		await manager.ToggleAudioMuteAsync();

		manager.IsAudioMuted.Should().BeTrue();
		audioPlayback.MuteRequests.Should().Equal(true);
		eventCount.Should().Be(1);
	}

	[Fact]
	public async Task ReconcileAudioAfterResumeAsync_ReconcilesCurrentInstruction()
	{
		var audioPlayback = new FakeInstructionAudioPlayback();
		var manager = new GameClientManager(new GameService(), audioPlayback);
		StartSimpleGame(manager);
		await manager.PendingAudioReconciliation;
		audioPlayback.ReconciledInstructions.Clear();

		await manager.ReconcileAudioAfterResumeAsync();

		audioPlayback.ReconciledInstructions.Should().Equal(manager.CurrentInstruction);
	}

	[Fact]
	public void DebateElapsed_BeforeDebatePhase_ReturnsNull()
	{
		var manager = new GameClientManager(new GameService(), timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow));
		StartSimpleGame(manager);

		manager.DebateElapsed.Should().BeNull();
	}

	[Fact]
	public void DebateElapsed_DuringDebateInstruction_ReturnsElapsedTime()
	{
		var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var manager = new GameClientManager(new GameService(), timeProvider: fakeTime);
		AdvanceToDebate(manager);

		fakeTime.Advance(TimeSpan.FromSeconds(42));

		manager.DebateElapsed.Should().NotBeNull();
		manager.DebateElapsed!.Value.Should().BeCloseTo(TimeSpan.FromSeconds(42), TimeSpan.FromMilliseconds(50));
	}

	[Fact]
	public void DebateElapsed_DuringNightPhase_ReturnsNull()
	{
		var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var manager = new GameClientManager(new GameService(), timeProvider: fakeTime);
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse());

		manager.CurrentPhase.Should().Be(GamePhase.Night);
		manager.DebateElapsed.Should().BeNull();
	}

	[Fact]
	public void DebateElapsed_AfterVotingBegins_ReturnsNull()
	{
		var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var manager = new GameClientManager(new GameService(), timeProvider: fakeTime);
		AdvanceToDebate(manager);
		manager.DebateElapsed.Should().NotBeNull();

		// Confirm the debate instruction to advance to voting
		var debateInstruction = (ConfirmationInstruction)manager.CurrentInstruction!;
		manager.ProcessInput(debateInstruction.CreateResponse());

		manager.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>();
		manager.DebateElapsed.Should().BeNull();
	}

	[Fact]
	public void DebateElapsed_AfterResumeFromSavedDebate_StartsTimerFromResume()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var manager = new GameClientManager(
			new GameService(),
			DisabledInstructionAudioPlayback.Instance,
			saveStore,
			fakeTime);
		AdvanceToDebate(manager);
		manager.DebateElapsed.Should().NotBeNull();

		// Construct a new manager from the same save store -- simulates app restart
		var resumeFakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var resumed = new GameClientManager(
			new GameService(),
			DisabledInstructionAudioPlayback.Instance,
			new FileGameSessionSaveStore(saveDirectory.Path),
			resumeFakeTime);

		resumed.CurrentInstruction.Should().NotBeNull();
		resumed.CurrentInstruction!.PublicAnnouncement.Should().Be(GameStrings.DebateStartsPrompt);
		resumed.DebateElapsed.Should().NotBeNull();
		resumeFakeTime.Advance(TimeSpan.FromSeconds(15));
		resumed.DebateElapsed!.Value.Should().BeCloseTo(TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(50));
	}

	[Fact]
	public void DebateElapsed_WhenNewDebateBegins_ResetsTimer()
	{
		var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var manager = new GameClientManager(new GameService(), timeProvider: fakeTime);
		AdvanceToDebate(manager);
		fakeTime.Advance(TimeSpan.FromMinutes(5));
		var firstDebateElapsed = manager.DebateElapsed!.Value;
		firstDebateElapsed.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(50));

		// Move past debate through voting and back to a second debate
		AdvancePastDebateToNextDebate(manager);

		// Timer should have reset -- elapsed should be near zero, not 5+ minutes
		manager.DebateElapsed.Should().NotBeNull();
		manager.DebateElapsed!.Value.Should().BeLessThan(TimeSpan.FromSeconds(1));
	}

	private static StartGameConfirmationInstruction StartSimpleGame(GameClientManager manager)
	{
		var players = PlayerNames.DefaultFive;
		var roles = new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};

		return manager.StartGame(players, roles);
	}

	private static StartGameConfirmationInstruction StartTwoWerewolfGame(GameClientManager manager)
	{
		var players = PlayerNames.DefaultFive;
		var roles = new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};

		return manager.StartGame(players, roles);
	}

	private static GameClientManager ResumeFromSave(string saveDirectoryPath) =>
		new(new GameService(), saveStore: new FileGameSessionSaveStore(saveDirectoryPath));

	private sealed class FakeInstructionAudioPlayback : IInstructionAudioPlayback
	{
		public bool IsMuted { get; private set; }
		public List<ModeratorInstruction?> ReconciledInstructions { get; } = [];
		public List<bool> MuteRequests { get; } = [];

		public Task ReconcileAsync(ModeratorInstruction? instruction, CancellationToken cancellationToken = default)
		{
			ReconciledInstructions.Add(instruction);
			return Task.CompletedTask;
		}

		public Task SetMutedAsync(
			bool isMuted,
			ModeratorInstruction? instruction,
			CancellationToken cancellationToken = default)
		{
			IsMuted = isMuted;
			MuteRequests.Add(isMuted);
			return ReconcileAsync(instruction, cancellationToken);
		}
	}

	private static void PlayToWerewolfVictoryAtDawn(GameClientManager manager)
	{
		var startInstruction = manager.StartGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		manager.ProcessInput(startInstruction.CreateResponse());
		var players = manager.CurrentSession!.GetPlayers().ToList();
		var werewolfIds = players.Take(2).Select(player => player.Id).ToHashSet();
		var victimId = players[2].Id;

		ConfirmCurrentInstruction(manager);
		SelectCurrentPlayers(manager, werewolfIds);
		SelectCurrentPlayers(manager, [victimId]);
		ConfirmCurrentInstruction(manager);
		ConfirmCurrentInstruction(manager);

		for (var step = 0; step < 20; step++)
		{
			switch (manager.CurrentInstruction)
			{
				case FinishedGameConfirmationInstruction:
					return;
				case AssignRolesInstruction assignRoles:
					var assignments = assignRoles.PlayersForAssignment.ToDictionary(
						playerId => playerId,
						_ => MainRoleType.SimpleVillager);
					manager.ProcessInput(assignRoles.CreateResponse(assignments));
					break;
				case ConfirmationInstruction confirmation:
					manager.ProcessInput(confirmation.CreateResponse());
					break;
				default:
					throw new InvalidOperationException(
						ClientTestReferences.ExceptionMessages.UnexpectedInstructionWhileReachingVictory(
							manager.CurrentInstruction?.GetType().Name));
			}
		}

		throw new InvalidOperationException(ClientTestReferences.ExceptionMessages.VictoryNotReached);
	}

	private static void ConfirmCurrentInstruction(GameClientManager manager)
	{
		var instruction = manager.CurrentInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(instruction.CreateResponse());
	}

	private static void SelectCurrentPlayers(GameClientManager manager, HashSet<Guid> playerIds)
	{
		var instruction = manager.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(instruction.CreateResponse(playerIds));
	}

	private static void AssignCurrentRoles(GameClientManager manager, MainRoleType role)
	{
		var instruction = manager.CurrentInstruction.Should().BeOfType<AssignRolesInstruction>().Subject;
		manager.ProcessInput(instruction.CreateResponse(
			instruction.PlayersForAssignment.ToDictionary(playerId => playerId, _ => role)));
	}

	private sealed class TemporaryDirectory : IDisposable
	{
		private TemporaryDirectory(string path)
		{
			Path = path;
		}

		public string Path { get; }

		public static TemporaryDirectory Create() =>
			new(Directory.CreateTempSubdirectory("werewolves-client-tests-").FullName);

		public void Dispose()
		{
			if (Directory.Exists(Path))
			{
				Directory.Delete(Path, recursive: true);
			}
		}
	}

	[Fact]
	public void DebateElapsed_ContinuesGrowingWithoutInteraction()
	{
		var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var manager = new GameClientManager(new GameService(), timeProvider: fakeTime);
		AdvanceToDebate(manager);

		fakeTime.Advance(TimeSpan.FromSeconds(10));
		var first = manager.DebateElapsed!.Value;

		fakeTime.Advance(TimeSpan.FromSeconds(20));
		var second = manager.DebateElapsed!.Value;

		first.Should().BeCloseTo(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50));
		second.Should().BeCloseTo(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(50));
	}

	private static void AdvancePastDebateToNextDebate(GameClientManager manager)
	{
		// Confirm debate to move to voting
		var debateInstruction = (ConfirmationInstruction)manager.CurrentInstruction!;
		manager.ProcessInput(debateInstruction.CreateResponse());

		// Continue through voting, night, dawn until the next debate
		for (var step = 0; step < 50; step++)
		{
			if (manager.CurrentPhase == GamePhase.Day &&
				manager.CurrentInstruction is ConfirmationInstruction &&
				manager.CurrentInstruction.PublicAnnouncement == GameStrings.DebateStartsPrompt)
			{
				return;
			}

			switch (manager.CurrentInstruction)
			{
				case FinishedGameConfirmationInstruction:
					throw new InvalidOperationException(ClientTestReferences.ExceptionMessages.GameEndedBeforeNextDebate);
				case ConfirmationInstruction ci:
					manager.ProcessInput(ci.CreateResponse());
					break;
				case SelectPlayersInstruction sp:
					// Vote for nobody (empty set if optional) to avoid eliminations
					if (sp.CountConstraint.IsOptional)
					{
						manager.ProcessInput(sp.CreateResponse([]));
					}
					else
					{
						var firstId = sp.SelectablePlayerIds.First();
						manager.ProcessInput(sp.CreateResponse([firstId]));
					}
					break;
				case AssignRolesInstruction assignRoles:
					var assignments = assignRoles.PlayersForAssignment.ToDictionary(
						playerId => playerId,
						_ => MainRoleType.SimpleVillager);
					manager.ProcessInput(assignRoles.CreateResponse(assignments));
					break;
				default:
					throw new InvalidOperationException(
						ClientTestReferences.ExceptionMessages.UnexpectedInstruction(
							manager.CurrentInstruction?.GetType().Name));
			}
		}

		throw new InvalidOperationException(ClientTestReferences.ExceptionMessages.NextDebateNotReached);
	}

	private static void AdvanceToDebate(GameClientManager manager)
	{
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse());

		for (var step = 0; step < 50; step++)
		{
			if (manager.CurrentPhase == GamePhase.Day &&
				manager.CurrentInstruction is ConfirmationInstruction &&
				manager.CurrentInstruction.PublicAnnouncement == GameStrings.DebateStartsPrompt)
			{
				return;
			}

			switch (manager.CurrentInstruction)
			{
				case ConfirmationInstruction ci:
					manager.ProcessInput(ci.CreateResponse());
					break;
				case SelectPlayersInstruction sp:
					var firstId = sp.SelectablePlayerIds.First();
					manager.ProcessInput(sp.CreateResponse([firstId]));
					break;
				case AssignRolesInstruction assignRoles:
					var assignments = assignRoles.PlayersForAssignment.ToDictionary(
						playerId => playerId,
						_ => MainRoleType.SimpleVillager);
					manager.ProcessInput(assignRoles.CreateResponse(assignments));
					break;
				default:
					throw new InvalidOperationException(
						ClientTestReferences.ExceptionMessages.UnexpectedInstructionWhileAdvancingToDebate(
							manager.CurrentInstruction?.GetType().Name));
			}
		}

		throw new InvalidOperationException(ClientTestReferences.ExceptionMessages.DebateNotReached);
	}

	private sealed class FakeTimeProvider : TimeProvider
	{
		private DateTimeOffset _utcNow;

		public FakeTimeProvider(DateTimeOffset startTime)
		{
			_utcNow = startTime;
		}

		public override DateTimeOffset GetUtcNow() => _utcNow;

		public void Advance(TimeSpan delta)
		{
			_utcNow += delta;
		}
	}

	private static LobbySetupState CreateThiefLobby(bool withPlayers = true)
	{
		var lobby = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager);
		if (withPlayers)
		{
			foreach (var playerName in PlayerNames.DefaultFive)
			{
				lobby.AddPlayer(playerName);
			}
		}

		return lobby;
	}

	private static LobbySetupState CreateSupportedLobby(bool withPlayers = true)
	{
		var lobby = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager);
		if (withPlayers)
		{
			foreach (var playerName in PlayerNames.DefaultFive)
			{
				lobby.AddPlayer(playerName);
			}

			lobby.IncrementRole(MainRoleType.SimpleWerewolf);
			lobby.IncrementRole(MainRoleType.Seer);
			for (var i = 0; i < 3; i++)
			{
				lobby.IncrementRole(MainRoleType.SimpleVillager);
			}
		}

		return lobby;
	}

	private static LobbySetupState CreateActorLobby(bool withPlayers = true)
	{
		var lobby = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.Actor,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Cupid,
			MainRoleType.Witch,
			MainRoleType.Hunter,
			MainRoleType.Seer,
			MainRoleType.Defender,
			MainRoleType.Elder);
		if (withPlayers)
		{
			foreach (var playerName in PlayerNames.DefaultFive)
			{
				lobby.AddPlayer(playerName);
			}

			lobby.IncrementRole(MainRoleType.Actor);
			lobby.IncrementRole(MainRoleType.SimpleWerewolf);
			for (var index = 0; index < 3; index++)
			{
				lobby.IncrementRole(MainRoleType.SimpleVillager);
			}
		}

		return lobby;
	}

	private static LobbySetupState CreateActorAndPrejudicedManipulatorLobby(
		bool withPlayers = true)
	{
		var lobby = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.Actor,
			MainRoleType.PrejudicedManipulator,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Cupid,
			MainRoleType.Witch,
			MainRoleType.Hunter);
		if (withPlayers)
		{
			foreach (var playerName in PlayerNames.DefaultFive)
			{
				lobby.AddPlayer(playerName);
			}

			lobby.IncrementRole(MainRoleType.Actor);
			lobby.IncrementRole(MainRoleType.PrejudicedManipulator);
			lobby.IncrementRole(MainRoleType.SimpleWerewolf);
			lobby.IncrementRole(MainRoleType.SimpleVillager);
			lobby.IncrementRole(MainRoleType.SimpleVillager);
		}

		return lobby;
	}

	private static LobbySetupState CreatePrejudicedManipulatorLobby(
		bool withPlayers = true)
	{
		var lobby = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.SimpleWerewolf,
			MainRoleType.PrejudicedManipulator,
			MainRoleType.SimpleVillager);
		if (withPlayers)
		{
			foreach (var playerName in PlayerNames.DefaultFive)
			{
				lobby.AddPlayer(playerName);
			}

			lobby.IncrementRole(MainRoleType.SimpleWerewolf);
			lobby.IncrementRole(MainRoleType.PrejudicedManipulator);
			for (var index = 0; index < 3; index++)
			{
				lobby.IncrementRole(MainRoleType.SimpleVillager);
			}
		}

		return lobby;
	}

	private static RoleLockIn CreateThiefRoleLockIn(long version, bool rotateOffer1IntoDealPool)
	{
		var cards = new[]
		{
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Thief),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager)
		};
		var dealPool = rotateOffer1IntoDealPool
			? new[] { cards[0].Id, cards[1].Id, cards[2].Id, cards[3].Id, cards[5].Id }
			: cards.Take(5).Select(card => card.Id).ToArray();
		var offer1 = rotateOffer1IntoDealPool ? cards[4].Id : cards[5].Id;

		return new RoleLockIn(
			version,
			playerCount: 5,
			cards,
			dealPool,
			offer1,
			cards[6].Id);
	}

	private static RoleLockIn CreateSupportedRoleLockIn(long version)
	{
		var cards = new[]
		{
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager)
		};
		return new RoleLockIn(
			version,
			playerCount: 5,
			cards,
			cards.Select(card => card.Id));
	}

	private static string CreateSchema1StagedLobbyPayload()
	{
		var roleLockIn = CreateSupportedRoleLockIn(version: 1);
		return JsonSerializer.Serialize(
			new
			{
				SchemaVersion = 1,
				Kind = "StagedLobby",
				StagedLobby = new
				{
					PlayerNames = PlayerNames.DefaultFive,
					RoleLockIn = new
					{
						roleLockIn.Version,
						roleLockIn.PlayerCount,
						RoleComposition = roleLockIn.RoleComposition,
						DealPoolCardIds = roleLockIn.DealPool
							.Select(card => card.Id)
							.ToArray(),
						Offer1CardId = roleLockIn.Offer1?.Id,
						Offer2CardId = roleLockIn.Offer2?.Id
					}
				},
				ActiveGame = (object?)null
			},
			new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			});
	}

	private static string CreateSchema1ActiveGamePayload(string serializedSession) =>
		JsonSerializer.Serialize(
			new
			{
				SchemaVersion = 1,
				Kind = "ActiveGame",
				StagedLobby = (object?)null,
				ActiveGame = new { SerializedSession = serializedSession }
			},
			new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			});

	private static string CreateInvalidSchema3UnionPayload(
		string kind,
		bool includeStagedLobby,
		bool includeActiveGame)
	{
		var lobby = CreateSupportedLobby();
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: lobby.PlayerRoster.Count,
			lobby.GetSelectedRoles());
		var stagedEnvelope = JsonNode.Parse(
			LocalRecoveryPayloadCodec.SerializeStagedLobby(
				lobby.PlayerRoster,
				roleLockIn,
				ActorSetupCards.None,
				publicGroupPartition: null))!;
		var activeEnvelope = JsonNode.Parse(
			LocalRecoveryPayloadCodec.SerializeActiveGame(
				CreateValidSerializedCoreSession()))!;
		return new JsonObject
		{
			["schemaVersion"] = 3,
			["kind"] = kind,
			["stagedLobby"] = includeStagedLobby
				? stagedEnvelope["stagedLobby"]!.DeepClone()
				: null,
			["activeGame"] = includeActiveGame
				? activeEnvelope["activeGame"]!.DeepClone()
				: null
		}.ToJsonString();
	}

	private static string CreateValidSerializedCoreSession()
	{
		var gameService = new GameService();
		var instruction = gameService.StartNewGame(new GameSessionConfig(
			PlayerNames.DefaultFive.ToList(),
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]));
		return gameService.GetGameStateView(instruction.GameGuid)!.Serialize();
	}

	private static string ReadRecoveryKind(string? payload)
	{
		payload.Should().NotBeNullOrWhiteSpace();
		using var document = JsonDocument.Parse(payload!);
		return document.RootElement.GetProperty("kind").GetString()!;
	}

	private static string ReadActiveGameSerializedSession(string payload)
	{
		using var document = JsonDocument.Parse(payload);
		return document.RootElement
			.GetProperty("activeGame")
			.GetProperty("serializedSession")
			.GetString()!;
	}

	private sealed class ThrowingSaveStore : IGameSessionSaveStore
	{
		public string? Load() => null;

		public void Save(string serializedSession) =>
			throw new IOException(ClientTestReferences.ExceptionMessages.SaveFailed);

		public void Clear()
		{
		}
	}

	private sealed class ToggleThrowSaveStore : IGameSessionSaveStore
	{
		private string? _payload;

		public bool ThrowOnSave { get; set; }
		public bool ThrowOnClear { get; set; }

		public string? Load() => _payload;

		public void Save(string serializedSession)
		{
			if (ThrowOnSave)
			{
				throw new IOException(ClientTestReferences.ExceptionMessages.SaveFailed);
			}
			_payload = serializedSession;
		}

		public void Clear()
		{
			if (ThrowOnClear)
			{
				throw new IOException(ClientTestReferences.ExceptionMessages.SaveFailed);
			}

			_payload = null;
		}
	}

	private sealed class RecordingSaveStore : IGameSessionSaveStore
	{
		private string? _payload;

		public List<string> SavedPayloads { get; } = new();

		public string? Load() => _payload;

		public void Save(string serializedSession)
		{
			SavedPayloads.Add(serializedSession);
			_payload = serializedSession;
		}

		public void Clear() => _payload = null;
	}

	[Fact]
	public void ClearSession_NullsActiveGameIdAndSessionAndInstruction()
	{
		var manager = new GameClientManager();
		StartSimpleGame(manager);
		manager.HasActiveSession.Should().BeTrue();

		manager.ClearSession();

		manager.ActiveGameId.Should().BeNull();
		manager.CurrentSession.Should().BeNull();
		manager.CurrentInstruction.Should().BeNull();
		manager.HasActiveSession.Should().BeFalse();
	}

	[Fact]
	public void ClearSession_RaisesStateChanged()
	{
		var manager = new GameClientManager();
		StartSimpleGame(manager);
		var eventCount = 0;
		manager.StateChanged += (_, _) => eventCount++;

		manager.ClearSession();

		eventCount.Should().Be(1);
	}

	[Fact]
	public void ClearSession_DeletesSaveFile()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var manager = new GameClientManager(new GameService(), saveStore: saveStore);
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse());
		var saveFilePath = Path.Combine(saveDirectory.Path, FileGameSessionSaveStore.SaveFileName);
		File.Exists(saveFilePath).Should().BeTrue();

		manager.ClearSession();

		File.Exists(saveFilePath).Should().BeFalse();
	}

}
