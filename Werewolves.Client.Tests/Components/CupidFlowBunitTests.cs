using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;

namespace Werewolves.Client.Tests.Components;

public sealed class CupidFlowBunitTests
{
	private static string PublicInstructionSelector =>
		$".{ClientTestReferences.Css.Classes.InstructionAnnouncement}";

	private static string PrivateInstructionSelector =>
		$".{ClientTestReferences.Css.Classes.InstructionPrivate}";

	private static string HoldButtonSelector =>
		Html.Selectors.ButtonWithClass(
			ClientTestReferences.Css.Classes.HoldButton);

	private static string PlayerOptionSelector =>
		Html.Selectors.ElementWithRole(
			Html.Elements.ListItem,
			Html.Roles.Option);

	[Fact]
	public async Task NightOne_RendersExactTwoPrivateSelectionRecognitionAndSeparateSleep()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var names = new[]
		{
			"Werewolf",
			"Cupid",
			"Lover A",
			"Lover B",
			"Villager A",
			"Villager B",
			"Villager C"
		};
		var roles = new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.Cupid,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};
		var start = manager.StartGame(names, roles);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var startNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(startNight.CreateResponse()).IsSuccess.Should().BeTrue();
		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var identifyCupid = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identifyCupid.RoleIdentification.Should().Be(MainRoleType.Cupid);
		manager.ProcessInput(identifyCupid.CreateResponse([players[1].Id]))
			.IsSuccess.Should().BeTrue();
		var wake = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(wake.CreateResponse()).IsSuccess.Should().BeTrue();
		var selection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var responses = new List<ModeratorResponse>();
		var selectionCut = RenderInstruction(
			context,
			selection,
			manager.CurrentRoster,
			responses);

		selection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectCupidLovers);
		selection.CountConstraint.Should().Be(NumberRangeConstraint.Exact(2));
		selectionCut.FindAll(PublicInstructionSelector).Should().BeEmpty();
		selectionCut.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.CupidTargetSelectionInstruction);
		selectionCut.FindAll(PlayerOptionSelector).Should().HaveCount(names.Length);
		FindHoldButton(selectionCut)
			.HasAttribute(Html.Attributes.Disabled)
			.Should().BeTrue();
		await AttemptDisabledHoldAsync(selectionCut, timing);
		responses.Should().BeEmpty();
		FindPlayerOption(selectionCut, names[2]).Click();
		FindHoldButton(selectionCut)
			.HasAttribute(Html.Attributes.Disabled)
			.Should().BeTrue();
		await AttemptDisabledHoldAsync(selectionCut, timing);
		responses.Should().BeEmpty();
		FindPlayerOption(selectionCut, names[3]).Click();
		FindHoldButton(selectionCut)
			.HasAttribute(Html.Attributes.Disabled)
			.Should().BeFalse();
		await CancelHoldAsync(selectionCut, timing);
		responses.Should().BeEmpty();

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			selectionCut,
			FindHoldButton(selectionCut),
			timing);

		responses.Should().ContainSingle();
		responses.Single().SelectedPlayerIds.Should().BeEquivalentTo(
			[players[2].Id, players[3].Id]);
		manager.ProcessInput(responses.Single()).IsSuccess.Should().BeTrue();
		var expectedRecognition = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var recoveredManager = new GameClientManager(
			new GameService(),
			saveStore: context.Services
				.GetRequiredService<IGameSessionSaveStore>());
		var recognition = recoveredManager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recognition.Should().BeEquivalentTo(expectedRecognition);
		var recognitionResponses = new List<ModeratorResponse>();
		var recognitionCut = RenderInstruction(
			context,
			recognition,
			recoveredManager.CurrentRoster,
			recognitionResponses);

		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeLovers);
		recognition.AffectedPlayerIds.Should().BeEquivalentTo(
			[players[2].Id, players[3].Id]);
		recognitionCut.FindAll(PublicInstructionSelector).Should().BeEmpty();
		recognitionCut.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.LoversRecognitionInstruction);
		recoveredManager.CurrentRoster
			.Where(entry => recognition.AffectedPlayerIds!.Contains(entry.PlayerId))
			.Should().OnlyContain(entry =>
				entry.StatusEffects.Contains(ClientStrings.StatusEffect_Lovers));

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			recognitionCut,
			FindHoldButton(recognitionCut),
			timing);

		recognitionResponses.Should().ContainSingle();
		recoveredManager.ProcessInput(recognitionResponses.Single())
			.IsSuccess.Should().BeTrue();
		var sleep = recoveredManager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var sleepCut = RenderInstruction(
			context,
			sleep,
			recoveredManager.CurrentRoster,
			[]);

		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().BeEquivalentTo(
			[players[2].Id, players[3].Id]);
		var publicSleepAnnouncement =
			sleepCut.Find(PublicInstructionSelector).TextContent;
		publicSleepAnnouncement.Should().Contain(
			GameStrings.LoversSleepAnnouncement);
		publicSleepAnnouncement.Should().NotContain(names[2]);
		publicSleepAnnouncement.Should().NotContain(names[3]);
		sleepCut.FindAll(PrivateInstructionSelector).Should().BeEmpty();
	}

	private static IRenderedComponent<InstructionRenderer> RenderInstruction(
		ModeratorComponentTestContext context,
		ModeratorInstruction instruction,
		IReadOnlyList<DashboardRosterEntry> roster,
		ICollection<ModeratorResponse> responses) =>
		context.RenderModeratorComponent<InstructionRenderer>(
			parameters => parameters
				.Add(component => component.Instruction, instruction)
				.Add(component => component.Roster, roster)
				.Add(
					component => component.OnResponse,
					EventCallback.Factory.Create<ModeratorResponse>(
						new object(),
						responses.Add)));

	private static IElement FindPlayerOption(
		IRenderedComponent<InstructionRenderer> cut,
		string text) =>
		cut.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				text,
				StringComparison.CurrentCulture));

	private static IElement FindHoldButton(
		IRenderedComponent<InstructionRenderer> cut) =>
		cut.Find(HoldButtonSelector);

	private static async Task AttemptDisabledHoldAsync(
		IRenderedComponent<InstructionRenderer> cut,
		ControlledHoldButtonTiming timing)
	{
		var holdButton = FindHoldButton(cut);
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();

		var holdTask = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		timing.AdvanceBy(
			RenderedHoldButtonDriver.HoldDuration +
			RenderedHoldButtonDriver.SuccessFlashDuration);
		await holdTask;
		await RenderedHoldButtonDriver.FlushAsync(cut);
	}

	private static async Task CancelHoldAsync(
		IRenderedComponent<InstructionRenderer> cut,
		ControlledHoldButtonTiming timing)
	{
		var holdButton = FindHoldButton(cut);
		var holdTask = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(TimeSpan.FromTicks(
			RenderedHoldButtonDriver.HoldDuration.Ticks / 2));
		await RenderedHoldButtonDriver.FlushAsync(cut);

		await RenderedHoldButtonDriver.LeaveHoldAsync(holdButton);
		await holdTask;
		timing.AdvanceBy(
			RenderedHoldButtonDriver.HoldDuration +
			RenderedHoldButtonDriver.SuccessFlashDuration);
		await RenderedHoldButtonDriver.FlushAsync(cut);
	}
}
