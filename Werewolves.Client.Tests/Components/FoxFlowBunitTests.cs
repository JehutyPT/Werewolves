using Bunit;
using FluentAssertions;
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

public sealed class FoxFlowBunitTests
{
	private static string HoldButtonSelector =>
		Html.Selectors.ButtonWithClass(
			ClientTestReferences.Css.Classes.HoldButton);

	private static string PlayerOptionSelector =>
		Html.Selectors.ElementWithRole(
			Html.Elements.ListItem,
			Html.Roles.Option);

	private static string PublicInstructionSelector =>
		$".{ClientTestReferences.Css.Classes.InstructionAnnouncement}";

	private static string PrivateInstructionSelector =>
		$".{ClientTestReferences.Css.Classes.InstructionPrivate}";

	[Theory]
	[InlineData(4, true, false)]
	[InlineData(3, false, true)]
	public async Task PerformedCheck_RendersLivingSelectionCanceledHoldPrivateResultAndPublicSleep(
		int centerIndex,
		bool isAffirmative,
		bool cancelSelectionHold)
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var players = AdvanceToFoxWake(manager);
		var fox = players[1];
		var center = players[centerIndex];
		var wake = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var wakeAnnouncement = GameStrings.RoleWakesUp.Format(
			GameStrings.FoxRoleName);
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(wakeAnnouncement);
		wake.PrivateInstruction.Should().BeNull();
		wake.AffectedPlayerIds.Should().Equal(fox.Id);

		var dashboard = context.RenderModeratorComponent<DashboardPage>();
		dashboard.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(wakeAnnouncement);
		dashboard.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		var selection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		selection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectFoxCenter);
		selection.PublicAnnouncement.Should().BeNull();
		selection.PrivateInstruction.Should().Be(
			GameStrings.FoxCenterSelectionInstruction);
		selection.CountConstraint.Should().Be(
			NumberRangeConstraint.SingleOptional);
		selection.SelectablePlayerIds.Should().BeEquivalentTo(
			players.Select(player => player.Id));
		selection.EmptySelectionOptionLabel.Should().Be(
			GameStrings.DeclineOption);
		dashboard.FindAll(PublicInstructionSelector).Should().BeEmpty();
		dashboard.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.FoxCenterSelectionInstruction);
		var options = dashboard.FindAll(PlayerOptionSelector);
		options.Should().HaveCount(players.Length + 1);
		options.Should().ContainSingle(option =>
			option.TextContent.Contains(
				GameStrings.DeclineOption,
				StringComparison.CurrentCulture));
		var holdButton = dashboard.Find(HoldButtonSelector);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		options.Single(option =>
				option.TextContent.Contains(
					center.Name,
					StringComparison.CurrentCulture))
			.Click();
		holdButton = dashboard.Find(HoldButtonSelector);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();

		if (cancelSelectionHold)
		{
			var pendingInstructionId = selection.InstructionId;
			var canceledHold =
				RenderedHoldButtonDriver.StartHoldAsync(holdButton);
			await RenderedHoldButtonDriver.FlushAsync(dashboard);
			timing.AdvanceBy(TimeSpan.FromMilliseconds(200));
			await RenderedHoldButtonDriver.LeaveHoldAsync(holdButton);
			await canceledHold;
			timing.AdvanceBy(RenderedHoldButtonDriver.HoldDuration);
			await RenderedHoldButtonDriver.FlushAsync(dashboard);

			manager.CurrentInstruction!.InstructionId.Should().Be(
				pendingInstructionId);
			manager.CurrentInstruction.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectFoxCenter);
		}

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		var feedback = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var expectedFeedback = isAffirmative
			? GameStrings.FoxAffirmativeFeedbackInstruction
			: GameStrings.FoxNegativeFeedbackInstruction;
		var otherFeedback = isAffirmative
			? GameStrings.FoxNegativeFeedbackInstruction
			: GameStrings.FoxAffirmativeFeedbackInstruction;
		feedback.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealFoxResult);
		feedback.PublicAnnouncement.Should().BeNull();
		feedback.PrivateInstruction.Should().Be(expectedFeedback);
		feedback.AffectedPlayerIds.Should().Equal(fox.Id);
		dashboard.FindAll(PublicInstructionSelector).Should().BeEmpty();
		var renderedFeedback =
			dashboard.Find(PrivateInstructionSelector).TextContent;
		renderedFeedback.Should().Contain(expectedFeedback)
			.And.NotContain(otherFeedback);
		foreach (var player in players)
		{
			renderedFeedback.Should().NotContain(player.Name);
		}

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);

		var sleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var sleepAnnouncement = GameStrings.RoleGoesToSleepSingle.Format(
			GameStrings.FoxRoleName);
		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(sleepAnnouncement);
		sleep.PrivateInstruction.Should().BeNull();
		dashboard.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(sleepAnnouncement);
		dashboard.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		dashboard.FindAll(PlayerOptionSelector).Should().BeEmpty();

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			dashboard.Find(HoldButtonSelector),
			timing);
		manager.CurrentInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
	}

	[Fact]
	public async Task Decline_RequiresExplicitRenderedChoiceAndSkipsPrivateFeedback()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		AdvanceToFoxWake(manager);
		var wake = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(wake.CreateResponse()).IsSuccess.Should().BeTrue();
		manager.CurrentInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectFoxCenter);

		var dashboard = context.RenderModeratorComponent<DashboardPage>();
		var holdButton = dashboard.Find(HoldButtonSelector);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		var decline = dashboard.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				GameStrings.DeclineOption,
				StringComparison.CurrentCulture));

		decline.Click();

		decline = dashboard.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				GameStrings.DeclineOption,
				StringComparison.CurrentCulture));
		decline.GetAttribute(Html.Attributes.AriaSelected).Should().Be(
			Html.AriaValues.True);
		holdButton = dashboard.Find(HoldButtonSelector);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			dashboard,
			holdButton,
			timing);

		var sleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(
				GameStrings.FoxRoleName));
		dashboard.FindAll(PrivateInstructionSelector).Should().BeEmpty();
		dashboard.FindAll(PlayerOptionSelector).Should().BeEmpty();
	}

	private static IPlayer[] AdvanceToFoxWake(GameClientManager manager)
	{
		var start = manager.StartPreparedGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Fox,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should()
			.BeTrue();
		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var werewolf = players[0];
		var fox = players[1];
		var victim = players[2];
		var collectiveObservation = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		collectiveObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		manager.ProcessInput(
				collectiveObservation.CreateResponse([werewolf.Id]))
			.IsSuccess.Should().BeTrue();
		var victimSelection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		manager.ProcessInput(victimSelection.CreateResponse([victim.Id]))
			.IsSuccess.Should().BeTrue();
		var collectiveSleep = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		collectiveSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		manager.ProcessInput(collectiveSleep.CreateResponse()).IsSuccess
			.Should().BeTrue();
		var identification = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.Fox);
		manager.ProcessInput(identification.CreateResponse([fox.Id]))
			.IsSuccess.Should().BeTrue();
		return players;
	}
}
