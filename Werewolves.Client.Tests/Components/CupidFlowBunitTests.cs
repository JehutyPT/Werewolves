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
using Werewolves.Core.StateModels.Extensions;
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

	[Fact]
	public async Task EngineProducedHeartbreakReveal_RehydratesAndSubmitsThroughGenericRenderer()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var names = new[]
		{
			"Werewolf",
			"Cupid",
			"Hunter lover",
			"Attacked lover",
			"Villager A",
			"Villager B",
			"Villager C"
		};
		var roles = new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.Cupid,
			MainRoleType.Hunter,
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
		var werewolf = players[0];
		var cupid = players[1];
		var hunterLover = players[2];
		var attackedLover = players[3];
		var identifyCupid = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(identifyCupid.CreateResponse([cupid.Id]))
			.IsSuccess.Should().BeTrue();
		var wakeCupid = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(wakeCupid.CreateResponse()).IsSuccess.Should().BeTrue();
		var selectLovers = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(selectLovers.CreateResponse(
				[hunterLover.Id, attackedLover.Id]))
			.IsSuccess.Should().BeTrue();
		var recognizeLovers = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(recognizeLovers.CreateResponse())
			.IsSuccess.Should().BeTrue();
		var sleepLovers = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(sleepLovers.CreateResponse()).IsSuccess.Should()
			.BeTrue();
		var observeWerewolves = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		observeWerewolves.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		manager.ProcessInput(observeWerewolves.CreateResponse([werewolf.Id]))
			.IsSuccess.Should().BeTrue();
		var selectVictim = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		selectVictim.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		manager.ProcessInput(selectVictim.CreateResponse([attackedLover.Id]))
			.IsSuccess.Should().BeTrue();
		var sleepWerewolves = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(sleepWerewolves.CreateResponse()).IsSuccess.Should()
			.BeTrue();
		var finishNight = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		manager.ProcessInput(finishNight.CreateResponse()).IsSuccess.Should()
			.BeTrue();
		var attackedReveal = manager.CurrentInstruction
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		manager.ProcessInput(attackedReveal.CreateResponse(new()
			{
				[attackedLover.Id] = MainRoleType.SimpleVillager
			}))
			.IsSuccess.Should().BeTrue();
		var expectedHeartbreakReveal = manager.CurrentInstruction
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		var recoveredManager = new GameClientManager(
			new GameService(),
			saveStore: context.Services
				.GetRequiredService<IGameSessionSaveStore>());

		var heartbreakReveal = recoveredManager.CurrentInstruction
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		heartbreakReveal.Should().BeEquivalentTo(expectedHeartbreakReveal);
		heartbreakReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignEliminationCascadeRoles);
		heartbreakReveal.PlayersForAssignment.Should().Equal(hunterLover.Id);
		heartbreakReveal.RolesForAssignment.Should().Contain(
			MainRoleType.Hunter);
		var responses = new List<ModeratorResponse>();
		var cut = RenderInstruction(
			context,
			heartbreakReveal,
			recoveredManager.CurrentRoster,
			responses);

		var publicText = cut.Find(PublicInstructionSelector).TextContent;
		publicText.Should().Contain(hunterLover.Name);
		names.Where(name => name != hunterLover.Name).Should().OnlyContain(
			name => !publicText.Contains(
				name,
				StringComparison.CurrentCulture));
		cut.Find(PrivateInstructionSelector).TextContent.Should().Contain(
			GameStrings.PublicRoleRevealInstruction);
		cut.FindAll("[role='group']").Should().ContainSingle(group =>
			group.GetAttribute(Html.Attributes.AriaLabel) ==
				ClientStrings.AssignRoles_Title &&
			group.TextContent.Contains(
				hunterLover.Name,
				StringComparison.CurrentCulture));
		var hunterRoleButton = FindButtonByText(
			cut,
			MainRoleType.Hunter.GetPublicName());
		hunterRoleButton.GetAttribute(Html.Attributes.AriaPressed).Should().Be(
			Html.AriaValues.False);
		await AttemptDisabledHoldAsync(cut, timing);
		responses.Should().BeEmpty();

		hunterRoleButton.Click();

		FindButtonByText(cut, MainRoleType.Hunter.GetPublicName())
			.GetAttribute(Html.Attributes.AriaPressed).Should().Be(
				Html.AriaValues.True);
		FindHoldButton(cut).HasAttribute(Html.Attributes.Disabled).Should()
			.BeFalse();
		await CancelHoldAsync(cut, timing);
		responses.Should().BeEmpty();

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			FindHoldButton(cut),
			timing);

		responses.Should().ContainSingle();
		var response = responses.Single();
		response.InstructionId.Should().Be(heartbreakReveal.InstructionId);
		response.Type.Should().Be(ExpectedInputType.AssignPlayerRoles);
		response.AssignedPlayerRoles.Should().BeEquivalentTo(
			new Dictionary<Guid, MainRoleType>
			{
				[hunterLover.Id] = MainRoleType.Hunter
			});
		recoveredManager.ProcessInput(response).IsSuccess.Should().BeTrue();
		var revealedHunter = recoveredManager.CurrentRoster.Single(
			entry => entry.PlayerId == hunterLover.Id);
		revealedHunter.IsDead.Should().BeTrue();
		revealedHunter.RoleVisibility.Should().Be(
			DashboardRoleVisibility.Public);
		revealedHunter.RoleLabel.Should().Be(
			MainRoleType.Hunter.GetPublicName());
		recoveredManager.CurrentInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Which.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
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

	private static IElement FindButtonByText(
		IRenderedComponent<InstructionRenderer> cut,
		string text) =>
		cut.FindAll(Html.Selectors.Button)
			.Single(button => button.TextContent.Contains(
				text,
				StringComparison.CurrentCulture));

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
