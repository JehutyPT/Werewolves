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

		var actorSetupCards = ActorSetupCards.CreateFromPrintedRoles(
			version: 1,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]);
		state.CanReplaceActorSetupCards(
			expectedCurrentVersion: 0,
			actorSetupCards).Should().BeTrue();
		state.ApplyAcceptedActorSetupCards(actorSetupCards);

		state.RequiresActorSetupCards.Should().BeFalse();
		state.RequiresPublicGroupPartition.Should().BeTrue();
		state.TryCreateSimulationScenario(out _).Should().BeFalse();

		AcceptCurrentRosterPartition(state);

		state.TryCreateSimulationScenario(out var scenario).Should().BeTrue();
		scenario.ActorSetupCards.Should().Be(actorSetupCards);
	}

	[Fact]
	public void ActorSetupReplacement_RequiresNextVersionAndStopsAfterLobbyExit()
	{
		var state = CreateStateWithAcceptedRoleLockIn(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.Actor);
		var accepted = ActorSetupCards.CreateFromPrintedRoles(
			version: 1,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]);
		state.CanReplaceActorSetupCards(0, accepted).Should().BeTrue();
		state.ApplyAcceptedActorSetupCards(accepted);
		var next = ActorSetupCards.CreateFromPrintedRoles(
			version: 2,
			[
				MainRoleType.Seer,
				MainRoleType.Defender,
				MainRoleType.Elder
			]);
		var skipped = ActorSetupCards.CreateFromPrintedRoles(
			version: 3,
			next.PrintedRoles);

		state.CanReplaceActorSetupCards(0, next).Should().BeFalse();
		state.CanReplaceActorSetupCards(1, skipped).Should().BeFalse();
		state.AcceptedActorSetupCards.Should().BeSameAs(accepted);

		state.FinalizeRoleLockIn(state.AcceptedRoleLockIn!);

		state.CanReplaceActorSetupCards(1, next).Should().BeFalse();
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
		var actorSetupCards = ActorSetupCards.CreateFromPrintedRoles(
			version: 1,
			[
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.Hunter
			]);
		state.CanReplaceActorSetupCards(0, actorSetupCards).Should().BeTrue();
		state.ApplyAcceptedActorSetupCards(actorSetupCards);
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

		state.CanReplaceRoleLockIn(1, replacement).Should().BeTrue();
		state.ApplyAcceptedRoleLockIn(replacement);

		state.AcceptedActorSetupCards.Should().Be(ActorSetupCards.None);
		state.RequiresActorSetupCards.Should().BeFalse();
		state.TryCreateSimulationScenario(out _).Should().BeTrue();
	}

	[Fact]
	public void ApplyAcceptedPublicGroupPartition_WithExactCurrentRoster_CompletesLobbySetup()
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
		var scenarioChanges = 0;
		state.SimulationScenarioChanged += (_, _) => scenarioChanges++;

		var canReplace = state.CanReplacePublicGroupPartition(partition);
		state.ApplyAcceptedPublicGroupPartition(partition);
		var created = state.TryCreateSimulationScenario(out var scenario);

		canReplace.Should().BeTrue();
		state.AcceptedPublicGroupPartition.Should().BeSameAs(partition);
		state.RequiresPublicGroupPartition.Should().BeFalse();
		created.Should().BeTrue();
		scenario.Should().NotBeNull();
		scenario.PublicGroupPartition.Should().Be(
			CanonicalPublicGroupPartition.Project(rosterIds, partition));
		scenarioChanges.Should().Be(1);
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
		state.CanReplacePublicGroupPartition(partition).Should().BeFalse();
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
		var scenarioChanges = 0;
		state.SimulationScenarioChanged += (_, _) => scenarioChanges++;
		var acceptedScenario = state.CreateSimulationScenario();

		state.CanReplacePublicGroupPartition(equivalent).Should().BeTrue();
		state.ApplyAcceptedPublicGroupPartition(equivalent);
		state.AcceptedPublicGroupPartition.Should().BeSameAs(accepted);
		state.CreateSimulationScenario().Should().Be(acceptedScenario);
		scenarioChanges.Should().Be(0);

		state.CanReplacePublicGroupPartition(replacement).Should().BeTrue();
		state.ApplyAcceptedPublicGroupPartition(replacement);
		state.AcceptedPublicGroupPartition.Should().BeSameAs(replacement);
		state.CreateSimulationScenario().Should().NotBe(acceptedScenario);
		scenarioChanges.Should().Be(1);

		state.FinalizeRoleLockIn(state.AcceptedRoleLockIn!);
		state.CanReplacePublicGroupPartition(accepted).Should().BeFalse();
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

		state.CanReplaceRoleLockIn(1, replacement).Should().BeTrue();
		state.ApplyAcceptedRoleLockIn(replacement);

		state.AcceptedPublicGroupPartition.Should().BeNull();
		state.RequiresPublicGroupPartition.Should().BeFalse();
		state.TryCreateSimulationScenario(out _).Should().BeTrue();
	}

	[Fact]
	public void RestoreAcceptedRoleLockIn_WithTypedRosterAndPartition_RestoresExactState()
	{
		MainRoleType[] roles =
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.PrejudicedManipulator
		];
		var state = LobbySetupMetadataFixture.StateWithRoles(roles);
		var roster = PlayerNames.DefaultFive
			.Select((name, index) => new GameSessionPlayerConfig(
				Guid.Parse($"20000000-0000-0000-0000-{index + 1:D12}"),
				name))
			.ToArray();
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 7,
			playerCount: roster.Length,
			roles);
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			[roster[0].Id, roster[3].Id],
			[roster[1].Id, roster[2].Id, roster[4].Id]);
		var scenarioChanges = 0;
		state.SimulationScenarioChanged += (_, _) => scenarioChanges++;

		state.RestoreAcceptedRoleLockIn(
			roster,
			roleLockIn,
			ActorSetupCards.None,
			partition);

		state.PlayerRoster.Select(player => player.Id).Should().Equal(
			roster.Select(player => player.Id));
		state.PlayerRoster.Select(player => player.Name).Should().Equal(
			roster.Select(player => player.Name));
		state.AcceptedRoleLockIn.Should().BeSameAs(roleLockIn);
		state.AcceptedPublicGroupPartition.Should().BeSameAs(partition);
		state.TryCreateSimulationScenario(out _).Should().BeTrue();
		scenarioChanges.Should().Be(1);
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
		state.CanReplaceRoleLockIn(0, roleLockIn).Should().BeTrue();
		state.ApplyAcceptedRoleLockIn(roleLockIn);

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
		state.CanReplaceRoleLockIn(0, roleLockIn).Should().BeTrue();
		state.ApplyAcceptedRoleLockIn(roleLockIn);
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
		state.CanReplacePublicGroupPartition(partition).Should().BeTrue();
		state.ApplyAcceptedPublicGroupPartition(partition);
		return partition;
	}
}
