using Bunit;
using AngleSharp.Dom;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Fixtures;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
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
	private static string PlayerOptionSelector =>
		Html.Selectors.ElementWithRole(Html.Elements.ListItem, Html.Roles.Option);
	private static string PublicInstructionSelector =>
		$".{ClientTestReferences.Css.Classes.InstructionAnnouncement}";
	private static string PrivateInstructionSelector =>
		$".{ClientTestReferences.Css.Classes.InstructionPrivate}";

	[Theory]
	[InlineData(MainRoleType.VillagerVillager)]
	[InlineData(MainRoleType.Angel)]
	[InlineData(MainRoleType.LittleGirl)]
	[InlineData(MainRoleType.Witch)]
	[InlineData(MainRoleType.Hunter)]
	[InlineData(MainRoleType.StutteringJudge)]
	[InlineData(MainRoleType.Scapegoat)]
	[InlineData(MainRoleType.VillageIdiot)]
	[InlineData(MainRoleType.AccursedWolfFather)]
	[InlineData(MainRoleType.BigBadWolf)]
	[InlineData(MainRoleType.Defender)]
	[InlineData(MainRoleType.WhiteWerewolf)]
	[InlineData(MainRoleType.Fox)]
	[InlineData(MainRoleType.DevotedServant)]
	[InlineData(MainRoleType.Elder)]
	public void SingleOptionalRoleLobby_UsesCatalogMetadataAsPortugueseToggle(
		MainRoleType role)
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var roleInfo = lobby.GetRoleInfo(role);
		var expectedDisplayName = role switch
		{
			MainRoleType.VillagerVillager => GameStrings.VillagerVillagerRoleName,
			MainRoleType.Angel => GameStrings.AngelRoleName,
			MainRoleType.LittleGirl => GameStrings.LittleGirlRoleName,
			MainRoleType.Witch => GameStrings.WitchRoleName,
			MainRoleType.Hunter => GameStrings.HunterRoleName,
			MainRoleType.StutteringJudge => GameStrings.StutteringJudgeRoleName,
			MainRoleType.Scapegoat => GameStrings.ScapegoatRoleName,
			MainRoleType.VillageIdiot => GameStrings.VillageIdiotRoleName,
			MainRoleType.AccursedWolfFather =>
				GameStrings.AccursedWolfFatherRoleName,
			MainRoleType.BigBadWolf => GameStrings.BigBadWolfRoleName,
			MainRoleType.Defender => GameStrings.DefenderRoleName,
			MainRoleType.WhiteWerewolf => GameStrings.WhiteWerewolfRoleName,
			MainRoleType.Fox => GameStrings.FoxRoleName,
			MainRoleType.DevotedServant => GameStrings.DevotedServantRoleName,
			MainRoleType.Elder => GameStrings.ElderRoleName,
			_ => throw new InvalidOperationException(
				$"Unexpected Single-Optional Role {role}.")
		};

		roleInfo.DisplayName.Should().Be(expectedDisplayName);
		roleInfo.Affordance.Should().Be(RoleAffordance.Toggle);
		roleInfo.BatchSize.Should().Be(1);

		var cut = context.RenderModeratorComponent<RoleSelectionPage>();
		var toggle = cut.FindAll(Html.Selectors.Button)
			.Single(button => button.GetAttribute(Html.Attributes.AriaLabel) == roleInfo.DisplayName);
		toggle.ParentElement!.TextContent.Should().Contain(roleInfo.DisplayName);
		toggle.GetAttribute(Html.Attributes.AriaPressed).Should().Be(Html.AriaValues.False);

		toggle.Click();

		lobby.GetRoleCount(role).Should().Be(1);
		toggle = cut.FindAll(Html.Selectors.Button)
			.Single(button => button.GetAttribute(Html.Attributes.AriaLabel) == roleInfo.DisplayName);
		toggle.GetAttribute(Html.Attributes.AriaPressed).Should().Be(Html.AriaValues.True);

		toggle.Click();

		lobby.GetRoleCount(role).Should().Be(0);
	}

	[Fact]
	public async Task LittleGirlUnknownWerewolfCollective_RendersGuidanceWithoutAddingControls()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartPreparedGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();

		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var littleGirl = players[0];
		var werewolf = players[1];
		var victim = players[2];
		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identification.RoleIdentification.Should().Be(MainRoleType.LittleGirl);
		manager.ProcessInput(identification.CreateResponse([littleGirl.Id]))
			.IsSuccess.Should().BeTrue();

		var observation = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		const int exactAgentCount = 1;
		var observationPrompt = GameStrings.WerewolfFactionAgentObservationPrompt
			.Format(exactAgentCount);
		var wakeAnnouncement = GameStrings.RoleHoldersWakeUp.Format(
			GameStrings.WerewolvesGroupName);
		var pendingInstructionId = observation.InstructionId;
		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		observation.PublicAnnouncement.Should().Be(wakeAnnouncement);
		observation.PrivateInstruction.Should()
			.Contain(observationPrompt)
			.And.Contain(GameStrings.LittleGirlOpeningGuidance);
		observation.CountConstraint.Should().Be(
			NumberRangeConstraint.Exact(exactAgentCount));
		observation.AffectedPlayerIds.Should().BeNull();

		var dashboard = context.RenderModeratorComponent<DashboardPage>();
		dashboard.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(wakeAnnouncement)
			.And.NotContain(observationPrompt);
		dashboard.Find(PrivateInstructionSelector).Click();
		dashboard.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(observationPrompt)
			.And.Contain(GameStrings.LittleGirlOpeningGuidance);
		var playerOptions = dashboard.FindAll(PlayerOptionSelector);
		playerOptions.Should().HaveCount(players.Length - 1);
		playerOptions.Should().NotContain(option => option.TextContent.Contains(
			littleGirl.Name,
			StringComparison.CurrentCulture));
		dashboard.FindAll(HoldButtonSelector).Should().ContainSingle();
		var observationHold = dashboard.Find(HoldButtonSelector);
		observationHold.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();

		playerOptions
			.Single(option => option.TextContent.Contains(
				werewolf.Name,
				StringComparison.CurrentCulture))
			.Click();
		observationHold = dashboard.Find(HoldButtonSelector);
		observationHold.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		var earlyHold = RenderedHoldButtonDriver.StartHoldAsync(observationHold);
		await RenderedHoldButtonDriver.FlushAsync(dashboard);
		timing.AdvanceBy(
			RenderedHoldButtonDriver.HoldDuration - TimeSpan.FromMilliseconds(1));
		await RenderedHoldButtonDriver.ReleaseHoldAsync(observationHold);
		await earlyHold;

		manager.CurrentInstruction!.InstructionId.Should().Be(pendingInstructionId);
		observationHold = dashboard.Find(HoldButtonSelector);
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			observationHold,
			timing);

		var victimSelection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		manager.ProcessInput(victimSelection.CreateResponse([victim.Id]))
			.IsSuccess.Should().BeTrue();

		var sleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var sleepAnnouncement = GameStrings.RoleHoldersGoToSleep.Format(
			GameStrings.WerewolvesGroupName);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(sleepAnnouncement);
		sleep.PrivateInstruction.Should().Be(GameStrings.LittleGirlClosingGuidance);
		sleep.AffectedPlayerIds.Should().Equal(werewolf.Id);
		sleep.AffectedPlayerIds.Should().NotContain(littleGirl.Id);

		var sleepDashboard = context.RenderModeratorComponent<DashboardPage>();
		sleepDashboard.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(sleepAnnouncement);
		sleepDashboard.Find(PrivateInstructionSelector).Click();
		sleepDashboard.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.LittleGirlClosingGuidance);
		sleepDashboard.FindAll(PlayerOptionSelector).Should().BeEmpty();
		var sleepHolds = sleepDashboard.FindAll(HoldButtonSelector);
		sleepHolds.Should().ContainSingle();
		sleepHolds.Single().TextContent.Should()
			.Contain(ClientStrings.Dashboard_ContinueButton);
	}

	[Theory]
	[InlineData(MainRoleType.TwoSisters, 2)]
	[InlineData(MainRoleType.ThreeBrothers, 3)]
	public void CardinalityRoleLobby_UsesProductionCatalogAsPortugueseBatchToggle(
		MainRoleType role,
		int batchSize)
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var roleInfo = lobby.GetRoleInfo(role);
		var expectedDisplayName = role switch
		{
			MainRoleType.TwoSisters => GameStrings.TwoSistersRoleName,
			MainRoleType.ThreeBrothers => GameStrings.ThreeBrothersRoleName,
			_ => throw new InvalidOperationException(
				$"Unexpected Cardinality Role {role}.")
		};

		roleInfo.DisplayName.Should().Be(expectedDisplayName);
		roleInfo.Affordance.Should().Be(RoleAffordance.Toggle);
		roleInfo.BatchSize.Should().Be(batchSize);

		var cut = context.RenderModeratorComponent<RoleSelectionPage>();
		var toggle = cut.FindAll(Html.Selectors.Button)
			.Single(button =>
				button.GetAttribute(Html.Attributes.AriaLabel) == roleInfo.DisplayName);
		toggle.TextContent.Should().Contain($"×{roleInfo.BatchSize}");
		toggle.GetAttribute(Html.Attributes.AriaPressed)
			.Should().Be(Html.AriaValues.False);

		toggle.Click();

		lobby.GetRoleCount(role).Should().Be(batchSize);
		toggle = cut.FindAll(Html.Selectors.Button)
			.Single(button =>
				button.GetAttribute(Html.Attributes.AriaLabel) == roleInfo.DisplayName);
		toggle.GetAttribute(Html.Attributes.AriaPressed)
			.Should().Be(Html.AriaValues.True);

		toggle.Click();

		lobby.GetRoleCount(role).Should().Be(0);
	}

	[Fact]
	public async Task VillagerVillagerPublicFromDeal_UsesCorrelatedPlayerSelectionAndCommitsOnlyAfterCompletedHold()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartPreparedGame(
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
	public void PrivateFactionAgentObservation_DoesNotRevealExactRoleOrIncreaseRevealedCount()
	{
		using var context = new ModeratorComponentTestContext();
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartPreparedGame(
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
		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		identification.RoleIdentification.Should().BeNull();

		manager.ProcessInput(identification.CreateResponse([holder.Id]))
			.IsSuccess.Should().BeTrue();

		holder.State.ModeratorKnownRole.Should().BeNull();
		manager.CurrentSession.GetFactionAgentKnowledge(holder.Id, Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		var cut = context.RenderModeratorComponent<DashboardPage>();
		var holderEntry = cut.FindAll("li")
			.Single(entry => entry.TextContent.Contains(holder.Name, StringComparison.CurrentCulture));
		holderEntry.TextContent.Should().Contain(DashboardRoster.UnknownRoleLabel);
		holderEntry.TextContent.Should().Contain(ClientStrings.Dashboard_RoleKnowledgeUnknown);

		var revealedLabel = cut.FindAll("span")
			.Single(element => element.TextContent.Trim() == ClientStrings.Dashboard_RevealedStatLabel);
		revealedLabel.ParentElement!.QuerySelector("strong")!.TextContent.Trim().Should().Be("0");
	}

	[Fact]
	public async Task StutteringJudgeSignalFlow_RendersLocalizedSetupAndObservationWithDeliberateHolds()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartPreparedGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();
		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var judge = players[0];
		var werewolf = players[1];
		var victim = players[4];
		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identification.RoleIdentification.Should().Be(MainRoleType.StutteringJudge);
		manager.ProcessInput(identification.CreateResponse([judge.Id]))
			.IsSuccess.Should().BeTrue();
		var setup = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		setup.Semantic.Should().Be(
			ModeratorInstructionSemantic.EstablishStutteringJudgeSignal);

		var cut = context.RenderModeratorComponent<DashboardPage>();

		cut.FindAll(PublicInstructionSelector).Should().BeEmpty();
		cut.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.StutteringJudgeSignalSetupInstruction);
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			cut.Find(HoldButtonSelector),
			timing);

		AdvanceToFirstDayDebate(manager, werewolf.Id, victim.Id);
		var debate = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		debate.Semantic.Should().Be(ModeratorInstructionSemantic.StartDayDebate);
		manager.ProcessInput(debate.CreateResponse()).IsSuccess.Should().BeTrue();
		var conductVote = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		conductVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.ConductDayVote);
		cut.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(GameStrings.VoteStartsPublicInstruction);
		cut.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.DayVoteConductInstruction);

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			cut.Find(HoldButtonSelector),
			timing);

		var signal = manager.CurrentInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		signal.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveStutteringJudgeSignal);
		signal.Options.Select(option => (option.Id, option.Label)).Should().Equal(
			(
				StutteringJudgeSignalOptionIds.Occurred,
				GameStrings.StutteringJudgeSignalOccurredOption),
			(
				StutteringJudgeSignalOptionIds.DidNotOccur,
				GameStrings.StutteringJudgeSignalDidNotOccurOption));
		cut.FindAll(PublicInstructionSelector).Should().BeEmpty();
		cut.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.StutteringJudgeSignalObservationInstruction);
		var occurredOption = cut.FindAll(Html.Selectors.Button)
			.Single(button => button.TextContent.Trim() ==
				GameStrings.StutteringJudgeSignalOccurredOption);
		var didNotOccurOption = cut.FindAll(Html.Selectors.Button)
			.Single(button => button.TextContent.Trim() ==
				GameStrings.StutteringJudgeSignalDidNotOccurOption);
		occurredOption.GetAttribute(Html.Attributes.AriaPressed)
			.Should().Be(Html.AriaValues.False);
		didNotOccurOption.GetAttribute(Html.Attributes.AriaPressed)
			.Should().Be(Html.AriaValues.False);
		occurredOption.Click();
		occurredOption = cut.FindAll(Html.Selectors.Button)
			.Single(button => button.TextContent.Trim() ==
				GameStrings.StutteringJudgeSignalOccurredOption);
		occurredOption.GetAttribute(Html.Attributes.AriaPressed)
			.Should().Be(Html.AriaValues.True);
		var pendingInstructionId = signal.InstructionId;
		var holdButton = cut.Find(HoldButtonSelector);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();

		var earlyHold = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(
			RenderedHoldButtonDriver.HoldDuration -
			TimeSpan.FromMilliseconds(1));
		await RenderedHoldButtonDriver.ReleaseHoldAsync(holdButton);
		await earlyHold;

		manager.CurrentInstruction!.InstructionId.Should().Be(
			pendingInstructionId);

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			cut.Find(HoldButtonSelector),
			timing);

		manager.CurrentInstruction!.InstructionId.Should().NotBe(
			pendingInstructionId);
		manager.CurrentInstruction.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
	}

	[Fact]
	public async Task WitchNightFlow_RendersPublicWakePrivatePotionsExplicitDeclineAndPublicSleep()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartPreparedGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();

		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var werewolf = players[0];
		var witch = players[1];
		var attackedPlayer = players[2];
		var werewolfObservation = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		werewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		werewolfObservation.RoleIdentification.Should().BeNull();
		manager.ProcessInput(werewolfObservation.CreateResponse([werewolf.Id]))
			.IsSuccess.Should().BeTrue();
		var werewolfVictim = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		werewolfVictim.Semantic.Should().Be(ModeratorInstructionSemantic.SelectWerewolfVictim);
		manager.ProcessInput(werewolfVictim.CreateResponse([attackedPlayer.Id]))
			.IsSuccess.Should().BeTrue();
		var werewolfSleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(werewolfSleep.CreateResponse()).IsSuccess.Should().BeTrue();

		var witchIdentification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		witchIdentification.RoleIdentification.Should().Be(MainRoleType.Witch);
		witchIdentification.PublicAnnouncement.Should()
			.Be(GameStrings.RoleWakesUp.Format(GameStrings.WitchRoleName));
		var cut = context.RenderModeratorComponent<DashboardPage>();
		cut.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(GameStrings.RoleWakesUp.Format(GameStrings.WitchRoleName));
		var witchOption = cut.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				witch.Name,
				StringComparison.CurrentCulture));
		witchOption.Click();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			cut.Find(HoldButtonSelector),
			timing);

		var healing = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		healing.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchHealingTarget);
		healing.EmptySelectionOptionLabel.Should().Be(GameStrings.DeclineOption);
		cut.FindAll(PublicInstructionSelector).Should().BeEmpty();
		cut.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(attackedPlayer.Name);
		var healingOptions = cut.FindAll(PlayerOptionSelector);
		healingOptions.Should().HaveCount(2);
		healingOptions.Should().ContainSingle(option =>
			option.TextContent.Contains(
				GameStrings.DeclineOption,
				StringComparison.CurrentCulture));
		var healingTarget = healingOptions.Single(option =>
			option.TextContent.Contains(
				attackedPlayer.Name,
				StringComparison.CurrentCulture));
		healingTarget.Click();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			cut.Find(HoldButtonSelector),
			timing);

		var poison = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		poison.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget);
		poison.EmptySelectionOptionLabel.Should().Be(GameStrings.DeclineOption);
		cut.FindAll(PublicInstructionSelector).Should().BeEmpty();
		cut.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.WitchPoisonSelectionInstruction);
		var poisonOptions = cut.FindAll(PlayerOptionSelector);
		poisonOptions.Should().HaveCount(4);
		poisonOptions.Should().NotContain(option =>
			option.TextContent.Contains(
				witch.Name,
				StringComparison.CurrentCulture));
		poisonOptions.Should().NotContain(option =>
			option.TextContent.Contains(
				attackedPlayer.Name,
				StringComparison.CurrentCulture));
		var declineOption = poisonOptions.Single(option =>
			option.TextContent.Contains(
				GameStrings.DeclineOption,
				StringComparison.CurrentCulture));
		declineOption.Click();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			cut.Find(HoldButtonSelector),
			timing);

		var sleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should()
			.Be(GameStrings.RoleGoesToSleepSingle.Format(GameStrings.WitchRoleName));
		cut.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(GameStrings.RoleGoesToSleepSingle.Format(GameStrings.WitchRoleName));
		cut.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		cut.FindAll(PlayerOptionSelector).Should().BeEmpty();
	}

	[Fact]
	public async Task WolfHoundNightFlow_RendersExactHolderPrivateAlignmentAndPublicSleepWithoutDisclosure()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartPreparedGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.WolfHound,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();

		var wolfHound = manager.CurrentSession!.GetPlayers().First();
		wolfHound.State.ModeratorKnownRole.Should().BeNull();
		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var wakeAnnouncement =
			GameStrings.RoleWakesUp.Format(GameStrings.WolfHoundRoleName);
		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.WolfHound);
		identification.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		identification.PublicAnnouncement.Should().Be(wakeAnnouncement);

		var dashboard = context.RenderModeratorComponent<DashboardPage>();
		var publicWake = dashboard.Find(PublicInstructionSelector);
		publicWake.TextContent.Should().Contain(wakeAnnouncement);
		publicWake.TextContent.Should().NotContain(wolfHound.Name);
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		var holderOption = dashboard.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				wolfHound.Name,
				StringComparison.CurrentCulture));
		holderOption.Click();
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		var alignment = manager.CurrentInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		alignment.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseWolfHoundAlignment);
		alignment.PublicAnnouncement.Should().BeNull();
		alignment.PrivateInstruction.Should().Be(
			GameStrings.WolfHoundAlignmentInstruction);
		alignment.SelectionRange.Should().Be(NumberRangeConstraint.Single);
		alignment.Options.Select(option => (option.Id, option.Label)).Should().Equal(
			(
				WolfHoundAlignmentOptionIds.Villagers,
				GameStrings.VillagersGroupName),
			(
				WolfHoundAlignmentOptionIds.Werewolves,
				GameStrings.WerewolvesGroupName));

		var responses = new List<ModeratorResponse>();
		var alignmentView =
			context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
				.Add(component => component.Instruction, alignment)
				.Add(component => component.Roster, manager.CurrentRoster)
				.Add(
					component => component.OnResponse,
					EventCallback.Factory.Create<ModeratorResponse>(
						this,
						responses.Add)));
		alignmentView.FindAll(PublicInstructionSelector).Should().BeEmpty();
		alignmentView.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.WolfHoundAlignmentInstruction);
		alignmentView.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		var villagersOption = alignmentView.FindAll(Html.Selectors.Button)
			.Single(button =>
				button.TextContent.Trim() == GameStrings.VillagersGroupName);
		villagersOption.GetAttribute(Html.Attributes.AriaPressed)
			.Should().Be(Html.AriaValues.False);
		villagersOption.Click();
		villagersOption = alignmentView.FindAll(Html.Selectors.Button)
			.Single(button =>
				button.TextContent.Trim() == GameStrings.VillagersGroupName);
		villagersOption.GetAttribute(Html.Attributes.AriaPressed)
			.Should().Be(Html.AriaValues.True);
		alignmentView.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			alignmentView,
			alignmentView.Find(HoldButtonSelector),
			timing);

		var response = responses.Should().ContainSingle().Subject;
		response.Type.Should().Be(ExpectedInputType.OptionSelection);
		response.InstructionId.Should().Be(alignment.InstructionId);
		response.SelectedOptionIds.Should().Equal(
			WolfHoundAlignmentOptionIds.Villagers);
		manager.ProcessInput(response).IsSuccess.Should().BeTrue();

		var sleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var sleepAnnouncement =
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.WolfHoundRoleName);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(sleepAnnouncement);
		sleep.PrivateInstruction.Should().BeNull();

		var sleepDashboard = context.RenderModeratorComponent<DashboardPage>();
		var publicSleep = sleepDashboard.Find(PublicInstructionSelector);
		publicSleep.TextContent.Should().Contain(sleepAnnouncement);
		publicSleep.TextContent.Should().NotContain(GameStrings.VillagersGroupName);
		publicSleep.TextContent.Should().NotContain(GameStrings.WerewolvesGroupName);
		sleepDashboard.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		sleepDashboard.FindAll(PlayerOptionSelector).Should().BeEmpty();

		var rosterProjection = manager.CurrentRoster
			.Single(entry => entry.PlayerId == wolfHound.Id);
		rosterProjection.RoleVisibility.Should().Be(
			DashboardRoleVisibility.ModeratorPrivate);
		rosterProjection.RoleLabel.Should().Be(GameStrings.WolfHoundRoleName);
		rosterProjection.RoleVisibilityLabel.Should().Be(
			ClientStrings.Dashboard_RoleKnowledgePrivate);
		var wolfHoundRosterEntry = sleepDashboard.FindAll("li")
			.Single(entry => entry.TextContent.Contains(
				wolfHound.Name,
				StringComparison.CurrentCulture));
		wolfHoundRosterEntry.TextContent.Should()
			.Contain(GameStrings.WolfHoundRoleName)
			.And.Contain(ClientStrings.Dashboard_RoleKnowledgePrivate)
			.And.NotContain(GameStrings.VillagersGroupName)
			.And.NotContain(GameStrings.WerewolvesGroupName);
	}

	[Fact]
	public async Task DefenderNightFlow_RendersPublicWakePrivateMandatoryTargetAndPublicSleep()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartPreparedGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();

		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var defender = players[0];
		var target = players[3];
		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var wakeAnnouncement = GameStrings.RoleWakesUp.Format(
			GameStrings.DefenderRoleName);
		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.Defender);
		identification.PublicAnnouncement.Should().Be(wakeAnnouncement);

		var dashboard = context.RenderModeratorComponent<DashboardPage>();
		dashboard.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(wakeAnnouncement)
			.And.NotContain(defender.Name);
		var holderOption = dashboard.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				defender.Name,
				StringComparison.CurrentCulture));
		holderOption.Click();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		var targetSelection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		targetSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectDefenderTarget);
		targetSelection.PublicAnnouncement.Should().BeNull();
		targetSelection.PrivateInstruction.Should().Be(
			GameStrings.DefenderTargetSelectionInstruction);
		targetSelection.AffectedPlayerIds.Should().Equal(defender.Id);
		targetSelection.CountConstraint.Should().Be(
			NumberRangeConstraint.Single);
		targetSelection.EmptySelectionOptionLabel.Should().BeNull();
		targetSelection.SelectablePlayerIds.Should().BeEquivalentTo(
			players.Select(player => player.Id));
		dashboard.FindAll(PublicInstructionSelector).Should().BeEmpty();
		dashboard.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.DefenderTargetSelectionInstruction);
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();

		var targetOption = dashboard.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				target.Name,
				StringComparison.CurrentCulture));
		targetOption.Click();
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		var sleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var sleepAnnouncement = GameStrings.RoleGoesToSleepSingle.Format(
			GameStrings.DefenderRoleName);
		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(sleepAnnouncement);
		sleep.PrivateInstruction.Should().BeNull();
		dashboard.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(sleepAnnouncement)
			.And.NotContain(defender.Name)
			.And.NotContain(target.Name);
		dashboard.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		dashboard.FindAll(PlayerOptionSelector).Should().BeEmpty();
	}

	[Fact]
	public async Task WhiteWerewolfNightOne_RendersPrivateIdentificationAndSkipsSoloAction()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var players = StartWhiteWerewolfGameAtFirstIdentification(manager);
		var whiteWerewolf = players[1];

		whiteWerewolf.State.ModeratorKnownRole.Should().BeNull();
		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var identificationPrompt =
			GameStrings.RoleSingleIdentificationPrompt.Format(
				GameStrings.WhiteWerewolfRoleName);
		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(
			MainRoleType.WhiteWerewolf);
		identification.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		identification.PublicAnnouncement.Should().BeNull();
		identification.PrivateInstruction.Should().Be(identificationPrompt);
		identification.AffectedPlayerIds.Should().BeNull();

		var dashboard = context.RenderModeratorComponent<DashboardPage>();
		dashboard.FindAll(PublicInstructionSelector).Should().BeEmpty();
		dashboard.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(identificationPrompt);
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		var holderOption = dashboard.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				whiteWerewolf.Name,
				StringComparison.CurrentCulture));
		holderOption.Click();
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		whiteWerewolf.State.ModeratorKnownRole.Should().Be(
			MainRoleType.WhiteWerewolf);
		whiteWerewolf.State.PubliclyRevealedRole.Should().BeNull();
		manager.CurrentInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Which.Semantic.Should().Be(
				ModeratorInstructionSemantic.FinishNightActions);
	}

	[Fact]
	public async Task WhiteWerewolfEvenNightFlow_RendersPublicWakePrivateOptionalAgentTargetExplicitDeclineAndPublicSleep()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var players = StartWhiteWerewolfGameAtFirstIdentification(manager);
		var werewolf = players[0];
		var whiteWerewolf = players[1];

		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(
				identification.CreateResponse([whiteWerewolf.Id]))
			.IsSuccess.Should().BeTrue();
		AdvanceToSecondNightStart(manager);
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();

		var collectiveWake = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(collectiveWake.CreateResponse())
			.IsSuccess.Should().BeTrue();
		var victimSelection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(victimSelection.CreateResponse([players[5].Id]))
			.IsSuccess.Should().BeTrue();
		var collectiveSleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(collectiveSleep.CreateResponse())
			.IsSuccess.Should().BeTrue();

		var wake = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var wakeAnnouncement = GameStrings.RoleWakesUp.Format(
			GameStrings.WhiteWerewolfRoleName);
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(whiteWerewolf.Id);
		wake.PublicAnnouncement.Should().Be(wakeAnnouncement);
		wake.PrivateInstruction.Should().BeNull();

		var dashboard = context.RenderModeratorComponent<DashboardPage>();
		dashboard.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(wakeAnnouncement)
			.And.NotContain(whiteWerewolf.Name);
		dashboard.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		dashboard.FindAll(PlayerOptionSelector).Should().BeEmpty();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		var targetSelection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		targetSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWhiteWerewolfTarget);
		targetSelection.RoleIdentification.Should().BeNull();
		targetSelection.CountConstraint.Should().Be(
			NumberRangeConstraint.SingleOptional);
		targetSelection.EmptySelectionOptionLabel.Should().Be(
			GameStrings.DeclineOption);
		targetSelection.SelectablePlayerIds.Should().Equal(werewolf.Id);
		targetSelection.AffectedPlayerIds.Should().Equal(whiteWerewolf.Id);
		targetSelection.PublicAnnouncement.Should().BeNull();
		targetSelection.PrivateInstruction.Should().Be(
			GameStrings.WhiteWerewolfTargetSelectionInstruction);

		dashboard.FindAll(PublicInstructionSelector).Should().BeEmpty();
		dashboard.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.WhiteWerewolfTargetSelectionInstruction);
		var options = dashboard.FindAll(PlayerOptionSelector);
		options.Should().HaveCount(2);
		options.Should().ContainSingle(option => option.TextContent.Contains(
			werewolf.Name,
			StringComparison.CurrentCulture));
		var declineOption = options.Single(option => option.TextContent.Contains(
			GameStrings.DeclineOption,
			StringComparison.CurrentCulture));
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();

		declineOption.Click();

		declineOption = dashboard.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				GameStrings.DeclineOption,
				StringComparison.CurrentCulture));
		declineOption.GetAttribute(Html.Attributes.AriaSelected).Should().Be(
			Html.AriaValues.True);
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		var sleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var sleepAnnouncement = GameStrings.RoleGoesToSleepSingle.Format(
			GameStrings.WhiteWerewolfRoleName);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(whiteWerewolf.Id);
		sleep.PublicAnnouncement.Should().Be(sleepAnnouncement);
		sleep.PrivateInstruction.Should().BeNull();
		dashboard.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(sleepAnnouncement)
			.And.NotContain(whiteWerewolf.Name)
			.And.NotContain(werewolf.Name);
		dashboard.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		dashboard.FindAll(PlayerOptionSelector).Should().BeEmpty();
	}

	[Fact]
	public async Task BigBadWolfNightFlow_RendersPublicWakePrivateMandatoryTargetAndPublicSleep()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartPreparedGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.BigBadWolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();

		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var werewolf = players[0];
		var bigBadWolf = players[1];
		var collectiveVictim = players[2];
		var additionalVictim = players[3];
		var factionObservation = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		factionObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		factionObservation.RoleIdentification.Should().BeNull();
		manager.ProcessInput(
				factionObservation.CreateResponse(
					[werewolf.Id, bigBadWolf.Id]))
			.IsSuccess.Should().BeTrue();
		var victimSelection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		manager.ProcessInput(
				victimSelection.CreateResponse([collectiveVictim.Id]))
			.IsSuccess.Should().BeTrue();
		var collectiveSleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(collectiveSleep.CreateResponse())
			.IsSuccess.Should().BeTrue();

		bigBadWolf.State.ModeratorKnownRole.Should().BeNull();
		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var wakeAnnouncement = GameStrings.RoleWakesUp.Format(
			GameStrings.BigBadWolfRoleName);
		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.BigBadWolf);
		identification.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		identification.PublicAnnouncement.Should().Be(wakeAnnouncement);

		var dashboard = context.RenderModeratorComponent<DashboardPage>();
		var publicWake = dashboard.Find(PublicInstructionSelector);
		publicWake.TextContent.Should().Contain(wakeAnnouncement);
		publicWake.TextContent.Should().NotContain(bigBadWolf.Name);
		dashboard.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(identification.PrivateInstruction!);
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		var holderOption = dashboard.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				bigBadWolf.Name,
				StringComparison.CurrentCulture));
		holderOption.Click();
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		var targetSelection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		targetSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectBigBadWolfTarget);
		targetSelection.PublicAnnouncement.Should().BeNull();
		targetSelection.PrivateInstruction.Should().Be(
			GameStrings.BigBadWolfTargetSelectionInstruction);
		targetSelection.AffectedPlayerIds.Should().Equal(bigBadWolf.Id);
		targetSelection.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		targetSelection.EmptySelectionOptionLabel.Should().BeNull();
		targetSelection.SelectablePlayerIds.Should()
			.Contain(additionalVictim.Id)
			.And.NotContain(collectiveVictim.Id)
			.And.NotContain(werewolf.Id)
			.And.NotContain(bigBadWolf.Id);

		var responses = new List<ModeratorResponse>();
		var targetView =
			context.RenderModeratorComponent<InstructionRenderer>(parameters =>
				parameters
					.Add(component => component.Instruction, targetSelection)
					.Add(component => component.Roster, manager.CurrentRoster)
					.Add(
						component => component.OnResponse,
						EventCallback.Factory.Create<ModeratorResponse>(
							this,
							responses.Add)));
		targetView.FindAll(PublicInstructionSelector).Should().BeEmpty();
		targetView.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.BigBadWolfTargetSelectionInstruction);
		targetView.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		var targetOption = targetView.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				additionalVictim.Name,
				StringComparison.CurrentCulture));
		targetOption.Click();
		targetView.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();

		var canceledHoldTask = RenderedHoldButtonDriver.StartHoldAsync(
			targetView.Find(HoldButtonSelector));
		await RenderedHoldButtonDriver.FlushAsync(targetView);
		timing.AdvanceBy(TimeSpan.FromMilliseconds(200));
		await RenderedHoldButtonDriver.LeaveHoldAsync(
			targetView.Find(HoldButtonSelector));
		await canceledHoldTask;
		timing.AdvanceBy(
			RenderedHoldButtonDriver.HoldDuration +
			RenderedHoldButtonDriver.SuccessFlashDuration);
		await RenderedHoldButtonDriver.FlushAsync(targetView);
		responses.Should().BeEmpty();

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			targetView,
			targetView.Find(HoldButtonSelector),
			timing);
		var response = responses.Should().ContainSingle().Subject;
		response.Type.Should().Be(ExpectedInputType.PlayerSelection);
		response.InstructionId.Should().Be(targetSelection.InstructionId);
		response.SelectedPlayerIds.Should().Equal(additionalVictim.Id);
		manager.ProcessInput(response).IsSuccess.Should().BeTrue();

		var sleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var sleepAnnouncement = GameStrings.RoleGoesToSleepSingle.Format(
			GameStrings.BigBadWolfRoleName);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(sleepAnnouncement);
		sleep.PrivateInstruction.Should().BeNull();

		var sleepDashboard = context.RenderModeratorComponent<DashboardPage>();
		sleepDashboard.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(sleepAnnouncement)
			.And.NotContain(bigBadWolf.Name)
			.And.NotContain(additionalVictim.Name);
		sleepDashboard.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		sleepDashboard.FindAll(PlayerOptionSelector).Should().BeEmpty();
	}

	[Fact]
	public void BigBadWolfNoTargetFlow_RendersPublicSleepWithoutPrivateTargetControl()
	{
		using var context = new ModeratorComponentTestContext();
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartPreparedGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.BigBadWolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();

		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var bigBadWolf = players[1];
		var collectiveVictim = players[4];
		var factionObservation = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(factionObservation.CreateResponse(
				[
					players[0].Id,
					bigBadWolf.Id,
					players[2].Id,
					players[3].Id
				]))
			.IsSuccess.Should().BeTrue();
		var victimSelection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(
				victimSelection.CreateResponse([collectiveVictim.Id]))
			.IsSuccess.Should().BeTrue();
		var collectiveSleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(collectiveSleep.CreateResponse())
			.IsSuccess.Should().BeTrue();
		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identification.RoleIdentification.Should().Be(MainRoleType.BigBadWolf);

		manager.ProcessInput(identification.CreateResponse([bigBadWolf.Id]))
			.IsSuccess.Should().BeTrue();

		var sleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(
				GameStrings.BigBadWolfRoleName));
		var dashboard = context.RenderModeratorComponent<DashboardPage>();
		dashboard.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(sleep.PublicAnnouncement);
		dashboard.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		dashboard.FindAll(PlayerOptionSelector).Should().BeEmpty();
	}

	[Fact]
	public async Task AccursedWolfFatherNightFlow_RendersPrivateInfectionChoiceWithGenericDeliberateHoldAndPublicSleep()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartPreparedGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();

		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var werewolf = players[0];
		var accursedWolfFather = players[1];
		var victim = players[3];
		var factionObservation = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		factionObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		factionObservation.RoleIdentification.Should().BeNull();
		manager.ProcessInput(
				factionObservation.CreateResponse(
					[werewolf.Id, accursedWolfFather.Id]))
			.IsSuccess.Should().BeTrue();
		var victimSelection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		manager.ProcessInput(victimSelection.CreateResponse([victim.Id]))
			.IsSuccess.Should().BeTrue();
		var collectiveSleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(collectiveSleep.CreateResponse())
			.IsSuccess.Should().BeTrue();

		accursedWolfFather.State.ModeratorKnownRole.Should().BeNull();
		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var wakeAnnouncement = GameStrings.RoleWakesUp.Format(
			GameStrings.AccursedWolfFatherRoleName);
		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(
			MainRoleType.AccursedWolfFather);
		identification.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		identification.PublicAnnouncement.Should().Be(wakeAnnouncement);

		var dashboard = context.RenderModeratorComponent<DashboardPage>();
		var publicWake = dashboard.Find(PublicInstructionSelector);
		publicWake.TextContent.Should().Contain(wakeAnnouncement);
		publicWake.TextContent.Should().NotContain(accursedWolfFather.Name);
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		var holderOption = dashboard.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				accursedWolfFather.Name,
				StringComparison.CurrentCulture));
		holderOption.Click();
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		var infectionChoice = manager.CurrentInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var privateInstruction =
			GameStrings.AccursedWolfFatherInfectionInstruction.Format(
				victim.Name);
		infectionChoice.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseAccursedWolfFatherInfection);
		infectionChoice.PublicAnnouncement.Should().BeNull();
		infectionChoice.PrivateInstruction.Should().Be(privateInstruction);
		infectionChoice.AffectedPlayerIds.Should().Equal(
			accursedWolfFather.Id);
		infectionChoice.SelectionRange.Should().Be(NumberRangeConstraint.Single);
		infectionChoice.Options.Select(option => (option.Id, option.Label))
			.Should().Equal(
				(
					AccursedWolfFatherInfectionOptionIds.Infect,
					GameStrings.AccursedWolfFatherInfectOption),
				(
					AccursedWolfFatherInfectionOptionIds.Decline,
					GameStrings.DeclineOption));

		var responses = new List<ModeratorResponse>();
		var choiceView =
			context.RenderModeratorComponent<InstructionRenderer>(parameters =>
				parameters
					.Add(component => component.Instruction, infectionChoice)
					.Add(component => component.Roster, manager.CurrentRoster)
					.Add(
						component => component.OnResponse,
						EventCallback.Factory.Create<ModeratorResponse>(
							this,
							responses.Add)));
		choiceView.FindAll(PublicInstructionSelector).Should().BeEmpty();
		choiceView.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(privateInstruction);
		var infectOption = choiceView.FindAll(Html.Selectors.Button)
			.Single(button =>
				button.TextContent.Trim() ==
				GameStrings.AccursedWolfFatherInfectOption);
		var declineOption = choiceView.FindAll(Html.Selectors.Button)
			.Single(button =>
				button.TextContent.Trim() == GameStrings.DeclineOption);
		infectOption.GetAttribute(Html.Attributes.AriaPressed)
			.Should().Be(Html.AriaValues.False);
		declineOption.GetAttribute(Html.Attributes.AriaPressed)
			.Should().Be(Html.AriaValues.False);
		choiceView.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();

		infectOption.Click();
		infectOption = choiceView.FindAll(Html.Selectors.Button)
			.Single(button =>
				button.TextContent.Trim() ==
				GameStrings.AccursedWolfFatherInfectOption);
		infectOption.GetAttribute(Html.Attributes.AriaPressed)
			.Should().Be(Html.AriaValues.True);
		choiceView.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();

		var holdButton = choiceView.Find(HoldButtonSelector);
		var canceledHoldTask =
			RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(choiceView);
		timing.AdvanceBy(TimeSpan.FromMilliseconds(200));
		await RenderedHoldButtonDriver.LeaveHoldAsync(holdButton);
		await canceledHoldTask;
		timing.AdvanceBy(
			RenderedHoldButtonDriver.HoldDuration +
			RenderedHoldButtonDriver.SuccessFlashDuration);
		await RenderedHoldButtonDriver.FlushAsync(choiceView);
		responses.Should().BeEmpty();

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			choiceView,
			choiceView.Find(HoldButtonSelector),
			timing);
		var response = responses.Should().ContainSingle().Subject;
		response.Type.Should().Be(ExpectedInputType.OptionSelection);
		response.InstructionId.Should().Be(infectionChoice.InstructionId);
		response.SelectedOptionIds.Should().Equal(
			AccursedWolfFatherInfectionOptionIds.Infect);
		manager.ProcessInput(response).IsSuccess.Should().BeTrue();

		var sleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var sleepAnnouncement = GameStrings.RoleGoesToSleepSingle.Format(
			GameStrings.AccursedWolfFatherRoleName);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(sleepAnnouncement);
		sleep.PrivateInstruction.Should().BeNull();

		var sleepDashboard =
			context.RenderModeratorComponent<DashboardPage>();
		var publicSleep = sleepDashboard.Find(PublicInstructionSelector);
		publicSleep.TextContent.Should().Contain(sleepAnnouncement);
		publicSleep.TextContent.Should().NotContain(victim.Name);
		publicSleep.TextContent.Should().NotContain(
			GameStrings.AccursedWolfFatherInfectOption);
		publicSleep.TextContent.Should().NotContain(GameStrings.DeclineOption);
		sleepDashboard.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		sleepDashboard.FindAll(PlayerOptionSelector).Should().BeEmpty();
	}

	[Fact]
	public async Task PiperNightFlow_UsesGenericExactTwoSelectionAndCombinedRecognitionContinue()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartPreparedGame(
			Enumerable.Range(1, 6).Select(PlayerNames.GeneratedPlayer).ToArray(),
			[
				MainRoleType.Piper,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();

		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var piper = players[0];
		var firstTarget = players[2];
		var secondTarget = players[3];
		var werewolfObservation = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(werewolfObservation.CreateResponse([players[1].Id]))
			.IsSuccess.Should().BeTrue();
		var victimSelection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(victimSelection.CreateResponse([players[5].Id]))
			.IsSuccess.Should().BeTrue();
		var werewolfSleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(werewolfSleep.CreateResponse()).IsSuccess.Should().BeTrue();
		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identification.RoleIdentification.Should().Be(MainRoleType.Piper);
		manager.ProcessInput(identification.CreateResponse([piper.Id]))
			.IsSuccess.Should().BeTrue();
		var wake = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		manager.ProcessInput(wake.CreateResponse()).IsSuccess.Should().BeTrue();

		var selection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		selection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectPiperTargets);
		selection.CountConstraint.Should().Be(NumberRangeConstraint.Exact(2));
		selection.SelectablePlayerIds.Should().NotContain(piper.Id);
		selection.AffectedPlayerIds.Should().Equal(piper.Id);
		var dashboard = context.RenderModeratorComponent<DashboardPage>();
		dashboard.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.PiperTargetSelectionInstruction);
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();

		FindOption(firstTarget).Click();
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		FindOption(secondTarget).Click();
		dashboard.Find(HoldButtonSelector)
			.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		firstTarget.State.HasStatusEffect(StatusEffectTypes.Charmed).Should().BeTrue();
		secondTarget.State.HasStatusEffect(StatusEffectTypes.Charmed).Should().BeTrue();
		var sleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		var recognition = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeCharmedPlayers);
		recognition.AffectedPlayerIds.Should().Equal(
			firstTarget.Id,
			secondTarget.Id);
		dashboard.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(GameStrings.PiperCharmedRecognitionAnnouncement);
		dashboard.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.PiperLivingCharmedRosterInstruction);
		dashboard.Find(HoldButtonSelector).TextContent.Should()
			.Contain(ClientStrings.Dashboard_ContinueButton);
		var charmedRosterEntries = manager.CurrentRoster
			.Where(entry => recognition.AffectedPlayerIds!.Contains(entry.PlayerId))
			.ToArray();
		charmedRosterEntries.Should().HaveCount(2);
		charmedRosterEntries.Should().OnlyContain(entry =>
			entry.StatusEffects.Contains(ClientStrings.StatusEffect_Charmed));

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);
		manager.CurrentInstruction!.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.RecognizeCharmedPlayers);

		IElement FindOption(IPlayer player) =>
			dashboard.FindAll(PlayerOptionSelector)
				.Single(option => option.TextContent.Contains(
					player.Name,
					StringComparison.CurrentCulture));
	}

	private static IPlayer[] StartWhiteWerewolfGameAtFirstIdentification(
		GameClientManager manager)
	{
		var start = manager.StartPreparedGame(
			Enumerable.Range(1, 7).Select(PlayerNames.GeneratedPlayer).ToArray(),
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();

		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var factionObservation = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		factionObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		manager.ProcessInput(factionObservation.CreateResponse(
				[players[0].Id, players[1].Id]))
			.IsSuccess.Should().BeTrue();
		var victimSelection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		manager.ProcessInput(victimSelection.CreateResponse([players[4].Id]))
			.IsSuccess.Should().BeTrue();
		var collectiveSleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(collectiveSleep.CreateResponse())
			.IsSuccess.Should().BeTrue();
		return players;
	}

	private static void AdvanceToSecondNightStart(GameClientManager manager)
	{
		for (var step = 0; step < 30; step++)
		{
			if (manager.TurnNumber == 2 && manager.CurrentPhase == GamePhase.Night)
			{
				manager.CurrentInstruction.Should()
					.BeOfType<ConfirmationInstruction>().Which.Semantic.Should().Be(
						ModeratorInstructionSemantic.StartNight);
				return;
			}

			var result = manager.CurrentInstruction switch
			{
				SelectPlayersInstruction
					{
						Semantic: ModeratorInstructionSemantic.RecordDayVote
					} instruction =>
					manager.ProcessInput(instruction.CreateResponse([])),
				AssignRolesInstruction instruction =>
					manager.ProcessInput(instruction.CreateResponse(
						instruction.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							_ => MainRoleType.SimpleVillager))),
				ConfirmationInstruction instruction =>
					manager.ProcessInput(instruction.CreateResponse()),
				_ => throw new InvalidOperationException(
					$"Unexpected instruction while advancing the scenario to Night 2: " +
					$"{manager.CurrentInstruction?.GetType().Name}.")
			};
			result.IsSuccess.Should().BeTrue();
		}

		throw new InvalidOperationException(
			"The scenario did not reach Night 2.");
	}

	private static void AdvanceToFirstDayDebate(
		GameClientManager manager,
		Guid werewolfId,
		Guid victimId)
	{
		for (var step = 0; step < 30; step++)
		{
			if (manager.CurrentPhase == GamePhase.Day &&
				manager.CurrentInstruction is ConfirmationInstruction
				{
					Semantic: ModeratorInstructionSemantic.StartDayDebate
				})
			{
				return;
			}

			var result = manager.CurrentInstruction switch
			{
				SelectPlayersInstruction
					{
						Semantic:
							ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup
					} instruction =>
					manager.ProcessInput(instruction.CreateResponse([werewolfId])),
				SelectPlayersInstruction
					{
						Semantic:
							ModeratorInstructionSemantic.SelectWerewolfVictim
					} instruction =>
					manager.ProcessInput(instruction.CreateResponse([victimId])),
				AssignRolesInstruction instruction =>
					manager.ProcessInput(instruction.CreateResponse(
						instruction.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							_ => MainRoleType.SimpleVillager))),
				ConfirmationInstruction instruction =>
					manager.ProcessInput(instruction.CreateResponse()),
				_ => throw new InvalidOperationException(
					$"Unexpected instruction while advancing the Stuttering Judge game to Day: " +
					$"{manager.CurrentInstruction?.GetType().Name}.")
			};
			result.IsSuccess.Should().BeTrue();
		}

		throw new InvalidOperationException(
			"The Stuttering Judge game did not reach the first Day debate.");
	}
}
