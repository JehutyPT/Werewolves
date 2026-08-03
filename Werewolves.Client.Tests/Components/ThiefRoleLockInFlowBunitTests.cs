using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public sealed class ThiefRoleLockInFlowBunitTests
{
	[Fact]
	public void ProductionRoute_IncompleteOrInvalidOffersStayInlineAndCommitNothing()
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedThiefLobby(lobby);
		var cut = context.RenderModeratorComponent<Routes>();
		cut.FindAll("button")
			.Single(button => button.TextContent.Contains(
				ClientStrings.LobbyRoster_ContinueToRolesButton))
			.Click();
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionConfigureRoleLockIn)).Click();

		cut.Find(TestId(ModeratorUiTestIds.ThiefRoleLockInCommit)).Click();

		cut.Find("[role='alert']").TextContent.Should().Contain(
			ClientStrings.ThiefRoleLockIn_IncompleteValidation);
		lobby.AcceptedRoleLockIn.Should().BeNull();

		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer1Options, MainRoleType.Seer);
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer2Options, MainRoleType.SimpleWerewolf);
		cut.Find(TestId(ModeratorUiTestIds.ThiefRoleLockInCommit)).Click();

		cut.Find("[role='alert']").TextContent.Should().Contain(
			ClientStrings.ThiefRoleLockIn_SaveFailedValidation);
		lobby.AcceptedRoleLockIn.Should().BeNull();
		cut.FindAll(TestId(ModeratorUiTestIds.ThiefRoleLockInPage)).Should().ContainSingle();
	}

	[Fact]
	public void ProductionRoute_AuthorsReviewsReplacesAndStartsPrintedRoleLockIn()
	{
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton(sp => new LobbyEvaluationCoordinator(
			sp.GetRequiredService<LobbySetupState>(),
			sp.GetRequiredService<ILocalTerminalLobbyCacheStore>(),
			sp.GetRequiredService<ILobbyTerminalEvaluator>(),
			new LobbyEvaluationSettings(
				SimulatorCapability.SafetyScreening,
				LobbyEvaluationDepth.DegenerateScreeningOnly),
			sp.GetRequiredService<TimeProvider>()));
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedPlayers(lobby);
		var cut = context.RenderModeratorComponent<Routes>();
		cut.FindAll("button")
			.Single(button => button.TextContent.Contains(
				ClientStrings.LobbyRoster_ContinueToRolesButton))
			.Click();
		ClickToggle(cut, MainRoleType.Thief);
		ClickRoleAdd(cut, MainRoleType.SimpleVillager, count: 2);
		ClickRoleAdd(cut, MainRoleType.SimpleWerewolf);
		ClickRoleAdd(cut, MainRoleType.Seer);
		ClickRoleAdd(cut, MainRoleType.Witch);
		ClickRoleAdd(cut, MainRoleType.Hunter);

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionConfigureRoleLockIn)).Click();
		cut.Find(TestId(ModeratorUiTestIds.ThiefRoleLockInPage)).TextContent
			.Should().Contain(ClientStrings.ThiefRoleLockIn_Title);
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer1Options, MainRoleType.SimpleVillager);
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer2Options, MainRoleType.SimpleVillager);
		cut.Find(TestId(ModeratorUiTestIds.ThiefRoleLockInCommit)).Click();

		cut.Find(TestId(ModeratorUiTestIds.ThiefRoleLockInSummary)).TextContent
			.Should().Contain(ClientStrings.ThiefRoleLockIn_SummaryTitle);
		lobby.AcceptedRoleLockIn.Should().NotBeNull();
		var first = lobby.AcceptedRoleLockIn!;
		first.Version.Should().Be(1);
		first.Offer1!.PrintedRole.Should().Be(MainRoleType.SimpleVillager);
		first.Offer2!.PrintedRole.Should().Be(MainRoleType.SimpleVillager);
		first.Offer1.Id.Should().NotBe(first.Offer2.Id);
		cut.Markup.Should().NotContain(first.Offer1.Id.ToString());
		cut.Markup.Should().NotContain(first.Offer2.Id.ToString());

		OpenRoleLockInReview(cut);
		cut.Find(TestId(ModeratorUiTestIds.ThiefRoleLockInBack)).Click();
		lobby.AcceptedRoleLockIn.Should().BeSameAs(first);
		OpenRoleLockInReview(cut);
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer1Options, MainRoleType.Seer);
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer2Options, MainRoleType.Witch);
		cut.Find(TestId(ModeratorUiTestIds.ThiefRoleLockInCommit)).Click();

		lobby.AcceptedRoleLockIn.Should().NotBeNull();
		var replacement = lobby.AcceptedRoleLockIn!;
		replacement.Version.Should().Be(2);
		replacement.Offer1!.PrintedRole.Should().Be(MainRoleType.Seer);
		replacement.Offer2!.PrintedRole.Should().Be(MainRoleType.Witch);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		cut.WaitForAssertion(() => context.Services
			.GetRequiredService<LobbyEvaluationCoordinator>()
			.State.Kind.Should().Be(LobbyEvaluationStateKind.CouldNotEvaluate));
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		cut.WaitForAssertion(() =>
		{
			var manager = context.Services.GetRequiredService<GameClientManager>();
			manager.HasActiveSession.Should().BeTrue();
			manager.CurrentSession!.RoleLockIn.RoleComposition.Select(card => card.Id)
				.Should().Equal(replacement.RoleComposition.Select(card => card.Id));
			cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().ContainSingle();
		});
	}

	private static void SeedThiefLobby(LobbySetupState lobby)
	{
		SeedPlayers(lobby);

		foreach (var role in new[]
		{
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.Hunter
		})
		{
			lobby.IncrementRole(role);
		}
	}

	private static void SeedPlayers(LobbySetupState lobby)
	{
		foreach (var name in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			lobby.AddPlayer(name);
		}
	}

	private static void ClickToggle(
		IRenderedComponent<Routes> cut,
		MainRoleType role)
	{
		cut.FindAll("button")
			.Single(button => button.GetAttribute("aria-label") == role.GetPublicName())
			.Click();
	}

	private static void ClickRoleAdd(
		IRenderedComponent<Routes> cut,
		MainRoleType role,
		int count = 1)
	{
		var ariaLabel = string.Format(
			ClientStrings.RoleSelection_AddRoleAriaFormat,
			role.GetPublicName());
		for (var index = 0; index < count; index++)
		{
			cut.FindAll("button")
				.Single(button => button.GetAttribute("aria-label") == ariaLabel)
				.Click();
		}
	}

	private static void ClickOffer(
		IRenderedComponent<Routes> cut,
		string groupTestId,
		MainRoleType role)
	{
		cut.Find(TestId(groupTestId))
			.QuerySelectorAll("button")
			.Single(button => button.TextContent.Trim() == role.GetPublicName())
			.Click();
	}

	private static void OpenRoleLockInReview(IRenderedComponent<Routes> cut)
	{
		cut.Find(TestId(ModeratorUiTestIds.ThiefRoleLockInReview)).Click();
		cut.WaitForAssertion(() =>
			cut.FindAll(TestId(ModeratorUiTestIds.ThiefRoleLockInPage))
				.Should().ContainSingle());
	}

	private static string TestId(string value) => $"[data-testid='{value}']";
}
