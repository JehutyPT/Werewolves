using System.Globalization;
using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Components;

public class SelectPlayersViewBunitTests
{
	private static string PlayerOptionSelector => Html.Selectors.ElementWithRole(Html.Elements.ListItem, Html.Roles.Option);
	private static string PlayerListSelector => $".{ClientTestReferences.Css.Classes.SelectPlayersList}";
	private static string SubmitHoldButtonSelector => Html.Selectors.ButtonWithClass(ClientTestReferences.Css.Classes.HoldButton);
	private static string SelectedPlayerOptionClass => ClientTestReferences.Css.Classes.SelectPlayersItemSelected;
	private static string TestInstructionPrompt => GameStrings.WerewolvesChooseVictimPrompt;
	private const int FirstPlayerSeatNumber = 1;
	private const int SecondPlayerSeatNumber = 2;
	private const string FirstPlayerName = PlayerNames.Ana;
	private const string SecondPlayerName = PlayerNames.Bruno;

	[Fact]
	public void SelectingPlayerUpdatesRenderedStateAndEnablesSubmit()
	{
		using var context = new ModeratorComponentTestContext();
		var anaId = Guid.NewGuid();
		var brunoId = Guid.NewGuid();
		var instruction = CreateInstruction(anaId, brunoId);
		var roster = new[]
		{
			CreateRosterEntry(anaId, FirstPlayerSeatNumber, FirstPlayerName),
			CreateRosterEntry(brunoId, SecondPlayerSeatNumber, SecondPlayerName)
		};

		var cut = context.RenderModeratorComponent<SelectPlayersView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster));

		var options = FindPlayerOptions(cut);
		options.Should().HaveCount(2);
		AssertPlayerOption(options[0], FirstPlayerSeatNumber, FirstPlayerName);
		AssertPlayerOption(options[1], SecondPlayerSeatNumber, SecondPlayerName);
		cut.Find(PlayerListSelector)
			.GetAttribute(Html.Attributes.AriaLabel)
			.Should()
			.Be(ClientStrings.SelectPlayers_ListAria);
		FindSubmitHoldButton(cut).HasAttribute(Html.Attributes.Disabled).Should().BeTrue();

		options[1].Click();

		options = FindPlayerOptions(cut);
		options[1].ClassList.Should().Contain(SelectedPlayerOptionClass);
		FindSubmitHoldButton(cut).HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
	}

	private static IReadOnlyList<IElement> FindPlayerOptions(IRenderedComponent<SelectPlayersView> cut) =>
		cut.FindAll(PlayerOptionSelector).ToArray();

	private static IElement FindSubmitHoldButton(IRenderedComponent<SelectPlayersView> cut) =>
		cut.Find(SubmitHoldButtonSelector);

	private static void AssertPlayerOption(IElement option, int seatNumber, string playerName)
	{
		option.TextContent.Should()
			.Contain(seatNumber.ToString(CultureInfo.InvariantCulture))
			.And.Contain(playerName);
	}

	private static SelectPlayersInstruction CreateInstruction(params Guid[] playerIds) =>
		(SelectPlayersInstruction)SelectPlayersConstructor.Invoke(
			[
				playerIds.ToHashSet(),
				NumberRangeConstraint.Single,
				null,
				TestInstructionPrompt,
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
