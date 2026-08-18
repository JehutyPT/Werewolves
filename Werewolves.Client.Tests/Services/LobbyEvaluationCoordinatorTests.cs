using System.Text;
using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class LobbyEvaluationCoordinatorTests
{
	[Fact]
	public async Task EffectiveSetupChanges_ReadMutableLocalCacheEachTime()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var firstRecord = AlreadyDecidedRecord(lobby.CreateSimulationScenario());
		var secondScenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager
			]);
		var secondRecord = AlreadyDecidedRecord(secondScenario);
		var local = new ControlledReadLocalStore();
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
			FullProbabilitySettings,
			TimeProvider.System);
		var firstRead = await local.NextReadAsync();
		firstRead.Complete(DocumentBytes(firstRecord, secondRecord));
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		ChangeToFourWerewolves(lobby);
		var secondRead = await local.NextReadAsync();
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		secondRead.Complete(DocumentBytes(firstRecord, secondRecord));
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		coordinator.State.Identity.Should().Be(secondRecord.CompatibilityIdentity);
		local.ReadCount.Should().Be(2);
	}

	[Fact]
	public async Task ThiefDraft_WaitsForAcceptedLockInThenEvaluatesItsExactScenario()
	{
		var lobby = CreateLobby(
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new RecordingLocalStore(bytes: null);
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.NotApplicable);
		local.ReadCount.Should().Be(0);
		evaluator.CallCount.Should().Be(0);

		var manager = new GameClientManager();
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			offer1: MainRoleType.SimpleVillager,
			offer2: MainRoleType.SimpleVillager).Should().BeTrue();
		var accepted = lobby.AcceptedRoleLockIn!;

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity!.Scenario.Should().Be(
			new SimulationScenario(accepted).ToCanonical());
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitUntilAsync(() => evaluator.CallCount == 1);

		local.ReadCount.Should().Be(1);
		evaluator.Scenarios.Should().ContainSingle()
			.Which.ToCanonical().Should().Be(new SimulationScenario(accepted).ToCanonical());
	}

	[Fact]
	public async Task RoleLockInSaveFailure_LeavesAcceptedEvaluationAndRecoveryUnchanged()
	{
		var lobby = CreateLobby(
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Hunter);
		var local = new ControlledReadLocalStore();
		var store = new ToggleThrowSaveStore();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			offer1: MainRoleType.Seer,
			offer2: MainRoleType.Hunter).Should().BeTrue();
		await WaitUntilAsync(() => local.ReadCount == 1);
		var accepted = lobby.AcceptedRoleLockIn;
		var acceptedIdentity = coordinator.State.Identity;
		var acceptedBytes = store.Load();
		store.ThrowOnSave = true;

		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 1,
			offer1: MainRoleType.Hunter,
			offer2: MainRoleType.Seer).Should().BeFalse();

		lobby.AcceptedRoleLockIn.Should().BeSameAs(accepted);
		manager.StagedRoleLockIn.Should().BeSameAs(accepted);
		store.Load().Should().Be(acceptedBytes);
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity.Should().Be(acceptedIdentity);
		local.ReadCount.Should().Be(1);
	}

	[Fact]
	public async Task PublicGroupPartitionSaveFailure_LeavesAcceptedEvaluationAndRecoveryUnchanged()
	{
		var lobby = CreateLobby(
			MainRoleType.PrejudicedManipulator,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new ControlledReadLocalStore();
		var store = new ToggleThrowSaveStore();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var rosterIds = lobby.PlayerRoster.Select(player => player.Id).ToArray();
		var acceptedPartition = PublicGroupPartition.Create(
			rosterIds,
			rosterIds.Take(2),
			rosterIds.Skip(2));
		manager.TryReplaceStagedPublicGroupPartition(lobby, acceptedPartition)
			.Should().BeTrue();
		var acceptedRead = await local.NextReadAsync();
		var acceptedIdentity = coordinator.State.Identity;
		var acceptedBytes = store.Load();
		var replacement = PublicGroupPartition.Create(
			rosterIds,
			[rosterIds[0], rosterIds[2]],
			[rosterIds[1], rosterIds[3], rosterIds[4]]);
		store.ThrowOnSave = true;

		manager.TryReplaceStagedPublicGroupPartition(lobby, replacement)
			.Should().BeFalse();

		lobby.AcceptedPublicGroupPartition.Should().BeSameAs(acceptedPartition);
		store.Load().Should().Be(acceptedBytes);
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity.Should().Be(acceptedIdentity);
		acceptedRead.CancellationToken.IsCancellationRequested.Should().BeFalse();
		local.ReadCount.Should().Be(1);
	}

	[Fact]
	public async Task PublicGroupPartitionChange_RestartsEvaluationForTheDurablyAcceptedAggregate()
	{
		var lobby = CreateLobby(
			MainRoleType.PrejudicedManipulator,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new ControlledReadLocalStore();
		var store = new ToggleThrowSaveStore();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var rosterIds = lobby.PlayerRoster.Select(player => player.Id).ToArray();
		var firstPartition = PublicGroupPartition.Create(
			rosterIds,
			rosterIds.Take(2),
			rosterIds.Skip(2));
		manager.TryReplaceStagedPublicGroupPartition(lobby, firstPartition)
			.Should().BeTrue();
		var firstRead = await local.NextReadAsync();
		var firstIdentity = coordinator.State.Identity;
		var replacement = PublicGroupPartition.Create(
			rosterIds,
			[rosterIds[0], rosterIds[2]],
			[rosterIds[1], rosterIds[3], rosterIds[4]]);

		manager.TryReplaceStagedPublicGroupPartition(lobby, replacement)
			.Should().BeTrue();
		_ = await local.NextReadAsync();

		lobby.AcceptedPublicGroupPartition.Should().BeSameAs(replacement);
		LocalRecoveryPayloadCodec.Deserialize(store.Load()!)
			.Should().BeOfType<StagedLobbyRecoveryPayload>()
			.Which.PublicGroupPartition.Should().Be(replacement);
		firstRead.CancellationToken.IsCancellationRequested.Should().BeTrue();
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity.Should().NotBe(firstIdentity);
		coordinator.State.Identity!.Scenario.Should().Be(
			lobby.CreateSimulationScenario().ToCanonical());
		local.ReadCount.Should().Be(2);
	}

	[Fact]
	public async Task EquivalentPublicGroupPartition_KeepsTheMatchingInFlightEvaluation()
	{
		var lobby = CreateLobby(
			MainRoleType.PrejudicedManipulator,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new ControlledReadLocalStore();
		var store = new ToggleThrowSaveStore();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var rosterIds = lobby.PlayerRoster.Select(player => player.Id).ToArray();
		var acceptedPartition = PublicGroupPartition.Create(
			rosterIds,
			rosterIds.Take(2),
			rosterIds.Skip(2));
		manager.TryReplaceStagedPublicGroupPartition(lobby, acceptedPartition)
			.Should().BeTrue();
		var pendingRead = await local.NextReadAsync();
		var pendingIdentity = coordinator.State.Identity;
		var durablePayload = store.Load();
		var equivalentPartition = PublicGroupPartition.Create(
			rosterIds,
			rosterIds.Skip(2).Reverse(),
			rosterIds.Take(2).Reverse());

		manager.TryReplaceStagedPublicGroupPartition(lobby, equivalentPartition)
			.Should().BeTrue();

		lobby.AcceptedPublicGroupPartition.Should().BeSameAs(acceptedPartition);
		store.Load().Should().Be(durablePayload);
		pendingRead.CancellationToken.IsCancellationRequested.Should().BeFalse();
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity.Should().Be(pendingIdentity);
		local.ReadCount.Should().Be(1);
	}

	[Fact]
	public async Task ActorSetupCardsSaveFailure_LeavesAcceptedEvaluationAndRecoveryUnchanged()
	{
		var lobby = CreateLobby(
			MainRoleType.Actor,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new ControlledReadLocalStore();
		var store = new ToggleThrowSaveStore();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		manager.TryReplaceStagedActorSetupCards(
			lobby,
			expectedCurrentVersion: 0,
			[MainRoleType.Cupid, MainRoleType.Defender, MainRoleType.Elder])
			.Should().BeTrue();
		var acceptedRead = await local.NextReadAsync();
		var acceptedActorSetupCards = lobby.AcceptedActorSetupCards;
		var acceptedIdentity = coordinator.State.Identity;
		var acceptedBytes = store.Load();
		store.ThrowOnSave = true;

		manager.TryReplaceStagedActorSetupCards(
			lobby,
			expectedCurrentVersion: acceptedActorSetupCards.Version,
			[MainRoleType.Elder, MainRoleType.Defender, MainRoleType.Cupid])
			.Should().BeFalse();

		lobby.AcceptedActorSetupCards.Should().BeSameAs(acceptedActorSetupCards);
		store.Load().Should().Be(acceptedBytes);
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity.Should().Be(acceptedIdentity);
		acceptedRead.CancellationToken.IsCancellationRequested.Should().BeFalse();
		local.ReadCount.Should().Be(1);
	}

	[Fact]
	public void ActorDraft_WaitsForAcceptedLockInAndSetupThenClassifiesItsExactScenario()
	{
		var lobby = CreateLobby(
			MainRoleType.Actor,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new RecordingLocalStore(bytes: null);
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		var classifiedScenarios = new List<SimulationScenario>();
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			SafetyScreeningSettings,
			TimeProvider.System,
			(scenario, _) =>
			{
				classifiedScenarios.Add(scenario);
				return new LobbyScenarioSupport(
					RulesValid: true,
					AppSupported: false,
					SimulatorSupported: false);
			});

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.NotApplicable);
		classifiedScenarios.Should().BeEmpty();
		local.ReadCount.Should().Be(0);
		evaluator.CallCount.Should().Be(0);

		var manager = new GameClientManager();
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.NotApplicable);
		classifiedScenarios.Should().BeEmpty();
		local.ReadCount.Should().Be(0);
		evaluator.CallCount.Should().Be(0);

		manager.TryReplaceStagedActorSetupCards(
			lobby,
			expectedCurrentVersion: 0,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]).Should().BeTrue();
		var expectedScenario = new SimulationScenario(
			lobby.AcceptedRoleLockIn!,
			lobby.AcceptedActorSetupCards);

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.NotApplicable);
		classifiedScenarios.Should().ContainSingle()
			.Which.ToCanonical().Should().Be(expectedScenario.ToCanonical());
		local.ReadCount.Should().Be(0);
		evaluator.CallCount.Should().Be(0);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task AcceptedThiefLockIn_PlayerReorderPreservesSetupAndRestartsOrdinalEvaluation(
		bool moveUp)
	{
		var lobby = CreateLobby(
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.PrejudicedManipulator,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var saveStore = new ToggleThrowSaveStore();
		var manager = new GameClientManager(
			new GameService(),
			saveStore: saveStore);
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			offer1: MainRoleType.PrejudicedManipulator,
			offer2: MainRoleType.SimpleVillager).Should().BeTrue();
		var acceptedLockIn = lobby.AcceptedRoleLockIn;
		var rosterIds = lobby.PlayerRoster.Select(player => player.Id).ToArray();
		var partition = PublicGroupPartition.Create(
			rosterIds,
			[rosterIds[0], rosterIds[2]],
			[rosterIds[1], rosterIds[3], rosterIds[4]]);
		manager.TryReplaceStagedPublicGroupPartition(lobby, partition).Should().BeTrue();
		var originalScenario = lobby.CreateSimulationScenario().ToCanonical();
		var evaluator = new ControlledEvaluator();
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingLocalStore(bytes: null),
			evaluator,
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		var call = await evaluator.NextCallAsync();

		(moveUp
			? manager.TryMoveStagedPlayerUp(lobby, index: 1)
			: manager.TryMoveStagedPlayerDown(lobby, index: 0)).Should().BeTrue();

		lobby.AcceptedRoleLockIn.Should().BeSameAs(acceptedLockIn);
		lobby.AcceptedPublicGroupPartition.Should().BeSameAs(partition);
		lobby.RequiresRoleLockIn.Should().BeFalse();
		lobby.RequiresPublicGroupPartition.Should().BeFalse();
		lobby.TryCreateSimulationScenario(out var reorderedScenario).Should().BeTrue();
		reorderedScenario.ToCanonical().Should().NotBe(originalScenario);
		call.CancellationToken.IsCancellationRequested.Should().BeTrue();
		var replacementCall = await evaluator.NextCallAsync();
		replacementCall.Identity.Scenario.Should().Be(reorderedScenario.ToCanonical());
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity!.Scenario.Should().Be(reorderedScenario.ToCanonical());
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		evaluator.CallCount.Should().Be(2);
		LocalRecoveryPayloadCodec.Deserialize(saveStore.Load()!)
			.Should().BeOfType<StagedLobbyRecoveryPayload>()
			.Which.PlayerRoster.Select(player => player.Id)
			.Should().Equal(lobby.PlayerRoster.Select(player => player.Id));
	}

	[Fact]
	public async Task CanonicallyEquivalentSeatingMove_KeepsMatchingInFlightEvaluationAfterDurablePublish()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new ControlledReadLocalStore();
		var store = new ToggleThrowSaveStore();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var pendingRead = await local.NextReadAsync();
		var pendingIdentity = coordinator.State.Identity;
		var originalIds = lobby.PlayerRoster.Select(player => player.Id).ToArray();
		var expectedIds = originalIds.ToArray();
		(expectedIds[0], expectedIds[1]) = (expectedIds[1], expectedIds[0]);

		manager.TryMoveStagedPlayerDown(lobby, index: 0).Should().BeTrue();

		lobby.PlayerRoster.Select(player => player.Id).Should().Equal(expectedIds);
		LocalRecoveryPayloadCodec.Deserialize(store.Load()!)
			.Should().BeOfType<StagedLobbyRecoveryPayload>()
			.Which.PlayerRoster.Select(player => player.Id)
			.Should().Equal(expectedIds);
		pendingRead.CancellationToken.IsCancellationRequested.Should().BeFalse();
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity.Should().Be(pendingIdentity);
		local.ReadCount.Should().Be(1);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task RosterMembershipClearFailure_KeepsAcceptedEvaluationAndRecoveryUnchanged(
		bool addPlayer)
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new ControlledReadLocalStore();
		var store = new ToggleThrowSaveStore();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var pendingRead = await local.NextReadAsync();
		var acceptedRoleLockIn = lobby.AcceptedRoleLockIn;
		var acceptedIdentity = coordinator.State.Identity;
		var acceptedPayload = store.Load();
		var acceptedRoster = lobby.PlayerRoster.ToArray();
		store.ThrowOnClear = true;

		var accepted = addPlayer
			? manager.TryAddStagedPlayer(lobby, "Fátima", out _)
			: manager.TryRemoveStagedPlayer(lobby, index: 2);

		accepted.Should().BeFalse();
		lobby.PlayerRoster.Should().Equal(acceptedRoster);
		lobby.AcceptedRoleLockIn.Should().BeSameAs(acceptedRoleLockIn);
		manager.StagedRoleLockIn.Should().BeSameAs(acceptedRoleLockIn);
		store.Load().Should().Be(acceptedPayload);
		pendingRead.CancellationToken.IsCancellationRequested.Should().BeFalse();
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity.Should().Be(acceptedIdentity);
		local.ReadCount.Should().Be(1);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task RosterMembershipClear_InvalidatesEvaluationOnlyAfterDurablePublication(
		bool addPlayer)
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new ControlledReadLocalStore();
		var store = new ToggleThrowSaveStore();
		var manager = new GameClientManager(new GameService(), saveStore: store);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var pendingRead = await local.NextReadAsync();

		var accepted = addPlayer
			? manager.TryAddStagedPlayer(lobby, "Fátima", out _)
			: manager.TryRemoveStagedPlayer(lobby, index: 2);

		accepted.Should().BeTrue();
		store.Load().Should().BeNull();
		lobby.AcceptedRoleLockInRequiresReplacement.Should().BeTrue();
		pendingRead.CancellationToken.IsCancellationRequested.Should().BeTrue();
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.NotApplicable);
		coordinator.State.Identity.Should().BeNull();
		local.ReadCount.Should().Be(1);
	}

	[Fact]
	public async Task ThrowingReentrantCancellationCallback_CannotBlockReplacementProgress()
	{
		var pump = new ControlledContinuationPump();
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		LobbyEvaluationCoordinator coordinator = null!;
		var evaluator = new ReentrantCancellationEvaluator(() =>
			coordinator.TryRequestLobbyExit());
		pump.Run(() =>
		{
			coordinator = new LobbyEvaluationCoordinator(
				lobby,
				new RecordingLocalStore(bytes: null),
				evaluator,
				FullProbabilitySettings,
				TimeProvider.System,
				(_, _) => new LobbyScenarioSupport(
					RulesValid: true,
					AppSupported: true,
					SimulatorSupported: true));
			coordinator.TryRequestLobbyExit().Should().BeFalse();
			pump.Drain();
		});
		await evaluator.FirstCallStarted.WaitAsync(TimeSpan.FromSeconds(5));

		var mutation = Task.Run(() =>
			lobby.DecrementRole(MainRoleType.SimpleWerewolf));
		await evaluator.CallbackEntered.WaitAsync(TimeSpan.FromSeconds(5));
		evaluator.CompleteFirstCall();
		var staleDrain = Task.Run(pump.Drain);
		try
		{
			await staleDrain.WaitAsync(TimeSpan.FromSeconds(5));
		}
		finally
		{
			evaluator.ReleaseCancellationCallback();
		}
		await mutation.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.CouldNotEvaluate);

		evaluator.ReentrantExitResult.Should().BeFalse();
		evaluator.CallCount.Should().Be(2);
		coordinator.State.Identity.Should().NotBeNull();
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.CouldNotEvaluate);
		coordinator.Dispose();
	}

	[Fact]
	public void SimulatorUnavailableProjection_ReleasesGateWithoutCacheOrFallback()
	{
		var local = new RecordingLocalStore(bytes: null);
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			local,
			evaluator,
			FullProbabilitySettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: false));

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.SimulatorUnavailable);
		coordinator.State.Identity.Should().BeNull();
		coordinator.EvaluationBlocksLobbyExit.Should().BeFalse();
		coordinator.TryRequestLobbyExit().Should().BeTrue();
		local.ReadCount.Should().Be(0);
		evaluator.CallCount.Should().Be(0);
	}

	[Fact]
	public async Task IdentityChange_ClearsRetainedCouldNotEvaluateAndRejectsOldRetry()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var clock = new ManualTimeProvider();
		var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingLocalStore(bytes: null),
			new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
			FullProbabilitySettings,
			clock);
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.CouldNotEvaluate);
		var failedIdentity = coordinator.State.Identity;

		ChangeToFourWerewolves(lobby);

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity.Should().NotBe(failedIdentity);
		coordinator.RetryCurrent().Should().BeFalse();
		coordinator.Dispose();
	}

	[Fact]
	public async Task RepeatedExitAttemptsAndSeatingEdit_DoNotDuplicateFallback()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var evaluator = new ControlledEvaluator();
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingLocalStore(bytes: null),
			evaluator,
			FullProbabilitySettings,
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		var call = await evaluator.NextCallAsync();
		lobby.MovePlayerDown(0).Should().BeTrue();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		evaluator.CallCount.Should().Be(1);

		call.Complete(new AlreadyDecidedTerminalEvaluation(
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut));
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);
		evaluator.CallCount.Should().Be(1);
	}

	[Fact]
	public async Task CacheReadIoFailures_AreMissesAndFallbackStillPersists()
	{
		var local = new ReadFailingLocalStore();
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			local,
			new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut)),
			FullProbabilitySettings,
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		local.Writes.Should().ContainSingle();
	}

	[Fact]
	public async Task UnexpectedTerminalCapture_PublishesCouldNotAndPersistsNothing()
	{
		var local = new RecordingLocalStore(bytes: null);
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			local,
			new RecordingEvaluator(new UnexpectedTerminalEvaluation()),
			FullProbabilitySettings,
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.CouldNotEvaluate);

		local.Writes.Should().BeEmpty();
		coordinator.TryRequestLobbyExit().Should().BeTrue();
	}

	[Fact]
	public async Task SafetyDegenerateFallback_PersistsReadableCurrentRecordAndBlocksExit()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new RecordingLocalStore(bytes: null);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(SafetyDegenerateEvaluation(lobby.CreateSimulationScenario())),
			SafetyScreeningSettings,
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.Degenerate);

		coordinator.EvaluationBlocksLobbyExit.Should().BeTrue();
		var persisted = TerminalLobbyCache.ReadDocument(local.Writes.Single());
		persisted.IsUsable.Should().BeTrue();
		TerminalLobbyCache.TryGet(
			persisted.Document!,
			coordinator.State.Identity!,
			out var record).Should().BeTrue();
		record.Should().BeOfType<DegenerateTerminalCacheRecord>();
	}

	[Theory]
	[InlineData(MainRoleType.SimpleWerewolf, MainRoleType.BigBadWolf, 2_000)]
	[InlineData(MainRoleType.Seer, MainRoleType.Defender, 3_000)]
	public async Task ActorReachableThiefDegenerateFallback_PersistsExactCurrentRecordAndBlocksExit(
		MainRoleType offer1,
		MainRoleType offer2,
		int expectedAttemptCount)
	{
		var lobby = CreateAcceptedActorThiefLobby(offer1, offer2);
		var scenario = lobby.CreateSimulationScenario();
		var local = new RecordingLocalStore(bytes: null);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(SafetyDegenerateEvaluation(scenario)),
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.Degenerate);

		coordinator.EvaluationBlocksLobbyExit.Should().BeTrue();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		coordinator.State.Identity!.Scenario.ActorSetupCards.Should().NotBeEmpty();
		coordinator.State.Identity.Scenario.ThiefOfferBranchPolicy!.Branches.Should().HaveCount(
			expectedAttemptCount / TerminalLobbyEvaluator.ScreeningAttemptCount);
		var persisted = TerminalLobbyCache.ReadDocument(local.Writes.Single());
		persisted.IsUsable.Should().BeTrue();
		TerminalLobbyCache.TryGet(
			persisted.Document!,
			coordinator.State.Identity,
			out var record).Should().BeTrue();
		var aggregate = record.Should().BeOfType<DegenerateTerminalCacheRecord>().Subject;
		aggregate.AttemptedRunCount.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.CompletedRunCount.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.IncompleteRunCount.Should().Be(0);
	}

	[Theory]
	[InlineData(MainRoleType.SimpleWerewolf, MainRoleType.BigBadWolf, 2_000, false)]
	[InlineData(MainRoleType.SimpleWerewolf, MainRoleType.BigBadWolf, 2_000, true)]
	[InlineData(MainRoleType.Seer, MainRoleType.Defender, 3_000, false)]
	[InlineData(MainRoleType.Seer, MainRoleType.Defender, 3_000, true)]
	public async Task ActorReachableThiefDegenerateBranchWithMixedSiblingEvidence_PersistsAndBlocksExit(
		MainRoleType offer1,
		MainRoleType offer2,
		int expectedAttemptCount,
		bool incompleteSibling)
	{
		var lobby = CreateAcceptedActorThiefLobby(offer1, offer2);
		var scenario = lobby.CreateSimulationScenario();
		var evaluation = SafetyMixedBranchDegenerateEvaluation(scenario, incompleteSibling);
		var local = new RecordingLocalStore(bytes: null);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(evaluation),
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.Degenerate);

		evaluation.ScreeningEvidence.AttemptedRunCount.Should().Be(expectedAttemptCount);
		evaluation.ScreeningEvidence.IncompleteRunCount.Should().Be(incompleteSibling ? 1 : 0);
		coordinator.EvaluationBlocksLobbyExit.Should().BeTrue();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		var persisted = TerminalLobbyCache.ReadDocument(local.Writes.Single());
		persisted.IsUsable.Should().BeTrue();
		TerminalLobbyCache.TryGet(
			persisted.Document!,
			coordinator.State.Identity!,
			out var record).Should().BeTrue();
		var aggregate = record.Should().BeOfType<DegenerateTerminalCacheRecord>().Subject;
		aggregate.AttemptedRunCount.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.CompletedRunCount.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.IncompleteRunCount.Should().Be(0);
	}

	[Fact]
	public async Task NonActorThiefDegenerateBranchWithIncompleteSibling_ActualEvaluatorPersistsAndBlocksExit()
	{
		var lobby = CreateAcceptedThiefLobby(
			MainRoleType.Seer,
			MainRoleType.Defender);
		var scenario = lobby.CreateSimulationScenario();
		var requestedAttemptCounts = new List<int>();
		var terminalEvaluator = new TerminalLobbyEvaluator(
			(batchScenario, identity, count, _) =>
			{
				requestedAttemptCounts.Add(count);
				return SafetyMixedBranchSourceEvidence(
					batchScenario,
					identity,
					count,
					incompleteSibling: true);
			});
		var local = new RecordingLocalStore(bytes: null);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new AsyncTerminalLobbyEvaluator(
				terminalEvaluator.Evaluate,
				TimeProvider.System),
			SafetyScreeningSettings,
			TimeProvider.System,
			(_, _) => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: true));

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.Degenerate);

		requestedAttemptCounts.Should().Equal(3_000);
		coordinator.State.Identity!.Scenario.ActorSetupCards.Should().BeEmpty();
		coordinator.State.Identity.Scenario.ThiefOfferBranchPolicy!.Branches.Should().HaveCount(3);
		coordinator.EvaluationBlocksLobbyExit.Should().BeTrue();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		var persisted = TerminalLobbyCache.ReadDocument(local.Writes.Single());
		persisted.IsUsable.Should().BeTrue();
		TerminalLobbyCache.TryGet(
			persisted.Document!,
			coordinator.State.Identity,
			out var record).Should().BeTrue();
		var aggregate = record.Should().BeOfType<DegenerateTerminalCacheRecord>().Subject;
		aggregate.AttemptedRunCount.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.CompletedRunCount.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.IncompleteRunCount.Should().Be(0);
		aggregate.GameResultFrequencyByTurn.Should().OnlyContain(cell => cell.EndingTurn == 1);
	}

	[Fact]
	public void ScenarioChange_DuringLocalReadStopsStalePipelineBeforeFallback()
	{
		var pump = new ControlledContinuationPump();
		pump.Run(() =>
		{
			var clock = new ManualTimeProvider();
			var lobby = CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
			var local = new ControlledReadLocalStore();
			var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
			var coordinator = new LobbyEvaluationCoordinator(
				lobby,
				local,
				evaluator,
				FullProbabilitySettings,
				clock);
			pump.Drain();
			var staleRead = local.NextReadAsync().GetAwaiter().GetResult();

			ChangeToVillagerMajority(lobby);
			pump.Drain();
			var currentRead = local.NextReadAsync().GetAwaiter().GetResult();
			staleRead.Complete(bytes: null);
			pump.Drain();
			clock.Advance(TimeSpan.FromMilliseconds(500));
			pump.Drain();

			evaluator.CallCount.Should().Be(0);
			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			coordinator.Dispose();
			currentRead.Complete(bytes: null);
			pump.Drain();
		});
	}

	[Fact]
	public void EquivalentRoleLockInReplacement_DuringLocalReadPreservesExactCurrentPipeline()
	{
		var pump = new ControlledContinuationPump();
		pump.Run(() =>
		{
			var lobby = CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
			var manager = new GameClientManager();
			manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
			var originalLockIn = lobby.AcceptedRoleLockIn!;
			var local = new ControlledReadLocalStore();
			var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
			using var coordinator = new LobbyEvaluationCoordinator(
				lobby,
				local,
				evaluator,
				SafetyScreeningSettings,
				new ManualTimeProvider());
			pump.Drain();
			var read = local.NextReadAsync().GetAwaiter().GetResult();
			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			var pendingIdentity = coordinator.State.Identity!;
			var replacement = RoleLockIn.CreateFromPrintedRoles(
				originalLockIn.Version + 1,
				originalLockIn.PlayerCount,
				originalLockIn.RoleComposition.Select(card => card.PrintedRole));

			manager.TryReplaceStagedRoleLockIn(
				lobby,
				originalLockIn.Version,
				replacement).Should().BeTrue();
			pump.Drain();

			var replacementLockIn = lobby.AcceptedRoleLockIn!;
			replacementLockIn.Version.Should().BeGreaterThan(originalLockIn.Version);
			replacementLockIn.RoleComposition.Select(card => card.Id).Should().NotIntersectWith(
				originalLockIn.RoleComposition.Select(card => card.Id));
			lobby.CreateSimulationScenario().ToCanonical().Should().Be(pendingIdentity.Scenario);
			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			coordinator.State.Identity.Should().Be(pendingIdentity);
			coordinator.EvaluationBlocksLobbyExit.Should().BeTrue();
			read.CancellationToken.IsCancellationRequested.Should().BeFalse();
			local.ReadCount.Should().Be(1);
			evaluator.CallCount.Should().Be(0);

			var exactLocal = new DegenerateTerminalCacheRecord(
				pendingIdentity,
				AggregateRows(1_000, 750, 250),
				AggregateCells(1_000, 750, 250, turnOneOnly: true));
			read.Complete(DocumentBytes(exactLocal));
			pump.Drain();

			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Degenerate);
			coordinator.State.Identity.Should().Be(pendingIdentity);
			local.ReadCount.Should().Be(1);
			evaluator.CallCount.Should().Be(0);
		});
	}

	[Fact]
	public async Task ScenarioChange_AtCommitBoundaryRejectsStaleWriteAndCurrentWriteSurvives()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new ControlledCommitLocalStore();
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut)),
			FullProbabilitySettings,
			TimeProvider.System);
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		var staleWrite = await local.NextWriteAsync();
		await staleWrite.CommitBoundary.WaitAsync(TimeSpan.FromSeconds(5));

		ChangeToFourWerewolves(lobby);
		var currentIdentity = coordinator.State.Identity;
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		staleWrite.ReleaseCommitBoundary();
		await staleWrite.Disposed.WaitAsync(TimeSpan.FromSeconds(5));
		var currentWrite = await local.NextWriteAsync();
		await currentWrite.CommitBoundary.WaitAsync(TimeSpan.FromSeconds(5));

		local.CommittedBytes.Should().BeNull("the stale request was denied at the commit boundary");
		currentWrite.ReleaseCommitBoundary();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		var persisted = TerminalLobbyCache.ReadDocument(local.CommittedBytes!.Value.Span);
		persisted.IsUsable.Should().BeTrue();
		persisted.Document!.Records.Should().ContainSingle(record =>
			record.CompatibilityIdentity.Equals(currentIdentity));
	}

	[Fact]
	public async Task ScenarioChange_DuringStagedWritePreventsAnyCommitAttempt()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var clock = new ManualTimeProvider();
		var local = new ControlledStageLocalStore();
		var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut)),
			FullProbabilitySettings,
			clock);
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await local.StageStarted.WaitAsync(TimeSpan.FromSeconds(5));

		ChangeToFourWerewolves(lobby);
		local.ReleaseStage();
		await local.StagedWriteDisposed.WaitAsync(TimeSpan.FromSeconds(5));

		local.CommitAttemptCount.Should().Be(0);
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.Dispose();
	}

	[Fact]
	public async Task CompactAggregateRecords_MapToTheirTerminalGateMeanings()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var identity = new SimulationCompatibilityIdentity(
			lobby.CreateSimulationScenario().ToCanonical(),
			SimulatorCapability.FullProbability.Identity);
		var rows1000 = AggregateRows(1_000, 750, 250);
		var cells1000 = AggregateCells(1_000, 750, 250, turnOneOnly: true);
		var rows10000 = AggregateRows(10_000, 7_000, 3_000);
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var cells10000 = new TerminalCacheTurnWindowFrequency[]
		{
			new(villager, 1, VictoryCheckWindow.Dawn, 3_000, 10_000),
			new(villager, 1, VictoryCheckWindow.PreNight, 4_000, 10_000),
			new(werewolf, 2, VictoryCheckWindow.PreNight, 3_000, 10_000)
		};
		var cases = new (TerminalLobbyCacheRecord Record, LobbyEvaluationStateKind Kind, bool Blocks)[]
		{
			(new DegenerateTerminalCacheRecord(identity, rows1000, cells1000),
				LobbyEvaluationStateKind.Degenerate, true),
			(new ProbabilityTerminalCacheRecord(identity, rows10000, cells10000),
				LobbyEvaluationStateKind.Probability, false)
		};

		foreach (var @case in cases)
		{
			using var coordinator = new LobbyEvaluationCoordinator(
				lobby,
				new RecordingLocalStore(DocumentBytes(@case.Record)),
				new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
				FullProbabilitySettings,
				TimeProvider.System);
			await WaitForStateAsync(coordinator, @case.Kind);
			coordinator.Depth.Should().Be(LobbyEvaluationDepth.FullProbability);

			if (@case.Kind == LobbyEvaluationStateKind.Probability)
			{
				var projectedVillager = coordinator.State.Probability!.Outcomes
					.Single(outcome => outcome.GameResult.Equals(villager));
				projectedVillager.Turns.Should().ContainSingle().Which.Should().Be(
					new LobbyProbabilityTurnData(1, 7_000, 10_000));
			}
			else
			{
				coordinator.State.Probability.Should().BeNull();
			}
			coordinator.EvaluationBlocksLobbyExit.Should().Be(@case.Blocks);
			coordinator.TryRequestLobbyExit().Should().Be(!@case.Blocks);
		}
	}

	[Fact]
	public async Task FullProbabilityAtScreeningDepth_CachedProbabilityMeansScreeningPassedWithoutProbabilityData()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var identity = new SimulationCompatibilityIdentity(
			lobby.CreateSimulationScenario().ToCanonical(),
			SimulatorCapability.FullProbability.Identity);
		var cachedProbability = new ProbabilityTerminalCacheRecord(
			identity,
			AggregateRows(10_000, 7_000, 3_000),
			AggregateCells(10_000, 7_000, 3_000, turnOneOnly: false));
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());

		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingLocalStore(DocumentBytes(cachedProbability)),
			evaluator,
			new LobbyEvaluationSettings(
				SimulatorCapability.FullProbability,
				LobbyEvaluationDepth.DegenerateScreeningOnly),
			TimeProvider.System);
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.ScreeningPassed);

		coordinator.Depth.Should().Be(LobbyEvaluationDepth.DegenerateScreeningOnly);
		coordinator.State.Probability.Should().BeNull();
		coordinator.EvaluationBlocksLobbyExit.Should().BeFalse();
		coordinator.TryRequestLobbyExit().Should().BeTrue();
		evaluator.CallCount.Should().Be(0);
	}

	[Fact]
	public async Task DegenerateScreeningOnly_CachedDegenerateResultStillBlocksWithoutFallback()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var identity = new SimulationCompatibilityIdentity(
			lobby.CreateSimulationScenario().ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var cachedDegenerate = new DegenerateTerminalCacheRecord(
			identity,
			AggregateRows(1_000, 750, 250),
			AggregateCells(1_000, 750, 250, turnOneOnly: true));
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());

		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingLocalStore(DocumentBytes(cachedDegenerate)),
			evaluator,
			SafetyScreeningSettings,
			TimeProvider.System);
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.Degenerate);

		coordinator.State.Probability.Should().BeNull();
		coordinator.EvaluationBlocksLobbyExit.Should().BeTrue();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		evaluator.CallCount.Should().Be(0);
	}

	[Fact]
	public async Task ActorDegenerateScreeningOnly_ScreeningPassedFallbackIsSessionLocalAndNotPersisted()
	{
		var lobby = CreateAcceptedActorLobby();
		var local = new RecordingLocalStore(bytes: null);
		var screeningPassed = new ScreeningPassedLobbyEvaluation();
		var evaluator = new RecordingEvaluator(screeningPassed);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			SafetyScreeningSettings,
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.ScreeningPassed);

		coordinator.State.Probability.Should().BeNull();
		coordinator.State.Identity!.Profile.Should().Be(
			new SimulatorProfileIdentity("safety-screening", "30"));
		coordinator.State.Identity.Scenario.ActorSetupCards.Should().Equal(
			MainRoleType.Cupid,
			MainRoleType.Defender,
			MainRoleType.Elder);
		coordinator.TryRequestLobbyExit().Should().BeTrue();
		local.Writes.Should().BeEmpty();
		evaluator.Capabilities.Should().Equal(SimulatorCapability.SafetyScreening);
		evaluator.Depths.Should().Equal(LobbyEvaluationDepth.DegenerateScreeningOnly);
	}

	[Fact]
	public async Task EquivalentActorSetupReplacement_PreservesSessionLocalScreeningPassed()
	{
		var lobby = CreateAcceptedActorLobby();
		var originalSetup = lobby.AcceptedActorSetupCards;
		var originalScenario = lobby.CreateSimulationScenario();
		var local = new RecordingLocalStore(bytes: null);
		var evaluator = new RecordingEvaluator(new ScreeningPassedLobbyEvaluation());
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			SafetyScreeningSettings,
			TimeProvider.System);
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.ScreeningPassed);
		var screenedIdentity = coordinator.State.Identity;
		var localReadCount = local.ReadCount;
		var manager = new GameClientManager();

		manager.TryReplaceStagedActorSetupCards(
			lobby,
			originalSetup.Version,
			[
				MainRoleType.Elder,
				MainRoleType.Defender,
				MainRoleType.Cupid
			]).Should().BeTrue();

		lobby.AcceptedActorSetupCards.Version.Should().BeGreaterThan(originalSetup.Version);
		lobby.AcceptedActorSetupCards.Cards.Select(card => card.Id).Should().NotIntersectWith(
			originalSetup.Cards.Select(card => card.Id));
		lobby.CreateSimulationScenario().ToCanonical().Should().Be(originalScenario.ToCanonical());
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.ScreeningPassed);
		coordinator.State.Identity.Should().Be(screenedIdentity);
		coordinator.EvaluationBlocksLobbyExit.Should().BeFalse();
		coordinator.TryRequestLobbyExit().Should().BeTrue();
		evaluator.CallCount.Should().Be(1);
		local.ReadCount.Should().Be(localReadCount);
		local.Writes.Should().BeEmpty();
	}

	[Fact]
	public async Task SuccessfulFallback_PreservesOtherValidLocalRecords()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var other = AlreadyDecidedRecord(new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager
			]));
		var local = new RecordingLocalStore(DocumentBytes(other));
		var evaluator = new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut));
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			FullProbabilitySettings,
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);
		evaluator.Depths.Should().Equal(LobbyEvaluationDepth.FullProbability);

		var written = TerminalLobbyCache.ReadDocument(local.Writes.Single());
		written.IsUsable.Should().BeTrue();
		written.Document!.Records.Should().HaveCount(2);
		written.Document.Records.Should().Contain(record =>
			record.CompatibilityIdentity.Equals(other.CompatibilityIdentity));
		written.Document.Records.Should().ContainSingle(record =>
			record.CompatibilityIdentity.Equals(coordinator.State.Identity));
	}

	[Fact]
	public async Task SuccessfulFallback_ReplacesCorruptLocalDocumentWithOneCanonicalRecord()
	{
		var local = new RecordingLocalStore("corrupt"u8.ToArray());
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			local,
			new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut)),
			FullProbabilitySettings,
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		var written = TerminalLobbyCache.ReadDocument(local.Writes.Single());
		written.IsUsable.Should().BeTrue();
		written.Document!.Records.Should().ContainSingle();
	}

	[Fact]
	public async Task StaleProfileLocalDocument_IsMissAndAcceleratedFallbackPersistsOnlyCurrentCanonicalRecord()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var currentRecord = AlreadyDecidedRecord(lobby.CreateSimulationScenario());
		var staleBytes = Encoding.UTF8.GetBytes(
			Encoding.UTF8.GetString(DocumentBytes(currentRecord)).Replace(
				"full-probability@4",
				"full-probability@3",
				StringComparison.Ordinal));
		var local = new RecordingLocalStore(staleBytes);
		var evaluator = new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut));
		var clock = new ManualTimeProvider();
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			FullProbabilitySettings,
			clock);

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		evaluator.CallCount.Should().Be(1);
		evaluator.Capabilities.Should().Equal(SimulatorCapability.FullProbability);
		evaluator.Depths.Should().Equal(LobbyEvaluationDepth.FullProbability);
		var writtenBytes = local.Writes.Should().ContainSingle().Subject;
		var written = TerminalLobbyCache.ReadDocument(writtenBytes);
		written.IsUsable.Should().BeTrue();
		written.Document!.Records.Should().ContainSingle()
			.Which.CompatibilityIdentity.Should().Be(currentRecord.CompatibilityIdentity);
		Encoding.UTF8.GetString(writtenBytes).Should()
			.Contain("full-probability@4")
			.And.NotContain("full-probability@3");
	}

	[Theory]
	[InlineData(PersistenceFailure.Stage)]
	[InlineData(PersistenceFailure.Commit)]
	public async Task SafetyDegenerateFallback_PersistenceFailureKeepsPriorBytesAndBlockingState(
		PersistenceFailure failure)
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var priorRecord = AlreadyDecidedRecord(new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]));
		var priorBytes = DocumentBytes(priorRecord);
		IFailingLocalStore local = failure switch
		{
			PersistenceFailure.Stage => new ThrowingStageLocalStore(priorBytes),
			PersistenceFailure.Commit => new ThrowingCommitLocalStore(priorBytes),
			_ => throw new ArgumentOutOfRangeException(nameof(failure))
		};
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			new RecordingEvaluator(SafetyDegenerateEvaluation(lobby.CreateSimulationScenario())),
			SafetyScreeningSettings,
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.Degenerate);

		local.Bytes.Should().NotBeNull();
		local.Bytes!.Value.ToArray().Should().Equal(priorBytes);
		coordinator.EvaluationBlocksLobbyExit.Should().BeTrue();
	}

	[Fact]
	public void Dispose_CancelsOutstandingWorkAndPreventsLatePublicationAndPersistence()
	{
		var pump = new ControlledContinuationPump();
		pump.Run(() =>
		{
			var evaluator = new ControlledEvaluator();
			var local = new RecordingLocalStore(bytes: null);
			var coordinator = new LobbyEvaluationCoordinator(
				CreateLobby(
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager),
				local,
				evaluator,
				FullProbabilitySettings,
				TimeProvider.System);
			var transitions = 0;
			coordinator.StateChanged += (_, _) => transitions++;
			coordinator.TryRequestLobbyExit().Should().BeFalse();
			pump.Drain();
			var call = evaluator.NextCallAsync().GetAwaiter().GetResult();

			coordinator.Dispose();
			call.CancellationToken.IsCancellationRequested.Should().BeTrue();
			call.Complete(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut));
			pump.Drain();

			transitions.Should().Be(0);
			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			local.Writes.Should().BeEmpty();
			coordinator.TryRequestLobbyExit().Should().BeFalse();
		});
	}

	[Fact]
	public async Task LocalHit_PreventsFallbackAndUsesTheSameProvenanceFreeState()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var record = AlreadyDecidedRecord(lobby.CreateSimulationScenario());
		var local = new RecordingLocalStore(DocumentBytes(record));
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			FullProbabilitySettings,
			TimeProvider.System);

		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		coordinator.State.Identity.Should().Be(record.CompatibilityIdentity);
		coordinator.State.DecidedGameResult.Should().Be(record.GameResult);
		coordinator.State.DecidedReason.Should().Be(record.Reason);
		coordinator.State.Should().NotBeNull();
		local.ReadCount.Should().Be(1);
		evaluator.CallCount.Should().Be(0);
	}

	[Fact]
	public async Task QuietPeriod_UsesInjectedClockAndStartsFallbackAtExactlyFiveHundredMilliseconds()
	{
		var clock = new ManualTimeProvider();
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			new RecordingLocalStore(bytes: null),
			evaluator,
			FullProbabilitySettings,
			clock);
		await WaitUntilAsync(() => coordinator.State.Kind == LobbyEvaluationStateKind.Pending);

		clock.Advance(TimeSpan.FromMilliseconds(499));
		evaluator.CallCount.Should().Be(0);
		clock.Advance(TimeSpan.FromMilliseconds(1));
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.CouldNotEvaluate);

		evaluator.CallCount.Should().Be(1);
		coordinator.TryRequestLobbyExit().Should().BeTrue();
	}

	[Fact]
	public void ScenarioChange_PreventsLateResultFromPublishingOrPersisting()
	{
		var pump = new ControlledContinuationPump();
		pump.Run(() =>
		{
			var lobby = CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
			var evaluator = new ControlledEvaluator();
			var local = new RecordingLocalStore(bytes: null);
			using var coordinator = new LobbyEvaluationCoordinator(
				lobby,
				local,
				evaluator,
				FullProbabilitySettings,
				TimeProvider.System);
			coordinator.TryRequestLobbyExit().Should().BeFalse();
			pump.Drain();
			var first = evaluator.NextCallAsync().GetAwaiter().GetResult();

			lobby.DecrementRole(MainRoleType.SimpleWerewolf);
			lobby.IncrementRole(MainRoleType.SimpleVillager);
			var currentIdentity = coordinator.State.Identity;
			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			currentIdentity.Should().NotBe(first.Identity);
			first.CancellationToken.IsCancellationRequested.Should().BeTrue();
			coordinator.TryRequestLobbyExit().Should().BeFalse(
				"the atomic exit decision must apply to the new pending snapshot");
			pump.Drain();
			var current = evaluator.NextCallAsync().GetAwaiter().GetResult();

			first.Complete(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut));
			pump.Drain();

			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			coordinator.State.Identity.Should().Be(currentIdentity);
			local.Writes.Should().BeEmpty();
			current.Complete(new CouldNotEvaluateLobbyEvaluation());
			pump.Drain();
			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.CouldNotEvaluate);
		});
	}

	[Fact]
	public async Task RetryCurrent_IsAcceptedOnlyForCurrentFailureAndImmediatelyReturnsToPending()
	{
		var evaluator = new QueueEvaluator(
			new CouldNotEvaluateLobbyEvaluation(),
			new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut));
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			new RecordingLocalStore(bytes: null),
			evaluator,
			FullProbabilitySettings,
			TimeProvider.System);

		coordinator.RetryCurrent().Should().BeFalse();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.CouldNotEvaluate);
		var failedIdentity = coordinator.State.Identity;

		coordinator.RetryCurrent().Should().BeTrue();
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity.Should().Be(failedIdentity);
		coordinator.RetryCurrent().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		evaluator.CallCount.Should().Be(2);
	}

	[Fact]
	public async Task TryRequestLobbyExit_PendingMissAcceleratesFallbackButDoesNotPermitExit()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var evaluator = new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut));
		var local = new RecordingLocalStore(bytes: null);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			FullProbabilitySettings,
			TimeProvider.System);

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.TryRequestLobbyExit().Should().BeFalse(
			"a pending request accelerates fallback but cannot skip it");
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		evaluator.CallCount.Should().Be(1);
		local.Writes.Should().ContainSingle();
		var persisted = TerminalLobbyCache.ReadDocument(local.Writes.Single());
		persisted.IsUsable.Should().BeTrue();
		TerminalLobbyCache.TryGet(
			persisted.Document!,
			coordinator.State.Identity!,
			out var record).Should().BeTrue();
		record.Should().BeOfType<AlreadyDecidedTerminalCacheRecord>();
		coordinator.TryRequestLobbyExit().Should().BeFalse(
			"already-decided evaluation blocks Lobby Exit");
	}

	[Fact]
	public async Task ExactCurrentLocal_IgnoresMismatchedRecordInTheSameDocument()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var scenario = lobby.CreateSimulationScenario();
		var consumerIdentity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var mismatchedIdentity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.FullProbability.Identity);
		var exactLocal = new DegenerateTerminalCacheRecord(
			consumerIdentity,
			AggregateRows(1_000, 750, 250),
			AggregateCells(1_000, 750, 250, turnOneOnly: true));
		var mismatchedRecord = new ProbabilityTerminalCacheRecord(
			mismatchedIdentity,
			AggregateRows(10_000, 7_000, 3_000),
			AggregateCells(10_000, 7_000, 3_000, turnOneOnly: false));
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		var local = new RecordingLocalStore(DocumentBytes(mismatchedRecord, exactLocal));
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			new LobbyEvaluationSettings(
				SimulatorCapability.SafetyScreening,
				LobbyEvaluationDepth.DegenerateScreeningOnly),
			TimeProvider.System);

		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.Degenerate);

		coordinator.State.Identity.Should().Be(consumerIdentity);
		evaluator.CallCount.Should().Be(0);
		local.Writes.Should().BeEmpty();
	}

	[Fact]
	public async Task EquivalentActorSetupReplacement_ReusesExactCurrentLocalDegenerateRecord()
	{
		var lobby = CreateAcceptedActorLobby();
		var originalSetup = lobby.AcceptedActorSetupCards;
		var originalScenario = lobby.CreateSimulationScenario();
		var identity = new SimulationCompatibilityIdentity(
			originalScenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var exactLocal = new DegenerateTerminalCacheRecord(
			identity,
			AggregateRows(1_000, 750, 250),
			AggregateCells(1_000, 750, 250, turnOneOnly: true));
		var manager = new GameClientManager();

		manager.TryReplaceStagedActorSetupCards(
			lobby,
			originalSetup.Version,
			[
				MainRoleType.Elder,
				MainRoleType.Defender,
				MainRoleType.Cupid
			]).Should().BeTrue();
		var replacementSetup = lobby.AcceptedActorSetupCards;
		var replacementScenario = lobby.CreateSimulationScenario();
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		var local = new RecordingLocalStore(DocumentBytes(exactLocal));

		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			SafetyScreeningSettings,
			TimeProvider.System);
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.Degenerate);

		replacementSetup.Version.Should().BeGreaterThan(originalSetup.Version);
		replacementSetup.PrintedRoles.Should().Equal(
			MainRoleType.Elder,
			MainRoleType.Defender,
			MainRoleType.Cupid);
		replacementSetup.Cards.Select(card => card.Id).Should().NotIntersectWith(
			originalSetup.Cards.Select(card => card.Id));
		replacementScenario.ToCanonical().Should().Be(originalScenario.ToCanonical());
		coordinator.State.Identity.Should().Be(identity);
		evaluator.CallCount.Should().Be(0);
		local.Writes.Should().BeEmpty();
	}

	[Fact]
	public async Task EquivalentRoleLockInReplacement_PreservesExactCurrentLocalDegenerateResult()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var manager = new GameClientManager();
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var originalLockIn = lobby.AcceptedRoleLockIn!;
		var originalScenario = lobby.CreateSimulationScenario();
		var identity = new SimulationCompatibilityIdentity(
			originalScenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var exactLocal = new DegenerateTerminalCacheRecord(
			identity,
			AggregateRows(1_000, 750, 250),
			AggregateCells(1_000, 750, 250, turnOneOnly: true));
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		var local = new RecordingLocalStore(DocumentBytes(exactLocal));
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			SafetyScreeningSettings,
			TimeProvider.System);
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.Degenerate);
		var localReadCount = local.ReadCount;
		var replacement = RoleLockIn.CreateFromPrintedRoles(
			originalLockIn.Version + 1,
			originalLockIn.PlayerCount,
			originalLockIn.RoleComposition.Select(card => card.PrintedRole));

		manager.TryReplaceStagedRoleLockIn(
			lobby,
			originalLockIn.Version,
			replacement).Should().BeTrue();

		var replacementLockIn = lobby.AcceptedRoleLockIn!;
		replacementLockIn.Version.Should().BeGreaterThan(originalLockIn.Version);
		replacementLockIn.RoleComposition.Select(card => card.Id).Should().NotIntersectWith(
			originalLockIn.RoleComposition.Select(card => card.Id));
		lobby.CreateSimulationScenario().ToCanonical().Should().Be(originalScenario.ToCanonical());
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Degenerate);
		coordinator.State.Identity.Should().Be(identity);
		coordinator.EvaluationBlocksLobbyExit.Should().BeTrue();
		evaluator.CallCount.Should().Be(0);
		local.ReadCount.Should().Be(localReadCount);
		local.Writes.Should().BeEmpty();
	}

	[Fact]
	public async Task MismatchedLocalRecord_IsAMissAndFallsBackUnderCurrentSafetyIdentity()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var scenario = lobby.CreateSimulationScenario();
		var consumerIdentity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var mismatchedRecord = new ProbabilityTerminalCacheRecord(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.FullProbability.Identity),
			AggregateRows(10_000, 7_000, 3_000),
			AggregateCells(10_000, 7_000, 3_000, turnOneOnly: false));
		var local = new RecordingLocalStore(DocumentBytes(mismatchedRecord));
		var evaluator = new RecordingEvaluator(new ScreeningPassedLobbyEvaluation());
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			new LobbyEvaluationSettings(
				SimulatorCapability.SafetyScreening,
				LobbyEvaluationDepth.DegenerateScreeningOnly),
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.ScreeningPassed);

		coordinator.State.Identity.Should().Be(consumerIdentity);
		evaluator.CallCount.Should().Be(1);
		local.Writes.Should().BeEmpty();
	}

	[Theory]
	[InlineData("safety-screening@29")]
	[InlineData("safety-screening@28")]
	[InlineData("safety-screening@27")]
	[InlineData("safety-screening@21")]
	[InlineData("core-simulator@1")]
	[InlineData("foreign-simulator@1")]
	public async Task NonCurrentSafetyLocalRecord_IsAMissBeforeBoundedFallback(
		string rejectedProfile)
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var scenario = lobby.CreateSimulationScenario();
		var consumerIdentity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var currentRecord = new DegenerateTerminalCacheRecord(
			consumerIdentity,
			AggregateRows(1_000, 750, 250),
			AggregateCells(1_000, 750, 250, turnOneOnly: true));
		var nonCurrentBytes = Encoding.UTF8.GetBytes(
			Encoding.UTF8.GetString(DocumentBytes(currentRecord)).Replace(
				SimulatorCapability.SafetyScreening.Identity.ToString(),
				rejectedProfile,
				StringComparison.Ordinal));
		var local = new RecordingLocalStore(nonCurrentBytes);
		var evaluator = new RecordingEvaluator(new ScreeningPassedLobbyEvaluation());
		var clock = new ManualTimeProvider();
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			new LobbyEvaluationSettings(
				SimulatorCapability.SafetyScreening,
				LobbyEvaluationDepth.DegenerateScreeningOnly),
			clock);

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.ScreeningPassed);

		coordinator.State.Identity.Should().Be(consumerIdentity);
		evaluator.CallCount.Should().Be(1);
		evaluator.Capabilities.Should().Equal(SimulatorCapability.SafetyScreening);
		evaluator.Depths.Should().Equal(LobbyEvaluationDepth.DegenerateScreeningOnly);
		local.Writes.Should().BeEmpty();
	}

	[Fact]
	public async Task SuccessfulFallback_PreservesRecordsFromOtherCurrentProducerProfiles()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var scenario = lobby.CreateSimulationScenario();
		var mismatchedLocal = new AlreadyDecidedTerminalCacheRecord(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.FullProbability.Identity),
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut);
		var local = new RecordingLocalStore(DocumentBytes(mismatchedLocal));
		var evaluator = new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut));
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			local,
			evaluator,
			SafetyScreeningSettings,
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		evaluator.CallCount.Should().Be(1);
		var persisted = TerminalLobbyCache.ReadDocument(local.Writes.Single());
		persisted.IsUsable.Should().BeTrue();
		persisted.Document!.Records.Should().HaveCount(2);
		persisted.Document.Records.Should().Contain(record =>
			record.CompatibilityIdentity.Equals(mismatchedLocal.CompatibilityIdentity));
		persisted.Document.Records.Should().ContainSingle(record =>
			record.CompatibilityIdentity.Equals(coordinator.State.Identity));
	}

	[Fact]
	public void Construction_AppliesSupportGatesAndBuildsAlreadyDecidedIdentityWithoutCacheability()
	{
		using var invalid = CreateCoordinator(LobbySetupMetadataFixture.DefaultState());
		invalid.State.Kind.Should().Be(LobbyEvaluationStateKind.NotApplicable);
		invalid.State.BlocksLobbyExit.Should().BeFalse();

		var appUnsupportedLobby = CreateLobby(
			MainRoleType.PrejudicedManipulator,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		using var appUnsupported = CreateCoordinator(appUnsupportedLobby);
		appUnsupported.State.Kind.Should().Be(LobbyEvaluationStateKind.NotApplicable);
		appUnsupported.State.Identity.Should().BeNull();
		appUnsupported.State.BlocksLobbyExit.Should().BeFalse();

		var decidedLobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var scenario = decidedLobby.CreateSimulationScenario();
		SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability).Cacheability.Should().BeNull();
		using var decided = CreateCoordinator(decidedLobby);

		decided.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		decided.State.Identity.Should().Be(new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.FullProbability.Identity));
		decided.State.BlocksLobbyExit.Should().BeTrue();
	}

	private static LobbyEvaluationCoordinator CreateCoordinator(LobbySetupState lobby) =>
		new(
			lobby,
			new InMemoryTerminalLobbyCacheStore(),
			DisabledLobbyTerminalEvaluator.Instance,
			FullProbabilitySettings,
			TimeProvider.System);

	private static LobbyEvaluationSettings FullProbabilitySettings { get; } =
		new(
			SimulatorCapability.FullProbability,
			LobbyEvaluationDepth.FullProbability);

	private static LobbyEvaluationSettings SafetyScreeningSettings { get; } =
		new(
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

	private static LobbySetupState CreateLobby(params MainRoleType[] roles)
	{
		var lobby = LobbySetupMetadataFixture.StateWithRoles(roles.Distinct().ToArray());
		for (var index = 0; index < 5; index++)
		{
			lobby.AddPlayer($"Player {index + 1}");
		}

		foreach (var role in roles)
		{
			lobby.IncrementRole(role);
		}

		return lobby;
	}

	private static LobbySetupState CreateAcceptedActorLobby()
	{
		var lobby = CreateLobby(
			MainRoleType.Actor,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var manager = new GameClientManager();
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		manager.TryReplaceStagedActorSetupCards(
			lobby,
			expectedCurrentVersion: 0,
			[
				MainRoleType.Cupid,
				MainRoleType.Defender,
				MainRoleType.Elder
			]).Should().BeTrue();

		return lobby;
	}

	private static LobbySetupState CreateAcceptedActorThiefLobby(
		MainRoleType offer1,
		MainRoleType offer2)
	{
		var lobby = CreateLobby(
			MainRoleType.Actor,
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			offer1,
			offer2);
		var manager = new GameClientManager();
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			offer1,
			offer2).Should().BeTrue();
		manager.TryReplaceStagedActorSetupCards(
			lobby,
			expectedCurrentVersion: 0,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Elder
			]).Should().BeTrue();
		return lobby;
	}

	private static LobbySetupState CreateAcceptedThiefLobby(
		MainRoleType offer1,
		MainRoleType offer2)
	{
		var lobby = CreateLobby(
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			offer1,
			offer2);
		var manager = new GameClientManager();
		manager.TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion: 0,
			offer1,
			offer2).Should().BeTrue();
		return lobby;
	}

	private static void ChangeToVillagerMajority(LobbySetupState lobby)
	{
		lobby.DecrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
	}

	private static void ChangeToFourWerewolves(LobbySetupState lobby)
	{
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		lobby.DecrementRole(MainRoleType.SimpleVillager);
	}

	private static AlreadyDecidedTerminalCacheRecord AlreadyDecidedRecord(
		SimulationScenario scenario) =>
		new(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.FullProbability.Identity),
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut);

	private static byte[] DocumentBytes(params TerminalLobbyCacheRecord[] records) =>
		TerminalLobbyCache.Write(TerminalLobbyCache.CreateDocument(records));

	private static TerminalCacheGameResultFrequency[] AggregateRows(
		int denominator,
		int villager,
		int werewolf) =>
	[
		new(new SingleFactionGameResult(Faction.Villager), villager, denominator),
		new(new SingleFactionGameResult(Faction.Werewolf), werewolf, denominator),
		new(new NoWinnerGameResult(), 0, denominator)
	];

	private static TerminalCacheTurnWindowFrequency[] AggregateCells(
		int denominator,
		int villager,
		int werewolf,
		bool turnOneOnly) =>
	[
		new(new SingleFactionGameResult(Faction.Villager), 1, VictoryCheckWindow.Dawn,
			villager, denominator),
		new(new SingleFactionGameResult(Faction.Werewolf), turnOneOnly ? 1 : 2,
			VictoryCheckWindow.PreNight, werewolf, denominator)
	];

	private static DegenerateTerminalEvaluation SafetyDegenerateEvaluation(
		SimulationScenario scenario)
	{
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var attemptCount = TerminalLobbyEvaluator.GetScreeningAttemptCount(identity.Scenario);
		var villagerCount = attemptCount * 3 / 4;
		var runs = Enumerable.Range(0, attemptCount)
			.Select(index =>
			{
				var result = index < villagerCount ? (GameResult)villager : werewolf;
				return new CompletedSimulationRun(
					new RunSeedMaterial(
						identity,
						BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
						index),
					result,
					endingTurn: 1,
					result == villager
						? VictoryCheckWindow.Dawn
						: VictoryCheckWindow.PreNight);
			});
		var source = new SimulationBatchSourceEvidence(
			identity.Scenario,
			identity.Profile,
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runs);
		return new DegenerateTerminalEvaluation(new SimulationResultEvidence(
			source,
			[Faction.Villager, Faction.Werewolf],
			[villager, werewolf, noWinner]));
	}

	private static DegenerateTerminalEvaluation SafetyMixedBranchDegenerateEvaluation(
		SimulationScenario scenario,
		bool incompleteSibling)
	{
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var attemptCount = TerminalLobbyEvaluator.GetScreeningAttemptCount(identity.Scenario);
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var source = SafetyMixedBranchSourceEvidence(
			scenario,
			identity,
			attemptCount,
			incompleteSibling);
		return new DegenerateTerminalEvaluation(new SimulationResultEvidence(
			source,
			[Faction.Villager, Faction.Werewolf],
			[villager, werewolf, noWinner]));
	}

	private static SimulationBatchSourceEvidence SafetyMixedBranchSourceEvidence(
		SimulationScenario scenario,
		SimulationCompatibilityIdentity identity,
		int attemptCount,
		bool incompleteSibling)
	{
		var strategy = BaselineRandomDecisionStrategy.SafetyScreeningIdentity;
		var policy = identity.Scenario.ThiefOfferBranchPolicy!;
		var incompleteRunNumber = Enumerable.Range(0, attemptCount)
			.Last(run => policy.GetBranch(run) == policy.Branches[1]);
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var runs = Enumerable.Range(0, attemptCount).Select(run =>
		{
			var seed = new RunSeedMaterial(identity, strategy, run);
			if (incompleteSibling && run == incompleteRunNumber)
			{
				return (SimulationRun)new IncompleteSimulationRun(seed);
			}

			var result = run % 2 == 0 ? (GameResult)villager : werewolf;
			var provingBranch = policy.GetBranch(run) == policy.Branches[0];
			return new CompletedSimulationRun(
				seed,
				result,
				provingBranch ? 1 : 2,
				result == villager
					? VictoryCheckWindow.Dawn
					: VictoryCheckWindow.PreNight);
		});
		return new SimulationBatchSourceEvidence(
			scenario.ToCanonical(),
			identity.Profile,
			strategy,
			runs);
	}

	private static Task WaitForStateAsync(
		LobbyEvaluationCoordinator coordinator,
		LobbyEvaluationStateKind expected)
	{
		if (coordinator.State.Kind == expected)
		{
			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		EventHandler? handler = null;
		handler = (_, _) =>
		{
			if (coordinator.State.Kind != expected)
			{
				return;
			}

			coordinator.StateChanged -= handler;
			completion.TrySetResult();
		};
		coordinator.StateChanged += handler;
		if (coordinator.State.Kind == expected)
		{
			coordinator.StateChanged -= handler;
			completion.TrySetResult();
		}

		return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		for (var attempt = 0; attempt < 100 && !condition(); attempt++)
		{
			await Task.Yield();
		}
		condition().Should().BeTrue();
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
				throw new IOException(
					ClientTestReferences.ExceptionMessages.SaveFailed);
			}

			_payload = serializedSession;
		}

		public void Clear()
		{
			if (ThrowOnClear)
			{
				throw new IOException(
					ClientTestReferences.ExceptionMessages.SaveFailed);
			}

			_payload = null;
		}
	}

	private sealed class ControlledContinuationPump : SynchronizationContext
	{
		private readonly object _sync = new();
		private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

		public override SynchronizationContext CreateCopy() => this;

		public override void Post(SendOrPostCallback callback, object? state)
		{
			lock (_sync)
			{
				_callbacks.Enqueue((callback, state));
			}
		}

		public void Run(Action action)
		{
			var previous = Current;
			SetSynchronizationContext(this);
			try
			{
				action();
			}
			finally
			{
				SetSynchronizationContext(previous);
			}
		}

		public void Drain() => Run(() =>
		{
			while (true)
			{
				(SendOrPostCallback Callback, object? State) next;
				lock (_sync)
				{
					if (_callbacks.Count == 0)
					{
						return;
					}
					next = _callbacks.Dequeue();
				}
				next.Callback(next.State);
			}
		});
	}

	private sealed class ControlledRead(CancellationToken cancellationToken)
	{
		public CancellationToken CancellationToken { get; } = cancellationToken;

		public TaskCompletionSource<ReadOnlyMemory<byte>?> Result { get; } =
			new();

		public void Complete(ReadOnlyMemory<byte>? bytes) => Result.TrySetResult(bytes);
	}

	private sealed class ControlledReadLocalStore : ILocalTerminalLobbyCacheStore
	{
		private readonly Queue<ControlledRead> _reads = new();
		private readonly SemaphoreSlim _available = new(0);
		private int _readCount;

		public int ReadCount => Volatile.Read(ref _readCount);

		public async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _readCount);
			var read = new ControlledRead(cancellationToken);
			lock (_reads)
			{
				_reads.Enqueue(read);
			}
			_available.Release();
			return await read.Result.Task;
		}

		public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("The blocked read must not progress to a write.");

		public async Task<ControlledRead> NextReadAsync()
		{
			await _available.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
			lock (_reads)
			{
				return _reads.Dequeue();
			}
		}
	}

	private sealed class ControlledCommitLocalStore : ILocalTerminalLobbyCacheStore
	{
		private readonly Queue<ControlledCommitWrite> _writes = new();
		private readonly SemaphoreSlim _available = new(0);

		public ReadOnlyMemory<byte>? CommittedBytes { get; private set; }

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);

		public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default)
		{
			var write = new ControlledCommitWrite(
				bytes,
				value => CommittedBytes = value.ToArray());
			lock (_writes)
			{
				_writes.Enqueue(write);
			}
			_available.Release();
			return ValueTask.FromResult<ILocalTerminalLobbyCacheWrite>(write);
		}

		public async Task<ControlledCommitWrite> NextWriteAsync()
		{
			await _available.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
			lock (_writes)
			{
				return _writes.Dequeue();
			}
		}
	}

	private sealed class ControlledStageLocalStore : ILocalTerminalLobbyCacheStore
	{
		private readonly TaskCompletionSource _stageStarted =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _releaseStage =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _stagedWriteDisposed =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task StageStarted => _stageStarted.Task;
		public Task StagedWriteDisposed => _stagedWriteDisposed.Task;
		public int CommitAttemptCount { get; private set; }

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);

		public async ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default)
		{
			_stageStarted.TrySetResult();
			await _releaseStage.Task;
			return new RecordingWrite(
				() => CommitAttemptCount++,
				() => _stagedWriteDisposed.TrySetResult());
		}

		public void ReleaseStage() => _releaseStage.TrySetResult();
	}

	private sealed class ControlledCommitWrite(
		ReadOnlyMemory<byte> bytes,
		Action<ReadOnlyMemory<byte>> commit) : ILocalTerminalLobbyCacheWrite
	{
		private readonly ManualResetEventSlim _release = new();
		private readonly TaskCompletionSource _boundary =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _disposed =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task CommitBoundary => _boundary.Task;
		public Task Disposed => _disposed.Task;

		public bool TryCommit(Func<Action, bool> commitIfAuthorized)
		{
			_boundary.TrySetResult();
			_release.Wait();
			return commitIfAuthorized(() => commit(bytes));
		}

		public void ReleaseCommitBoundary() => _release.Set();

		public ValueTask DisposeAsync()
		{
			_release.Dispose();
			_disposed.TrySetResult();
			return ValueTask.CompletedTask;
		}
	}

	private sealed class RecordingLocalStore(ReadOnlyMemory<byte>? bytes)
		: ILocalTerminalLobbyCacheStore
	{
		public ReadOnlyMemory<byte>? Bytes { get; set; } = bytes;
		public int ReadCount { get; private set; }
		public List<byte[]> Writes { get; } = [];

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ReadCount++;
			return ValueTask.FromResult(Bytes);
		}

		public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> value,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult<ILocalTerminalLobbyCacheWrite>(
				new RecordingWrite(() => Writes.Add(value.ToArray())));
		}
	}

	public enum PersistenceFailure
	{
		Stage,
		Commit
	}

	private interface IFailingLocalStore : ILocalTerminalLobbyCacheStore
	{
		ReadOnlyMemory<byte>? Bytes { get; }
	}

	private sealed class ThrowingStageLocalStore(
		ReadOnlyMemory<byte>? bytes = null) : IFailingLocalStore
	{
		public ReadOnlyMemory<byte>? Bytes { get; } = bytes;

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(Bytes);

		public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromException<ILocalTerminalLobbyCacheWrite>(
				new IOException("injected persistence failure"));
	}

	private sealed class ThrowingCommitLocalStore(
		ReadOnlyMemory<byte>? bytes = null) : IFailingLocalStore
	{
		public ReadOnlyMemory<byte>? Bytes { get; } = bytes;

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(Bytes);

		public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<ILocalTerminalLobbyCacheWrite>(new ThrowingCommitWrite());
	}

	private sealed class ThrowingCommitWrite : ILocalTerminalLobbyCacheWrite
	{
		public bool TryCommit(Func<Action, bool> commitIfAuthorized) =>
			throw new IOException("injected commit failure");

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class ReadFailingLocalStore : ILocalTerminalLobbyCacheStore
	{
		public List<byte[]> Writes { get; } = [];

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default) =>
			ValueTask.FromException<ReadOnlyMemory<byte>?>(
				new IOException("injected local read failure"));

		public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<ILocalTerminalLobbyCacheWrite>(
				new RecordingWrite(() => Writes.Add(bytes.ToArray())));
	}

	private sealed class RecordingWrite(
		Action commit,
		Action? dispose = null) : ILocalTerminalLobbyCacheWrite
	{
		private bool _completed;

		public bool TryCommit(Func<Action, bool> commitIfAuthorized)
		{
			ObjectDisposedException.ThrowIf(_completed, this);
			var committed = false;
			var authorized = commitIfAuthorized(() =>
			{
				commit();
				committed = true;
			});
			authorized.Should().Be(committed);
			_completed = true;
			return committed;
		}

		public ValueTask DisposeAsync()
		{
			_completed = true;
			dispose?.Invoke();
			return ValueTask.CompletedTask;
		}
	}

	private sealed class RecordingEvaluator(LobbyEvaluationResult result)
		: ILobbyTerminalEvaluator
	{
		public int CallCount { get; private set; }
		public List<SimulationScenario> Scenarios { get; } = [];
		public List<SimulatorCapability> Capabilities { get; } = [];
		public List<LobbyEvaluationDepth> Depths { get; } = [];

		public Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			SimulatorCapability capability,
			LobbyEvaluationDepth depth,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CallCount++;
			Scenarios.Add(scenario);
			Capabilities.Add(capability);
			Depths.Add(depth);
			return Task.FromResult(result);
		}
	}

	private sealed class QueueEvaluator(params LobbyEvaluationResult[] results)
		: ILobbyTerminalEvaluator
	{
		private readonly Queue<LobbyEvaluationResult> _results = new(results);
		public int CallCount { get; private set; }

		public Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			SimulatorCapability capability,
			LobbyEvaluationDepth depth,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CallCount++;
			return Task.FromResult(_results.Dequeue());
		}
	}

	private sealed class ControlledEvaluator : ILobbyTerminalEvaluator
	{
		private readonly Queue<ControlledCall> _calls = new();
		private readonly SemaphoreSlim _available = new(0);
		public int CallCount { get; private set; }

		public async Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			SimulatorCapability capability,
			LobbyEvaluationDepth depth,
			CancellationToken cancellationToken = default)
		{
			CallCount++;
			var identity = new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				capability.Identity);
			var call = new ControlledCall(identity, cancellationToken);
			lock (_calls)
			{
				_calls.Enqueue(call);
			}
			_available.Release();
			return await call.Result.Task;
		}

		public async Task<ControlledCall> NextCallAsync()
		{
			await _available.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
			lock (_calls)
			{
				return _calls.Dequeue();
			}
		}
	}

	private sealed record UnexpectedTerminalEvaluation : TerminalLobbyEvaluation;

	private sealed class ReentrantCancellationEvaluator(Func<bool> reenter)
		: ILobbyTerminalEvaluator
	{
		private readonly TaskCompletionSource<LobbyEvaluationResult> _firstResult =
			new();
		private readonly TaskCompletionSource _firstCallStarted =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _callbackEntered =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _callbackRelease =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task FirstCallStarted => _firstCallStarted.Task;
		public Task CallbackEntered => _callbackEntered.Task;
		public bool? ReentrantExitResult { get; private set; }
		public int CallCount { get; private set; }

		public async Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			SimulatorCapability capability,
			LobbyEvaluationDepth depth,
			CancellationToken cancellationToken = default)
		{
			CallCount++;
			if (CallCount > 1)
			{
				return new CouldNotEvaluateLobbyEvaluation();
			}

			var registration = cancellationToken.Register(() =>
			{
				_callbackEntered.TrySetResult();
				ReentrantExitResult = reenter();
				_callbackRelease.Task.GetAwaiter().GetResult();
				throw new InvalidOperationException("injected cancellation callback failure");
			});
			_firstCallStarted.TrySetResult();
			try
			{
				return await _firstResult.Task;
			}
			finally
			{
				registration.Unregister();
			}
		}

		public void CompleteFirstCall() =>
			_firstResult.TrySetResult(new CouldNotEvaluateLobbyEvaluation());

		public void ReleaseCancellationCallback() =>
			_callbackRelease.TrySetResult();
	}

	private sealed class ControlledCall(
		SimulationCompatibilityIdentity identity,
		CancellationToken cancellationToken)
	{
		public SimulationCompatibilityIdentity Identity { get; } = identity;
		public CancellationToken CancellationToken { get; } = cancellationToken;
		public TaskCompletionSource<LobbyEvaluationResult> Result { get; } =
			new();

		public void Complete(LobbyEvaluationResult result) => Result.TrySetResult(result);
	}
}
