using System.Globalization;
using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
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
	public void SelectablePlayersRenderOnlyCoreProvidedChoicesInSeatingOrder()
	{
		using var context = new ModeratorComponentTestContext();
		var anaId = Guid.NewGuid();
		var brunoId = Guid.NewGuid();
		var dianaId = Guid.NewGuid();
		var nonSelectableId = Guid.NewGuid();
		var instruction = CreateInstruction(dianaId, anaId, brunoId);
		var roster = new[]
		{
			CreateRosterEntry(dianaId, 4, PlayerNames.Diana),
			CreateRosterEntry(nonSelectableId, 1, PlayerNames.Eduardo),
			CreateRosterEntry(anaId, 3, PlayerNames.Ana),
			CreateRosterEntry(brunoId, 2, PlayerNames.Bruno)
		};

		var cut = context.RenderModeratorComponent<SelectPlayersView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster));

		var options = FindPlayerOptions(cut);
		options.Should().HaveCount(3);
		AssertPlayerOption(options[0], 2, PlayerNames.Bruno);
		AssertPlayerOption(options[1], 3, PlayerNames.Ana);
		AssertPlayerOption(options[2], 4, PlayerNames.Diana);
		options.Should().NotContain(option =>
			option.TextContent.Contains(PlayerNames.Eduardo, StringComparison.CurrentCulture));
	}

	[Fact]
	public void SelectionStateIsVisibleAccessibleAndChangeable()
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

		cut.Find(PlayerListSelector)
			.GetAttribute(Html.Attributes.Role)
			.Should()
			.Be(Html.Roles.Listbox);
		cut.Find(PlayerListSelector)
			.GetAttribute(Html.Attributes.AriaLabel)
			.Should()
			.Be(ClientStrings.SelectPlayers_ListAria);
		FindSubmitHoldButton(cut)
			.GetAttribute(Html.Attributes.AriaLabel)
			.Should()
			.Be(ClientStrings.Common_HoldToConfirm);

		var options = FindPlayerOptions(cut);
		AssertPlayerOption(options[0], FirstPlayerSeatNumber, FirstPlayerName);
		AssertPlayerOption(options[1], SecondPlayerSeatNumber, SecondPlayerName);
		AssertSelectionState(options[0], isSelected: false);
		AssertSelectionState(options[1], isSelected: false);
		FindSubmitHoldButton(cut).HasAttribute(Html.Attributes.Disabled).Should().BeTrue();

		options[0].Click();

		options = FindPlayerOptions(cut);
		AssertSelectionState(options[0], isSelected: true);
		AssertSelectionState(options[1], isSelected: false);
		FindSubmitHoldButton(cut).HasAttribute(Html.Attributes.Disabled).Should().BeFalse();

		options[1].Click();

		options = FindPlayerOptions(cut);
		AssertSelectionState(options[0], isSelected: false);
		AssertSelectionState(options[1], isSelected: true);
		FindSubmitHoldButton(cut).HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
	}

	[Fact]
	public void ExplicitEmptySelectionOptionRendersWithInitialDisabledHold()
	{
		using var context = new ModeratorComponentTestContext();
		var anaId = Guid.NewGuid();
		var brunoId = Guid.NewGuid();
		var instruction = CreateInstruction(
			NumberRangeConstraint.SingleOptional,
			GameStrings.DayVoteNoEliminationOption,
			anaId,
			brunoId);
		var roster = new[]
		{
			CreateRosterEntry(anaId, FirstPlayerSeatNumber, FirstPlayerName),
			CreateRosterEntry(brunoId, SecondPlayerSeatNumber, SecondPlayerName)
		};

		var cut = context.RenderModeratorComponent<SelectPlayersView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster));

		var options = FindPlayerOptions(cut);
		options.Should().HaveCount(3);
		AssertPlayerOption(options[1], FirstPlayerSeatNumber, FirstPlayerName);
		AssertPlayerOption(options[2], SecondPlayerSeatNumber, SecondPlayerName);

		var emptySelectionOption = FindPlayerOptionByText(cut, GameStrings.DayVoteNoEliminationOption);
		AssertSelectionState(emptySelectionOption, isSelected: false);
		FindSubmitHoldButton(cut).HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
	}

	[Fact]
	public async Task ExplicitEmptySelectionOptionEmitsEmptyPlayerSelectionResponse()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = CreateContext(timing);
		var responses = new List<ModeratorResponse>();
		var anaId = Guid.NewGuid();
		var brunoId = Guid.NewGuid();
		var instruction = CreateInstruction(
			NumberRangeConstraint.SingleOptional,
			GameStrings.DayVoteNoEliminationOption,
			anaId,
			brunoId);
		var roster = new[]
		{
			CreateRosterEntry(anaId, FirstPlayerSeatNumber, FirstPlayerName),
			CreateRosterEntry(brunoId, SecondPlayerSeatNumber, SecondPlayerName)
		};

		var cut = context.RenderModeratorComponent<SelectPlayersView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		FindPlayerOptionByName(cut, FirstPlayerName).Click();
		FindPlayerOptionByText(cut, GameStrings.DayVoteNoEliminationOption).Click();

		var emptySelectionOption = FindPlayerOptionByText(cut, GameStrings.DayVoteNoEliminationOption);
		AssertSelectionState(emptySelectionOption, isSelected: true);
		AssertSelectionState(FindPlayerOptionByName(cut, FirstPlayerName), isSelected: false);
		var holdButton = FindSubmitHoldButton(cut);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);

		responses.Should().ContainSingle();
		var response = responses.Single();
		response.Type.Should().Be(ExpectedInputType.PlayerSelection);
		response.SelectedPlayerIds.Should().BeEmpty();
	}

	[Fact]
	public async Task PlayerSelectionClearsExplicitEmptySelectionAndEmitsSelectedPlayerResponse()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = CreateContext(timing);
		var responses = new List<ModeratorResponse>();
		var anaId = Guid.NewGuid();
		var brunoId = Guid.NewGuid();
		var instruction = CreateInstruction(
			NumberRangeConstraint.SingleOptional,
			GameStrings.DayVoteNoEliminationOption,
			anaId,
			brunoId);
		var roster = new[]
		{
			CreateRosterEntry(anaId, FirstPlayerSeatNumber, FirstPlayerName),
			CreateRosterEntry(brunoId, SecondPlayerSeatNumber, SecondPlayerName)
		};

		var cut = context.RenderModeratorComponent<SelectPlayersView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		FindPlayerOptionByText(cut, GameStrings.DayVoteNoEliminationOption).Click();
		FindPlayerOptionByName(cut, FirstPlayerName).Click();

		AssertSelectionState(FindPlayerOptionByText(cut, GameStrings.DayVoteNoEliminationOption), isSelected: false);
		AssertSelectionState(FindPlayerOptionByName(cut, FirstPlayerName), isSelected: true);
		var holdButton = FindSubmitHoldButton(cut);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);

		responses.Should().ContainSingle();
		var response = responses.Single();
		response.Type.Should().Be(ExpectedInputType.PlayerSelection);
		response.SelectedPlayerIds.Should().BeEquivalentTo([anaId]);
	}

	[Fact]
	public async Task IncompleteSelectionKeepsHoldDisabledAndDoesNotSubmit()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = CreateContext(timing);
		var responses = new List<ModeratorResponse>();
		var anaId = Guid.NewGuid();
		var brunoId = Guid.NewGuid();
		var instruction = CreateInstruction(NumberRangeConstraint.Exact(2), anaId, brunoId);
		var roster = new[]
		{
			CreateRosterEntry(anaId, FirstPlayerSeatNumber, FirstPlayerName),
			CreateRosterEntry(brunoId, SecondPlayerSeatNumber, SecondPlayerName)
		};

		var cut = context.RenderModeratorComponent<SelectPlayersView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		FindPlayerOptionByName(cut, FirstPlayerName).Click();

		var holdButton = FindSubmitHoldButton(cut);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		await AttemptDisabledHoldAsync(cut, holdButton, timing);

		responses.Should().BeEmpty();
	}

	[Fact]
	public async Task DeliberateHoldEmitsExactlyOnePlayerSelectionResponse()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = CreateContext(timing);
		var responses = new List<ModeratorResponse>();
		var anaId = Guid.NewGuid();
		var brunoId = Guid.NewGuid();
		var dianaId = Guid.NewGuid();
		var instruction = CreateInstruction(NumberRangeConstraint.Exact(2), anaId, brunoId, dianaId);
		var roster = new[]
		{
			CreateRosterEntry(anaId, FirstPlayerSeatNumber, FirstPlayerName),
			CreateRosterEntry(brunoId, SecondPlayerSeatNumber, SecondPlayerName),
			CreateRosterEntry(dianaId, 3, PlayerNames.Diana)
		};

		var cut = context.RenderModeratorComponent<SelectPlayersView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		FindPlayerOptionByName(cut, FirstPlayerName).Click();
		FindPlayerOptionByName(cut, PlayerNames.Diana).Click();

		var holdButton = FindSubmitHoldButton(cut);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);

		responses.Should().ContainSingle();
		var response = responses.Single();
		response.Type.Should().Be(ExpectedInputType.PlayerSelection);
		response.SelectedPlayerIds.Should().BeEquivalentTo([anaId, dianaId]);
	}

	private static IReadOnlyList<IElement> FindPlayerOptions(IRenderedComponent<SelectPlayersView> cut) =>
		cut.FindAll(PlayerOptionSelector).ToArray();

	private static IElement FindSubmitHoldButton(IRenderedComponent<SelectPlayersView> cut) =>
		cut.Find(SubmitHoldButtonSelector);

	private static IElement FindPlayerOptionByName(IRenderedComponent<SelectPlayersView> cut, string playerName) =>
		FindPlayerOptions(cut)
			.Single(option => option.TextContent.Contains(playerName, StringComparison.CurrentCulture));

	private static IElement FindPlayerOptionByText(IRenderedComponent<SelectPlayersView> cut, string text) =>
		FindPlayerOptions(cut)
			.Single(option => option.TextContent.Contains(text, StringComparison.CurrentCulture));

	private static void AssertPlayerOption(IElement option, int seatNumber, string playerName)
	{
		option.TextContent.Should()
			.Contain(seatNumber.ToString(CultureInfo.InvariantCulture))
			.And.Contain(playerName);
	}

	private static void AssertSelectionState(IElement option, bool isSelected)
	{
		option.GetAttribute(Html.Attributes.AriaSelected)
			.Should()
			.Be(isSelected ? Html.AriaValues.True : Html.AriaValues.False);

		if (isSelected)
		{
			option.ClassList.Should().Contain(SelectedPlayerOptionClass);
		}
		else
		{
			option.ClassList.Should().NotContain(SelectedPlayerOptionClass);
		}
	}

	private static async Task AttemptDisabledHoldAsync<TComponent>(
		IRenderedComponent<TComponent> cut,
		IElement holdButton,
		ControlledHoldButtonTiming timing)
		where TComponent : IComponent
	{
		var holdTask = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		timing.AdvanceBy(RenderedHoldButtonDriver.HoldDuration + RenderedHoldButtonDriver.SuccessFlashDuration);
		await holdTask;
		await RenderedHoldButtonDriver.FlushAsync(cut);
	}

	private static SelectPlayersInstruction CreateInstruction(params Guid[] playerIds) =>
		CreateInstruction(NumberRangeConstraint.Single, playerIds);

	private static SelectPlayersInstruction CreateInstruction(NumberRangeConstraint countConstraint, params Guid[] playerIds) =>
		(SelectPlayersInstruction)SelectPlayersConstructor.Invoke(
			[
				playerIds.ToHashSet(),
				countConstraint,
				null,
				TestInstructionPrompt,
				null
			]);

	private static SelectPlayersInstruction CreateInstruction(
		NumberRangeConstraint countConstraint,
		string? emptySelectionOptionLabel,
		params Guid[] playerIds)
	{
		var instruction = CreateInstruction(countConstraint, playerIds);

		return instruction with { EmptySelectionOptionLabel = emptySelectionOptionLabel };
	}

	private static ModeratorComponentTestContext CreateContext(ControlledHoldButtonTiming timing)
	{
		var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		return context;
	}

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
