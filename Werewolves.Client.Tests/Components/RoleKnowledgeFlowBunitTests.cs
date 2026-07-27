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
	private static string PlayerOptionSelector =>
		Html.Selectors.ElementWithRole(Html.Elements.ListItem, Html.Roles.Option);
	private static string PublicInstructionSelector =>
		$".{ClientTestReferences.Css.Classes.InstructionAnnouncement}";
	private static string PrivateInstructionSelector =>
		$".{ClientTestReferences.Css.Classes.InstructionPrivate}";

	[Theory]
	[InlineData(MainRoleType.VillagerVillager)]
	[InlineData(MainRoleType.Witch)]
	[InlineData(MainRoleType.Hunter)]
	[InlineData(MainRoleType.StutteringJudge)]
	[InlineData(MainRoleType.Scapegoat)]
	public void SingleOptionalRoleLobby_UsesCatalogMetadataAsPortugueseToggle(
		MainRoleType role)
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var roleInfo = lobby.GetRoleInfo(role);
		var expectedDisplayName = role switch
		{
			MainRoleType.VillagerVillager => GameStrings.VillagerVillagerRoleName,
			MainRoleType.Witch => GameStrings.WitchRoleName,
			MainRoleType.Hunter => GameStrings.HunterRoleName,
			MainRoleType.StutteringJudge => GameStrings.StutteringJudgeRoleName,
			MainRoleType.Scapegoat => GameStrings.ScapegoatRoleName,
			_ => throw new InvalidOperationException(
				$"Unexpected Single-Optional Role {role}.")
		};

		roleInfo.DisplayName.Should().Be(expectedDisplayName);
		roleInfo.Affordance.Should().Be(RoleAffordance.Toggle);
		roleInfo.BatchSize.Should().Be(1);

		var cut = context.RenderModeratorComponent<RoleSelectionPage>();
		var toggle = cut.FindAll(Html.Selectors.Button)
			.Single(button => button.GetAttribute(Html.Attributes.AriaLabel) == roleInfo.DisplayName);
		toggle.GetAttribute(Html.Attributes.AriaPressed).Should().Be(Html.AriaValues.False);

		toggle.Click();

		lobby.GetRoleCount(role).Should().Be(1);
		toggle = cut.FindAll(Html.Selectors.Button)
			.Single(button => button.GetAttribute(Html.Attributes.AriaLabel) == roleInfo.DisplayName);
		toggle.GetAttribute(Html.Attributes.AriaPressed).Should().Be(Html.AriaValues.True);

		toggle.Click();

		lobby.GetRoleCount(role).Should().Be(0);
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

	[Fact]
	public async Task StutteringJudgeSignalFlow_RendersLocalizedSetupAndObservationWithDeliberateHolds()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var start = manager.StartGame(
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
		var start = manager.StartGame(
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
		var werewolfIdentification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		werewolfIdentification.RoleIdentification.Should().Be(MainRoleType.SimpleWerewolf);
		manager.ProcessInput(werewolfIdentification.CreateResponse([werewolf.Id]))
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
						RoleIdentification: MainRoleType.SimpleWerewolf
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
