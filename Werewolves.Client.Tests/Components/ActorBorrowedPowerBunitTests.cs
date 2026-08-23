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
	[InlineData(ActorBorrowedPowerFamily.Hunter)]
	[InlineData(ActorBorrowedPowerFamily.Elder)]
	[InlineData(ActorBorrowedPowerFamily.Scapegoat)]
	[InlineData(ActorBorrowedPowerFamily.VillageIdiot)]
	[InlineData(ActorBorrowedPowerFamily.BearTamer)]
	[InlineData(ActorBorrowedPowerFamily.KnightWithRustySword)]
	public async Task BorrowedFamily_RendersPortugueseAudienceContractWithoutLineageLeak(
		ActorBorrowedPowerFamily family)
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		CultureInfo.CurrentUICulture.Name.Should().Be(
			ModeratorComponentTestContext.PortugueseCulture.Name);
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var responses = new List<ModeratorResponse>();
		var scenario = ActorBorrowedInstructionFixture.Create(family);

		scenario.Instruction.Semantic.Should().Be(ExpectedSemantic(family));
		if (scenario.Instruction is SelectPlayersInstruction playerSelection)
		{
			playerSelection.RoleIdentification.Should().BeNull();
		}
		AssertInstructionShape(family, scenario.Instruction);
		var renderedInstructions = scenario.Expectations
			.Select(expectation => RenderAndAssertExpectation(
				context,
				expectation,
				scenario,
				responses))
			.ToArray();
		var cut = renderedInstructions[0];

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

	private IRenderedComponent<InstructionRenderer> RenderAndAssertExpectation(
		ModeratorComponentTestContext context,
		ActorBorrowedInstructionExpectation expectation,
		ActorBorrowedInstructionScenario scenario,
		ICollection<ModeratorResponse> responses)
	{
		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters =>
			parameters
				.Add(component => component.Instruction, expectation.Instruction)
				.Add(component => component.Roster, scenario.Roster)
				.Add(
					component => component.OnResponse,
					EventCallback.Factory.Create<ModeratorResponse>(
						this,
						responses.Add)));

		if (expectation.AffectedPlayerIds is null)
		{
			expectation.Instruction.AffectedPlayerIds.Should().BeNull();
		}
		else
		{
			expectation.Instruction.AffectedPlayerIds.Should().BeEquivalentTo(
				expectation.AffectedPlayerIds);
		}

		if (expectation.PrivateFragments.Count > 0)
		{
			expectation.Instruction.PrivateInstruction.Should().NotBeNull();
			ExpandPrivateInstruction(cut);
		}
		else
		{
			expectation.Instruction.PrivateInstruction.Should().BeNull();
		}

		var blocks = cut.FindAll(TestId(ModeratorUiTestIds.InstructionBlock));
		var privateBlocks = blocks.Where(block => block.ClassList.Contains(
			ClientTestReferences.Css.Classes.InstructionBlockPrivate)).ToArray();
		if (expectation.PrivateFragments.Count == 0)
		{
			privateBlocks.Should().BeEmpty();
		}
		else
		{
			privateBlocks.Should().ContainSingle();
			var privateBlock = privateBlocks.Single();
			privateBlock.QuerySelector(
					$".{ClientTestReferences.Css.Classes.InstructionPrivate}")
				.Should().NotBeNull();
			foreach (var fragment in expectation.PrivateFragments)
			{
				privateBlock.TextContent.Should().Contain(fragment);
			}
		}

		var publicBlocks = blocks.Where(block => block.ClassList.Contains(
			ClientTestReferences.Css.Classes.InstructionBlockAnnouncement))
			.ToArray();
		if (expectation.PublicFragments.Count == 0)
		{
			expectation.Instruction.PublicAnnouncement.Should().BeNull();
			publicBlocks.Should().BeEmpty();
		}
		else
		{
			expectation.Instruction.PublicAnnouncement.Should().NotBeNull();
			publicBlocks.Should().ContainSingle();
			publicBlocks.Single().QuerySelector(
					$".{ClientTestReferences.Css.Classes.InstructionAnnouncement}")
				.Should().NotBeNull();
			foreach (var fragment in expectation.PublicFragments)
			{
				publicBlocks.Single().TextContent.Should().Contain(fragment);
			}
		}

		var publicText = string.Concat(publicBlocks.Select(block =>
			block.TextContent));
		var forbiddenPublicFacts = new[]
			{
				scenario.SourceRole.GetPublicName(),
				scenario.SourceRole.ToString()
			}
			.Concat(expectation.PrivateFragments)
			.Concat(expectation.ConfidentialPublicFragments);
		if (!expectation.AllowsActorIdentityInPublic)
		{
			forbiddenPublicFacts = forbiddenPublicFacts.Concat(
				[GameStrings.ActorRoleName, MainRoleType.Actor.ToString()]);
		}

		foreach (var confidentialFact in forbiddenPublicFacts
			.Distinct(StringComparer.CurrentCulture))
		{
			publicText.Should().NotContain(confidentialFact);
		}
		foreach (var fragment in expectation.ForbiddenMarkupFragments)
		{
			cut.Markup.Should().NotContain(fragment);
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

		return cut;
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
					.Which.CountConstraint.Should().Be(NumberRangeConstraint.Exact(1));
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
			case ActorBorrowedPowerFamily.Hunter:
				var hunter = instruction.Should()
					.BeOfType<SelectPlayersInstruction>().Subject;
				hunter.CountConstraint.Should().Be(NumberRangeConstraint.Single);
				hunter.EmptySelectionOptionLabel.Should().BeNull();
				return;
			case ActorBorrowedPowerFamily.Scapegoat:
				instruction.Should().BeOfType<SelectPlayersInstruction>()
					.Which.CountConstraint.Should().Be(
						NumberRangeConstraint.AtLeast(1));
				return;
			case ActorBorrowedPowerFamily.BearTamer:
				var bearTamer = instruction.Should()
					.BeOfType<ConfirmationInstruction>().Subject;
				bearTamer.SoundEffects.Should().Equal(SoundEffectsEnum.BearGrowl);
				return;
			case ActorBorrowedPowerFamily.Elder or
				ActorBorrowedPowerFamily.VillageIdiot or
				ActorBorrowedPowerFamily.KnightWithRustySword:
				instruction.Should().BeOfType<ConfirmationInstruction>();
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
		ActorBorrowedPowerFamily.Hunter =>
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget,
		ActorBorrowedPowerFamily.Elder =>
			ModeratorInstructionSemantic.AnnounceVillagerRolePowerSuppression,
		ActorBorrowedPowerFamily.Scapegoat =>
			ModeratorInstructionSemantic.SelectScapegoatPermittedVoters,
		ActorBorrowedPowerFamily.VillageIdiot =>
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon,
		ActorBorrowedPowerFamily.BearTamer =>
			ModeratorInstructionSemantic.AnnounceBearTamerGrowl,
		ActorBorrowedPowerFamily.KnightWithRustySword =>
			ModeratorInstructionSemantic.AnnounceDawnVictims,
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static string TestId(string value) => $"[data-testid='{value}']";
}
