using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class InstructionRendererBunitTests
{
	private static string PublicInstructionSelector => $".{ClientTestReferences.Css.Classes.InstructionAnnouncement}";
	private static string PrivateInstructionSelector => $".{ClientTestReferences.Css.Classes.InstructionPrivate}";

	[Fact]
	public void ConfirmationInstruction_WithPublicAndPrivateGuidance_ShowsBothGuidanceBlocks()
	{
		using var context = new ModeratorComponentTestContext();
		var instruction = CreateConfirmationInstruction(
			publicAnnouncement: GameStrings.NightStartsPrompt,
			privateInstruction: GameStrings.ConfirmNightStarted);

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction));

		var publicToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_AnnounceLabel);
		var privateToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_ModeratorLabel);

		publicToggle.TextContent.Should().Contain(GameStrings.NightStartsPrompt);
		privateToggle.TextContent.Should().Contain(GameStrings.ConfirmNightStarted);
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
		cut.FindAll("button").Should().NotContain(button =>
			button.GetAttribute("aria-label") == ClientStrings.Dashboard_ModeratorLabel);
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
		cut.FindAll("button").Should().NotContain(button =>
			button.GetAttribute("aria-label") == ClientStrings.Dashboard_AnnounceLabel);
	}

	[Fact]
	public async Task ConfirmationInstruction_HoldAction_HasAccessibleNameAndEmitsConfirmationResponse()
	{
		using var context = new ModeratorComponentTestContext();
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
		action.GetAttribute("type").Should().Be("button");
		action.TextContent.Should().Contain(ClientStrings.SelectPlayers_SubmitButton);

		await action.TriggerEventAsync("onpointerdown", new PointerEventArgs());

		receivedResponse.Should().NotBeNull();
		receivedResponse!.Type.Should().Be(ExpectedInputType.Confirmation);
		receivedResponse.Confirmation.Should().BeTrue();
	}

	private static ConfirmationInstruction CreateConfirmationInstruction(
		string? publicAnnouncement = null,
		string? privateInstruction = null) =>
		(ConfirmationInstruction)ConfirmationConstructor.Invoke(
			[publicAnnouncement, privateInstruction, null]);

	private static readonly ConstructorInfo ConfirmationConstructor =
		typeof(ConfirmationInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 3);
}

internal static class InstructionRendererBunitTestExtensions
{
	public static AngleSharp.Dom.IElement FindButtonByAccessibleName<TComponent>(
		this Bunit.IRenderedComponent<TComponent> rendered,
		string accessibleName)
		where TComponent : IComponent =>
		rendered.FindAll("button")
			.Single(button => button.GetAttribute("aria-label") == accessibleName);
}
