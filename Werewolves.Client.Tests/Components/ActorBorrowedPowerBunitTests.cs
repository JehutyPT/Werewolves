using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;

namespace Werewolves.Client.Tests.Components;

public sealed class ActorBorrowedPowerBunitTests
{
	[Theory]
	[InlineData(ActorBorrowedPowerFamily.Seer)]
	[InlineData(ActorBorrowedPowerFamily.Cupid)]
	[InlineData(ActorBorrowedPowerFamily.Witch)]
	[InlineData(ActorBorrowedPowerFamily.LittleGirl)]
	[InlineData(ActorBorrowedPowerFamily.Defender)]
	[InlineData(ActorBorrowedPowerFamily.Fox)]
	[InlineData(ActorBorrowedPowerFamily.StutteringJudge)]
	public async Task BorrowedFamily_RendersPortuguesePrivateContextWithoutPublicLineageLeak(
		ActorBorrowedPowerFamily family)
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		CultureInfo.CurrentUICulture.Name.Should().Be(
			ModeratorComponentTestContext.PortugueseCulture.Name);
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var responses = new List<ModeratorResponse>();
		var scenario = ActorBorrowedInstructionFixture.Create(family);
		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters =>
			parameters
				.Add(component => component.Instruction, scenario.Instruction)
				.Add(component => component.Roster, scenario.Roster)
				.Add(
					component => component.OnResponse,
					EventCallback.Factory.Create<ModeratorResponse>(
						this,
						responses.Add)));

		scenario.Instruction.Semantic.Should().Be(ExpectedSemantic(family));
		if (scenario.Instruction is SelectPlayersInstruction playerSelection)
		{
			playerSelection.RoleIdentification.Should().BeNull();
		}
		scenario.Instruction.PublicAnnouncement.Should().Be(
			family == ActorBorrowedPowerFamily.LittleGirl
				? GameStrings.RoleHoldersWakeUp.Format(
					GameStrings.WerewolvesGroupName)
				: null);
		if (family == ActorBorrowedPowerFamily.LittleGirl)
		{
			scenario.Instruction.AffectedPlayerIds.Should().BeNull();
		}
		else
		{
			scenario.Instruction.AffectedPlayerIds.Should().Equal(scenario.ActorId);
		}
		AssertInstructionShape(family, scenario.Instruction);

		ExpandPrivateInstruction(cut);
		var blocks = cut.FindAll(TestId(ModeratorUiTestIds.InstructionBlock));
		var privateBlock = blocks.Single(block => block.ClassList.Contains(
			ClientTestReferences.Css.Classes.InstructionBlockPrivate));
		privateBlock.QuerySelector(
				$".{ClientTestReferences.Css.Classes.InstructionPrivate}")
			.Should().NotBeNull();
		foreach (var fragment in scenario.PrivateFragments)
		{
			privateBlock.TextContent.Should().Contain(fragment);
		}

		var publicBlocks = blocks.Where(block => block.ClassList.Contains(
			ClientTestReferences.Css.Classes.InstructionBlockAnnouncement))
			.ToArray();
		if (scenario.Instruction.PublicAnnouncement is null)
		{
			publicBlocks.Should().BeEmpty();
		}
		else
		{
			publicBlocks.Should().ContainSingle();
			publicBlocks.Single().QuerySelector(
					$".{ClientTestReferences.Css.Classes.InstructionAnnouncement}")
				.Should().NotBeNull();
			publicBlocks.Single().TextContent.Should().Contain(
				scenario.Instruction.PublicAnnouncement);
		}

		var publicText = string.Concat(publicBlocks.Select(block =>
			block.TextContent));
		foreach (var privateFact in new[]
			{
				GameStrings.ActorRoleName,
				MainRoleType.Actor.ToString(),
				scenario.SourceRole.GetPublicName(),
				scenario.SourceRole.ToString()
			}
			.Concat(scenario.PrivateFragments)
			.Concat(scenario.PrivateFacts)
			.Distinct(StringComparer.CurrentCulture))
		{
			publicText.Should().NotContain(privateFact);
		}
		foreach (var lineageId in scenario.SensitiveLineageIds)
		{
			foreach (var format in new[] { "D", "N" })
			{
				cut.Markup.Contains(
					lineageId.ToString(format),
					StringComparison.OrdinalIgnoreCase).Should().BeFalse();
			}
		}

		if (family is not
			(ActorBorrowedPowerFamily.Seer or
			 ActorBorrowedPowerFamily.Cupid or
			 ActorBorrowedPowerFamily.StutteringJudge))
		{
			responses.Should().BeEmpty();
			return;
		}

		SelectResponseValue(cut, scenario);
		var holdButton = cut.Find(TestId(ModeratorUiTestIds.HoldButton));
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);
		await RenderedHoldButtonDriver.ReleaseHoldAsync(holdButton);

		var response = responses.Should().ContainSingle().Subject;
		response.InstructionId.Should().Be(scenario.Instruction.InstructionId);
		response.Type.Should().Be(scenario.Instruction switch
		{
			ConfirmationInstruction => ExpectedInputType.Continue,
			SelectPlayersInstruction => ExpectedInputType.PlayerSelection,
			SelectOptionsInstruction => ExpectedInputType.OptionSelection,
			_ => throw new ArgumentOutOfRangeException()
		});
		if (scenario.SelectedPlayerIds.Count > 0)
		{
			response.SelectedPlayerIds.Should().BeEquivalentTo(
				scenario.SelectedPlayerIds);
		}
		if (scenario.SelectedOptionId is not null)
		{
			response.SelectedOptionIds.Should().Equal(scenario.SelectedOptionId);
		}
	}

	private static void AssertInstructionShape(
		ActorBorrowedPowerFamily family,
		ModeratorInstruction instruction)
	{
		switch (family)
		{
			case ActorBorrowedPowerFamily.Seer or ActorBorrowedPowerFamily.Fox:
				instruction.Should().BeOfType<ConfirmationInstruction>();
				return;
			case ActorBorrowedPowerFamily.Cupid:
				instruction.Should().BeOfType<SelectPlayersInstruction>()
					.Which.CountConstraint.Should().Be(NumberRangeConstraint.Exact(2));
				return;
			case ActorBorrowedPowerFamily.Witch:
				var witch = instruction.Should()
					.BeOfType<SelectPlayersInstruction>().Subject;
				witch.CountConstraint.Should().Be(NumberRangeConstraint.SingleOptional);
				witch.EmptySelectionOptionLabel.Should().Be(GameStrings.DeclineOption);
				return;
			case ActorBorrowedPowerFamily.LittleGirl:
				instruction.Should().BeOfType<SelectPlayersInstruction>()
					.Which.CountConstraint.Should().Be(NumberRangeConstraint.AtLeast(1));
				return;
			case ActorBorrowedPowerFamily.Defender:
				instruction.Should().BeOfType<SelectPlayersInstruction>()
					.Which.CountConstraint.Should().Be(NumberRangeConstraint.Single);
				return;
			case ActorBorrowedPowerFamily.StutteringJudge:
				var options = instruction.Should()
					.BeOfType<SelectOptionsInstruction>().Subject;
				options.SelectionRange.Should().Be(NumberRangeConstraint.Single);
				options.Options.Select(option => (option.Id, option.Label)).Should()
					.Equal(
						(StutteringJudgeSignalOptionIds.Occurred,
							GameStrings.StutteringJudgeSignalOccurredOption),
						(StutteringJudgeSignalOptionIds.DidNotOccur,
							GameStrings.StutteringJudgeSignalDidNotOccurOption));
				return;
			default:
				throw new ArgumentOutOfRangeException(nameof(family));
		}
	}

	private static void ExpandPrivateInstruction(
		IRenderedComponent<InstructionRenderer> cut)
	{
		var toggle = cut.FindButtonByAccessibleName(
			ClientStrings.Dashboard_ModeratorLabel);
		if (toggle.GetAttribute(Html.Attributes.AriaExpanded) ==
			Html.AriaValues.False)
		{
			toggle.Click();
		}
	}

	private static void SelectResponseValue(
		IRenderedComponent<InstructionRenderer> cut,
		ActorBorrowedInstructionScenario scenario)
	{
		foreach (var playerId in scenario.SelectedPlayerIds)
		{
			var playerName = scenario.Roster.Single(player =>
				player.PlayerId == playerId).Name;
			cut.FindAll(Html.Selectors.ElementWithRole(
					Html.Elements.ListItem,
					Html.Roles.Option))
				.Single(option => option.TextContent.Contains(
					playerName,
					StringComparison.CurrentCulture))
				.Click();
		}

		if (scenario.SelectedOptionId is not null)
		{
			var instruction = (SelectOptionsInstruction)scenario.Instruction;
			var label = instruction.Options.Single(option =>
				StringComparer.Ordinal.Equals(
					option.Id,
					scenario.SelectedOptionId)).Label;
			cut.FindAll(TestId(ModeratorUiTestIds.SelectOptionsOption))
				.Single(option => StringComparer.CurrentCulture.Equals(
					option.TextContent.Trim(),
					label))
				.Click();
		}
	}

	private static ModeratorInstructionSemantic ExpectedSemantic(
		ActorBorrowedPowerFamily family) => family switch
	{
		ActorBorrowedPowerFamily.Seer => ModeratorInstructionSemantic.RevealSeerResult,
		ActorBorrowedPowerFamily.Cupid => ModeratorInstructionSemantic.SelectCupidLovers,
		ActorBorrowedPowerFamily.Witch =>
			ModeratorInstructionSemantic.SelectWitchHealingTarget,
		ActorBorrowedPowerFamily.LittleGirl =>
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup,
		ActorBorrowedPowerFamily.Defender =>
			ModeratorInstructionSemantic.SelectDefenderTarget,
		ActorBorrowedPowerFamily.Fox => ModeratorInstructionSemantic.RevealFoxResult,
		ActorBorrowedPowerFamily.StutteringJudge =>
			ModeratorInstructionSemantic.ObserveStutteringJudgeSignal,
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static string TestId(string value) => $"[data-testid='{value}']";
}
