using System.Collections.Immutable;
using System.Reflection;
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

public class InstructionRendererBunitTests
{
	private static string PublicInstructionSelector => $".{ClientTestReferences.Css.Classes.InstructionAnnouncement}";
	private static string PrivateInstructionSelector => $".{ClientTestReferences.Css.Classes.InstructionPrivate}";
	private static string DashboardActionZoneSelector => $".{ClientTestReferences.Css.Classes.DashboardActionZone}";
	private static string HoldButtonSelector => Html.Selectors.ButtonWithClass(ClientTestReferences.Css.Classes.HoldButton);
	private static string PlayerOptionSelector => Html.Selectors.ElementWithRole(Html.Elements.ListItem, Html.Roles.Option);

	[Fact]
	public void ConfirmationInstruction_WithPublicAndPrivateGuidance_InitiallyExpandsBothGuidanceBlocks()
	{
		using var context = new ModeratorComponentTestContext();
		var publicAnnouncement = $"{GameStrings.NightStartsPrompt}\n{GameStrings.DebateStartsPrompt}";
		var privateInstruction = $"{GameStrings.ConfirmNightStarted}\n{GameStrings.RevealRolePromptSpecify}";
		var instruction = CreateConfirmationInstruction(
			publicAnnouncement: publicAnnouncement,
			privateInstruction: privateInstruction);

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction));

		var publicToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_AnnounceLabel);
		var privateToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_ModeratorLabel);

		publicToggle.GetAttribute(Html.Attributes.AriaExpanded).Should().Be(Html.AriaValues.True);
		privateToggle.GetAttribute(Html.Attributes.AriaExpanded).Should().Be(Html.AriaValues.True);
		publicToggle.TextContent.Should().Contain(GameStrings.NightStartsPrompt);
		publicToggle.TextContent.Should().Contain(GameStrings.DebateStartsPrompt);
		privateToggle.TextContent.Should().Contain(GameStrings.ConfirmNightStarted);
		privateToggle.TextContent.Should().Contain(GameStrings.RevealRolePromptSpecify);
		cut.Markup.Should().NotContain(ClientStrings.Common_TapToExpand);
	}

	[Fact]
	public void ConfirmationInstruction_WithPublicAndPrivateGuidance_CanToggleEachExpandedBlockIndependently()
	{
		using var context = new ModeratorComponentTestContext();
		var instruction = CreateConfirmationInstruction(
			publicAnnouncement: $"{GameStrings.NightStartsPrompt}\n{GameStrings.DebateStartsPrompt}",
			privateInstruction: $"{GameStrings.ConfirmNightStarted}\n{GameStrings.RevealRolePromptSpecify}");

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction));

		var publicToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_AnnounceLabel);
		var privateToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_ModeratorLabel);

		publicToggle.Click();
		publicToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_AnnounceLabel);
		privateToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_ModeratorLabel);

		publicToggle.GetAttribute(Html.Attributes.AriaExpanded).Should().Be(Html.AriaValues.False);
		privateToggle.GetAttribute(Html.Attributes.AriaExpanded).Should().Be(Html.AriaValues.True);
		publicToggle.TextContent.Should().NotContain(GameStrings.DebateStartsPrompt);
		privateToggle.TextContent.Should().Contain(GameStrings.RevealRolePromptSpecify);

		publicToggle.Click();
		privateToggle.Click();
		publicToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_AnnounceLabel);
		privateToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_ModeratorLabel);

		publicToggle.GetAttribute(Html.Attributes.AriaExpanded).Should().Be(Html.AriaValues.True);
		privateToggle.GetAttribute(Html.Attributes.AriaExpanded).Should().Be(Html.AriaValues.False);
		publicToggle.TextContent.Should().Contain(GameStrings.DebateStartsPrompt);
		privateToggle.TextContent.Should().NotContain(GameStrings.RevealRolePromptSpecify);
	}

	[Fact]
	public void ConfirmationInstruction_WithOnlyPublicGuidance_ShowsPublicGuidanceWithoutPrivateBlock()
	{
		using var context = new ModeratorComponentTestContext();
		var instruction = CreateConfirmationInstruction(publicAnnouncement: GameStrings.NightStartsPrompt);

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction));

		cut.Find(PublicInstructionSelector)
			.TextContent.Should()
			.Contain(GameStrings.NightStartsPrompt);
		cut.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		cut.FindAll(Html.Selectors.Button).Should().NotContain(button =>
			button.GetAttribute(Html.Attributes.AriaLabel) == ClientStrings.Dashboard_ModeratorLabel);
	}

	[Fact]
	public void ConfirmationInstruction_WithOnlyPrivateGuidance_ShowsPrivateGuidanceWithoutPublicBlock()
	{
		using var context = new ModeratorComponentTestContext();
		var instruction = CreateConfirmationInstruction(privateInstruction: GameStrings.ConfirmNightStarted);

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction));

		cut.Find(PrivateInstructionSelector)
			.TextContent.Should()
			.Contain(GameStrings.ConfirmNightStarted);
		cut.FindAll(PublicInstructionSelector).Should().BeEmpty();
		cut.FindAll(Html.Selectors.Button).Should().NotContain(button =>
			button.GetAttribute(Html.Attributes.AriaLabel) == ClientStrings.Dashboard_AnnounceLabel);
	}

	[Fact]
	public async Task ConfirmationInstruction_HoldAction_HasAccessibleNameAndEmitsConfirmationResponse()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var instruction = CreateConfirmationInstruction(
			publicAnnouncement: GameStrings.NightActionsCompletePrompt,
			privateInstruction: GameStrings.ConfirmNightStarted);
		ModeratorResponse? receivedResponse = null;

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(
					this,
					response => receivedResponse = response)));

		var action = cut.FindButtonByAccessibleName(ClientStrings.Common_HoldToConfirm);
		action.GetAttribute(Html.Attributes.Type).Should().Be(Html.AttributeValues.ButtonType);
		action.TextContent.Should().Contain(ClientStrings.SelectPlayers_SubmitButton);

		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, action, timing);

		receivedResponse.Should().NotBeNull();
		receivedResponse!.Type.Should().Be(ExpectedInputType.Confirmation);
		receivedResponse.Confirmation.Should().BeTrue();
	}

	[Fact]
	public void AssignRolesInstruction_RendersAssignmentSurfaceWithRosterLabels()
	{
		using var context = new ModeratorComponentTestContext();
		var assignablePlayerId = Guid.NewGuid();
		var otherRosterPlayerId = Guid.NewGuid();
		var instruction = CreateAssignRolesInstruction(
			[assignablePlayerId],
			[MainRoleType.SimpleVillager]);

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster,
				[
					CreateRosterEntry(assignablePlayerId, 1, PlayerNames.Ana),
					CreateRosterEntry(otherRosterPlayerId, 2, PlayerNames.Carla)
				]));

		cut.FindAll("[role='group']")
			.Should()
			.ContainSingle(group =>
				group.GetAttribute(Html.Attributes.AriaLabel) == ClientStrings.AssignRoles_Title &&
				group.TextContent.Contains(PlayerNames.Ana, StringComparison.CurrentCulture));
		cut.Markup.Should().NotContain(PlayerNames.Carla);
		cut.FindAll(Html.Selectors.Button)
			.Should()
			.ContainSingle(button =>
				button.TextContent.Contains(MainRoleType.SimpleVillager.GetPublicName(), StringComparison.CurrentCulture));
	}

	[Fact]
	public void SelectPlayersInstruction_RendersRosterResolvedPlayerChoicesAndSingleInputActionZone()
	{
		using var context = new ModeratorComponentTestContext();
		var selectableId = Guid.NewGuid();
		var nonSelectableId = Guid.NewGuid();
		var instruction = CreateSelectPlayersInstruction(selectableId);
		var roster = new[]
		{
			CreateRosterEntry(nonSelectableId, 1, PlayerNames.Bruno),
			CreateRosterEntry(selectableId, 2, PlayerNames.Ana)
		};

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster));

		var options = cut.FindAll(PlayerOptionSelector);
		options.Should().ContainSingle();
		options.Single().TextContent.Should().Contain(PlayerNames.Ana);
		options.Single().TextContent.Should().NotContain(PlayerNames.Bruno);

		var actionZones = cut.FindAll(DashboardActionZoneSelector);
		actionZones.Should().ContainSingle();
		actionZones.Single().TextContent.Should().Contain(ClientStrings.SelectPlayers_SubmitButton);
		actionZones.Single().QuerySelector(HoldButtonSelector).Should().NotBeNull();
	}

	[Fact]
	public void SelectOptionsInstruction_WithPublicAndPrivateGuidance_KeepsPrivateGuidanceCollapsedInitially()
	{
		using var context = new ModeratorComponentTestContext();
		var options = new[] { "Acordar", "Continuar a dormir", "Alertar a aldeia" };
		var instruction = CreateSelectOptionsInstruction(
			NumberRangeConstraint.Single,
			options,
			publicAnnouncement: GameStrings.NightStartsPrompt,
			privateInstruction: $"{GameStrings.ConfirmNightStarted}\n{GameStrings.RevealRolePromptSpecify}");

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction));

		var publicToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_AnnounceLabel);
		var privateToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_ModeratorLabel);

		publicToggle.GetAttribute(Html.Attributes.AriaExpanded).Should().Be(Html.AriaValues.True);
		privateToggle.GetAttribute(Html.Attributes.AriaExpanded).Should().Be(Html.AriaValues.False);
		publicToggle.TextContent.Should().Contain(GameStrings.NightStartsPrompt);
		privateToggle.TextContent.Should().Contain(GameStrings.ConfirmNightStarted);
		privateToggle.TextContent.Should().NotContain(GameStrings.RevealRolePromptSpecify);

		var actionZones = cut.FindAll(DashboardActionZoneSelector);
		actionZones.Should().ContainSingle();
		actionZones.Single().TextContent.Should().Contain(ClientStrings.Dashboard_ContinueButton);
		actionZones.Single().QuerySelectorAll(HoldButtonSelector).Should().ContainSingle();
	}

	[Fact]
	public void SelectOptionsInstruction_RendersCoreProvidedOptionControlsAndSingleInputActionZone()
	{
		using var context = new ModeratorComponentTestContext();
		var options = new[] { "Acordar", "Continuar a dormir", "Alertar a aldeia" };
		var instruction = CreateSelectOptionsInstruction(NumberRangeConstraint.Single, options);

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction));

		var optionGroup = cut.FindAll("*")
			.Single(element => element.GetAttribute(Html.Attributes.AriaLabel) == ClientStrings.SelectOptions_Title);
		var optionButtons = optionGroup.QuerySelectorAll(Html.Selectors.Button).ToArray();
		optionButtons.Select(button => button.TextContent.Trim())
			.Should()
			.BeEquivalentTo(options);
		optionButtons.Should().OnlyContain(button =>
			button.GetAttribute(Html.Attributes.Type) == Html.AttributeValues.ButtonType);
		optionButtons.Should().OnlyContain(button =>
			button.GetAttribute(Html.Attributes.AriaPressed) == Html.AriaValues.False);

		var actionZones = cut.FindAll(DashboardActionZoneSelector);
		actionZones.Should().ContainSingle();
		actionZones.Single().TextContent.Should().Contain(ClientStrings.Dashboard_ContinueButton);
		actionZones.Single().QuerySelectorAll(HoldButtonSelector).Should().ContainSingle();
	}

	private static ConfirmationInstruction CreateConfirmationInstruction(
		string? publicAnnouncement = null,
		string? privateInstruction = null) =>
		(ConfirmationInstruction)ConfirmationConstructor.Invoke(
			[publicAnnouncement, privateInstruction, null]);

	private static AssignRolesInstruction CreateAssignRolesInstruction(
		IEnumerable<Guid> playerIds,
		IReadOnlyList<MainRoleType> roles) =>
		(AssignRolesInstruction)AssignRolesConstructor.Invoke(
			[
				playerIds.ToImmutableHashSet(),
				roles,
				null,
				GameStrings.RevealRolePromptSpecify,
				null
			]);

	private static SelectPlayersInstruction CreateSelectPlayersInstruction(params Guid[] playerIds) =>
		(SelectPlayersInstruction)SelectPlayersConstructor.Invoke(
			[
				playerIds.ToHashSet(),
				NumberRangeConstraint.Single,
				null,
				GameStrings.WerewolvesChooseVictimPrompt,
				null
			]);

	private static SelectOptionsInstruction CreateSelectOptionsInstruction(
		NumberRangeConstraint selectionRange,
		params string[] options) =>
		CreateSelectOptionsInstruction(
			selectionRange,
			options,
			publicAnnouncement: null,
			privateInstruction: GameStrings.ConfirmNightStarted);

	private static SelectOptionsInstruction CreateSelectOptionsInstruction(
		NumberRangeConstraint selectionRange,
		IEnumerable<string> options,
		string? publicAnnouncement,
		string? privateInstruction) =>
		(SelectOptionsInstruction)SelectOptionsConstructor.Invoke(
			[
				new HashSet<string>(options, StringComparer.CurrentCulture),
				selectionRange,
				publicAnnouncement,
				privateInstruction,
				null
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

	private static readonly ConstructorInfo ConfirmationConstructor =
		typeof(ConfirmationInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 3);

	private static readonly ConstructorInfo AssignRolesConstructor =
		typeof(AssignRolesInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 5);

	private static readonly ConstructorInfo SelectPlayersConstructor =
		typeof(SelectPlayersInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 5);

	private static readonly ConstructorInfo SelectOptionsConstructor =
		typeof(SelectOptionsInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 5);
}

internal static class InstructionRendererBunitTestExtensions
{
	public static AngleSharp.Dom.IElement FindButtonByAccessibleName<TComponent>(
		this Bunit.IRenderedComponent<TComponent> rendered,
		string accessibleName)
		where TComponent : IComponent =>
		rendered.FindAll(Html.Selectors.Button)
			.Single(button => button.GetAttribute(Html.Attributes.AriaLabel) == accessibleName);
}
