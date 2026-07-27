using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Components;

public sealed class RoleKnowledgeFlowBunitTests
{
	private static string HoldButtonSelector =>
		Html.Selectors.ButtonWithClass(ClientTestReferences.Css.Classes.HoldButton);

	[Fact]
	public void VillagerVillagerLobby_UsesCatalogMetadataAsSingleOptionalPortugueseToggle()
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var roleInfo = lobby.GetRoleInfo(MainRoleType.VillagerVillager);

		roleInfo.DisplayName.Should().Be(GameStrings.VillagerVillagerRoleName);
		roleInfo.Affordance.Should().Be(RoleAffordance.Toggle);
		roleInfo.BatchSize.Should().Be(1);

		var cut = context.RenderModeratorComponent<RoleSelectionPage>();
		var toggle = cut.FindAll(Html.Selectors.Button)
			.Single(button => button.GetAttribute(Html.Attributes.AriaLabel) == roleInfo.DisplayName);
		toggle.GetAttribute(Html.Attributes.AriaPressed).Should().Be(Html.AriaValues.False);

		toggle.Click();

		lobby.GetRoleCount(MainRoleType.VillagerVillager).Should().Be(1);
		toggle = cut.FindAll(Html.Selectors.Button)
			.Single(button => button.GetAttribute(Html.Attributes.AriaLabel) == roleInfo.DisplayName);
		toggle.GetAttribute(Html.Attributes.AriaPressed).Should().Be(Html.AriaValues.True);

		toggle.Click();

		lobby.GetRoleCount(MainRoleType.VillagerVillager).Should().Be(0);
	}

	[Fact]
	public void TwoSistersLobby_UsesProductionCatalogAsPortuguesePairToggle()
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var roleInfo = lobby.GetRoleInfo(MainRoleType.TwoSisters);

		roleInfo.DisplayName.Should().Be(GameStrings.TwoSistersRoleName);
		roleInfo.Affordance.Should().Be(RoleAffordance.Toggle);
		roleInfo.BatchSize.Should().Be(2);

		var cut = context.RenderModeratorComponent<RoleSelectionPage>();
		var toggle = cut.FindAll(Html.Selectors.Button)
			.Single(button =>
				button.GetAttribute(Html.Attributes.AriaLabel) == roleInfo.DisplayName);
		toggle.TextContent.Should().Contain($"×{roleInfo.BatchSize}");
		toggle.GetAttribute(Html.Attributes.AriaPressed)
			.Should().Be(Html.AriaValues.False);

		toggle.Click();

		lobby.GetRoleCount(MainRoleType.TwoSisters).Should().Be(2);
		toggle = cut.FindAll(Html.Selectors.Button)
			.Single(button =>
				button.GetAttribute(Html.Attributes.AriaLabel) == roleInfo.DisplayName);
		toggle.GetAttribute(Html.Attributes.AriaPressed)
			.Should().Be(Html.AriaValues.True);

		toggle.Click();

		lobby.GetRoleCount(MainRoleType.TwoSisters).Should().Be(0);
	}

	[Fact]
	public async Task VillagerVillagerPublicFromDeal_UsesCorrelatedPlayerSelectionAndCommitsOnlyAfterCompletedHold()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.VillagerVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var observation = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		observation.Semantic.Should().Be(ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal);
		var holder = manager.CurrentSession!.GetPlayers().ElementAt(2);
		var pendingInstructionId = observation.InstructionId;

		var cut = context.RenderModeratorComponent<DashboardPage>();
		var holderOption = cut.FindAll("[role='option']")
			.Single(option => option.TextContent.Contains(holder.Name, StringComparison.CurrentCulture));
		holderOption.Click();
		var holdButton = cut.Find(HoldButtonSelector);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();

		var earlyHold = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(RenderedHoldButtonDriver.HoldDuration - TimeSpan.FromMilliseconds(1));
		await RenderedHoldButtonDriver.ReleaseHoldAsync(holdButton);
		await earlyHold;

		manager.CurrentInstruction!.InstructionId.Should().Be(pendingInstructionId);
		holder.State.CurrentRole.Should().BeNull();
		holder.State.PhysicalCharacterCardRole.Should().BeNull();
		holder.State.ModeratorKnownRole.Should().BeNull();
		holder.State.PubliclyRevealedRole.Should().BeNull();

		holdButton = cut.Find(HoldButtonSelector);
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);

		manager.CurrentInstruction!.InstructionId.Should().NotBe(pendingInstructionId);
		holder.State.CurrentRole.Should().Be(MainRoleType.VillagerVillager);
		holder.State.PhysicalCharacterCardRole.Should().Be(MainRoleType.VillagerVillager);
		holder.State.ModeratorKnownRole.Should().Be(MainRoleType.VillagerVillager);
		holder.State.PubliclyRevealedRole.Should().Be(MainRoleType.VillagerVillager);
		var rosterEntry = manager.CurrentRoster.Single(entry => entry.PlayerId == holder.Id);
		rosterEntry.RoleVisibility.Should().Be(DashboardRoleVisibility.Public);
		rosterEntry.RoleVisibilityLabel.Should().Be(ClientStrings.Dashboard_RoleKnowledgePublic);
		cut.Markup.Should().Contain(ClientStrings.Dashboard_RoleKnowledgePublic);
		cut.Markup.Should().Contain(MainRoleType.VillagerVillager.GetPublicName());

		var revealedLabel = cut.FindAll("span")
			.Single(element => element.TextContent.Trim() == ClientStrings.Dashboard_RevealedStatLabel);
		revealedLabel.ParentElement!.QuerySelector("strong")!.TextContent.Trim().Should().Be("1");
	}

	[Fact]
	public void PrivateRoleIdentification_IsMarkedModeratorOnlyAndDoesNotIncreaseRevealedCount()
	{
		using var context = new ModeratorComponentTestContext();
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();
		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var holder = manager.CurrentSession!.GetPlayers().First();

		manager.ProcessInput(identification.CreateResponse([holder.Id]))
			.IsSuccess.Should().BeTrue();

		var cut = context.RenderModeratorComponent<DashboardPage>();
		var holderEntry = cut.FindAll("li")
			.Single(entry => entry.TextContent.Contains(holder.Name, StringComparison.CurrentCulture));
		holderEntry.TextContent.Should().Contain(identification.RoleIdentification!.Value.GetPublicName());
		holderEntry.TextContent.Should().Contain(ClientStrings.Dashboard_RoleKnowledgePrivate);
		cut.Markup.Should().Contain(ClientStrings.Dashboard_RoleKnowledgeUnknown);

		var revealedLabel = cut.FindAll("span")
			.Single(element => element.TextContent.Trim() == ClientStrings.Dashboard_RevealedStatLabel);
		revealedLabel.ParentElement!.QuerySelector("strong")!.TextContent.Trim().Should().Be("0");
	}
}
