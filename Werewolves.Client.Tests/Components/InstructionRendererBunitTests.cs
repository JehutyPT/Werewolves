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
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
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
	public void CurrentInstructionFamilies_RenderTheirEstablishedSingleResponseSurface()
	{
		var selectablePlayerId = Guid.NewGuid();
		var roster = new[]
		{
			CreateRosterEntry(selectablePlayerId, 1, PlayerNames.Ana)
		};
		var cases = new[]
		{
			new InstructionFamilyCase(
				CreateConfirmationInstruction(publicAnnouncement: GameStrings.NightStartsPrompt),
				ClientStrings.Dashboard_ContinueButton,
				IsResponseEnabled: true),
			new InstructionFamilyCase(
				CreateSelectPlayersInstruction(selectablePlayerId),
				ClientStrings.SelectPlayers_SubmitButton,
				IsResponseEnabled: false),
			new InstructionFamilyCase(
				CreateSelectOptionsInstruction(
					NumberRangeConstraint.Single,
					GameStrings.ConfirmNightStarted),
				ClientStrings.Dashboard_ContinueButton,
				IsResponseEnabled: false),
			new InstructionFamilyCase(
				CreateAssignRolesInstruction(
					[selectablePlayerId],
					[MainRoleType.SimpleVillager]),
				ClientStrings.Dashboard_ContinueButton,
				IsResponseEnabled: false),
			new InstructionFamilyCase(
				CreateDevotedServantVoteWindowInstruction(selectablePlayerId),
				ClientStrings.Dashboard_ContinueButton,
				IsResponseEnabled: true),
			new InstructionFamilyCase(
				new FinishedGameConfirmationInstruction(
					new SingleFactionGameResult(Faction.Villager),
					VictoryCheckWindow.Dawn),
				ExpectedActionLabel: null,
				IsResponseEnabled: false)
		};

		foreach (var testCase in cases)
		{
			using var context = new ModeratorComponentTestContext();
			var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
				.Add(component => component.Instruction, testCase.Instruction)
				.Add(component => component.Roster, roster));

			cut.FindAll(DashboardActionZoneSelector).Should().ContainSingle();
			var responseControls = cut.FindAll(HoldButtonSelector);
			if (testCase.ExpectedActionLabel is null)
			{
				responseControls.Should().BeEmpty();
				cut.FindAll(Html.Selectors.Button).Should().ContainSingle(button =>
					button.HasAttribute(Html.Attributes.Disabled) &&
					button.TextContent.Trim() == ClientStrings.Dashboard_RespondButton);
				continue;
			}

			responseControls.Should().ContainSingle();
			responseControls.Single().TextContent.Should().Contain(testCase.ExpectedActionLabel);
			responseControls.Single().HasAttribute(Html.Attributes.Disabled).Should()
				.Be(!testCase.IsResponseEnabled);
		}
	}

	[Theory]
	[InlineData(MainRoleType.TwoSisters)]
	[InlineData(MainRoleType.ThreeBrothers)]
	public void RoleHolderConfirmations_RenderThroughGenericPublicContinueWithoutTimer(
		MainRoleType role)
	{
		var roleName = role.GetPublicName();
		var recognitionAnnouncement =
			GameStrings.RoleHoldersRecognitionPrompt.Format(roleName);
		var announcements = new[]
		{
			recognitionAnnouncement,
			GameStrings.RoleHoldersCommunicationPrompt.Format(roleName),
			GameStrings.RoleHoldersGoToSleep.Format(roleName)
		};

		foreach (var announcement in announcements)
		{
			using var context = new ModeratorComponentTestContext();
			var instruction = CreateConfirmationInstruction(
				publicAnnouncement: announcement);

			var cut = context.RenderModeratorComponent<InstructionRenderer>(
				parameters => parameters.Add(
					component => component.Instruction,
					instruction));

			cut.Find(PublicInstructionSelector).TextContent.Should()
				.Contain(announcement);
			cut.FindAll(PrivateInstructionSelector).Should().BeEmpty();
			cut.Markup.Should().NotContain(
				ClientStrings.Dashboard_DebateTimerLabel);
			cut.FindAll(HoldButtonSelector).Should().ContainSingle();
		}
	}

	[Fact]
	public void VillageIdiotPardonConsequence_RendersThroughGenericPortugueseConfirmation()
	{
		using var context = new ModeratorComponentTestContext();
		var announcement = GameStrings.VillageIdiotPardonAnnouncement.Format(
			PlayerNames.Ana);
		var instruction = CreateConfirmationInstruction(
			publicAnnouncement: announcement);

		var cut = context.RenderModeratorComponent<InstructionRenderer>(
			parameters => parameters.Add(
				component => component.Instruction,
				instruction));

		cut.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(announcement);
		cut.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		var actionZones = cut.FindAll(DashboardActionZoneSelector);
		actionZones.Should().ContainSingle();
		actionZones.Single().TextContent.Should()
			.Contain(ClientStrings.Dashboard_ContinueButton);
		actionZones.Single().QuerySelectorAll(HoldButtonSelector)
			.Should().ContainSingle();
	}

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
		var haptic = new RecordingHapticFeedbackService();
		context.Services.AddSingleton<IHapticFeedbackService>(haptic);
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
		haptic.ClickCount.Should().Be(3);
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
	public async Task ConfirmationInstruction_ContinueAction_IsLocalizedOneWayHoldAndEmitsResponse()
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

		var action = cut.FindAll(Html.Selectors.Button)
			.Single(button => button.TextContent.Trim() == ClientStrings.Dashboard_ContinueButton);
		action.GetAttribute(Html.Attributes.Type).Should().Be(Html.AttributeValues.ButtonType);
		cut.FindAll(HoldButtonSelector).Should().ContainSingle();

		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, action, timing);

		receivedResponse.Should().NotBeNull();
		receivedResponse!.Type.Should().Be(ExpectedInputType.Continue);
		receivedResponse.InstructionId.Should().Be(instruction.InstructionId);
	}

	[Fact]
	public async Task BearTamerGrowl_RendersPrivatePortugueseGenericContinueWithoutPublicOrPlayerLeakage()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var instruction = CreateConfirmationInstruction(
			privateInstruction: GameStrings.BearTamerGrowlInstruction,
			soundEffects: [SoundEffectsEnum.BearGrowl]);
		ModeratorResponse? receivedResponse = null;

		var cut = context.RenderModeratorComponent<InstructionRenderer>(
			parameters => parameters
				.Add(component => component.Instruction, instruction)
				.Add(component => component.Roster,
					[
						CreateRosterEntry(Guid.NewGuid(), 1, PlayerNames.Ana),
						CreateRosterEntry(Guid.NewGuid(), 2, PlayerNames.Carla)
					])
				.Add(component => component.OnResponse,
					EventCallback.Factory.Create<ModeratorResponse>(
						this,
						response => receivedResponse = response)));

		cut.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.BearTamerGrowlInstruction);
		cut.FindAll(PublicInstructionSelector).Should().BeEmpty();
		cut.Markup.Should().NotContain(PlayerNames.Ana);
		cut.Markup.Should().NotContain(PlayerNames.Carla);
		var action = cut.FindAll(Html.Selectors.Button)
			.Single(button =>
				button.TextContent.Trim() ==
				ClientStrings.Dashboard_ContinueButton);

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			action,
			timing);

		receivedResponse.Should().NotBeNull();
		receivedResponse!.Type.Should().Be(ExpectedInputType.Continue);
		receivedResponse.InstructionId.Should().Be(instruction.InstructionId);
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
	public void RustySwordDawnCause_RendersAsDedicatedPortugueseConfirmation()
	{
		using var context = new ModeratorComponentTestContext();
		var victimId = Guid.NewGuid();
		var announcement = GameStrings.MultipleVictimEliminatedAnnounce.Format(
			GameStrings.RustySwordDiseaseEliminationAnnouncement.Format(
				PlayerNames.Ana));
		var instruction = CreateConfirmationInstruction(
			publicAnnouncement: announcement,
			affectedPlayerIds: [victimId]);

		var cut = context.RenderModeratorComponent<InstructionRenderer>(
			parameters => parameters
				.Add(component => component.Instruction, instruction)
				.Add(component => component.Roster,
					[CreateRosterEntry(victimId, 1, PlayerNames.Ana)]));

		cut.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(announcement);
		cut.FindAll("[role='group']").Should().BeEmpty();
		cut.FindAll(DashboardActionZoneSelector).Should().ContainSingle();
		cut.FindAll(HoldButtonSelector).Should().ContainSingle();
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
	public async Task LiveHunterFinalShot_RendersMandatoryLocalizedLegalLivingTargetsAndEmitsOneCorrelatedSelection()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Witch,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var nightStart = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(nightStart.CreateResponse()).IsSuccess.Should().BeTrue();

		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var werewolf = players[0];
		var witch = players[1];
		var hunter = players[2];
		var concurrentVictim = players[3];
		var selectedTarget = players[4];
		var werewolfIdentification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(werewolfIdentification.CreateResponse([werewolf.Id]))
			.IsSuccess.Should().BeTrue();
		var werewolfVictim = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(werewolfVictim.CreateResponse([hunter.Id]))
			.IsSuccess.Should().BeTrue();
		var werewolfSleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(werewolfSleep.CreateResponse()).IsSuccess.Should().BeTrue();

		var witchIdentification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(witchIdentification.CreateResponse([witch.Id]))
			.IsSuccess.Should().BeTrue();
		var healing = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(healing.CreateResponse([])).IsSuccess.Should().BeTrue();
		var poison = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(poison.CreateResponse([concurrentVictim.Id]))
			.IsSuccess.Should().BeTrue();
		var witchSleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(witchSleep.CreateResponse()).IsSuccess.Should().BeTrue();
		var finishNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		finishNight.Semantic.Should().Be(ModeratorInstructionSemantic.FinishNightActions);
		manager.ProcessInput(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		var reveal = manager.CurrentInstruction
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		reveal.PlayersForAssignment.Should().BeEquivalentTo(
			[hunter.Id, concurrentVictim.Id]);
		manager.ProcessInput(reveal.CreateResponse(new()
		{
			[hunter.Id] = MainRoleType.Hunter,
			[concurrentVictim.Id] = MainRoleType.SimpleVillager
		})).IsSuccess.Should().BeTrue();

		var instruction = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var responses = new List<ModeratorResponse>();
		var livingRoster = manager.CurrentRoster
			.Where(entry => !entry.IsDead)
			.ToArray();

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, manager.CurrentRoster)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		instruction.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		instruction.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		instruction.EmptySelectionOptionLabel.Should().BeNull();
		instruction.SelectablePlayerIds.Should().BeEquivalentTo(
			livingRoster.Select(entry => entry.PlayerId));
		manager.CurrentRoster.Single(entry => entry.PlayerId == hunter.Id)
			.IsDead.Should().BeTrue();
		manager.CurrentRoster.Single(entry => entry.PlayerId == concurrentVictim.Id)
			.IsDead.Should().BeTrue();
		cut.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(GameStrings.HunterFinalShotSelectionInstruction);

		var options = cut.FindAll(PlayerOptionSelector);
		options.Should().HaveCount(livingRoster.Length);
		foreach (var legalTarget in livingRoster)
		{
			options.Should().ContainSingle(option =>
				option.TextContent.Contains(
					legalTarget.Name,
					StringComparison.CurrentCulture));
		}

		options.Should().NotContain(option =>
			option.TextContent.Contains(hunter.Name, StringComparison.CurrentCulture));
		options.Should().NotContain(option =>
			option.TextContent.Contains(
				concurrentVictim.Name,
				StringComparison.CurrentCulture));
		options.Should().NotContain(option =>
			option.TextContent.Contains(
				GameStrings.DeclineOption,
				StringComparison.CurrentCulture));

		var actionZones = cut.FindAll(DashboardActionZoneSelector);
		actionZones.Should().ContainSingle();
		actionZones.Single().TextContent.Should()
			.Contain(ClientStrings.SelectPlayers_SubmitButton);
		actionZones.Single().QuerySelectorAll(HoldButtonSelector)
			.Should().ContainSingle();
		var holdButton = cut.Find(HoldButtonSelector);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();

		var selectedTargetOption = options.Single(option =>
			option.TextContent.Contains(
				selectedTarget.Name,
				StringComparison.CurrentCulture));
		selectedTargetOption.Click();
		selectedTargetOption = cut.FindAll(PlayerOptionSelector).Single(option =>
			option.TextContent.Contains(
				selectedTarget.Name,
				StringComparison.CurrentCulture));
		AssertOptionSelected(selectedTargetOption, isSelected: true);
		holdButton = cut.Find(HoldButtonSelector);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();

		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);

		responses.Should().ContainSingle();
		responses.Single().Type.Should().Be(ExpectedInputType.PlayerSelection);
		responses.Single().InstructionId.Should().Be(instruction.InstructionId);
		responses.Single().SelectedPlayerIds.Should().Equal(selectedTarget.Id);
	}

	[Fact]
	public async Task DayVoteInstruction_RendersExplicitDrawChoiceAndRequiresAChosenOutcome()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = CreateManagerAtDayVote();
		var instruction = manager.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var responses = new List<ModeratorResponse>();

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, DashboardRoster.FromSession(manager.CurrentSession))
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		var options = cut.FindAll(PlayerOptionSelector);
		options.Should().HaveCount(instruction.SelectablePlayerIds.Count + 1);
		var drawOption = options.Single(option =>
			option.TextContent.Contains(GameStrings.DayVoteNoEliminationOption, StringComparison.CurrentCulture));
		AssertOptionSelected(drawOption, isSelected: false);

		var holdButton = cut.Find(HoldButtonSelector);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		await AttemptDisabledHoldAsync(cut, holdButton, timing);
		responses.Should().BeEmpty();

		drawOption.Click();

		drawOption = cut.FindAll(PlayerOptionSelector).Single(option =>
			option.TextContent.Contains(GameStrings.DayVoteNoEliminationOption, StringComparison.CurrentCulture));
		AssertOptionSelected(drawOption, isSelected: true);
		holdButton = cut.Find(HoldButtonSelector);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();

		var earlyHoldTask = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(RenderedHoldButtonDriver.HoldDuration - TimeSpan.FromMilliseconds(1));
		await RenderedHoldButtonDriver.ReleaseHoldAsync(holdButton);
		await earlyHoldTask;
		responses.Should().BeEmpty();

		holdButton = cut.Find(HoldButtonSelector);
		var canceledHoldTask = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(TimeSpan.FromMilliseconds(200));
		await RenderedHoldButtonDriver.LeaveHoldAsync(holdButton);
		await canceledHoldTask;
		timing.AdvanceBy(
			RenderedHoldButtonDriver.HoldDuration +
			RenderedHoldButtonDriver.SuccessFlashDuration);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		responses.Should().BeEmpty();

		holdButton = cut.Find(HoldButtonSelector);
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);

		responses.Should().ContainSingle();
		responses.Single().Type.Should().Be(ExpectedInputType.PlayerSelection);
		responses.Single().InstructionId.Should().Be(instruction.InstructionId);
		responses.Single().SelectedPlayerIds.Should().BeEmpty();
	}

	[Fact]
	public void SelectOptionsInstruction_WithPublicAndPrivateGuidance_KeepsPrivateGuidanceCollapsedInitially()
	{
		using var context = new ModeratorComponentTestContext();
		var haptic = new RecordingHapticFeedbackService();
		context.Services.AddSingleton<IHapticFeedbackService>(haptic);
		var options = new[]
		{
			GameStrings.NightStartsPrompt,
			GameStrings.DebateStartsPrompt,
			GameStrings.RevealRolePromptSpecify
		};
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

		privateToggle.Click();
		publicToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_AnnounceLabel);
		privateToggle = cut.FindButtonByAccessibleName(ClientStrings.Dashboard_ModeratorLabel);

		publicToggle.GetAttribute(Html.Attributes.AriaExpanded).Should().Be(Html.AriaValues.False);
		privateToggle.GetAttribute(Html.Attributes.AriaExpanded).Should().Be(Html.AriaValues.True);
		publicToggle.TextContent.Should().NotContain(GameStrings.DebateStartsPrompt);
		privateToggle.TextContent.Should().Contain(GameStrings.RevealRolePromptSpecify);
		cut.FindAll(DashboardActionZoneSelector).Should().ContainSingle();
		cut.FindAll(HoldButtonSelector).Should().ContainSingle();
		haptic.ClickCount.Should().Be(1);
	}

	[Fact]
	public void ReplacingInputInstruction_ResetsGuidanceAndDraftSelection()
	{
		using var context = new ModeratorComponentTestContext();
		var first = CreateSelectOptionsInstruction(
			NumberRangeConstraint.Single,
			[new ModeratorOption("first", GameStrings.ConfirmNightStarted)],
			publicAnnouncement: $"{GameStrings.NightStartsPrompt}\n{GameStrings.DebateStartsPrompt}",
			privateInstruction: $"{GameStrings.ConfirmNightStarted}\n{GameStrings.RevealRolePromptSpecify}");
		var second = CreateSelectOptionsInstruction(
			NumberRangeConstraint.Single,
			[new ModeratorOption("second", GameStrings.DebateStartsPrompt)],
			publicAnnouncement: $"{GameStrings.NightActionsCompletePrompt}\n{GameStrings.DebateStartsPrompt}",
			privateInstruction: $"{GameStrings.ConfirmNightStarted}\n{GameStrings.RevealRolePromptSpecify}");

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, first));
		cut.FindButtonByAccessibleName(ClientStrings.Dashboard_ModeratorLabel).Click();
		cut.FindAll(Html.Selectors.Button)
			.Single(button => button.TextContent.Trim() == first.Options.Single().Label)
			.Click();

		cut.Render(parameters => parameters
			.Add(component => component.Instruction, second));

		cut.FindButtonByAccessibleName(ClientStrings.Dashboard_AnnounceLabel)
			.GetAttribute(Html.Attributes.AriaExpanded).Should().Be(Html.AriaValues.True);
		cut.FindButtonByAccessibleName(ClientStrings.Dashboard_ModeratorLabel)
			.GetAttribute(Html.Attributes.AriaExpanded).Should().Be(Html.AriaValues.False);
		cut.FindAll(Html.Selectors.Button)
			.Single(button => button.TextContent.Trim() == second.Options.Single().Label)
			.GetAttribute(Html.Attributes.AriaPressed).Should().Be(Html.AriaValues.False);
	}

	[Fact]
	public void SelectOptionsInstruction_RendersCoreProvidedOptionControlsAndSingleInputActionZone()
	{
		using var context = new ModeratorComponentTestContext();
		var localizedLabels = new[]
		{
			GameStrings.NightStartsPrompt,
			GameStrings.DebateStartsPrompt,
			GameStrings.RevealRolePromptSpecify
		};
		localizedLabels.Should().OnlyHaveUniqueItems();
		var labelsByLexicalOrder = localizedLabels
			.Order(StringComparer.Ordinal)
			.ToArray();
		var options = new[]
		{
			new ModeratorOption("z-option", labelsByLexicalOrder[2]),
			new ModeratorOption("a-option", labelsByLexicalOrder[0]),
			new ModeratorOption("m-option", labelsByLexicalOrder[1])
		};
		var instruction = CreateSelectOptionsInstruction(NumberRangeConstraint.Single, options);

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction));

		var optionGroup = cut.FindAll("*")
			.Single(element => element.GetAttribute(Html.Attributes.AriaLabel) == ClientStrings.SelectOptions_Title);
		var optionButtons = optionGroup.QuerySelectorAll(Html.Selectors.Button).ToArray();
		optionButtons.Select(button => button.TextContent.Trim())
			.Should()
			.Equal(options.Select(option => option.Label));
		optionButtons.Should().OnlyContain(button =>
			button.GetAttribute(Html.Attributes.Type) == Html.AttributeValues.ButtonType);
		optionButtons.Should().OnlyContain(button =>
			button.GetAttribute(Html.Attributes.AriaPressed) == Html.AriaValues.False);

		var actionZones = cut.FindAll(DashboardActionZoneSelector);
		actionZones.Should().ContainSingle();
		actionZones.Single().TextContent.Should().Contain(ClientStrings.Dashboard_ContinueButton);
		actionZones.Single().QuerySelectorAll(HoldButtonSelector).Should().ContainSingle();
	}

	[Fact]
	public async Task LiveThiefOfferChoice_RendersPrivatePortugueseCoreOptionsForDesignatedPlayer()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		GameStrings.Culture = ModeratorComponentTestContext.PortugueseCulture;
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var cards = new[]
		{
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Thief),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Seer),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Seer)
		};
		var lockIn = new RoleLockIn(
			version: 1,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Take(5).Select(card => card.Id),
			offer1CardId: cards[5].Id,
			offer2CardId: cards[6].Id);
		var start = manager.StartGame(new GameSessionConfig(
			PlayerNames.DefaultFive.ToList(),
			lockIn));
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var nightStart = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(nightStart.CreateResponse()).IsSuccess.Should().BeTrue();
		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var thiefHolder = players[0];
		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identification.RoleIdentification.Should().Be(MainRoleType.Thief);
		manager.ProcessInput(identification.CreateResponse([thiefHolder.Id]))
			.IsSuccess.Should().BeTrue();

		var choice = manager.CurrentInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		choice.Semantic.Should().Be(ModeratorInstructionSemantic.ChooseThiefOffer);
		choice.PublicAnnouncement.Should().BeNull();
		choice.PrivateInstruction.Should().Be(GameStrings.ThiefOfferSelectionInstruction);
		choice.AffectedPlayerIds.Should().Equal(thiefHolder.Id);
		choice.Options.Select(option => option.Id).Should().Equal(
			ThiefOfferOptionIds.Offer1,
			ThiefOfferOptionIds.Offer2,
			ThiefOfferOptionIds.Decline);
		ModeratorResponse? receivedResponse = null;
		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, choice)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(
					this,
					response => receivedResponse = response)));

		cut.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.ThiefOfferSelectionInstruction);
		cut.FindAll(PublicInstructionSelector).Should().BeEmpty();
		var optionGroup = cut.FindAll("*")
			.Single(element =>
				element.GetAttribute(Html.Attributes.AriaLabel) ==
				ClientStrings.SelectOptions_Title);
		var optionButtons = optionGroup.QuerySelectorAll(Html.Selectors.Button).ToArray();
		optionButtons.Select(button => button.TextContent.Trim()).Should().Equal(
			choice.Options.Select(option => option.Label));
		optionButtons.Should().HaveCount(choice.Options.Count);
		optionButtons.Take(2).Select(button => button.TextContent.Trim()).Should().Equal(
			MainRoleType.Seer.GetPublicName(),
			MainRoleType.Seer.GetPublicName());
		optionButtons[0].Should().NotBeSameAs(optionButtons[1]);
		optionButtons[1].Click();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			cut.Find(HoldButtonSelector),
			timing);

		receivedResponse.Should().NotBeNull();
		var response = receivedResponse!;
		response.Type.Should().Be(ExpectedInputType.OptionSelection);
		response.InstructionId.Should().Be(choice.InstructionId);
		response.SelectedOptionIds.Should().Equal(ThiefOfferOptionIds.Offer2);
		manager.ProcessInput(response).IsSuccess.Should().BeTrue();
		manager.CurrentInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Which.Semantic.Should().Be(
				ModeratorInstructionSemantic.PutRoleToSleep);
		thiefHolder.State.CurrentRole.Should().Be(MainRoleType.Seer);
	}

	private static ConfirmationInstruction CreateConfirmationInstruction(
		string? publicAnnouncement = null,
		string? privateInstruction = null,
		IReadOnlyList<SoundEffectsEnum>? soundEffects = null,
		IReadOnlyList<Guid>? affectedPlayerIds = null) =>
		(ConfirmationInstruction)ConfirmationConstructor.Invoke(
			[
				publicAnnouncement,
				privateInstruction,
				affectedPlayerIds,
				Guid.Empty,
				soundEffects
			]);

	private static AssignRolesInstruction CreateAssignRolesInstruction(
		IEnumerable<Guid> playerIds,
		IReadOnlyList<MainRoleType> roles,
		string? publicAnnouncement = null) =>
		(AssignRolesInstruction)AssignRolesConstructor.Invoke(
			[
				playerIds.ToImmutableHashSet(),
				roles,
				publicAnnouncement,
				GameStrings.RevealRolePromptSpecify,
				null,
				Guid.Empty
			]);

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
		IEnumerable<ModeratorOption> options) =>
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
		CreateSelectOptionsInstruction(
			selectionRange,
			options.Select((label, index) => new ModeratorOption($"option-{index}", label)),
			publicAnnouncement,
			privateInstruction);

	private static SelectOptionsInstruction CreateSelectOptionsInstruction(
		NumberRangeConstraint selectionRange,
		IEnumerable<ModeratorOption> options,
		string? publicAnnouncement,
		string? privateInstruction) =>
		(SelectOptionsInstruction)SelectOptionsConstructor.Invoke(
			[
				options
					.ToArray(),
				selectionRange,
				publicAnnouncement,
				privateInstruction,
				null,
				Guid.Empty
			]);

	private static DevotedServantVoteWindowInstruction CreateDevotedServantVoteWindowInstruction(
		Guid selectablePlayerId) =>
		(DevotedServantVoteWindowInstruction)DevotedServantVoteWindowConstructor.Invoke(
			[
				Guid.NewGuid(),
				new HashSet<Guid> { selectablePlayerId },
				GameStrings.DevotedServantVoteWindowAnnouncement
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

	private static async Task AttemptDisabledHoldAsync<TComponent>(
		IRenderedComponent<TComponent> cut,
		AngleSharp.Dom.IElement holdButton,
		ControlledHoldButtonTiming timing)
		where TComponent : IComponent
	{
		var holdTask = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		timing.AdvanceBy(RenderedHoldButtonDriver.HoldDuration + RenderedHoldButtonDriver.SuccessFlashDuration);
		await holdTask;
		await RenderedHoldButtonDriver.FlushAsync(cut);
	}

	private static void AssertOptionSelected(AngleSharp.Dom.IElement option, bool isSelected)
	{
		option.GetAttribute(Html.Attributes.AriaSelected)
			.Should()
			.Be(isSelected ? Html.AriaValues.True : Html.AriaValues.False);

		if (isSelected)
		{
			option.ClassList.Should().Contain(ClientTestReferences.Css.Classes.SelectPlayersItemSelected);
		}
		else
		{
			option.ClassList.Should().NotContain(ClientTestReferences.Css.Classes.SelectPlayersItemSelected);
		}
	}

	private static GameClientManager CreateManagerAtDayVote()
	{
		var manager = new GameClientManager(new GameService());
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse());

		for (var step = 0; step < 50; step++)
		{
			if (manager.CurrentPhase == GamePhase.Day &&
				manager.CurrentInstruction is ConfirmationInstruction debateInstruction &&
				debateInstruction.PublicAnnouncement == GameStrings.DebateStartsPrompt)
			{
				manager.ProcessInput(debateInstruction.CreateResponse());
				manager.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>();
				return manager;
			}

			switch (manager.CurrentInstruction)
			{
				case ConfirmationInstruction confirmation:
					manager.ProcessInput(confirmation.CreateResponse());
					break;
				case SelectPlayersInstruction selectPlayers:
					manager.ProcessInput(selectPlayers.CreateResponse([selectPlayers.SelectablePlayerIds.First()]));
					break;
				case AssignRolesInstruction assignRoles:
					var assignments = assignRoles.PlayersForAssignment.ToDictionary(
						playerId => playerId,
						_ => MainRoleType.SimpleVillager);
					manager.ProcessInput(assignRoles.CreateResponse(assignments));
					break;
				default:
					throw new InvalidOperationException(
						$"Unexpected instruction while advancing to day vote: {manager.CurrentInstruction?.GetType().Name}");
			}
		}

		throw new InvalidOperationException("Day vote instruction was not reached.");
	}

	private static StartGameConfirmationInstruction StartSimpleGame(GameClientManager manager) =>
		manager.StartGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

	private static readonly ConstructorInfo ConfirmationConstructor =
		typeof(ConfirmationInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 5);

	private static readonly ConstructorInfo AssignRolesConstructor =
		typeof(AssignRolesInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 6);

	private static readonly ConstructorInfo SelectPlayersConstructor =
		typeof(SelectPlayersInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 6);

	private static readonly ConstructorInfo SelectOptionsConstructor =
		typeof(SelectOptionsInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 6);

	private static readonly ConstructorInfo DevotedServantVoteWindowConstructor =
		typeof(DevotedServantVoteWindowInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 3);

	private sealed record InstructionFamilyCase(
		ModeratorInstruction Instruction,
		string? ExpectedActionLabel,
		bool IsResponseEnabled);

	private sealed class RecordingHapticFeedbackService : IHapticFeedbackService
	{
		public int ClickCount { get; private set; }

		public void Click() => ClickCount++;

		public void LongPress()
		{
		}
	}
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
