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

public class AssignRolesViewBunitTests
{
	private const string GroupRole = "group";

	[Fact]
	public void InstructionPlayers_RenderWithRosterLabelsAndExcludeOtherRosterPlayers()
	{
		using var context = new ModeratorComponentTestContext();
		var anaId = Guid.NewGuid();
		var brunoId = Guid.NewGuid();
		var carlaId = Guid.NewGuid();
		var instruction = CreateAssignRolesInstruction(
			[anaId, brunoId],
			[MainRoleType.SimpleVillager, MainRoleType.SimpleWerewolf]);
		var roster = new[]
		{
			CreateRosterEntry(anaId, 1, PlayerNames.Ana),
			CreateRosterEntry(brunoId, 2, PlayerNames.Bruno),
			CreateRosterEntry(carlaId, 3, PlayerNames.Carla)
		};

		var cut = RenderAssignRolesView(context, instruction, roster);

		cut.Markup.Should().Contain(PlayerNames.Ana);
		cut.Markup.Should().NotContain(PlayerNames.Bruno);
		cut.Markup.Should().NotContain(PlayerNames.Carla);

		cut.FindButtonByAccessibleName(ClientStrings.AssignRoles_NextPlayerAria).Click();

		cut.Markup.Should().NotContain(PlayerNames.Ana);
		cut.Markup.Should().Contain(PlayerNames.Bruno);
		cut.Markup.Should().NotContain(PlayerNames.Carla);
	}

	[Fact]
	public void InstructionRoles_RenderWithPublicLabelsAndRoleGroupNames()
	{
		using var context = new ModeratorComponentTestContext();
		var playerId = Guid.NewGuid();
		var instruction = CreateAssignRolesInstruction(
			[playerId],
			[
				MainRoleType.SimpleVillager,
				MainRoleType.Seer,
				MainRoleType.SimpleWerewolf
			]);

		var cut = RenderAssignRolesView(
			context,
			instruction,
			[CreateRosterEntry(playerId, 1, PlayerNames.Ana)]);

		FindGroupByAccessibleName(cut, RoleGroup.Villagers.GetDisplayName())
			.TextContent.Should()
			.Contain(MainRoleType.SimpleVillager.GetPublicName())
			.And.Contain(MainRoleType.Seer.GetPublicName());
		FindGroupByAccessibleName(cut, RoleGroup.Werewolves.GetDisplayName())
			.TextContent.Should()
			.Contain(MainRoleType.SimpleWerewolf.GetPublicName());
		cut.Markup.Should().NotContain(MainRoleType.WildChild.GetPublicName());
	}

	[Fact]
	public void AssignmentControls_HaveRenderedAccessibleNamesAndPressedState()
	{
		using var context = new ModeratorComponentTestContext();
		var playerId = Guid.NewGuid();
		var instruction = CreateAssignRolesInstruction(
			[playerId],
			[MainRoleType.SimpleVillager, MainRoleType.SimpleWerewolf]);

		var cut = RenderAssignRolesView(
			context,
			instruction,
			[CreateRosterEntry(playerId, 1, PlayerNames.Ana)]);

		FindGroupByAccessibleName(cut, ClientStrings.AssignRoles_Title)
			.TextContent.Should()
			.Contain(PlayerNames.Ana);
		cut.FindButtonByAccessibleName(ClientStrings.AssignRoles_PreviousPlayerAria)
			.HasAttribute(Html.Attributes.Disabled)
			.Should()
			.BeTrue();
		cut.FindButtonByAccessibleName(ClientStrings.AssignRoles_NextPlayerAria)
			.HasAttribute(Html.Attributes.Disabled)
			.Should()
			.BeTrue();
		FindGroupByAccessibleName(cut, RoleGroup.Villagers.GetDisplayName())
			.TextContent.Should()
			.Contain(MainRoleType.SimpleVillager.GetPublicName());

		var roleButton = FindButtonByText(cut, MainRoleType.SimpleVillager.GetPublicName());
		roleButton.GetAttribute(Html.Attributes.AriaPressed).Should().Be(Html.AriaValues.False);

		roleButton.Click();

		FindButtonByText(cut, MainRoleType.SimpleVillager.GetPublicName())
			.GetAttribute(Html.Attributes.AriaPressed)
			.Should()
			.Be(Html.AriaValues.True);
		cut.FindButtonByAccessibleName(ClientStrings.Common_HoldToConfirm)
			.TextContent.Should()
			.Contain(ClientStrings.Dashboard_ContinueButton);
	}

