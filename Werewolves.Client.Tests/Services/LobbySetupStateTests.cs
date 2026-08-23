using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Services;

public class LobbySetupStateTests
{
	[Fact]
	public void DecideRoleLockIn_ReturnsCompleteReplaceDecisionWithoutPublishing()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager);
		foreach (var playerName in PlayerNames.DefaultFive)
		{
			state.AddPlayer(playerName).Should().Be(AddPlayerResult.Success);
		}
		state.IncrementRole(MainRoleType.Thief);
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 5; index++)
		{
			state.IncrementRole(MainRoleType.SimpleVillager);
		}
		var replacement = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			state.PlayerRoster.Count,
			state.GetSelectedRoles(),
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var scenarioChanges = 0;
		state.SimulationScenarioChanged += (_, _) => scenarioChanges++;

		var decision = state.Decide(
			new LobbyChange.ReplaceRoleLockIn(
				expectedCurrentVersion: 0,
				replacement));

		decision.Should().NotBeNull();
		decision!.NextAggregate.PlayerRoster.Should().Equal(state.PlayerRoster);
		decision.NextAggregate.AcceptedRoleLockIn.Should().BeSameAs(replacement);
		decision.NextAggregate.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
		decision.NextAggregate.AcceptedPublicGroupPartition.Should().BeNull();
		decision.Persistence.Should()
			.BeOfType<LobbyPersistenceInstruction.Replace>()
			.Which.Aggregate.Should().BeSameAs(decision.NextAggregate);
		decision.CanonicalScenarioDelta.Before.Should().BeNull();
		decision.CanonicalScenarioDelta.After.Should().NotBeNull();
		decision.CanonicalScenarioDelta.HasIdentityChanged.Should().BeTrue();
		decision.Commit.Should().NotBeNull();
		state.AcceptedRoleLockIn.Should().BeNull();
		state.RequiresRoleLockIn.Should().BeTrue();
		scenarioChanges.Should().Be(0);
	}

	[Fact]
	public void DecidePostGameRecovery_ReturnsClearDecisionWithoutPublishing()
	{
		var source = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer);
		var target = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer);
		var expectedRoster = source.PlayerRoster.ToArray();
		var expectedRoleLockIn = source.AcceptedRoleLockIn!;

		var decision = target.Decide(
			new LobbyChange.RecoverPostGameLobby(
				expectedRoster,
				expectedRoleLockIn,
				source.AcceptedActorSetupCards,
				source.AcceptedPublicGroupPartition));

		decision.Should().NotBeNull();
		decision!.Persistence.Should().BeOfType<LobbyPersistenceInstruction.Clear>();
		decision.NextAggregate.PlayerRoster.Should().Equal(expectedRoster);
		decision.NextAggregate.AcceptedRoleLockIn.Should().BeSameAs(expectedRoleLockIn);
		target.PlayerRoster.Should().BeEmpty();
		target.AcceptedRoleLockIn.Should().BeNull();
	}

	[Fact]
	public void DecideInvalidPostGameRecovery_ReturnsClearWipeWithoutPublishing()
	{
		var source = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer);
		var target = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager);
		target.AddPlayer("Temporary").Should().Be(AddPlayerResult.Success);
		target.IncrementRole(MainRoleType.SimpleWerewolf);

		var decision = target.Decide(
			new LobbyChange.RecoverPostGameLobby(
				source.PlayerRoster,
				source.AcceptedRoleLockIn!,
				source.AcceptedActorSetupCards,
				source.AcceptedPublicGroupPartition));

		decision.Should().NotBeNull();
		decision!.Persistence.Should().BeOfType<LobbyPersistenceInstruction.Clear>();
		decision.NextAggregate.PlayerRoster.Should().BeEmpty();
		decision.NextAggregate.RoleCounts.Should().BeEmpty();
		decision.NextAggregate.AcceptedRoleLockIn.Should().BeNull();
		target.PlayerRoster.Should().ContainSingle();
		target.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(1);
	}

	[Fact]
	public void DecidePostGameWipe_ReturnsKeepDecisionWithoutPublishing()
	{
		var target = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer);
		var acceptedRoleLockIn = target.AcceptedRoleLockIn;

		var decision = target.Decide(new LobbyChange.WipePostGameLobby());

		decision.Should().NotBeNull();
		decision!.Persistence.Should().BeOfType<LobbyPersistenceInstruction.Keep>();
		decision.NextAggregate.PlayerRoster.Should().BeEmpty();
		decision.NextAggregate.RoleCounts.Should().BeEmpty();
		decision.NextAggregate.AcceptedRoleLockIn.Should().BeNull();
		target.PlayerRoster.Should().NotBeEmpty();
		target.AcceptedRoleLockIn.Should().BeSameAs(acceptedRoleLockIn);
	}

	[Fact]
	public void DecideActorSetupCards_ReturnsCompleteReplaceDecisionWithoutPublishing()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.Actor);
		var replacement = ActorSetupCards.CreateFromPrintedRoles(
			version: 1,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]);

		var decision = state.Decide(
			new LobbyChange.ReplaceActorSetupCards(
				expectedCurrentVersion: 0,
				replacement));

		decision.Should().NotBeNull();
		decision!.NextAggregate.AcceptedRoleLockIn
			.Should().BeSameAs(state.AcceptedRoleLockIn);
		decision.NextAggregate.AcceptedActorSetupCards.Should().BeSameAs(replacement);
		decision.Persistence.Should()
			.BeOfType<LobbyPersistenceInstruction.Replace>();
		decision.CanonicalScenarioDelta.Before.Should().BeNull();
		decision.CanonicalScenarioDelta.After.Should().NotBeNull();
		decision.CanonicalScenarioDelta.HasIdentityChanged.Should().BeTrue();
		state.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
	}

	[Fact]
	public void DecidePublicGroupPartition_ReturnsCompleteReplaceDecisionWithoutPublishing()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.PrejudicedManipulator);
		var rosterIds = state.PlayerRoster.Select(player => player.Id).ToArray();
		var replacement = PublicGroupPartition.Create(
			rosterIds,
			[rosterIds[0], rosterIds[2]],
			[rosterIds[1], rosterIds[3], rosterIds[4]]);

		var decision = state.Decide(
			new LobbyChange.ReplacePublicGroupPartition(replacement));

		decision.Should().NotBeNull();
		decision!.NextAggregate.AcceptedPublicGroupPartition
			.Should().BeSameAs(replacement);
		decision.Persistence.Should()
			.BeOfType<LobbyPersistenceInstruction.Replace>();
		decision.CanonicalScenarioDelta.Before.Should().BeNull();
		decision.CanonicalScenarioDelta.After.Should().NotBeNull();
		state.AcceptedPublicGroupPartition.Should().BeNull();
	}

	[Fact]
	public void DecideSeatingOrderMove_ReturnsCompleteReplaceDecisionWithoutPublishing()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.PrejudicedManipulator);
		AcceptCurrentRosterPartition(state);
		var originalRoster = state.PlayerRoster.ToArray();

		var decision = state.Decide(
			new LobbyChange.MovePlayer(fromIndex: 0, toIndex: 1));

		decision.Should().NotBeNull();
		decision!.NextAggregate.PlayerRoster.Should().Equal(
			originalRoster[1],
			originalRoster[0],
			originalRoster[2],
			originalRoster[3],
			originalRoster[4]);
		decision.Persistence.Should()
			.BeOfType<LobbyPersistenceInstruction.Replace>();
		decision.CanonicalScenarioDelta.Before.Should().NotBeNull();
		decision.CanonicalScenarioDelta.After.Should().NotBeNull();
		decision.CanonicalScenarioDelta.HasIdentityChanged.Should().BeTrue();
		state.PlayerRoster.Should().Equal(originalRoster);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void DecidePlayerMembershipChange_ClearsRecoveryWithoutPublishing(
		bool addPlayer)
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.PrejudicedManipulator);
		AcceptCurrentRosterPartition(state);
		var originalRoster = state.PlayerRoster.ToArray();
		var addedPlayer = new GameSessionPlayerConfig(
			Guid.Parse("30000000-0000-0000-0000-000000000001"),
			"Fátima");
		LobbyChange change = addPlayer
			? new LobbyChange.AddPlayer(addedPlayer)
			: new LobbyChange.RemovePlayer(index: 2);

		var decision = state.Decide(change);

		decision.Should().NotBeNull();
		decision!.Persistence.Should()
			.BeOfType<LobbyPersistenceInstruction.Clear>();
		decision.NextAggregate.AcceptedRoleLockIn
			.Should().BeSameAs(state.AcceptedRoleLockIn);
		decision.NextAggregate.AcceptedRoleLockInRequiresReplacement
			.Should().BeTrue();
		decision.NextAggregate.AcceptedPublicGroupPartition.Should().BeNull();
		decision.NextAggregate.PlayerRoster.Count.Should().Be(
			originalRoster.Length + (addPlayer ? 1 : -1));
		decision.CanonicalScenarioDelta.Before.Should().NotBeNull();
		decision.CanonicalScenarioDelta.After.Should().BeNull();
		decision.CanonicalScenarioDelta.HasIdentityChanged.Should().BeTrue();
		state.PlayerRoster.Should().Equal(originalRoster);
		state.AcceptedRoleLockInRequiresReplacement.Should().BeFalse();
	}

	[Fact]
	public void DecideImplicitRoleLockIn_PersistsCanonicalEquivalentAggregateWithoutPublishing()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer);
		foreach (var playerName in PlayerNames.DefaultFive)
		{
			state.AddPlayer(playerName).Should().Be(AddPlayerResult.Success);
		}
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.IncrementRole(MainRoleType.Seer);
		for (var index = 0; index < 3; index++)
		{
			state.IncrementRole(MainRoleType.SimpleVillager);
		}
		var replacement = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			state.PlayerRoster.Count,
			state.GetSelectedRoles());

		var decision = state.Decide(
			new LobbyChange.AcceptImplicitRoleLockIn(
				expectedCurrentVersion: 0,
				replacement));

		decision.Should().NotBeNull();
		decision!.NextAggregate.AcceptedRoleLockIn.Should().BeSameAs(replacement);
		decision.Persistence.Should()
			.BeOfType<LobbyPersistenceInstruction.Replace>();
		decision.CanonicalScenarioDelta.Before.Should().NotBeNull();
		decision.CanonicalScenarioDelta.After.Should().Be(
			decision.CanonicalScenarioDelta.Before);
		decision.CanonicalScenarioDelta.HasIdentityChanged.Should().BeFalse();
		state.AcceptedRoleLockIn.Should().BeNull();
	}

	[Fact]
	public void DecideImplicitRoleLockIn_WithStaleExpectedVersion_RejectsWithoutPublishing()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer);
		foreach (var playerName in PlayerNames.DefaultFive)
		{
			state.AddPlayer(playerName).Should().Be(AddPlayerResult.Success);
		}
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.IncrementRole(MainRoleType.Seer);
		for (var index = 0; index < 3; index++)
		{
			state.IncrementRole(MainRoleType.SimpleVillager);
		}
		var acceptedRoleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			state.PlayerRoster.Count,
			state.GetSelectedRoles());
		var acceptedDecision = state.Decide(
			new LobbyChange.AcceptImplicitRoleLockIn(
				expectedCurrentVersion: 0,
				acceptedRoleLockIn))!;
		state.Publish(acceptedDecision.Commit);
		var staleReplacement = RoleLockIn.CreateFromPrintedRoles(
			version: 2,
			state.PlayerRoster.Count,
			state.GetSelectedRoles());

		var staleDecision = state.Decide(
			new LobbyChange.AcceptImplicitRoleLockIn(
				expectedCurrentVersion: 0,
				staleReplacement));

		staleDecision.Should().BeNull();
		state.AcceptedRoleLockIn.Should().BeSameAs(acceptedRoleLockIn);
	}

	[Fact]
	public void DecideDraftSeatingOrderMove_KeepsPersistenceAndCanonicalIdentity()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager);
		foreach (var playerName in PlayerNames.DefaultFive)
		{
			state.AddPlayer(playerName);
		}
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 4; index++)
		{
			state.IncrementRole(MainRoleType.SimpleVillager);
		}

		var decision = state.Decide(
			new LobbyChange.MovePlayer(fromIndex: 0, toIndex: 1));

		decision.Should().NotBeNull();
		decision!.Persistence.Should()
			.BeOfType<LobbyPersistenceInstruction.Keep>();
		decision.CanonicalScenarioDelta.Before.Should().NotBeNull();
		decision.CanonicalScenarioDelta.After.Should().Be(
			decision.CanonicalScenarioDelta.Before);
		decision.CanonicalScenarioDelta.HasIdentityChanged.Should().BeFalse();
		state.PlayerNames.Should().Equal(PlayerNames.DefaultFive);
	}

	[Fact]
	public void DecideStaleRoleLockIn_ReturnsNoPublishableDecision()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var accepted = state.AcceptedRoleLockIn;
		var staleReplacement = RoleLockIn.CreateFromPrintedRoles(
			version: 2,
			state.PlayerRoster.Count,
			state.GetSelectedRoles());
		var scenarioChanges = 0;
		state.SimulationScenarioChanged += (_, _) => scenarioChanges++;

		var decision = state.Decide(
			new LobbyChange.ReplaceRoleLockIn(
				expectedCurrentVersion: 0,
				staleReplacement));

		decision.Should().BeNull();
		state.AcceptedRoleLockIn.Should().BeSameAs(accepted);
		scenarioChanges.Should().Be(0);
	}

	[Fact]
	public void DecideCanceledChange_ReturnsNoPublishableDecision()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		var scenarioChanges = 0;
		state.SimulationScenarioChanged += (_, _) => scenarioChanges++;

		var decision = state.Decide(null!);

		decision.Should().BeNull();
		state.PlayerRoster.Should().BeEmpty();
		state.AcceptedRoleLockIn.Should().BeNull();
		scenarioChanges.Should().Be(0);
	}

	[Fact]
	public void DecideInvalidSeatingMove_ReturnsNoPublishableDecision()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer(PlayerNames.Ana);
		state.AddPlayer(PlayerNames.Bruno);
		var originalRoster = state.PlayerRoster.ToArray();
		var scenarioChanges = 0;
		state.SimulationScenarioChanged += (_, _) => scenarioChanges++;

		var decision = state.Decide(
			new LobbyChange.MovePlayer(fromIndex: 0, toIndex: 2));

		decision.Should().BeNull();
		state.PlayerRoster.Should().Equal(originalRoster);
		scenarioChanges.Should().Be(0);
	}

	[Fact]
	public void DecideIncompleteActorSetup_ReturnsNoPublishableDecision()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.Actor);
		var acceptedRoleLockIn = state.AcceptedRoleLockIn;
		var scenarioChanges = 0;
		state.SimulationScenarioChanged += (_, _) => scenarioChanges++;

		var decision = state.Decide(
			new LobbyChange.ReplaceActorSetupCards(
				expectedCurrentVersion: 0,
				ActorSetupCards.None));

		decision.Should().BeNull();
		state.AcceptedRoleLockIn.Should().BeSameAs(acceptedRoleLockIn);
		state.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
		scenarioChanges.Should().Be(0);
	}

	[Fact]
	public void Publish_AppliesTheCompleteDecisionWithoutRaisingCallbacks()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager);
		foreach (var playerName in PlayerNames.DefaultFive)
		{
			state.AddPlayer(playerName);
		}
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 4; index++)
		{
			state.IncrementRole(MainRoleType.SimpleVillager);
		}
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			state.PlayerRoster.Count,
			state.GetSelectedRoles());
		var decision = state.Decide(
			new LobbyChange.AcceptImplicitRoleLockIn(
				expectedCurrentVersion: 0,
				roleLockIn))!;
		var scenarioChanges = 0;
		state.SimulationScenarioChanged += (_, _) => scenarioChanges++;

		state.Publish(decision.Commit);

		state.AcceptedRoleLockIn.Should().BeSameAs(roleLockIn);
		state.PlayerRoster.Should().Equal(decision.NextAggregate.PlayerRoster);
		state.GetSelectedRoles().Should().BeEquivalentTo(
			decision.NextAggregate.RoleCounts.SelectMany(entry =>
				Enumerable.Repeat(entry.Key, entry.Value)));
		scenarioChanges.Should().Be(0);
	}

	[Fact]
	public void AddPlayer_WhenRosterAtSupportedMaximum_RejectsWithoutEffects()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		for (var index = 0; index < GameSessionConfig.MaximumPlayerCount; index++)
		{
			state.AddPlayer(PlayerNames.GeneratedPlayer(index))
				.Should().Be(AddPlayerResult.Success);
		}
		var originalRoster = state.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.ToArray();
		var notifications = 0;
		state.SimulationScenarioChanged += (_, _) => notifications++;

		var result = state.AddPlayer(
			PlayerNames.GeneratedPlayer(GameSessionConfig.MaximumPlayerCount));

		result.Should().Be(AddPlayerResult.PlayerLimitReached);
		state.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.Should().Equal(originalRoster);
		notifications.Should().Be(0);
	}

	[Fact]
	public void SimulationScenarioChanged_RaisesOnlyWhenScenarioIdentityMaterialChanges()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		var eventCount = 0;
		state.SimulationScenarioChanged += (_, _) => eventCount++;

		state.AddPlayer(PlayerNames.Ana);
		eventCount.Should().Be(1);

		state.AddPlayer(PlayerNames.AnaLowercase).Should().Be(AddPlayerResult.DuplicateName);
		state.MovePlayerUp(0).Should().BeFalse();
		eventCount.Should().Be(1);

		state.AddPlayer(PlayerNames.Bruno);
		state.MovePlayerDown(0).Should().BeTrue();
		eventCount.Should().Be(2, "Seating Order is not Simulation Scenario identity");

		state.IncrementRole(MainRoleType.Seer);
		eventCount.Should().Be(3);

		state.IncrementRole(MainRoleType.Seer);
		state.DecrementRole(MainRoleType.SimpleWerewolf);
		eventCount.Should().Be(3, "Role mutations that leave the selected counts unchanged are identity-neutral");

		state.DecrementRole(MainRoleType.Seer);
		state.RemovePlayerAt(0).Should().BeTrue();
		eventCount.Should().Be(5);

		state.Reset();
		eventCount.Should().Be(6);
		state.Reset();
		eventCount.Should().Be(6);
	}

	[Fact]
	public void CreateSimulationScenario_UsesPlayerCountAndRoleCompositionWithoutRosterIdentity()
	{
		var first = LobbySetupMetadataFixture.DefaultState();
		var second = LobbySetupMetadataFixture.DefaultState();
		foreach (var name in PlayerNames.DefaultFive)
		{
			first.AddPlayer(name);
			second.AddPlayer($"{name}-different");
		}

		foreach (var state in new[] { first, second })
		{
			state.IncrementRole(MainRoleType.SimpleWerewolf);
			state.IncrementRole(MainRoleType.Seer);
			state.IncrementRole(MainRoleType.SimpleVillager);
			state.IncrementRole(MainRoleType.SimpleVillager);
			state.IncrementRole(MainRoleType.SimpleVillager);
		}
		first.MovePlayerDown(0).Should().BeTrue();

		var scenario = first.CreateSimulationScenario();

		scenario.Should().Be(new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]));
		scenario.Should().Be(second.CreateSimulationScenario());
		scenario.ActorSetupCards.Cards.Should().BeEmpty();
		scenario.RuleState.Should().Be(SimulationRuleState.Default);
	}

	[Theory]
	[InlineData(MainRoleType.Actor)]
	[InlineData(MainRoleType.PrejudicedManipulator)]
	public void TryCreateSimulationScenario_BeforeConditionalRoleLockIn_FailsClosed(
		MainRoleType conditionalRole)
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			conditionalRole,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager);
		foreach (var playerName in PlayerNames.DefaultFive)
		{
			state.AddPlayer(playerName);
		}
		state.IncrementRole(conditionalRole);
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 3; index++)
		{
			state.IncrementRole(MainRoleType.SimpleVillager);
		}

		var created = state.TryCreateSimulationScenario(out var scenario);

		state.AcceptedRoleLockIn.Should().BeNull();
		created.Should().BeFalse();
		scenario.Should().BeNull();
	}

	[Fact]
	public void TryCreateSimulationScenario_WithReachableManipulatorAndNoPartition_FailsClosed()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.PrejudicedManipulator);

		var created = state.TryCreateSimulationScenario(out var scenario);

		state.RequiresPublicGroupPartition.Should().BeTrue();
		state.AcceptedPublicGroupPartition.Should().BeNull();
		created.Should().BeFalse();
		scenario.Should().BeNull();
	}

	[Fact]
	public void ActorAndManipulatorReachable_RequireActorSetupBeforePublicGroupPartition()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.WildChild,
			MainRoleType.Actor,
			MainRoleType.PrejudicedManipulator);

		state.RequiresActorSetupCards.Should().BeTrue();
		state.RequiresPublicGroupPartition.Should().BeFalse(
			"Actor Setup Cards precede the existing partition gate");
		state.TryCreateSimulationScenario(out _).Should().BeFalse();
		var manager = new GameClientManager();
		manager.TryReplaceStagedActorSetupCards(
			state,
			expectedCurrentVersion: 0,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]).Should().BeTrue();
		var actorSetupCards = state.AcceptedActorSetupCards;

		state.RequiresActorSetupCards.Should().BeFalse();
		state.RequiresPublicGroupPartition.Should().BeTrue();
		state.TryCreateSimulationScenario(out _).Should().BeFalse();

		AcceptCurrentRosterPartition(state);

		state.TryCreateSimulationScenario(out var scenario).Should().BeTrue();
		scenario.ActorSetupCards.Should().Be(actorSetupCards);
	}

	[Fact]
	public void ActorSetupReplacement_RejectsStaleVersionAndStopsAfterLobbyExit()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.Actor);
		var manager = new GameClientManager();
		manager.TryReplaceStagedActorSetupCards(
			state,
			expectedCurrentVersion: 0,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]).Should().BeTrue();
		var accepted = state.AcceptedActorSetupCards;
		MainRoleType[] next =
		[
				MainRoleType.Seer,
				MainRoleType.Defender,
				MainRoleType.Elder
		];

		manager.TryReplaceStagedActorSetupCards(state, 0, next)
			.Should().BeFalse();
		state.AcceptedActorSetupCards.Should().BeSameAs(accepted);

		manager.StartGame(state);

		manager.TryReplaceStagedActorSetupCards(state, 1, next)
			.Should().BeFalse();
		state.AcceptedActorSetupCards.Should().BeSameAs(accepted);
	}

	[Fact]
	public void RoleLockInReplacement_WhenActorBecomesUnreachable_ClearsActorSetup()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.Actor);
		var manager = new GameClientManager();
		manager.TryReplaceStagedActorSetupCards(
			state,
			expectedCurrentVersion: 0,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]).Should().BeTrue();
		var replacement = RoleLockIn.CreateFromPrintedRoles(
			version: 2,
			playerCount: state.PlayerRoster.Count,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		manager.TryReplaceStagedRoleLockIn(state, 1, replacement)
			.Should().BeTrue();

		state.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
		state.RequiresActorSetupCards.Should().BeFalse();
		state.TryCreateSimulationScenario(out _).Should().BeTrue();
	}

	[Fact]
	public void PublicGroupPartitionReplacement_WithExactCurrentRoster_CompletesLobbySetup()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.PrejudicedManipulator);
		var rosterIds = state.PlayerRoster.Select(player => player.Id).ToArray();
		var partition = PublicGroupPartition.Create(
			rosterIds,
			firstGroupPlayerIds: [rosterIds[0], rosterIds[2]],
			secondGroupPlayerIds: [rosterIds[1], rosterIds[3], rosterIds[4]]);
		var scenarioChanged = false;
		state.SimulationScenarioChanged += (_, _) => scenarioChanged = true;
		var manager = new GameClientManager();

		var replaced = manager.TryReplaceStagedPublicGroupPartition(
			state,
			partition);
		var created = state.TryCreateSimulationScenario(out var scenario);

		replaced.Should().BeTrue();
		state.AcceptedPublicGroupPartition.Should().BeSameAs(partition);
		state.RequiresPublicGroupPartition.Should().BeFalse();
		created.Should().BeTrue();
		scenario.Should().NotBeNull();
		scenario.PublicGroupPartition.Should().Be(
			CanonicalPublicGroupPartition.Project(rosterIds, partition));
		scenarioChanged.Should().BeTrue();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void RosterMembershipChange_ClearsAcceptedPartitionAndStalesRoleLockIn(
		bool addPlayer)
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.PrejudicedManipulator);
		var partition = AcceptCurrentRosterPartition(state);

		var changed = addPlayer
			? state.AddPlayer("Extra") == AddPlayerResult.Success
			: state.RemovePlayerAt(0);

		changed.Should().BeTrue();
		state.AcceptedPublicGroupPartition.Should().BeNull();
		state.AcceptedRoleLockInRequiresReplacement.Should().BeTrue();
		new GameClientManager()
			.TryReplaceStagedPublicGroupPartition(state, partition)
			.Should().BeFalse();
	}

	[Fact]
	public void PublicGroupPartitionReplacement_HandlesNoOpMeaningfulAndFinalizedValues()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.PrejudicedManipulator);
		var accepted = AcceptCurrentRosterPartition(state);
		var rosterIds = state.PlayerRoster.Select(player => player.Id).ToArray();
		var equivalent = PublicGroupPartition.Create(
			rosterIds.Reverse(),
			accepted.SecondGroupPlayerIds.Reverse(),
			accepted.FirstGroupPlayerIds.Reverse());
		var replacement = PublicGroupPartition.Create(
			rosterIds,
			[rosterIds[0], rosterIds[1]],
			[rosterIds[2], rosterIds[3], rosterIds[4]]);
		var scenarioChanged = false;
		state.SimulationScenarioChanged += (_, _) => scenarioChanged = true;
		var acceptedScenario = state.CreateSimulationScenario();
		var manager = new GameClientManager();

		manager.TryReplaceStagedPublicGroupPartition(state, equivalent)
			.Should().BeTrue();
		state.AcceptedPublicGroupPartition.Should().BeSameAs(accepted);
		state.CreateSimulationScenario().Should().Be(acceptedScenario);
		scenarioChanged.Should().BeFalse();

		manager.TryReplaceStagedPublicGroupPartition(state, replacement)
			.Should().BeTrue();
		state.AcceptedPublicGroupPartition.Should().BeSameAs(replacement);
		state.CreateSimulationScenario().Should().NotBe(acceptedScenario);
		scenarioChanged.Should().BeTrue();

		manager.StartGame(state);
		manager.TryReplaceStagedPublicGroupPartition(state, accepted)
			.Should().BeFalse();
	}

	[Fact]
	public void RoleLockInReplacement_WhenManipulatorBecomesUnreachable_ClearsPartition()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.PrejudicedManipulator);
		AcceptCurrentRosterPartition(state);
		var replacement = RoleLockIn.CreateFromPrintedRoles(
			version: 2,
			playerCount: state.PlayerRoster.Count,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.Seer,
				MainRoleType.Witch
			]);

		new GameClientManager()
			.TryReplaceStagedRoleLockIn(state, 1, replacement)
			.Should().BeTrue();

		state.AcceptedPublicGroupPartition.Should().BeNull();
		state.RequiresPublicGroupPartition.Should().BeFalse();
		state.TryCreateSimulationScenario(out _).Should().BeTrue();
	}

	[Fact]
	public void RoleDraftEdit_WithAcceptedPartition_RequiresFreshRoleLockInBeforeScenario()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.PrejudicedManipulator);
		var partition = AcceptCurrentRosterPartition(state);

		state.IncrementRole(MainRoleType.SimpleVillager);

		state.AcceptedPublicGroupPartition.Should().BeSameAs(partition);
		state.AcceptedRoleLockInRequiresReplacement.Should().BeTrue();
		state.RequiresRoleLockIn.Should().BeTrue();
		state.TryCreateSimulationScenario(out var scenario).Should().BeFalse();
		scenario.Should().BeNull();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void TryCreateSimulationScenario_WithManipulatorInEitherOffer_RequiresPartition(
		bool manipulatorIsOffer1)
	{
		MainRoleType[] roles =
		[
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.PrejudicedManipulator,
			MainRoleType.Hunter
		];
		var state = LobbySetupMetadataFixture.StateWithRoles(roles.Distinct().ToArray());
		foreach (var name in PlayerNames.DefaultFive)
		{
			state.AddPlayer(name).Should().Be(AddPlayerResult.Success);
		}
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: state.PlayerRoster.Count,
			roles,
			offer1: manipulatorIsOffer1
				? MainRoleType.PrejudicedManipulator
				: MainRoleType.Hunter,
			offer2: manipulatorIsOffer1
				? MainRoleType.Hunter
				: MainRoleType.PrejudicedManipulator);
		new GameClientManager()
			.TryReplaceStagedRoleLockIn(state, 0, roleLockIn)
			.Should().BeTrue();

		state.RequiresPublicGroupPartition.Should().BeTrue();
		state.TryCreateSimulationScenario(out _).Should().BeFalse();
	}

	[Fact]
	public void Reorder_WithAcceptedPartition_MovesWholeIdentityAndInvalidatesScenario()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.PrejudicedManipulator);
		var partition = AcceptCurrentRosterPartition(state);
		var originalRoster = state.PlayerRoster.ToArray();
		var scenarioChanges = 0;
		state.SimulationScenarioChanged += (_, _) => scenarioChanges++;
		var originalScenario = state.CreateSimulationScenario();

		state.MovePlayerDown(0).Should().BeTrue();
		state.TryCreateSimulationScenario(out var reorderedScenario).Should().BeTrue();

		state.PlayerRoster.Should().Equal(
			originalRoster[1],
			originalRoster[0],
			originalRoster[2],
			originalRoster[3],
			originalRoster[4]);
		state.AcceptedPublicGroupPartition.Should().BeSameAs(partition);
		state.AcceptedRoleLockInRequiresReplacement.Should().BeFalse();
		reorderedScenario.Should().NotBe(originalScenario);
		reorderedScenario.PublicGroupPartition.Should().Be(
			CanonicalPublicGroupPartition.Project(
				state.PlayerRoster.Select(player => player.Id).ToArray(),
				partition));
		scenarioChanges.Should().Be(1);
	}

	[Fact]
	public void RemoveThenAdd_DoesNotReuseRemovedPlayerIdentity()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer(PlayerNames.Ana).Should().Be(AddPlayerResult.Success);
		state.AddPlayer(PlayerNames.Bruno).Should().Be(AddPlayerResult.Success);
		var retained = state.PlayerRoster[0];
		var removedId = state.PlayerRoster[1].Id;

		state.RemovePlayerAt(1).Should().BeTrue();
		state.AddPlayer(PlayerNames.Catarina).Should().Be(AddPlayerResult.Success);

		state.PlayerRoster[0].Should().BeSameAs(retained);
		state.PlayerRoster[1].Id.Should().NotBe(Guid.Empty);
		state.PlayerRoster[1].Id.Should().NotBe(removedId);
	}

	[Fact]
	public void Construction_ProjectsLobbySetupMetadataIntoInitialState()
	{
		var metadata = LobbySetupMetadataFixture.ForRoles(
			MainRoleType.Seer,
			MainRoleType.SimpleWerewolf);

		var state = new LobbySetupState(metadata);

		state.MinimumPlayerCount.Should().Be(metadata.MinimumPlayerCount);
		state.AvailableRoles.Should().Equal(
			MainRoleType.Seer,
			MainRoleType.SimpleWerewolf);
		state.PlayerNames.Should().BeEmpty();
		state.TotalSelectedRoleCount.Should().Be(0);
	}

	[Fact]
	public void AddPlayer_AppendsNamesInSeatingOrder()
	{
		var state = LobbySetupMetadataFixture.DefaultState();

		state.AddPlayer(PlayerNames.Ana).Should().Be(AddPlayerResult.Success);
		state.AddPlayer(PlayerNames.Bruno).Should().Be(AddPlayerResult.Success);
		state.AddPlayer(PlayerNames.Catarina).Should().Be(AddPlayerResult.Success);

		state.PlayerNames.Should().Equal(PlayerNames.DefaultThree);
	}

	[Fact]
	public void AddPlayer_CreatesOrderedStableRosterIdentities()
	{
		var state = LobbySetupMetadataFixture.DefaultState();

		state.AddPlayer($"  {PlayerNames.Ana}  ").Should().Be(AddPlayerResult.Success);
		var firstEntry = state.PlayerRoster.Single();
		state.AddPlayer(PlayerNames.Bruno).Should().Be(AddPlayerResult.Success);

		state.PlayerRoster.Select(player => player.Name).Should().Equal(
			PlayerNames.Ana,
			PlayerNames.Bruno);
		state.PlayerRoster.Select(player => player.Id)
			.Should().NotContain(Guid.Empty)
			.And.OnlyHaveUniqueItems();
		state.PlayerRoster[0].Should().BeSameAs(firstEntry);
		state.PlayerNames.Should().Equal(PlayerNames.Ana, PlayerNames.Bruno);
	}

	[Fact]
	public void RemovePlayerAt_RemovesNameFromSeatingOrder()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer(PlayerNames.Ana);
		state.AddPlayer(PlayerNames.Bruno);
		state.AddPlayer(PlayerNames.Catarina);

		state.RemovePlayerAt(1).Should().BeTrue();

		state.PlayerNames.Should().Equal(PlayerNames.Ana, PlayerNames.Catarina);
	}

	[Fact]
	public void MovePlayerUp_SwapsPlayerWithPreviousSeat()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer(PlayerNames.Ana);
		state.AddPlayer(PlayerNames.Bruno);
		state.AddPlayer(PlayerNames.Catarina);

		state.MovePlayerUp(2).Should().BeTrue();

		state.PlayerNames.Should().Equal(PlayerNames.Ana, PlayerNames.Catarina, PlayerNames.Bruno);
	}

	[Fact]
	public void MovePlayerDown_SwapsPlayerWithNextSeat()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer(PlayerNames.Ana);
		state.AddPlayer(PlayerNames.Bruno);
		state.AddPlayer(PlayerNames.Catarina);

		state.MovePlayerDown(0).Should().BeTrue();

		state.PlayerNames.Should().Equal(PlayerNames.Bruno, PlayerNames.Ana, PlayerNames.Catarina);
	}

	[Fact]
	public void CanMovePlayer_ReportsDisabledAtSeatingOrderBoundaries()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer(PlayerNames.Ana);
		state.AddPlayer(PlayerNames.Bruno);
		state.AddPlayer(PlayerNames.Catarina);

		state.CanMovePlayerUp(0).Should().BeFalse();
		state.CanMovePlayerDown(2).Should().BeFalse();
		state.CanMovePlayerUp(1).Should().BeTrue();
		state.CanMovePlayerDown(1).Should().BeTrue();
	}

	[Fact]
	public void HasPlayerConfigIssues_ReturnsTooFewPlayers_WhenUnderMinimum()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer(PlayerNames.Ana);
		state.AddPlayer(PlayerNames.Bruno);

		state.HasPlayerConfigIssues(out var issues).Should().BeTrue();
		issues.Should().ContainSingle(e => e.Type == GameConfigValidationErrorType.TooFewPlayers);
	}

	[Fact]
	public void AddPlayer_RejectsDuplicateNameCaseInsensitive()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer(PlayerNames.Ana).Should().Be(AddPlayerResult.Success);

		state.AddPlayer(PlayerNames.AnaLowercase).Should().Be(AddPlayerResult.DuplicateName);
		state.AddPlayer(PlayerNames.AnaUppercase).Should().Be(AddPlayerResult.DuplicateName);

		state.PlayerNames.Should().Equal(PlayerNames.Ana);
	}

	[Fact]
	public void HasPlayerConfigIssues_ReturnsNoIssues_WhenRosterIsValid()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer(PlayerNames.Ana);
		state.AddPlayer(PlayerNames.Bruno);
		state.AddPlayer(PlayerNames.Catarina);
		state.AddPlayer(PlayerNames.Diana);
		state.AddPlayer(PlayerNames.Eduardo);

		state.HasPlayerConfigIssues(out var issues).Should().BeFalse();
		issues.Should().BeEmpty();
	}

	[Fact]
	public void AvailableRoles_ReflectsSetupMetadataOrder()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.Seer,
			MainRoleType.SimpleWerewolf);

		state.AvailableRoles.Should().Equal(
			MainRoleType.Seer,
			MainRoleType.SimpleWerewolf);
	}

	[Fact]
	public void IncrementRole_SingleOptionalRole_CapsAtOne()
	{
		var state = LobbySetupMetadataFixture.DefaultState();

		state.IncrementRole(MainRoleType.Seer);
		state.GetRoleCount(MainRoleType.Seer).Should().Be(1);

		state.IncrementRole(MainRoleType.Seer);
		state.GetRoleCount(MainRoleType.Seer).Should().Be(1);
	}

	[Fact]
	public void ThiefDraft_SameSingleOptionalRoleCanFillBothMutuallyExclusiveOffers()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Witch,
			MainRoleType.Hunter,
			MainRoleType.Seer);
		for (var index = 0; index < 5; index++)
		{
			state.AddPlayer(PlayerNames.GeneratedPlayer(index));
		}
		state.IncrementRole(MainRoleType.Thief);
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.IncrementRole(MainRoleType.SimpleVillager);
		state.IncrementRole(MainRoleType.Witch);
		state.IncrementRole(MainRoleType.Hunter);

		state.IncrementRole(MainRoleType.Seer);
		state.IncrementRole(MainRoleType.Seer);

		var seer = state.GetRoleInfo(MainRoleType.Seer);
		seer.Count.Should().Be(2);
		seer.Affordance.Should().Be(RoleAffordance.Stepper);
		seer.CanIncrement.Should().BeFalse();
		state.TotalSelectedRoleCount.Should().Be(state.ExpectedRoleCount);
		state.HasRoleConfigIssues(out var issues).Should().BeFalse();
		issues.Should().BeEmpty();
	}

	[Fact]
	public void IncrementRole_StepperRole_IncrementsByOne()
	{
		var state = LobbySetupMetadataFixture.DefaultState();

		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(1);

		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(2);

		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(3);
	}

	[Fact]
	public void DecrementRole_StepperRole_DecrementsByOneFlooredAtZero()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.IncrementRole(MainRoleType.SimpleWerewolf);

		state.DecrementRole(MainRoleType.SimpleWerewolf);
		state.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(1);

		state.DecrementRole(MainRoleType.SimpleWerewolf);
		state.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(0);

		state.DecrementRole(MainRoleType.SimpleWerewolf);
		state.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(0);
	}

	[Fact]
	public void IncrementAndDecrementRole_ExactOptionalRole_TogglesFullBatch()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.TwoSisters,
			MainRoleType.ThreeBrothers);

		state.IncrementRole(MainRoleType.TwoSisters);
		state.GetRoleCount(MainRoleType.TwoSisters).Should().Be(2);

		state.IncrementRole(MainRoleType.TwoSisters);
		state.GetRoleCount(MainRoleType.TwoSisters).Should().Be(2);

		state.DecrementRole(MainRoleType.TwoSisters);
		state.GetRoleCount(MainRoleType.TwoSisters).Should().Be(0);

		state.IncrementRole(MainRoleType.ThreeBrothers);
		state.GetRoleCount(MainRoleType.ThreeBrothers).Should().Be(3);

		state.DecrementRole(MainRoleType.ThreeBrothers);
		state.GetRoleCount(MainRoleType.ThreeBrothers).Should().Be(0);
	}

	[Fact]
	public void GetSelectedRoles_FlattensCountsIntoRepeatedList()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.TwoSisters);
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.IncrementRole(MainRoleType.Seer);
		state.IncrementRole(MainRoleType.TwoSisters);

		var roles = state.GetSelectedRoles();

		roles.Should().HaveCount(5);
		roles.Count(r => r == MainRoleType.SimpleWerewolf).Should().Be(2);
		roles.Count(r => r == MainRoleType.Seer).Should().Be(1);
		roles.Count(r => r == MainRoleType.TwoSisters).Should().Be(2);
	}

	[Fact]
	public void TotalSelectedRoleCount_SumsAllRoleCounts()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.IncrementRole(MainRoleType.SimpleVillager);
		state.IncrementRole(MainRoleType.SimpleVillager);
		state.IncrementRole(MainRoleType.Seer);

		state.TotalSelectedRoleCount.Should().Be(4);
	}

	[Fact]
	public void ExpectedRoleCount_CountsOnlyThiefRoleCompositionExtras()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.Thief,
			MainRoleType.Actor);
		for (var i = 0; i < 5; i++)
			state.AddPlayer(PlayerNames.GeneratedPlayer(i));

		state.ExpectedRoleCount.Should().Be(5);

		state.IncrementRole(MainRoleType.Thief);
		state.ExpectedRoleCount.Should().Be(7);

		state.IncrementRole(MainRoleType.Actor);
		state.ExpectedRoleCount.Should().Be(7);
	}

	[Fact]
	public void HasConfigIssues_DetectsRoleCountMismatch()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		for (var i = 0; i < 5; i++)
			state.AddPlayer(PlayerNames.GeneratedPlayer(i));

		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.IncrementRole(MainRoleType.SimpleVillager);

		state.HasConfigIssues(out var issues).Should().BeTrue();
		issues.Should().Contain(e => e.Type == GameConfigValidationErrorType.TooFewRoles);
	}

	[Fact]
	public void HasRoleConfigIssues_ReturnsOnlyRoleIssues_WhenPlayerIssuesAlsoExist()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer(PlayerNames.Ana);
		state.AddPlayer(PlayerNames.Bruno);

		state.HasRoleConfigIssues(out var issues).Should().BeTrue();
		issues.Should().ContainSingle(e => e.Type == GameConfigValidationErrorType.TooFewRoles);
		issues.Should().NotContain(e => e.Type == GameConfigValidationErrorType.TooFewPlayers);
	}

	[Fact]
	public void HasConfigIssues_ReturnsNoIssues_WhenConfigIsValid()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		for (var i = 0; i < 5; i++)
			state.AddPlayer(PlayerNames.GeneratedPlayer(i));

		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.IncrementRole(MainRoleType.SimpleVillager);
		state.IncrementRole(MainRoleType.SimpleVillager);
		state.IncrementRole(MainRoleType.SimpleVillager);
		state.IncrementRole(MainRoleType.Seer);

		state.HasConfigIssues(out var issues).Should().BeFalse();
		issues.Should().BeEmpty();
	}

	[Fact]
	public void CanDecrementRole_ReturnsFalseWhenCountIsZero()
	{
		var state = LobbySetupMetadataFixture.DefaultState();

		state.CanDecrementRole(MainRoleType.Seer).Should().BeFalse();

		state.IncrementRole(MainRoleType.Seer);
		state.CanDecrementRole(MainRoleType.Seer).Should().BeTrue();

		state.DecrementRole(MainRoleType.Seer);
		state.CanDecrementRole(MainRoleType.Seer).Should().BeFalse();
	}

	[Fact]
	public void GetRoleInfo_Seer_ReturnsToggleWithBatchSizeOne()
	{
		var state = LobbySetupMetadataFixture.DefaultState();

		var info = state.GetRoleInfo(MainRoleType.Seer);

		info.Affordance.Should().Be(RoleAffordance.Toggle);
		info.BatchSize.Should().Be(1);
	}

	[Fact]
	public void GetRoleInfo_SimpleWerewolf_ReturnsStepper()
	{
		var state = LobbySetupMetadataFixture.DefaultState();

		var info = state.GetRoleInfo(MainRoleType.SimpleWerewolf);

		info.Affordance.Should().Be(RoleAffordance.Stepper);
	}

	[Fact]
	public void GetRoleInfo_TwoSisters_ReturnsToggleWithBatchSizeTwo()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(MainRoleType.TwoSisters);

		var info = state.GetRoleInfo(MainRoleType.TwoSisters);

		info.Affordance.Should().Be(RoleAffordance.Toggle);
		info.BatchSize.Should().Be(2);
	}

	[Fact]
	public void GetRoleInfo_ThreeBrothers_ReturnsToggleWithBatchSizeThree()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(MainRoleType.ThreeBrothers);

		var info = state.GetRoleInfo(MainRoleType.ThreeBrothers);

		info.Affordance.Should().Be(RoleAffordance.Toggle);
		info.BatchSize.Should().Be(3);
	}

	[Fact]
	public void GetRoleInfo_ReflectsCountAndCanFlags_AfterMutations()
	{
		var state = LobbySetupMetadataFixture.DefaultState();

		var before = state.GetRoleInfo(MainRoleType.Seer);
		before.Count.Should().Be(0);
		before.CanIncrement.Should().BeTrue();
		before.CanDecrement.Should().BeFalse();

		state.IncrementRole(MainRoleType.Seer);

		var after = state.GetRoleInfo(MainRoleType.Seer);
		after.Count.Should().Be(1);
		after.CanIncrement.Should().BeFalse();
		after.CanDecrement.Should().BeTrue();
	}

	[Fact]
	public void GetRoleInfo_ReturnsSetupMetadataFields()
	{
		var metadata = LobbySetupMetadataFixture.ForRoles(MainRoleType.Seer);
		var state = new LobbySetupState(metadata);
		var seerMetadata = metadata.AvailableRoles.Single();

		var info = state.GetRoleInfo(MainRoleType.Seer);

		info.DisplayName.Should().Be(seerMetadata.DisplayName);
		info.Group.Should().Be(seerMetadata.Group);
		info.GroupDisplayName.Should().Be(seerMetadata.GroupDisplayName);
	}

	[Fact]
	public void AvailableRoleGroups_UsesLobbyGroupOrderAndGroupLabels()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.Gypsy,
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.Angel,
			MainRoleType.WildChild);

		var groups = state.AvailableRoleGroups;

		groups.Select(group => group.Group).Should().Equal(
			RoleGroup.Villagers,
			RoleGroup.Werewolves,
			RoleGroup.Ambiguous,
			RoleGroup.Loners,
			RoleGroup.NewMoon);
		groups.Select(group => group.DisplayName).Should().Equal(
			RoleGroup.Villagers.GetDisplayName(),
			RoleGroup.Werewolves.GetDisplayName(),
			RoleGroup.Ambiguous.GetDisplayName(),
			RoleGroup.Loners.GetDisplayName(),
			RoleGroup.NewMoon.GetDisplayName());
		groups.SelectMany(group => group.Roles).Select(info => info.Role).Should().Equal(
			MainRoleType.Seer,
			MainRoleType.SimpleWerewolf,
			MainRoleType.WildChild,
			MainRoleType.Angel,
			MainRoleType.Gypsy);
	}

	[Fact]
	public void AvailableRoleGroups_PlacesWhiteWerewolfInLoners()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(MainRoleType.WhiteWerewolf);

		var group = state.AvailableRoleGroups.Single(group =>
			group.Roles.Any(role => role.Role == MainRoleType.WhiteWerewolf));

		group.Group.Should().Be(RoleGroup.Loners);
	}

	[Fact]
	public void AvailableRoleGroups_DoesNotDeriveGroupLabelFromFirstRoleInGroup()
	{
		var mislabeledFirstRole = LobbySetupMetadataFixture.RoleMetadata(MainRoleType.Seer) with
		{
			GroupDisplayName = ClientTestReferences.FixtureLabels.UnexpectedRoleGroupDisplayName
		};
		var state = new LobbySetupState(new LobbySetupMetadata(
			GameSessionConfig.MinimumPlayerCount,
			[
				mislabeledFirstRole,
				LobbySetupMetadataFixture.RoleMetadata(MainRoleType.SimpleVillager)
			]));

		var group = state.AvailableRoleGroups.Should().ContainSingle().Subject;

		group.Group.Should().Be(RoleGroup.Villagers);
		group.DisplayName.Should().Be(RoleGroup.Villagers.GetDisplayName());
	}

	[Fact]
	public void Reset_ClearsPlayersAndRoleCounts()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer(PlayerNames.Ana);
		state.AddPlayer(PlayerNames.Bruno);
		state.AddPlayer(PlayerNames.Catarina);
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.IncrementRole(MainRoleType.Seer);

		state.Reset();

		state.PlayerNames.Should().BeEmpty();
		state.GetSelectedRoles().Should().BeEmpty();
		state.TotalSelectedRoleCount.Should().Be(0);
	}

	private static LobbySetupState CreateStateWithAcceptedRoleLockIn(
		params MainRoleType[] roleComposition)
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			roleComposition.Distinct().ToArray());
		foreach (var name in PlayerNames.DefaultFive)
		{
			state.AddPlayer(name).Should().Be(AddPlayerResult.Success);
		}
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: PlayerNames.DefaultFive.Length,
			roleComposition);
		new GameClientManager()
			.TryReplaceStagedRoleLockIn(state, 0, roleLockIn)
			.Should().BeTrue();
		return state;
	}

	private static PublicGroupPartition AcceptCurrentRosterPartition(
		LobbySetupState state)
	{
		var rosterIds = state.PlayerRoster.Select(player => player.Id).ToArray();
		var partition = PublicGroupPartition.Create(
			rosterIds,
			firstGroupPlayerIds: [rosterIds[0], rosterIds[2]],
			secondGroupPlayerIds: rosterIds.Except([rosterIds[0], rosterIds[2]]));
		new GameClientManager()
			.TryReplaceStagedPublicGroupPartition(state, partition)
			.Should().BeTrue();
		return partition;
	}
}
