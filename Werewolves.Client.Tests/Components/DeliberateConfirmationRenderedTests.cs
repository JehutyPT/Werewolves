using System.Collections.Immutable;
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
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Components;

public class DeliberateConfirmationRenderedTests
{
	private static string HoldButtonSelector =>
		Html.Selectors.ButtonWithClass(ClientTestReferences.Css.Classes.HoldButton);

	[Fact]
	public async Task DisabledInputSubmitStates_CannotSubmitFromRenderedViews()
	{
		await VerifyDisabledSelectPlayersCannotSubmitAsync();
		await VerifyDisabledSelectOptionsCannotSubmitAsync();
		await VerifyDisabledAssignRolesCannotSubmitAsync();
	}

	[Fact]
	public async Task SelectPlayersView_DeliberateHoldEmitsSelectedPlayerResponse()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = CreateContext(timing);
		var responses = new List<ModeratorResponse>();
		var playerId = Guid.NewGuid();
		var instruction = CreateSelectPlayersInstruction(playerId);
		var roster = new[] { CreateRosterEntry(playerId, 1, PlayerNames.Ana) };

		var cut = context.RenderModeratorComponent<SelectPlayersView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		FindElementContainingText(cut, Html.Elements.ListItem, PlayerNames.Ana).Click();
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, FindHoldButton(cut), timing);

		responses.Should().ContainSingle();
		var response = responses.Single();
		response.Type.Should().Be(ExpectedInputType.PlayerSelection);
		response.SelectedPlayerIds.Should().BeEquivalentTo([playerId]);
	}

	[Fact]
	public async Task SelectOptionsView_DeliberateHoldEmitsSelectedOptionResponse()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = CreateContext(timing);
		var responses = new List<ModeratorResponse>();
		var selectedOption = GameStrings.NightStartsPrompt;
		var instruction = CreateSelectOptionsInstruction(
			selectedOption,
			GameStrings.DebateStartsPrompt);

		var cut = context.RenderModeratorComponent<SelectOptionsView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		FindButtonByText(cut, selectedOption).Click();
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, FindHoldButton(cut), timing);

		responses.Should().ContainSingle();
		var response = responses.Single();
		response.Type.Should().Be(ExpectedInputType.OptionSelection);
		response.InstructionId.Should().Be(instruction.InstructionId);
		response.SelectedOptionIds.Should().Equal(["option-0"]);
	}

	[Fact]
	public async Task AssignRolesView_DeliberateHoldEmitsAssignedRolesResponse()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = CreateContext(timing);
		var responses = new List<ModeratorResponse>();
		var playerId = Guid.NewGuid();
		var role = MainRoleType.SimpleVillager;
		var instruction = CreateAssignRolesInstruction(playerId, role);
		var roster = new[] { CreateRosterEntry(playerId, 1, PlayerNames.Ana) };

		var cut = context.RenderModeratorComponent<AssignRolesView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		FindButtonByText(cut, role.GetPublicName()).Click();
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, FindHoldButton(cut), timing);

		responses.Should().ContainSingle();
		var response = responses.Single();
		response.Type.Should().Be(ExpectedInputType.AssignPlayerRoles);
		response.AssignedPlayerRoles.Should().ContainSingle();
		response.AssignedPlayerRoles![playerId].Should().Be(role);
	}

	private async Task VerifyDisabledSelectPlayersCannotSubmitAsync()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = CreateContext(timing);
		var responses = new List<ModeratorResponse>();
		var playerId = Guid.NewGuid();
		var instruction = CreateSelectPlayersInstruction(playerId);
		var roster = new[] { CreateRosterEntry(playerId, 1, PlayerNames.Ana) };

		var cut = context.RenderModeratorComponent<SelectPlayersView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		await AttemptDisabledHoldAsync(cut, FindHoldButton(cut), timing);

		responses.Should().BeEmpty("a player must be selected before the game-altering response is available");
	}

	private async Task VerifyDisabledSelectOptionsCannotSubmitAsync()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = CreateContext(timing);
		var responses = new List<ModeratorResponse>();
		var instruction = CreateSelectOptionsInstruction(
			GameStrings.NightStartsPrompt,
			GameStrings.DebateStartsPrompt);

		var cut = context.RenderModeratorComponent<SelectOptionsView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		await AttemptDisabledHoldAsync(cut, FindHoldButton(cut), timing);

		responses.Should().BeEmpty("an option must be selected before the game-altering response is available");
	}

	private async Task VerifyDisabledAssignRolesCannotSubmitAsync()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = CreateContext(timing);
		var responses = new List<ModeratorResponse>();
		var playerId = Guid.NewGuid();
		var instruction = CreateAssignRolesInstruction(playerId, MainRoleType.SimpleVillager);
		var roster = new[] { CreateRosterEntry(playerId, 1, PlayerNames.Ana) };

		var cut = context.RenderModeratorComponent<AssignRolesView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		await AttemptDisabledHoldAsync(cut, FindHoldButton(cut), timing);

		responses.Should().BeEmpty("all players must be assigned before the game-altering response is available");
	}

	private static async Task AttemptDisabledHoldAsync<TComponent>(
		IRenderedComponent<TComponent> cut,
		IElement holdButton,
		ControlledHoldButtonTiming timing)
		where TComponent : IComponent
	{
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();

		var holdTask = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		timing.AdvanceBy(RenderedHoldButtonDriver.HoldDuration + RenderedHoldButtonDriver.SuccessFlashDuration);
		await holdTask;
		await RenderedHoldButtonDriver.FlushAsync(cut);
	}

	private static ModeratorComponentTestContext CreateContext(ControlledHoldButtonTiming timing)
	{
		var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		return context;
	}

	private static IElement FindHoldButton<TComponent>(IRenderedComponent<TComponent> cut)
		where TComponent : IComponent =>
		cut.Find(HoldButtonSelector);

	private static IElement FindButtonByText<TComponent>(IRenderedComponent<TComponent> cut, string text)
		where TComponent : IComponent =>
		FindElementContainingText(cut, Html.Elements.Button, text);

	private static IElement FindElementContainingText<TComponent>(
		IRenderedComponent<TComponent> cut,
		string elementName,
		string text)
		where TComponent : IComponent =>
		cut.FindAll(elementName)
			.Single(element => element.TextContent.Contains(text, StringComparison.CurrentCulture));

	private static SelectPlayersInstruction CreateSelectPlayersInstruction(params Guid[] playerIds) =>
		(SelectPlayersInstruction)SelectPlayersConstructor.Invoke(
			[
				playerIds.ToHashSet(),
				NumberRangeConstraint.Single,
				null,
				GameStrings.WerewolvesChooseVictimPrompt,
				null,
				Guid.Empty
			]);

	private static SelectOptionsInstruction CreateSelectOptionsInstruction(params string[] options) =>
		(SelectOptionsInstruction)SelectOptionsConstructor.Invoke(
			[
				options
					.Select((label, index) => new ModeratorOption($"option-{index}", label))
					.ToArray(),
				NumberRangeConstraint.Single,
				null,
				GameStrings.ConfirmNightStarted,
				null,
				Guid.Empty
			]);

	private static AssignRolesInstruction CreateAssignRolesInstruction(Guid playerId, MainRoleType role) =>
		(AssignRolesInstruction)AssignRolesConstructor.Invoke(
			[
				ImmutableHashSet.Create(playerId),
				new[] { role },
				null,
				GameStrings.RevealRolePromptSpecify,
				null,
				Guid.Empty
			]);

	private static DashboardRosterEntry CreateRosterEntry(Guid playerId, int seatNumber, string name) =>
		new(
			playerId,
			seatNumber,
			name,
			DashboardRoster.UnknownRoleLabel,
			IsRoleKnown: false,
			DashboardRoster.HealthLabel(PlayerHealth.Alive),
			IsDead: false,
			StatusEffects: [],
			DashboardRoster.NoStatusEffectsLabel);

	private static readonly ConstructorInfo SelectPlayersConstructor =
		typeof(SelectPlayersInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 6);

	private static readonly ConstructorInfo SelectOptionsConstructor =
		typeof(SelectOptionsInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 6);

	private static readonly ConstructorInfo AssignRolesConstructor =
		typeof(AssignRolesInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 6);
}