	[Fact]
	public void RoleSelectionState_IsVisibleAndCanBeChangedOrCleared()
	{
		using var context = new ModeratorComponentTestContext();
		var playerId = Guid.NewGuid();
		var instruction = CreateAssignRolesInstruction(
			[playerId],
			[MainRoleType.SimpleVillager, MainRoleType.SimpleWerewolf]);
		var cut = RenderAssignRolesView(
			context,
			instruction,
			[CreateRosterEntry(playerId, 1, PlayerNames.Ana)]);
		var villagerLabel = MainRoleType.SimpleVillager.GetPublicName();
		var werewolfLabel = MainRoleType.SimpleWerewolf.GetPublicName();

		AssignmentSurfaceText(cut).Should().Contain(ClientStrings.AssignRoles_SelectRolePrompt);

		FindButtonByText(cut, villagerLabel).Click();

		AssignmentSurfaceText(cut).Should().Contain(villagerLabel);
		FindButtonByText(cut, villagerLabel).GetAttribute(Html.Attributes.AriaPressed).Should().Be(Html.AriaValues.True);

		FindButtonByText(cut, werewolfLabel).Click();

		AssignmentSurfaceText(cut).Should().Contain(werewolfLabel);
		FindButtonByText(cut, villagerLabel).GetAttribute(Html.Attributes.AriaPressed).Should().Be(Html.AriaValues.False);
		FindButtonByText(cut, werewolfLabel).GetAttribute(Html.Attributes.AriaPressed).Should().Be(Html.AriaValues.True);

		FindButtonByText(cut, werewolfLabel).Click();

		AssignmentSurfaceText(cut).Should().Contain(ClientStrings.AssignRoles_SelectRolePrompt);
		FindButtonByText(cut, werewolfLabel).GetAttribute(Html.Attributes.AriaPressed).Should().Be(Html.AriaValues.False);
	}

	[Fact]
	public void RoleAssignedToAnotherPlayer_IsUnavailableUnlessInstructionIncludesAnotherCopy()
	{
		var anaId = Guid.NewGuid();
		var brunoId = Guid.NewGuid();
		var roster = new[]
		{
			CreateRosterEntry(anaId, 1, PlayerNames.Ana),
			CreateRosterEntry(brunoId, 2, PlayerNames.Bruno)
		};
		var villagerLabel = MainRoleType.SimpleVillager.GetPublicName();

		using (var context = new ModeratorComponentTestContext())
		{
			var cut = RenderAssignRolesView(
				context,
				CreateAssignRolesInstruction(
					[anaId, brunoId],
					[MainRoleType.SimpleVillager, MainRoleType.SimpleWerewolf]),
				roster);

			FindButtonByText(cut, villagerLabel).Click();
			cut.FindButtonByAccessibleName(ClientStrings.AssignRoles_NextPlayerAria).Click();

			VisibleButtonText(cut).Should().NotContain(villagerLabel);
		}

		using (var context = new ModeratorComponentTestContext())
		{
			var cut = RenderAssignRolesView(
				context,
				CreateAssignRolesInstruction(
					[anaId, brunoId],
					[MainRoleType.SimpleVillager, MainRoleType.SimpleVillager]),
				roster);

			FindButtonByText(cut, villagerLabel).Click();
			cut.FindButtonByAccessibleName(ClientStrings.AssignRoles_NextPlayerAria).Click();

			VisibleButtonText(cut).Should().Contain(villagerLabel);
		}
	}

	[Fact]
	public async Task SubmitHold_RemainsDisabledAndNonSubmittingUntilEveryPlayerIsAssigned()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var anaId = Guid.NewGuid();
		var brunoId = Guid.NewGuid();
		var responses = new List<ModeratorResponse>();
		var cut = RenderAssignRolesView(
			context,
			CreateAssignRolesInstruction(
				[anaId, brunoId],
				[MainRoleType.SimpleVillager, MainRoleType.SimpleWerewolf]),
			[
				CreateRosterEntry(anaId, 1, PlayerNames.Ana),
				CreateRosterEntry(brunoId, 2, PlayerNames.Bruno)
			],
			responses);

