using FluentAssertions;
using Werewolves.Client.Services;
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
		var state = CreateDefaultState();

		state.AddPlayer("Ana").Should().Be(AddPlayerResult.Success);
		state.AddPlayer("Bruno").Should().Be(AddPlayerResult.Success);
		state.AddPlayer("Catarina").Should().Be(AddPlayerResult.Success);

		state.PlayerNames.Should().Equal("Ana", "Bruno", "Catarina");
	}

	[Fact]
	public void RemovePlayerAt_RemovesNameFromSeatingOrder()
	{
		var state = CreateDefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");

		state.RemovePlayerAt(1).Should().BeTrue();

		state.PlayerNames.Should().Equal("Ana", "Catarina");
	}

	[Fact]
	public void MovePlayerUp_SwapsPlayerWithPreviousSeat()
	{
		var state = CreateDefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");

		state.MovePlayerUp(2).Should().BeTrue();

		state.PlayerNames.Should().Equal("Ana", "Catarina", "Bruno");
	}

	[Fact]
	public void MovePlayerDown_SwapsPlayerWithNextSeat()
	{
		var state = CreateDefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");

		state.MovePlayerDown(0).Should().BeTrue();

		state.PlayerNames.Should().Equal("Bruno", "Ana", "Catarina");
	}

	[Fact]
	public void CanMovePlayer_ReportsDisabledAtSeatingOrderBoundaries()
	{
		var state = CreateDefaultState();
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
		var state = CreateDefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");

		state.HasPlayerConfigIssues(out var issues).Should().BeTrue();
		issues.Should().ContainSingle(e => e.Type == GameConfigValidationErrorType.TooFewPlayers);
	}

	[Fact]
	public void AddPlayer_RejectsDuplicateNameCaseInsensitive()
	{
		var state = CreateDefaultState();
		state.AddPlayer("Ana").Should().Be(AddPlayerResult.Success);

		state.AddPlayer("ana").Should().Be(AddPlayerResult.DuplicateName);
		state.AddPlayer("ANA").Should().Be(AddPlayerResult.DuplicateName);

		state.PlayerNames.Should().Equal("Ana");
	}

	[Fact]
	public void HasPlayerConfigIssues_ReturnsNoIssues_WhenRosterIsValid()
	{
		var state = CreateDefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");
		state.AddPlayer("Catarina");
		state.AddPlayer("Diana");
		state.AddPlayer("Eduardo");

		state.HasPlayerConfigIssues(out var issues).Should().BeFalse();
		issues.Should().BeEmpty();
	}

	[Fact]
	public void AvailableRoles_ContainsOnlyEngineSupportedRoles()
	{
		var state = CreateDefaultState();

		state.AvailableRoles.Should().Equal(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager);
		state.AvailableRoles.Should().NotContain(MainRoleType.Witch);
		state.AvailableRoles.Should().NotContain(MainRoleType.TwoSisters);
	}

	[Fact]
	public void IncrementRole_SingleOptionalRole_CapsAtOne()
	{
		var state = CreateDefaultState();

		state.IncrementRole(MainRoleType.Seer);
		state.GetRoleCount(MainRoleType.Seer).Should().Be(1);

		state.IncrementRole(MainRoleType.Seer);
		state.GetRoleCount(MainRoleType.Seer).Should().Be(1);
	}

	[Fact]
	public void IncrementRole_StepperRole_IncrementsByOne()
	{
		var state = CreateDefaultState();

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
		var state = CreateDefaultState();
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
		var state = new LobbySetupState(CreateSetupMetadata(
			MainRoleType.TwoSisters,
			MainRoleType.ThreeBrothers));

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
		var state = new LobbySetupState(CreateSetupMetadata(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.TwoSisters));
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
		var state = CreateDefaultState();
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		state.IncrementRole(MainRoleType.SimpleVillager);
		state.IncrementRole(MainRoleType.SimpleVillager);
		state.IncrementRole(MainRoleType.Seer);

		state.TotalSelectedRoleCount.Should().Be(4);
	}

	[Fact]
	public void ExpectedRoleCount_MatchesPlayerCountPlusSpecialRoleExtras()
	{
		var state = new LobbySetupState(CreateSetupMetadata(
			MainRoleType.Thief,
			MainRoleType.Actor));
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
		var state = CreateDefaultState();
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
		var state = CreateDefaultState();
		state.AddPlayer("Ana");
		state.AddPlayer("Bruno");

		state.HasRoleConfigIssues(out var issues).Should().BeTrue();
		issues.Should().ContainSingle(e => e.Type == GameConfigValidationErrorType.TooFewRoles);
		issues.Should().NotContain(e => e.Type == GameConfigValidationErrorType.TooFewPlayers);
	}

	[Fact]
	public void HasConfigIssues_ReturnsNoIssues_WhenConfigIsValid()
	{
		var state = CreateDefaultState();
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
		var state = CreateDefaultState();

		state.CanDecrementRole(MainRoleType.Seer).Should().BeFalse();

		state.IncrementRole(MainRoleType.Seer);
		state.CanDecrementRole(MainRoleType.Seer).Should().BeTrue();

		state.DecrementRole(MainRoleType.Seer);
		state.CanDecrementRole(MainRoleType.Seer).Should().BeFalse();
	}

	[Fact]
	public void GetRoleInfo_Seer_ReturnsToggleWithBatchSizeOne()
	{
		var state = CreateDefaultState();

		var info = state.GetRoleInfo(MainRoleType.Seer);

		info.Affordance.Should().Be(RoleAffordance.Toggle);
		info.BatchSize.Should().Be(1);
	}

	[Fact]
	public void GetRoleInfo_SimpleWerewolf_ReturnsStepper()
	{
		var state = CreateDefaultState();

		var info = state.GetRoleInfo(MainRoleType.SimpleWerewolf);

		info.Affordance.Should().Be(RoleAffordance.Stepper);
	}

	[Fact]
	public void GetRoleInfo_TwoSisters_ReturnsToggleWithBatchSizeTwo()
	{
		var state = new LobbySetupState(CreateSetupMetadata(MainRoleType.TwoSisters));

		var info = state.GetRoleInfo(MainRoleType.TwoSisters);

		info.Affordance.Should().Be(RoleAffordance.Toggle);
		info.BatchSize.Should().Be(2);
	}

	[Fact]
	public void GetRoleInfo_ThreeBrothers_ReturnsToggleWithBatchSizeThree()
	{
		var state = new LobbySetupState(CreateSetupMetadata(MainRoleType.ThreeBrothers));

		var info = state.GetRoleInfo(MainRoleType.ThreeBrothers);

		info.Affordance.Should().Be(RoleAffordance.Toggle);
		info.BatchSize.Should().Be(3);
	}

	[Fact]
	public void GetRoleInfo_ReflectsCountAndCanFlags_AfterMutations()
	{
		var state = CreateDefaultState();

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
	public void GetRoleInfo_DisplayName_MatchesPublicName()
	{
		var state = CreateDefaultState();

		var info = state.GetRoleInfo(MainRoleType.Seer);

		info.DisplayName.Should().Be(MainRoleType.Seer.GetPublicName());
	}

	[Fact]
	public void AvailableRoleGroups_UsesLobbyGroupOrderAndGroupLabels()
	{
		var state = new LobbySetupState(CreateSetupMetadata(
			MainRoleType.Gypsy,
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.Angel,
			MainRoleType.WildChild));

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
		var mislabeledFirstRole = CreateRoleMetadata(MainRoleType.Seer) with
		{
			GroupDisplayName = "Unexpected first role group label"
		};
		var state = new LobbySetupState(new LobbySetupMetadata(
			GameSessionConfig.MinimumPlayerCount,
			[
				mislabeledFirstRole,
				CreateRoleMetadata(MainRoleType.SimpleVillager)
			]));

		var group = state.AvailableRoleGroups.Should().ContainSingle().Subject;

		group.Group.Should().Be(RoleGroup.Villagers);
		group.DisplayName.Should().Be(RoleGroup.Villagers.GetDisplayName());
	}

	private static LobbySetupMetadata CreateSetupMetadata(params MainRoleType[] roles)
	{
		return new LobbySetupMetadata(
			GameSessionConfig.MinimumPlayerCount,
			roles.Select(CreateRoleMetadata).ToArray());
	}

	private static LobbySetupState CreateDefaultState()
	{
		return new LobbySetupState(CreateSetupMetadata(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager));
	}

	private static LobbySetupRoleMetadata CreateRoleMetadata(MainRoleType role)
	{
		var group = role.GetRoleGroup();
		return new LobbySetupRoleMetadata(
			role,
			role.GetPublicName(),
			group,
			group.GetDisplayName(),
			GameSessionConfig.RoleCountConstraints[role]);
	}
}
