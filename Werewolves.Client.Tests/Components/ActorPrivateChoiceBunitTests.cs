using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public sealed class ActorPrivateChoiceBunitTests
{
	private static readonly Guid ActorId = Guid.Parse("42000000-0000-0000-0000-000000000001");
	private static readonly Guid SeerCardId = Guid.Parse("42000000-0000-0000-0000-000000000002");
	private static readonly Guid WitchCardId = Guid.Parse("42000000-0000-0000-0000-000000000003");

	[Fact]
	public async Task PrivateActorChoice_CompletedHoldSubmitsOneCorrelatedSkipWithoutPublicSourceCopy()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var responses = new List<ModeratorResponse>();
		var instruction = CreateActorChoice();

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		instruction.PublicAnnouncement.Should().BeNull();
		instruction.PrivateInstruction.Should().Be(GameStrings.ActorSetupCardSelectionInstruction);
		instruction.AffectedPlayerIds.Should().Equal([ActorId]);
		var instructionBlocks = cut.FindAll(TestId(ModeratorUiTestIds.InstructionBlock));
		instructionBlocks.Should().ContainSingle();
		instructionBlocks.Single().ClassList.Should().Contain(
			ClientTestReferences.Css.Classes.InstructionBlockPrivate);
		instructionBlocks.Single().TextContent.Should().Contain(
			GameStrings.ActorSetupCardSelectionInstruction);
		cut.Find(TestId(ModeratorUiTestIds.SelectOptionsList))
			.GetAttribute("aria-label").Should().Be(ClientStrings.SelectOptions_Title);
		var optionButtons = cut.FindAll(TestId(ModeratorUiTestIds.SelectOptionsOption));
		optionButtons.Select(button => button.TextContent.Trim()).Should().Equal(
			MainRoleType.Seer.GetPublicName(),
			MainRoleType.Witch.GetPublicName());
		cut.Markup.Should().NotContain(SeerCardId.ToString("D"));
		cut.Markup.Should().NotContain(WitchCardId.ToString("D"));

		var holdButton = cut.Find(TestId(ModeratorUiTestIds.HoldButton));
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);
		await RenderedHoldButtonDriver.ReleaseHoldAsync(holdButton);

		responses.Should().ContainSingle();
		responses.Single().InstructionId.Should().Be(instruction.InstructionId);
		responses.Single().SelectedOptionIds.Should().BeEmpty();
	}

	[Fact]
	public async Task PrivateActorChoice_SelectedCardShortAndCanceledHoldsDoNothingThenCompletedHoldSubmitsOnce()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var responses = new List<ModeratorResponse>();
		var instruction = CreateActorChoice();
		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));
		cut.FindAll(TestId(ModeratorUiTestIds.SelectOptionsOption))[1].Click();

		var holdButton = cut.Find(TestId(ModeratorUiTestIds.HoldButton));
		var shortHold = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(RenderedHoldButtonDriver.HoldDuration - TimeSpan.FromMilliseconds(1));
		await RenderedHoldButtonDriver.ReleaseHoldAsync(holdButton);
		await shortHold;
		responses.Should().BeEmpty();

		holdButton = cut.Find(TestId(ModeratorUiTestIds.HoldButton));
		var canceledHold = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(TimeSpan.FromMilliseconds(200));
		await RenderedHoldButtonDriver.LeaveHoldAsync(holdButton);
		await canceledHold;
		timing.AdvanceBy(
			RenderedHoldButtonDriver.HoldDuration +
			RenderedHoldButtonDriver.SuccessFlashDuration);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		responses.Should().BeEmpty();

		holdButton = cut.Find(TestId(ModeratorUiTestIds.HoldButton));
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);
		await RenderedHoldButtonDriver.ReleaseHoldAsync(holdButton);

		responses.Should().ContainSingle();
		responses.Single().InstructionId.Should().Be(instruction.InstructionId);
		responses.Single().SelectedOptionIds.Should().Equal(WitchCardId.ToString("D"));
	}

	private static SelectOptionsInstruction CreateActorChoice() =>
		(SelectOptionsInstruction)SelectOptionsConstructor.Invoke(
		[
			ModeratorInstructionSemantic.ChooseActorSetupCard,
			new[]
			{
				new ModeratorOption(SeerCardId.ToString("D"), MainRoleType.Seer.GetPublicName()),
				new ModeratorOption(WitchCardId.ToString("D"), MainRoleType.Witch.GetPublicName())
			},
			NumberRangeConstraint.SingleOptional,
			null,
			GameStrings.ActorSetupCardSelectionInstruction,
			new[] { ActorId },
			Guid.Parse("42000000-0000-0000-0000-000000000004")
		]);

	private static readonly ConstructorInfo SelectOptionsConstructor =
		typeof(SelectOptionsInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(constructor => constructor.GetParameters().Length == 7);

	private static string TestId(string value) => $"[data-testid='{value}']";
}
