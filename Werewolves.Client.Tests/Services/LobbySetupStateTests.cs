using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class LobbySetupStateTests
{
	[Fact]
	public void Construction_PublicSeamRequiresLobbySetupMetadata()
	{
		var publicConstructors = typeof(LobbySetupState).GetConstructors();

		publicConstructors.Should().ContainSingle()
			.Which.GetParameters().Should().ContainSingle()
			.Which.ParameterType.Should().Be(typeof(LobbySetupMetadata));
	}

	[Fact]
	public void AddPlayer_AppendsNamesInSeatingOrder()
	{
		var state = LobbySetupMetadataFixture.DefaultState();

		state.AddPlayer("Ana").Should().Be(AddPlayerResult.Success);
		state.AddPlayer("Bruno").Should().Be(AddPlayerResult.Success);
		state.AddPlayer("Catarina").Should().Be(AddPlayerResult.Success);

		state.PlayerNames.Should().Equal("Ana", "Bruno", "Catarina");
	}

	[Fact]
	public void RemovePlayerAt_RemovesNameFromSeatingOrder()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");

		state.RemovePlayerAt(1).Should().BeTrue();

		state.PlayerNames.Should().Equal("Ana", "Catarina");
	}

	[Fact]
	public void MovePlayerUp_SwapsPlayerWithPreviousSeat()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");

		state.MovePlayerUp(2).Should().BeTrue();

		state.PlayerNames.Should().Equal("Ana", "Catarina", "Bruno");
	}

	[Fact]
	public void MovePlayerDown_SwapsPlayerWithNextSeat()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");

		state.MovePlayerDown(0).Should().BeTrue();

		state.PlayerNames.Should().Equal("Bruno", "Ana", "Catarina");
	}

	[Fact]
	public void CanMovePlayer_ReportsDisabledAtSeatingOrderBoundaries()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");

		state.CanMovePlayerUp(0).Should().BeFalse();
		state.CanMovePlayerDown(2).Should().BeFalse();
		state.CanMovePlayerUp(1).Should().BeTrue();
		state.CanMovePlayerDown(1).Should().BeTrue();
	}

	[Fact]
	public void HasPlayerConfigIssues_ReturnsTooFewPlayers_WhenUnderMinimum()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");

		state.HasPlayerConfigIssues(out var issues).Should().BeTrue();
		issues.Should().ContainSingle(e => e.Type == GameConfigValidationErrorType.TooFewPlayers);
	}

	[Fact]
	public void AddPlayer_RejectsDuplicateNameCaseInsensitive()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer("Ana").Should().Be(AddPlayerResult.Success);

		state.AddPlayer("ana").Should().Be(AddPlayerResult.DuplicateName);
		state.AddPlayer("ANA").Should().Be(AddPlayerResult.DuplicateName);

		state.PlayerNames.Should().Equal("Ana");
	}

	[Fact]
	public void HasPlayerConfigIssues_ReturnsNoIssues_WhenRosterIsValid()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");
		state.AddPlayer("Diana");
		state.AddPlayer("Eduardo");

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
	public void ExpectedRoleCount_MatchesPlayerCountPlusSpecialRoleExtras()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.Thief,
			MainRoleType.Actor);
		for (var i = 0; i < 5; i++)
			state.AddPlayer($"Player{i}");

		state.ExpectedRoleCount.Should().Be(5);

		state.IncrementRole(MainRoleType.Thief);
		state.ExpectedRoleCount.Should().Be(7);

		state.IncrementRole(MainRoleType.Actor);
		state.ExpectedRoleCount.Should().Be(10);
	}

	[Fact]
	public void HasConfigIssues_DetectsRoleCountMismatch()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		for (var i = 0; i < 5; i++)
			state.AddPlayer($"Player{i}");

		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.IncrementRole(MainRoleType.SimpleVillager);

		state.HasConfigIssues(out var issues).Should().BeTrue();
		issues.Should().Contain(e => e.Type == GameConfigValidationErrorType.TooFewRoles);
	}

	[Fact]
	public void HasRoleConfigIssues_ReturnsOnlyRoleIssues_WhenPlayerIssuesAlsoExist()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");

		state.HasRoleConfigIssues(out var issues).Should().BeTrue();
		issues.Should().ContainSingle(e => e.Type == GameConfigValidationErrorType.TooFewRoles);
		issues.Should().NotContain(e => e.Type == GameConfigValidationErrorType.TooFewPlayers);
	}

	[Fact]
	public void HasConfigIssues_ReturnsNoIssues_WhenConfigIsValid()
	{
		var state = LobbySetupMetadataFixture.DefaultState();
		for (var i = 0; i < 5; i++)
			state.AddPlayer($"Player{i}");

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
			GroupDisplayName = "Unexpected first role group label"
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
}
