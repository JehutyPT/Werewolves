using System.Reflection;
using Bunit;
using FluentAssertions;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class SelectPlayersViewBunitTests
{
	[Fact]
	public void SelectingPlayerUpdatesRenderedStateAndEnablesSubmit()
	{
		using var context = new ModeratorComponentTestContext();
		var anaId = Guid.NewGuid();
		var brunoId = Guid.NewGuid();
		var instruction = CreateInstruction(anaId, brunoId);
		var roster = new[]
		{
			CreateRosterEntry(anaId, 1, "Ana"),
			CreateRosterEntry(brunoId, 2, "Bruno")
		};

		var cut = context.RenderModeratorComponent<SelectPlayersView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster));

		var options = cut.FindAll("li[role='option']");
		options.Should().HaveCount(2);
		options[0].TextContent.Should().Contain("1").And.Contain("Ana");
		options[1].TextContent.Should().Contain("2").And.Contain("Bruno");
		cut.Find(".ww-select-players-list")
			.GetAttribute("aria-label")
			.Should()
			.Be(ClientStrings.SelectPlayers_ListAria);
		cut.Find("button.ww-btn-hold").HasAttribute("disabled").Should().BeTrue();

		options[1].Click();

		options = cut.FindAll("li[role='option']");
		options[1].ClassList.Should().Contain("ww-select-players-item--selected");
		cut.Find("button.ww-btn-hold").HasAttribute("disabled").Should().BeFalse();
	}

	private static SelectPlayersInstruction CreateInstruction(params Guid[] playerIds) =>
		(SelectPlayersInstruction)SelectPlayersConstructor.Invoke(
			[
				playerIds.ToHashSet(),
				NumberRangeConstraint.Single,
				null,
				"Escolhe um jogador.",
				null
			]);

	private static DashboardRosterEntry CreateRosterEntry(Guid playerId, int seatNumber, string name) =>
		new(
			playerId,
			seatNumber,
			name,
			DashboardRoster.UnknownRoleLabel,
			IsRoleKnown: false,
			DashboardRoster.HealthLabel(Werewolves.Core.StateModels.Enums.PlayerHealth.Alive),
			IsDead: false,
			StatusEffects: [],
			DashboardRoster.NoStatusEffectsLabel);

	private static readonly ConstructorInfo SelectPlayersConstructor =
		typeof(SelectPlayersInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 5);
}
