using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class SelectPlayersInputStateTests
{
	private static DashboardRosterEntry MakeEntry(Guid id, int seat, string name) =>
		new(id, seat, name, "role label", false, "health label", false, [], "status label");

	#region Filtering and ordering

	[Fact]
	public void Constructor_FiltersRosterToSelectablePlayersOnly()
	{
		var p1 = Guid.NewGuid();
		var p2 = Guid.NewGuid();
		var p3 = Guid.NewGuid();
		var roster = new[]
		{
			MakeEntry(p1, 1, "Ana"),
			MakeEntry(p2, 2, "Bruno"),
			MakeEntry(p3, 3, "Catarina")
		};
		var selectable = new HashSet<Guid> { p1, p3 };

		var state = new SelectPlayersInputState(roster, selectable, NumberRangeConstraint.Single);

		state.SelectablePlayers.Should().HaveCount(2);
		state.SelectablePlayers.Select(p => p.Name).Should().Equal("Ana", "Catarina");
	}

	[Fact]
	public void Constructor_OrdersSelectablePlayersBySeatNumber()
	{
		var p1 = Guid.NewGuid();
		var p2 = Guid.NewGuid();
		var p3 = Guid.NewGuid();
		var roster = new[]
		{
			MakeEntry(p2, 3, "Bruno"),
			MakeEntry(p3, 1, "Catarina"),
			MakeEntry(p1, 2, "Ana")
		};
		var selectable = new HashSet<Guid> { p1, p2, p3 };

		var state = new SelectPlayersInputState(roster, selectable, NumberRangeConstraint.Single);

		state.SelectablePlayers.Select(p => p.Name).Should().Equal("Catarina", "Ana", "Bruno");
	}

	#endregion

	#region Single-select mode

	[Fact]
	public void ToggleSelection_SingleSelect_SelectsPlayer()
	{
		var p1 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1 }, NumberRangeConstraint.Single);

		var changed = state.ToggleSelection(p1);

		changed.Should().BeTrue();
		state.IsSelected(p1).Should().BeTrue();
		state.SelectedPlayerIds.Should().HaveCount(1);
	}

	[Fact]
	public void ToggleSelection_SingleSelect_DeselectsAlreadySelectedPlayer()
	{
		var p1 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1 }, NumberRangeConstraint.Single);
		state.ToggleSelection(p1);

		var changed = state.ToggleSelection(p1);

		changed.Should().BeTrue();
		state.IsSelected(p1).Should().BeFalse();
		state.SelectedPlayerIds.Should().BeEmpty();
	}

	[Fact]
	public void ToggleSelection_SingleSelect_ReplacesExistingSelection()
	{
		var p1 = Guid.NewGuid();
		var p2 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana"), MakeEntry(p2, 2, "Bruno") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1, p2 }, NumberRangeConstraint.Single);
		state.ToggleSelection(p1);

		var changed = state.ToggleSelection(p2);

		changed.Should().BeTrue();
		state.IsSelected(p1).Should().BeFalse();
		state.IsSelected(p2).Should().BeTrue();
		state.SelectedPlayerIds.Should().HaveCount(1);
	}

	[Fact]
	public void IsSingleSelect_WhenConstraintMaxIsOne_ReturnsTrue()
	{
		var p1 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1 }, NumberRangeConstraint.Single);

		state.IsSingleSelect.Should().BeTrue();
	}

	#endregion

	#region Multi-select mode

	[Fact]
	public void ToggleSelection_MultiSelect_AllowsMultipleSelections()
	{
		var p1 = Guid.NewGuid();
		var p2 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana"), MakeEntry(p2, 2, "Bruno") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1, p2 }, NumberRangeConstraint.Exact(2));

		state.ToggleSelection(p1);
		state.ToggleSelection(p2);

		state.IsSelected(p1).Should().BeTrue();
		state.IsSelected(p2).Should().BeTrue();
		state.SelectedPlayerIds.Should().HaveCount(2);
	}

	[Fact]
	public void ToggleSelection_MultiSelect_RejectsSelectionBeyondMaximum()
	{
		var p1 = Guid.NewGuid();
		var p2 = Guid.NewGuid();
		var p3 = Guid.NewGuid();
		var roster = new[]
		{
			MakeEntry(p1, 1, "Ana"),
			MakeEntry(p2, 2, "Bruno"),
			MakeEntry(p3, 3, "Catarina")
		};
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1, p2, p3 }, NumberRangeConstraint.Exact(2));
		state.ToggleSelection(p1);
		state.ToggleSelection(p2);

		var changed = state.ToggleSelection(p3);

		changed.Should().BeFalse();
		state.IsSelected(p3).Should().BeFalse();
		state.SelectedPlayerIds.Should().HaveCount(2);
	}

	[Fact]
	public void ToggleSelection_MultiSelect_AllowsDeselectWhenAtMaximum()
	{
		var p1 = Guid.NewGuid();
		var p2 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana"), MakeEntry(p2, 2, "Bruno") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1, p2 }, NumberRangeConstraint.Exact(2));
		state.ToggleSelection(p1);
		state.ToggleSelection(p2);

		var changed = state.ToggleSelection(p1);

		changed.Should().BeTrue();
		state.IsSelected(p1).Should().BeFalse();
		state.IsSelected(p2).Should().BeTrue();
		state.SelectedPlayerIds.Should().HaveCount(1);
	}

	[Fact]
	public void IsSingleSelect_WhenConstraintMaxIsGreaterThanOne_ReturnsFalse()
	{
		var p1 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1 }, NumberRangeConstraint.Exact(2));

		state.IsSingleSelect.Should().BeFalse();
	}

	#endregion

	#region CanSubmit

	[Fact]
	public void CanSubmit_WhenSelectionMeetsConstraint_ReturnsTrue()
	{
		var p1 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1 }, NumberRangeConstraint.Single);
		state.ToggleSelection(p1);

		state.CanSubmit.Should().BeTrue();
	}

	[Fact]
	public void CanSubmit_WhenSelectionIsEmpty_ReturnsFalse()
	{
		var p1 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1 }, NumberRangeConstraint.Single);

		state.CanSubmit.Should().BeFalse();
	}

	[Fact]
	public void CanSubmit_WhenSelectionBelowMinimum_ReturnsFalse()
	{
		var p1 = Guid.NewGuid();
		var p2 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana"), MakeEntry(p2, 2, "Bruno") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1, p2 }, NumberRangeConstraint.Exact(2));
		state.ToggleSelection(p1);

		state.CanSubmit.Should().BeFalse();
	}

	[Fact]
	public void CanSubmit_OptionalConstraint_WhenSelectionIsEmpty_ReturnsTrue()
	{
		var p1 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1 }, NumberRangeConstraint.SingleOptional);

		state.CanSubmit.Should().BeTrue();
	}

	[Fact]
	public void CanSubmit_RangeConstraint_WhenSelectionWithinRange_ReturnsTrue()
	{
		var p1 = Guid.NewGuid();
		var p2 = Guid.NewGuid();
		var p3 = Guid.NewGuid();
		var roster = new[]
		{
			MakeEntry(p1, 1, "Ana"),
			MakeEntry(p2, 2, "Bruno"),
			MakeEntry(p3, 3, "Catarina")
		};
		var state = new SelectPlayersInputState(
			roster,
			new HashSet<Guid> { p1, p2, p3 },
			NumberRangeConstraint.Range(1, 3));
		state.ToggleSelection(p1);
		state.ToggleSelection(p2);

		state.CanSubmit.Should().BeTrue();
	}

	#endregion

	#region GetSelection

	[Fact]
	public void GetSelection_ReturnsNewHashSetWithSelectedIds()
	{
		var p1 = Guid.NewGuid();
		var p2 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana"), MakeEntry(p2, 2, "Bruno") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1, p2 }, NumberRangeConstraint.Exact(2));
		state.ToggleSelection(p1);
		state.ToggleSelection(p2);

		var selection = state.GetSelection();

		selection.Should().BeEquivalentTo(new[] { p1, p2 });
	}

	[Fact]
	public void GetSelection_ReturnsDefensiveCopy()
	{
		var p1 = Guid.NewGuid();
		var roster = new[] { MakeEntry(p1, 1, "Ana") };
		var state = new SelectPlayersInputState(roster, new HashSet<Guid> { p1 }, NumberRangeConstraint.Single);
		state.ToggleSelection(p1);

		var selection = state.GetSelection();
		selection.Clear();

		state.IsSelected(p1).Should().BeTrue("clearing the returned set should not affect internal state");
	}

	#endregion

	#region Works for both scenarios (targeting and role-identification)

	[Fact]
	public void WorksForTargetingScenario_SingleVictimSelection()
	{
		// Werewolves choosing a victim: single-select from alive non-werewolf players
		var wolf = Guid.NewGuid();
		var villager1 = Guid.NewGuid();
		var villager2 = Guid.NewGuid();
		var villager3 = Guid.NewGuid();
		var roster = new[]
		{
			MakeEntry(wolf, 1, "Lobo"),
			MakeEntry(villager1, 2, "Ana"),
			MakeEntry(villager2, 3, "Bruno"),
			MakeEntry(villager3, 4, "Catarina")
		};
		// Only non-werewolves are selectable
		var selectable = new HashSet<Guid> { villager1, villager2, villager3 };

		var state = new SelectPlayersInputState(roster, selectable, NumberRangeConstraint.Single);

		state.SelectablePlayers.Should().HaveCount(3);
		state.SelectablePlayers.Select(p => p.Name).Should().Equal("Ana", "Bruno", "Catarina");

		state.ToggleSelection(villager2);
		state.CanSubmit.Should().BeTrue();
		state.GetSelection().Should().BeEquivalentTo(new[] { villager2 });
	}

	[Fact]
	public void WorksForRoleIdentificationScenario_IdentifyWhoWokeUp()
	{
		// Moderator indicating who woke up: single-select from unassigned players
		var p1 = Guid.NewGuid();
		var p2 = Guid.NewGuid();
		var p3 = Guid.NewGuid();
		var p4 = Guid.NewGuid();
		var p5 = Guid.NewGuid();
		var roster = new[]
		{
			MakeEntry(p1, 1, "Ana"),
			MakeEntry(p2, 2, "Bruno"),
			MakeEntry(p3, 3, "Catarina"),
			MakeEntry(p4, 4, "Diana"),
			MakeEntry(p5, 5, "Eduardo")
		};
		// All unassigned players are selectable for identification
		var selectable = new HashSet<Guid> { p1, p2, p3, p4, p5 };

		var state = new SelectPlayersInputState(roster, selectable, NumberRangeConstraint.Single);

		state.SelectablePlayers.Should().HaveCount(5);
		state.ToggleSelection(p3);
		state.CanSubmit.Should().BeTrue();
		state.GetSelection().Should().BeEquivalentTo(new[] { p3 });
	}

	#endregion
}