		await AttemptDisabledHoldAsync(cut, timing);
		responses.Should().BeEmpty();

		FindButtonByText(cut, MainRoleType.SimpleVillager.GetPublicName()).Click();

		await AttemptDisabledHoldAsync(cut, timing);
		responses.Should().BeEmpty();

		cut.FindButtonByAccessibleName(ClientStrings.AssignRoles_NextPlayerAria).Click();
		FindButtonByText(cut, MainRoleType.SimpleWerewolf.GetPublicName()).Click();

		FindHoldButton(cut).HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
	}

	[Fact]
	public async Task DeliberateHold_EmitsExactlyOneAssignedRolesResponse()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var anaId = Guid.NewGuid();
		var brunoId = Guid.NewGuid();
		var responses = new List<ModeratorResponse>();
		var expectedAssignments = new Dictionary<Guid, MainRoleType>
		{
			[anaId] = MainRoleType.SimpleVillager,
			[brunoId] = MainRoleType.SimpleWerewolf
		};
		var cut = RenderAssignRolesView(
			context,
			CreateAssignRolesInstruction(
				[anaId, brunoId],
				[MainRoleType.SimpleVillager, MainRoleType.SimpleWerewolf]),
			[
				CreateRosterEntry(anaId, 1, PlayerNames.Ana),
				CreateRosterEntry(brunoId, 2, PlayerNames.Bruno)
			],
			responses);

		FindButtonByText(cut, MainRoleType.SimpleVillager.GetPublicName()).Click();
		cut.FindButtonByAccessibleName(ClientStrings.AssignRoles_NextPlayerAria).Click();
		FindButtonByText(cut, MainRoleType.SimpleWerewolf.GetPublicName()).Click();

		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, FindHoldButton(cut), timing);

		responses.Should().ContainSingle();
		var response = responses.Single();
		response.Type.Should().Be(ExpectedInputType.AssignPlayerRoles);
		response.AssignedPlayerRoles.Should().BeEquivalentTo(expectedAssignments);
	}

	private static IRenderedComponent<AssignRolesView> RenderAssignRolesView(
		ModeratorComponentTestContext context,
		AssignRolesInstruction instruction,
		IReadOnlyList<DashboardRosterEntry> roster,
		List<ModeratorResponse>? responses = null) =>
		context.RenderModeratorComponent<AssignRolesView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(
					context,
					response => responses?.Add(response))));

	private static IElement FindGroupByAccessibleName<TComponent>(
		IRenderedComponent<TComponent> cut,
		string accessibleName)
		where TComponent : IComponent =>
		cut.FindAll($"[role='{GroupRole}']")
			.Single(element => element.GetAttribute(Html.Attributes.AriaLabel) == accessibleName);

	private static string AssignmentSurfaceText<TComponent>(IRenderedComponent<TComponent> cut)
		where TComponent : IComponent =>
		FindGroupByAccessibleName(cut, ClientStrings.AssignRoles_Title).TextContent;

	private static IElement FindButtonByText<TComponent>(
		IRenderedComponent<TComponent> cut,
		string text)
		where TComponent : IComponent =>
		cut.FindAll(Html.Selectors.Button)
			.Single(button => button.TextContent.Contains(text, StringComparison.CurrentCulture));

	private static IEnumerable<string> VisibleButtonText<TComponent>(IRenderedComponent<TComponent> cut)
		where TComponent : IComponent =>
		cut.FindAll(Html.Selectors.Button).Select(button => button.TextContent);

	private static IElement FindHoldButton<TComponent>(IRenderedComponent<TComponent> cut)
		where TComponent : IComponent =>
		cut.FindButtonByAccessibleName(ClientStrings.Common_HoldToConfirm);

	private static async Task AttemptDisabledHoldAsync<TComponent>(
		IRenderedComponent<TComponent> cut,
		ControlledHoldButtonTiming timing)
		where TComponent : IComponent
	{
		var holdButton = FindHoldButton(cut);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();

		var holdTask = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		timing.AdvanceBy(RenderedHoldButtonDriver.HoldDuration + RenderedHoldButtonDriver.SuccessFlashDuration);
		await holdTask;
		await RenderedHoldButtonDriver.FlushAsync(cut);
	}

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

	private static readonly ConstructorInfo AssignRolesConstructor =
		typeof(AssignRolesInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 5);
}
