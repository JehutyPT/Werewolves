using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class LobbyEvaluationPanelTests
{
	[Fact]
	public void AsyncStateTransitions_ExposeOneConcisePoliteAtomicStatusAnnouncement()
	{
		using var context = new ModeratorComponentTestContext();
		var identity = CreateIdentity();
		var cut = context.RenderModeratorComponent<LobbyEvaluationPanel>(parameters => parameters
			.Add(component => component.State, LobbyEvaluationState.Pending(identity)));

		var status = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationAsyncStatus));
		status.GetAttribute("role").Should().Be("status");
		status.GetAttribute("aria-live").Should().Be("polite");
		status.GetAttribute("aria-atomic").Should().Be("true");
		status.TextContent.Should().Be(ClientStrings.LobbyEvaluation_Pending);

		cut.Render(parameters => parameters
			.Add(component => component.State, CreateProbabilityState(identity)));

		status = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationAsyncStatus));
		status.TextContent.Should().Be(ClientStrings.LobbyEvaluation_Probability);
		status.TextContent.Should().NotContain(ClientStrings.LobbyEvaluation_DetailToggle);
		cut.FindAll("[aria-live]").Should().ContainSingle()
			.Which.GetAttribute("data-testid")
			.Should().Be(ModeratorUiTestIds.LobbyEvaluationAsyncStatus);

		cut.Render(parameters => parameters
			.Add(component => component.State, LobbyEvaluationState.CouldNotEvaluate(identity)));

		status = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationAsyncStatus));
		status.TextContent.Should().Be(ClientStrings.LobbyEvaluation_CouldNotEvaluate);
		cut.FindAll("[aria-live]").Should().ContainSingle();
	}

	[Fact]
	public void Pending_RendersCompactLocalizedProgressWithoutBypassActions()
	{
		using var context = new ModeratorComponentTestContext();
		var cut = context.RenderModeratorComponent<LobbyEvaluationPanel>(parameters => parameters
			.Add(component => component.State, LobbyEvaluationState.Pending(CreateIdentity())));

		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationPanel))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_Pending);
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationRetry)).Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationDisclosure)).Should().BeEmpty();
	}

	[Fact]
	public void AlreadyDecided_NamesAndExplainsTheTerminalGameResultWithoutBypass()
	{
		using var context = new ModeratorComponentTestContext();
		var identity = CreateIdentity(villagers: 2, werewolves: 3);
		var gameResult = new SingleFactionGameResult(Faction.Werewolf);
		var cut = context.RenderModeratorComponent<LobbyEvaluationPanel>(parameters => parameters
			.Add(component => component.State, LobbyEvaluationState.AlreadyDecided(
				identity,
				gameResult,
				AlreadyDecidedReason.WerewolfControlShortcut)));

		var summary = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary));
		summary.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_AlreadyDecided);
		summary.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_FactionWerewolf);
		summary.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_ReasonWerewolfControl);
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationRetry)).Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationDisclosure)).Should().BeEmpty();
	}

	[Fact]
	public void Degenerate_ExplainsThatEveryBaselineGameEndedDuringTurnOne()
	{
		using var context = new ModeratorComponentTestContext();
		var cut = context.RenderModeratorComponent<LobbyEvaluationPanel>(parameters => parameters
			.Add(component => component.State, LobbyEvaluationState.Degenerate(
				CreateIdentity())));

		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_Degenerate);
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationRetry)).Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationDisclosure)).Should().BeEmpty();
	}

	[Fact]
	public void Probability_KeepsCompleteSummaryVisibleWhileAccessibleTurnDetailTogglesInline()
	{
		using var context = new ModeratorComponentTestContext();
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var identity = CreateIdentity();
		var state = LobbyEvaluationState.ProbabilityResult(
			identity,
			new LobbyProbabilityData(
			[
				new LobbyProbabilityOutcomeData(villager, 0, 10_000, []),
				new LobbyProbabilityOutcomeData(
					werewolf,
					1,
					10_000,
					[new LobbyProbabilityTurnData(3, 1, 10_000)]),
				new LobbyProbabilityOutcomeData(
					noWinner,
					9_999,
					10_000,
					[new LobbyProbabilityTurnData(1, 9_999, 10_000)])
			]));
		var cut = context.RenderModeratorComponent<LobbyEvaluationPanel>(parameters => parameters
			.Add(component => component.State, state));

		var summary = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary));
		summary.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_FactionVillager);
		summary.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_NotObserved);
		summary.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_FactionWerewolf);
		summary.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_LessThanOnePercent);
		summary.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_GameResultNoWinner);
		var disclosure = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationDisclosure));
		disclosure.GetAttribute("aria-label").Should().Be(ClientStrings.LobbyEvaluation_DetailToggle);
		disclosure.GetAttribute("aria-expanded").Should().Be("false");
		var detailId = disclosure.GetAttribute("aria-controls");
		detailId.Should().NotBeNullOrWhiteSpace();
		var detail = cut.Find($"#{detailId}");
		detail.GetAttribute("data-testid").Should().Be(ModeratorUiTestIds.LobbyEvaluationDetail);
		detail.HasAttribute("hidden").Should().BeTrue();

		disclosure.Click();

		disclosure = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationDisclosure));
		disclosure.GetAttribute("aria-label").Should().Be(ClientStrings.LobbyEvaluation_DetailToggle);
		disclosure.GetAttribute("aria-expanded").Should().Be("true");
		detail = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationDetail));
		detail.Id.Should().Be(detailId);
		detail.HasAttribute("hidden").Should().BeFalse();
		detail.QuerySelector("table").Should().BeNull();
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationTurnEntry)).Should().HaveCount(2);
		detail.TextContent.Should().Contain(Format(ClientStrings.LobbyEvaluation_TurnFormat, 1));
		detail.TextContent.Should().Contain(Format(ClientStrings.LobbyEvaluation_TurnFormat, 3));
		detail.TextContent.Should().NotContain(nameof(VictoryCheckWindow.Dawn));
		detail.TextContent.Should().NotContain(nameof(VictoryCheckWindow.PreNight));
		summary.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_NotObserved);
		var renderedEvaluation = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationPanel)).TextContent;
		renderedEvaluation.Should().NotContain("PMF");
		renderedEvaluation.Should().NotContain("CDF");
		renderedEvaluation.Should().NotContain("Reference Turn Horizon");
		renderedEvaluation.Should().NotContain(identity.ToString());
		renderedEvaluation.Should().NotContain(10_000.ToString(CultureInfo.InvariantCulture));
	}

	[Fact]
	public void ProbabilityDetail_LeavesWithItsIdentityAndEachNewIdentityStartsCollapsed()
	{
		using var context = new ModeratorComponentTestContext();
		var firstState = CreateProbabilityState(CreateIdentity());
		var secondIdentity = CreateIdentity(villagers: 4, werewolves: 2);
		var secondState = CreateProbabilityState(secondIdentity);
		var cut = context.RenderModeratorComponent<LobbyEvaluationPanel>(parameters => parameters
			.Add(component => component.State, firstState));

		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationDisclosure)).Click();
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationDetail)).Should().ContainSingle();

		cut.Render(parameters => parameters
			.Add(component => component.State, LobbyEvaluationState.Pending(secondIdentity)));

		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationDetail)).Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationDisclosure)).Should().BeEmpty();

		cut.Render(parameters => parameters
			.Add(component => component.State, secondState));

		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationDisclosure))
			.GetAttribute("aria-expanded").Should().Be("false");
		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationDetail))
			.HasAttribute("hidden").Should().BeTrue();
	}

	[Fact]
	public void CouldNotEvaluate_ShowsOnlyTheLocalizedCurrentFailureRetryAction()
	{
		using var context = new ModeratorComponentTestContext();
		var retries = 0;
		var cut = context.RenderModeratorComponent<LobbyEvaluationPanel>(parameters => parameters
			.Add(component => component.State, LobbyEvaluationState.CouldNotEvaluate(CreateIdentity()))
			.Add(component => component.OnRetry, EventCallback.Factory.Create(this, () => retries++)));

		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_CouldNotEvaluate);
		var retry = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationRetry));
		retry.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_Retry);
		retry.HasAttribute("disabled").Should().BeFalse();

		retry.Click();

		retries.Should().Be(1);
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationDisclosure)).Should().BeEmpty();
	}

	[Fact]
	public void SimulatorUnavailable_MakesUnavailabilityVisibleWithoutActions()
	{
		using var context = new ModeratorComponentTestContext();
		var cut = context.RenderModeratorComponent<LobbyEvaluationPanel>(parameters => parameters
			.Add(component => component.State, LobbyEvaluationState.SimulatorUnavailable()));

		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_SimulatorUnavailable);
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationRetry)).Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationDisclosure)).Should().BeEmpty();
	}

	[Fact]
	public void NotApplicable_RendersNoEvaluationPanel()
	{
		using var context = new ModeratorComponentTestContext();
		var cut = context.RenderModeratorComponent<LobbyEvaluationPanel>(parameters => parameters
			.Add(component => component.State, LobbyEvaluationState.NotApplicable()));

		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationPanel)).Should().BeEmpty();
	}

	[Fact]
	public void ScreeningPassed_RendersNoEvaluationPanel()
	{
		using var context = new ModeratorComponentTestContext();
		var cut = context.RenderModeratorComponent<LobbyEvaluationPanel>(parameters => parameters
			.Add(component => component.State, LobbyEvaluationState.ScreeningPassed(CreateIdentity())));

		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationPanel)).Should().BeEmpty();
	}

	private static SimulationCompatibilityIdentity CreateIdentity(
		int villagers = 3,
		int werewolves = 2)
	{
		var scenario = new SimulationScenario(
			villagers + werewolves,
			Enumerable.Repeat(MainRoleType.SimpleVillager, villagers)
				.Concat(Enumerable.Repeat(MainRoleType.SimpleWerewolf, werewolves)));
		return new(scenario.ToCanonical(), SimulatorProfile.Active.Identity);
	}

	private static LobbyEvaluationState CreateProbabilityState(
		SimulationCompatibilityIdentity identity)
	{
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		return LobbyEvaluationState.ProbabilityResult(
			identity,
			new LobbyProbabilityData(
			[
				new LobbyProbabilityOutcomeData(
					villager,
					7_000,
					10_000,
					[new LobbyProbabilityTurnData(1, 7_000, 10_000)]),
				new LobbyProbabilityOutcomeData(
					werewolf,
					3_000,
					10_000,
					[new LobbyProbabilityTurnData(2, 3_000, 10_000)]),
				new LobbyProbabilityOutcomeData(noWinner, 0, 10_000, [])
			]));
	}

	private static string TestId(string value) => $"[data-testid='{value}']";

	private static string Format(string format, params object[] args) =>
		string.Format(ModeratorComponentTestContext.PortugueseCulture, format, args);
}
