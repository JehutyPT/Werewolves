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
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Components;

public sealed class ActorBorrowedPowerBunitTests
{
	private static readonly Guid ActorId =
		Guid.Parse("72000000-0000-0000-0000-000000000001");
	private static readonly Guid FirstTargetId =
		Guid.Parse("72000000-0000-0000-0000-000000000002");
	private static readonly Guid SecondTargetId =
		Guid.Parse("72000000-0000-0000-0000-000000000003");
	private static readonly Guid ThirdTargetId =
		Guid.Parse("72000000-0000-0000-0000-000000000004");
	private static readonly Guid InstructionId =
		Guid.Parse("72000000-0000-0000-0000-000000000005");
	private static readonly IReadOnlyList<Guid> SensitiveLineageIds =
	[
		Guid.Parse("72000000-0000-0000-0000-000000000101"),
		Guid.Parse("72000000-0000-0000-0000-000000000102"),
		Guid.Parse("72000000-0000-0000-0000-000000000103")
	];

	[Theory]
	[InlineData(BorrowedFamily.Seer)]
	[InlineData(BorrowedFamily.Cupid)]
	[InlineData(BorrowedFamily.Witch)]
	[InlineData(BorrowedFamily.LittleGirl)]
	[InlineData(BorrowedFamily.Defender)]
	[InlineData(BorrowedFamily.Fox)]
	[InlineData(BorrowedFamily.StutteringJudge)]
	public async Task BorrowedFamily_RendersPortuguesePrivateContextWithoutPublicLineageLeak(
		BorrowedFamily family)
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var responses = new List<ModeratorResponse>();
		var scenario = CreateScenario(family);
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
		scenario.Instruction.PublicAnnouncement.Should().Be(
			family == BorrowedFamily.LittleGirl
				? GameStrings.RoleHoldersWakeUp.Format(
					GameStrings.WerewolvesGroupName)
				: null);
		if (family == BorrowedFamily.LittleGirl)
		{
			scenario.Instruction.AffectedPlayerIds.Should().BeNull();
		}
		else
		{
			scenario.Instruction.AffectedPlayerIds.Should().Equal(ActorId);
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
				scenario.SourceRole.GetPublicName()
			}
			.Concat(scenario.PrivateFragments)
			.Concat(scenario.PrivateFacts)
			.Distinct(StringComparer.CurrentCulture))
		{
			publicText.Should().NotContain(privateFact);
		}
		foreach (var lineageId in SensitiveLineageIds)
		{
			cut.Markup.Contains(
				lineageId.ToString("D"),
				StringComparison.OrdinalIgnoreCase).Should().BeFalse();
		}

		if (family is not
			(BorrowedFamily.Seer or
			 BorrowedFamily.Cupid or
			 BorrowedFamily.StutteringJudge))
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
		response.Type.Should().Be(scenario.ResponseShape switch
		{
			ResponseShape.Confirmation => ExpectedInputType.Continue,
			ResponseShape.Players => ExpectedInputType.PlayerSelection,
			ResponseShape.Options => ExpectedInputType.OptionSelection,
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

	private static Scenario CreateScenario(BorrowedFamily family)
	{
		var roster = CreateRoster();
		return family switch
		{
			BorrowedFamily.Seer => new(
				MainRoleType.Seer,
				new ConfirmationInstruction(
					ModeratorInstructionSemantic.RevealSeerResult,
					privateInstruction: GameStrings.SeerResultWerewolfTeam.Format(
						PlayerNames.Catarina),
					affectedPlayerIds: [ActorId],
					instructionId: InstructionId),
				roster,
				[GameStrings.SeerResultWerewolfTeam.Format(PlayerNames.Catarina)],
				[PlayerNames.Catarina],
				ResponseShape.Confirmation,
				[],
				null),
			BorrowedFamily.Cupid => new(
				MainRoleType.Cupid,
				new SelectPlayersInstruction(
					ModeratorInstructionSemantic.SelectCupidLovers,
					[FirstTargetId, SecondTargetId, ThirdTargetId],
					NumberRangeConstraint.Exact(2),
					privateInstruction: GameStrings.CupidTargetSelectionInstruction,
					affectedPlayerIds: [ActorId],
					instructionId: InstructionId),
				roster,
				[GameStrings.CupidTargetSelectionInstruction],
				[PlayerNames.Catarina, PlayerNames.Eduardo],
				ResponseShape.Players,
				[FirstTargetId, SecondTargetId],
				null),
			BorrowedFamily.Witch => new(
				MainRoleType.Witch,
				new SelectPlayersInstruction(
					ModeratorInstructionSemantic.SelectWitchHealingTarget,
					[FirstTargetId],
					NumberRangeConstraint.SingleOptional,
					privateInstruction:
						GameStrings.WitchHealingSelectionInstruction.Format(
							PlayerNames.Catarina),
					affectedPlayerIds: [ActorId],
					instructionId: InstructionId)
				{
					EmptySelectionOptionLabel = GameStrings.DeclineOption
				},
				roster,
				[GameStrings.WitchHealingSelectionInstruction.Format(
					PlayerNames.Catarina)],
				[PlayerNames.Catarina],
				ResponseShape.Players,
				[],
				null),
			BorrowedFamily.LittleGirl => new(
				MainRoleType.LittleGirl,
				new SelectPlayersInstruction(
					ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup,
					[ActorId, FirstTargetId, SecondTargetId, ThirdTargetId],
					NumberRangeConstraint.AtLeast(1),
					publicAnnouncement: GameStrings.RoleHoldersWakeUp.Format(
						GameStrings.WerewolvesGroupName),
					privateInstruction: LittleGirlPrivateInstruction,
					instructionId: InstructionId),
				roster,
				[
					GameStrings.WerewolfFactionAgentObservationPrompt,
					GameStrings.LittleGirlOpeningGuidance
				],
				[PlayerNames.Catarina],
				ResponseShape.Players,
				[],
				null),
			BorrowedFamily.Defender => new(
				MainRoleType.Defender,
				new SelectPlayersInstruction(
					ModeratorInstructionSemantic.SelectDefenderTarget,
					[ActorId, FirstTargetId, SecondTargetId],
					NumberRangeConstraint.Single,
					privateInstruction:
						GameStrings.DefenderTargetSelectionInstruction,
					affectedPlayerIds: [ActorId],
					instructionId: InstructionId),
				roster,
				[GameStrings.DefenderTargetSelectionInstruction],
				[PlayerNames.Catarina],
				ResponseShape.Players,
				[],
				null),
			BorrowedFamily.Fox => new(
				MainRoleType.Fox,
				new ConfirmationInstruction(
					ModeratorInstructionSemantic.RevealFoxResult,
					privateInstruction:
						GameStrings.FoxAffirmativeFeedbackInstruction,
					affectedPlayerIds: [ActorId],
					instructionId: InstructionId),
				roster,
				[GameStrings.FoxAffirmativeFeedbackInstruction],
				[PlayerNames.Catarina],
				ResponseShape.Confirmation,
				[],
				null),
			BorrowedFamily.StutteringJudge => new(
				MainRoleType.StutteringJudge,
				new SelectOptionsInstruction(
					ModeratorInstructionSemantic.ObserveStutteringJudgeSignal,
					[
						new ModeratorOption(
							StutteringJudgeSignalOptionIds.Occurred,
							GameStrings.StutteringJudgeSignalOccurredOption),
						new ModeratorOption(
							StutteringJudgeSignalOptionIds.DidNotOccur,
							GameStrings.StutteringJudgeSignalDidNotOccurOption)
					],
					NumberRangeConstraint.Single,
					privateInstruction:
						GameStrings.StutteringJudgeSignalObservationInstruction,
					affectedPlayerIds: [ActorId],
					instructionId: InstructionId),
				roster,
				[GameStrings.StutteringJudgeSignalObservationInstruction],
				[
					GameStrings.StutteringJudgeSignalOccurredOption,
					GameStrings.StutteringJudgeSignalDidNotOccurOption
				],
				ResponseShape.Options,
				[],
				StutteringJudgeSignalOptionIds.Occurred),
			_ => throw new ArgumentOutOfRangeException(nameof(family))
		};
	}

	private static void AssertInstructionShape(
		BorrowedFamily family,
		ModeratorInstruction instruction)
	{
		switch (family)
		{
			case BorrowedFamily.Seer or BorrowedFamily.Fox:
				instruction.Should().BeOfType<ConfirmationInstruction>();
				return;
			case BorrowedFamily.Cupid:
				instruction.Should().BeOfType<SelectPlayersInstruction>()
					.Which.CountConstraint.Should().Be(NumberRangeConstraint.Exact(2));
				return;
			case BorrowedFamily.Witch:
				var witch = instruction.Should()
					.BeOfType<SelectPlayersInstruction>().Subject;
				witch.CountConstraint.Should().Be(NumberRangeConstraint.SingleOptional);
				witch.EmptySelectionOptionLabel.Should().Be(GameStrings.DeclineOption);
				return;
			case BorrowedFamily.LittleGirl:
				instruction.Should().BeOfType<SelectPlayersInstruction>()
					.Which.CountConstraint.Should().Be(NumberRangeConstraint.AtLeast(1));
				return;
			case BorrowedFamily.Defender:
				instruction.Should().BeOfType<SelectPlayersInstruction>()
					.Which.CountConstraint.Should().Be(NumberRangeConstraint.Single);
				return;
			case BorrowedFamily.StutteringJudge:
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
		Scenario scenario)
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
		BorrowedFamily family) => family switch
	{
		BorrowedFamily.Seer => ModeratorInstructionSemantic.RevealSeerResult,
		BorrowedFamily.Cupid => ModeratorInstructionSemantic.SelectCupidLovers,
		BorrowedFamily.Witch =>
			ModeratorInstructionSemantic.SelectWitchHealingTarget,
		BorrowedFamily.LittleGirl =>
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup,
		BorrowedFamily.Defender =>
			ModeratorInstructionSemantic.SelectDefenderTarget,
		BorrowedFamily.Fox => ModeratorInstructionSemantic.RevealFoxResult,
		BorrowedFamily.StutteringJudge =>
			ModeratorInstructionSemantic.ObserveStutteringJudgeSignal,
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static string LittleGirlPrivateInstruction => string.Join(
		Environment.NewLine + Environment.NewLine,
		GameStrings.WerewolfFactionAgentObservationPrompt,
		GameStrings.LittleGirlOpeningGuidance);

	private static IReadOnlyList<DashboardRosterEntry> CreateRoster() =>
	[
		CreateRosterEntry(ActorId, 1, PlayerNames.Ana),
		CreateRosterEntry(FirstTargetId, 2, PlayerNames.Catarina),
		CreateRosterEntry(SecondTargetId, 3, PlayerNames.Eduardo),
		CreateRosterEntry(ThirdTargetId, 4, PlayerNames.Filipe)
	];

	private static DashboardRosterEntry CreateRosterEntry(
		Guid playerId,
		int seatNumber,
		string name) => new(
		playerId,
		seatNumber,
		name,
		DashboardRoster.UnknownRoleLabel,
		IsRoleKnown: false,
		DashboardRoster.HealthLabel(PlayerHealth.Alive),
		IsDead: false,
		StatusEffects: [],
		DashboardRoster.NoStatusEffectsLabel);

	private static string TestId(string value) => $"[data-testid='{value}']";

	public enum BorrowedFamily
	{
		Seer,
		Cupid,
		Witch,
		LittleGirl,
		Defender,
		Fox,
		StutteringJudge
	}

	private enum ResponseShape
	{
		Confirmation,
		Players,
		Options
	}

	private sealed record Scenario(
		MainRoleType SourceRole,
		ModeratorInstruction Instruction,
		IReadOnlyList<DashboardRosterEntry> Roster,
		IReadOnlyList<string> PrivateFragments,
		IReadOnlyList<string> PrivateFacts,
		ResponseShape ResponseShape,
		IReadOnlyList<Guid> SelectedPlayerIds,
		string? SelectedOptionId);
}
