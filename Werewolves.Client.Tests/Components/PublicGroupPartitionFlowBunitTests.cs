using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public sealed class PublicGroupPartitionFlowBunitTests
{
	private static string HoldButtonSelector =>
		ClientTestReferences.Html.Selectors.ButtonWithClass(
			ClientTestReferences.Css.Classes.HoldButton);
	private static string PlayerOptionSelector =>
		ClientTestReferences.Html.Selectors.ElementWithRole(
			ClientTestReferences.Html.Elements.ListItem,
			ClientTestReferences.Html.Roles.Option);
	private static string PublicInstructionSelector =>
		$".{ClientTestReferences.Css.Classes.InstructionAnnouncement}";
	private static string PrivateInstructionSelector =>
		$".{ClientTestReferences.Css.Classes.InstructionPrivate}";

	[Fact]
	public void ProductionRoute_NonThiefPrejudicedManipulatorOpensRequiredPartitionStep()
	{
		using var context = new ModeratorComponentTestContext();
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();

		foreach (var playerName in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			cut.Find("#player-name").Input(playerName);
			cut.Find("form.ww-roster-form").Submit();
		}
		cut.FindAll("button")
			.Single(button => button.TextContent.Contains(
				ClientStrings.LobbyRoster_ContinueToRolesButton))
			.Click();

		ClickRole(cut, MainRoleType.SimpleWerewolf);
		ClickRole(cut, MainRoleType.PrejudicedManipulator);
		ClickRole(cut, MainRoleType.SimpleVillager, count: 3);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage))
			.Should().ContainSingle();
	}

	[Fact]
	public async Task ProductionRoute_PortugueseNightOneIdentifiesPrejudicedManipulatorThroughRenderedHoldBeforeWerewolves()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var game = context.Services.GetRequiredService<GameClientManager>();
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();

		OpenNonThiefPrejudicedManipulatorPartition(cut);
		ClientStrings.Culture!.Name.Should().Be(
			ModeratorComponentTestContext.PortugueseCulture.Name);
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionPage))
			.TextContent.Should().Contain(ClientStrings.PublicGroupPartition_Title);
		var chosenPlayer = lobby.PlayerRoster[0];
		AssignPlayer(cut, playerIndex: 0, ModeratorUiTestIds.PublicGroupPartitionFirstChoice);
		for (var index = 1; index < lobby.PlayerRoster.Count; index++)
		{
			AssignPlayer(cut, index, ModeratorUiTestIds.PublicGroupPartitionSecondChoice);
		}
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionCommit)).Click();
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().ContainSingle();
		var renderedInstructions = new List<Type>();
		var start = game.CurrentInstruction
			.Should().BeOfType<StartGameConfirmationInstruction>().Subject;
		renderedInstructions.Add(start.GetType());
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			cut.Find(HoldButtonSelector),
			timing);

		var nightStart = game.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		renderedInstructions.Add(nightStart.GetType());
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			cut.Find(HoldButtonSelector),
			timing);

		var identification = game.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		renderedInstructions.Add(identification.GetType());
		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(
			MainRoleType.PrejudicedManipulator);
		identification.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		var wakeAnnouncement = GameStrings.RoleWakesUp.Format(
			GameStrings.PrejudicedManipulatorRoleName);
		identification.PublicAnnouncement.Should().Be(wakeAnnouncement);
		var identificationPrompt = GameStrings.RoleSingleIdentificationPrompt.Format(
			GameStrings.PrejudicedManipulatorRoleName);
		identification.PrivateInstruction.Should().Be(identificationPrompt);
		var renderedWakeAnnouncement = cut.Find(PublicInstructionSelector).TextContent;
		renderedWakeAnnouncement.Should().Contain(wakeAnnouncement);
		foreach (var player in lobby.PlayerRoster)
		{
			renderedWakeAnnouncement.Should().NotContain(player.Name);
		}
		cut.Find(PrivateInstructionSelector).TextContent.Should().Contain(
			identificationPrompt);
		cut.FindAll(TestId(ModeratorUiTestIds.InstructionBlock))
			.Should().HaveCount(2);
		var playerOptions = cut.FindAll(PlayerOptionSelector);
		playerOptions.Should().HaveCount(lobby.PlayerRoster.Count);
		playerOptions.Single(option => option.TextContent.Contains(
			chosenPlayer.Name,
			StringComparison.CurrentCulture)).Click();
		cut.Find(HoldButtonSelector)
			.HasAttribute(ClientTestReferences.Html.Attributes.Disabled)
			.Should().BeFalse();

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			cut.Find(HoldButtonSelector),
			timing);

		var werewolfObservation = game.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		renderedInstructions.Add(werewolfObservation.GetType());
		werewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		cut.Find(PublicInstructionSelector).TextContent.Should().Contain(
			GameStrings.RoleHoldersWakeUp.Format(GameStrings.WerewolvesGroupName));
		var identifiedState = game.CurrentSession!.GetPlayerState(chosenPlayer.Id);
		identifiedState.ModeratorKnownRole.Should().Be(
			MainRoleType.PrejudicedManipulator);
		identifiedState.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.PrejudicedManipulator);
		identifiedState.PhysicalCharacterCardId.Should().NotBeNull();
		identifiedState.PubliclyRevealedRole.Should().BeNull();
		renderedInstructions.Should().NotContain(
			type => typeof(AssignRolesInstruction).IsAssignableFrom(type));
		cut.FindAll($"[role='group'][aria-label='{ClientStrings.AssignRoles_Title}']")
			.Should().BeEmpty();
	}

	[Fact]
	public void PartitionPage_RendersNamesAndCommitsSingletonUsingStablePlayerIds()
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		var playerNames = new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" };

		foreach (var playerName in playerNames)
		{
			cut.Find("#player-name").Input(playerName);
			cut.Find("form.ww-roster-form").Submit();
		}
		var roster = lobby.PlayerRoster.ToArray();
		cut.FindAll("button")
			.Single(button => button.TextContent.Contains(
				ClientStrings.LobbyRoster_ContinueToRolesButton))
			.Click();
		ClickRole(cut, MainRoleType.SimpleWerewolf);
		ClickRole(cut, MainRoleType.PrejudicedManipulator);
		ClickRole(cut, MainRoleType.SimpleVillager, count: 3);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		var rows = cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPlayer));
		rows.Select(row => row.QuerySelector(".ww-role-label")!.TextContent.Trim())
			.Should().Equal(playerNames);
		foreach (var player in roster)
		{
			cut.Markup.Should().NotContain(player.Id.ToString());
		}
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPlayer))[0]
			.QuerySelector(TestId(ModeratorUiTestIds.PublicGroupPartitionFirstChoice))!
			.Click();
		for (var index = 1; index < roster.Length; index++)
		{
			cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPlayer))[index]
				.QuerySelector(TestId(ModeratorUiTestIds.PublicGroupPartitionSecondChoice))!
				.Click();
		}
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionCommit)).Click();

		lobby.AcceptedPublicGroupPartition.Should().NotBeNull();
		lobby.AcceptedPublicGroupPartition!.FirstGroupPlayerIds
			.Should().BeEquivalentTo([roster[0].Id]);
		lobby.AcceptedPublicGroupPartition.SecondGroupPlayerIds
			.Should().BeEquivalentTo(roster.Skip(1).Select(player => player.Id));
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().BeEmpty();
		cut.Find("#role-selection-title").TextContent
			.Should().Be(ClientStrings.RoleSelection_Title);
	}

	[Fact]
	public void ProductionRoute_AcceptedPartitionReviewsBacksOutReplacesAndThenStarts()
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		OpenNonThiefPrejudicedManipulatorPartition(cut);
		var roster = lobby.PlayerRoster.ToArray();
		AssignPlayer(cut, playerIndex: 0, ModeratorUiTestIds.PublicGroupPartitionFirstChoice);
		for (var index = 1; index < roster.Length; index++)
		{
			AssignPlayer(cut, index, ModeratorUiTestIds.PublicGroupPartitionSecondChoice);
		}
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionCommit)).Click();
		var firstAccepted = lobby.AcceptedPublicGroupPartition;

		firstAccepted.Should().NotBeNull();
		cut.FindAll(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Should().ContainSingle();
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionReview)).Click();
		Choice(cut, playerIndex: 0, ModeratorUiTestIds.PublicGroupPartitionFirstChoice)
			.GetAttribute("aria-pressed").Should().Be("true");
		Choice(cut, playerIndex: 1, ModeratorUiTestIds.PublicGroupPartitionSecondChoice)
			.GetAttribute("aria-pressed").Should().Be("true");

		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionBack)).Click();

		lobby.AcceptedPublicGroupPartition.Should().BeSameAs(firstAccepted);
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionReview)).Click();
		AssignPlayer(cut, playerIndex: 1, ModeratorUiTestIds.PublicGroupPartitionFirstChoice);
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionCommit)).Click();

		lobby.AcceptedPublicGroupPartition.Should().NotBeSameAs(firstAccepted);
		lobby.AcceptedPublicGroupPartition!.FirstGroupPlayerIds
			.Should().BeEquivalentTo([roster[0].Id, roster[1].Id]);
		lobby.AcceptedPublicGroupPartition.SecondGroupPlayerIds
			.Should().BeEquivalentTo(roster.Skip(2).Select(player => player.Id));
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().ContainSingle();
	}

	[Fact]
	public void ProductionRoute_PartitionReplacementSaveFailureStaysInlineAndPreservesAcceptedState()
	{
		using var context = new ModeratorComponentTestContext();
		var store = new ToggleFailSaveStore();
		context.Services.AddSingleton<IGameSessionSaveStore>(store);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		OpenNonThiefPrejudicedManipulatorPartition(cut);
		AssignPlayer(cut, playerIndex: 0, ModeratorUiTestIds.PublicGroupPartitionFirstChoice);
		for (var index = 1; index < lobby.PlayerRoster.Count; index++)
		{
			AssignPlayer(cut, index, ModeratorUiTestIds.PublicGroupPartitionSecondChoice);
		}
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionCommit)).Click();
		var accepted = lobby.AcceptedPublicGroupPartition;
		var acceptedBytes = store.Load();

		accepted.Should().NotBeNull();
		acceptedBytes.Should().NotBeNullOrWhiteSpace();
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionReview)).Click();
		AssignPlayer(cut, playerIndex: 1, ModeratorUiTestIds.PublicGroupPartitionFirstChoice);
		store.ThrowOnSave = true;
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionCommit)).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().ContainSingle();
		cut.Find("[role='alert']").TextContent.Should().Contain(
			ClientStrings.PublicGroupPartition_SaveFailedValidation);
		lobby.AcceptedPublicGroupPartition.Should().BeSameAs(accepted);
		store.Load().Should().Be(acceptedBytes);
	}

	[Theory]
	[InlineData(MainRoleType.PrejudicedManipulator, MainRoleType.Seer)]
	[InlineData(MainRoleType.Seer, MainRoleType.PrejudicedManipulator)]
	public void ProductionRoute_ThiefOfferReachabilityOpensOnePartitionAfterOrderedRoleLockIn(
		MainRoleType offer1,
		MainRoleType offer2)
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		foreach (var playerName in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			cut.Find("#player-name").Input(playerName);
			cut.Find("form.ww-roster-form").Submit();
		}
		cut.FindAll("button")
			.Single(button => button.TextContent.Contains(
				ClientStrings.LobbyRoster_ContinueToRolesButton))
			.Click();
		ClickRole(cut, MainRoleType.Thief);
		ClickRole(cut, MainRoleType.PrejudicedManipulator);
		ClickRole(cut, MainRoleType.Seer);
		ClickRole(cut, MainRoleType.SimpleWerewolf);
		ClickRole(cut, MainRoleType.SimpleVillager, count: 3);

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionConfigureRoleLockIn)).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.ThiefRoleLockInPage)).Should().ContainSingle();
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().BeEmpty();
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer1Options, offer1);
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer2Options, offer2);
		cut.Find(TestId(ModeratorUiTestIds.ThiefRoleLockInCommit)).Click();

		var accepted = lobby.AcceptedRoleLockIn;
		accepted.Should().NotBeNull();
		accepted!.Offer1!.PrintedRole.Should().Be(offer1);
		accepted.Offer2!.PrintedRole.Should().Be(offer2);
		accepted.Offer1.Id.Should().NotBe(accepted.Offer2.Id);
		cut.FindAll(TestId(ModeratorUiTestIds.ThiefRoleLockInPage)).Should().BeEmpty();
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().ContainSingle();
	}

	[Fact]
	public void ProductionRoute_SamePrintedPrejudicedManipulatorOffersOpenOnePartitionWithoutCopySelection()
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		foreach (var playerName in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			cut.Find("#player-name").Input(playerName);
			cut.Find("form.ww-roster-form").Submit();
		}
		cut.FindAll("button")
			.Single(button => button.TextContent.Contains(
				ClientStrings.LobbyRoster_ContinueToRolesButton))
			.Click();
		ClickRole(cut, MainRoleType.Thief);
		ClickRole(cut, MainRoleType.PrejudicedManipulator, count: 2);
		ClickRole(cut, MainRoleType.SimpleWerewolf);
		ClickRole(cut, MainRoleType.SimpleVillager, count: 3);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionConfigureRoleLockIn)).Click();

		foreach (var groupTestId in new[]
		{
			ModeratorUiTestIds.ThiefOffer1Options,
			ModeratorUiTestIds.ThiefOffer2Options
		})
		{
			cut.Find(TestId(groupTestId)).QuerySelectorAll("button")
				.Count(button => button.TextContent.Trim() ==
					MainRoleType.PrejudicedManipulator.GetPublicName())
				.Should().Be(1);
		}
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer1Options, MainRoleType.PrejudicedManipulator);
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer2Options, MainRoleType.PrejudicedManipulator);
		cut.Find(TestId(ModeratorUiTestIds.ThiefRoleLockInCommit)).Click();

		var accepted = lobby.AcceptedRoleLockIn;
		accepted.Should().NotBeNull();
		accepted!.Offer1!.PrintedRole.Should().Be(MainRoleType.PrejudicedManipulator);
		accepted.Offer2!.PrintedRole.Should().Be(MainRoleType.PrejudicedManipulator);
		accepted.Offer1.Id.Should().NotBe(accepted.Offer2.Id);
		cut.Markup.Should().NotContain(accepted.Offer1.Id.ToString());
		cut.Markup.Should().NotContain(accepted.Offer2.Id.ToString());
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().ContainSingle();
	}

	[Fact]
	public void ProductionRoute_ThiefDealPoolReachabilityOpensOnePartitionAfterOfferAcceptance()
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		foreach (var playerName in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			cut.Find("#player-name").Input(playerName);
			cut.Find("form.ww-roster-form").Submit();
		}
		cut.FindAll("button")
			.Single(button => button.TextContent.Contains(
				ClientStrings.LobbyRoster_ContinueToRolesButton))
			.Click();
		ClickRole(cut, MainRoleType.Thief);
		ClickRole(cut, MainRoleType.PrejudicedManipulator);
		ClickRole(cut, MainRoleType.Seer);
		ClickRole(cut, MainRoleType.Witch);
		ClickRole(cut, MainRoleType.SimpleWerewolf);
		ClickRole(cut, MainRoleType.SimpleVillager, count: 2);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionConfigureRoleLockIn)).Click();
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer1Options, MainRoleType.Seer);
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer2Options, MainRoleType.Witch);
		cut.Find(TestId(ModeratorUiTestIds.ThiefRoleLockInCommit)).Click();

		var accepted = lobby.AcceptedRoleLockIn;
		accepted.Should().NotBeNull();
		accepted!.DealPool.Select(card => card.PrintedRole)
			.Should().Contain(MainRoleType.PrejudicedManipulator);
		accepted.Offer1!.PrintedRole.Should().NotBe(MainRoleType.PrejudicedManipulator);
		accepted.Offer2!.PrintedRole.Should().NotBe(MainRoleType.PrejudicedManipulator);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().ContainSingle();
	}

	[Fact]
	public void ProductionRoute_UnreachableAcceptedThiefLockSkipsPartitionAndKeepsExistingStartEvaluation()
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
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		foreach (var playerName in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			cut.Find("#player-name").Input(playerName);
			cut.Find("form.ww-roster-form").Submit();
		}
		cut.FindAll("button")
			.Single(button => button.TextContent.Contains(
				ClientStrings.LobbyRoster_ContinueToRolesButton))
			.Click();
		ClickRole(cut, MainRoleType.Thief);
		ClickRole(cut, MainRoleType.Seer);
		ClickRole(cut, MainRoleType.Witch);
		ClickRole(cut, MainRoleType.SimpleWerewolf);
		ClickRole(cut, MainRoleType.SimpleVillager, count: 3);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionConfigureRoleLockIn)).Click();
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer1Options, MainRoleType.Seer);
		ClickOffer(cut, ModeratorUiTestIds.ThiefOffer2Options, MainRoleType.Witch);
		cut.Find(TestId(ModeratorUiTestIds.ThiefRoleLockInCommit)).Click();

		lobby.AcceptedRoleLockIn.Should().NotBeNull();
		lobby.AcceptedRoleLockIn!.RoleComposition.Select(card => card.PrintedRole)
			.Should().NotContain(MainRoleType.PrejudicedManipulator);
		lobby.RequiresPublicGroupPartition.Should().BeFalse();
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().BeEmpty();
		cut.WaitForAssertion(() => context.Services
			.GetRequiredService<LobbyEvaluationCoordinator>()
			.State.Kind.Should().Be(LobbyEvaluationStateKind.CouldNotEvaluate));
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().ContainSingle();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void RosterReorder_UsesStagedTransactionAndPreservesPartitionMembership(
		bool moveUp)
	{
		using var context = new ModeratorComponentTestContext();
		var store = new ToggleFailSaveStore();
		context.Services.AddSingleton<IGameSessionSaveStore>(store);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		OpenNonThiefPrejudicedManipulatorPartition(cut);
		AssignPlayer(cut, playerIndex: 0, ModeratorUiTestIds.PublicGroupPartitionFirstChoice);
		for (var index = 1; index < lobby.PlayerRoster.Count; index++)
		{
			AssignPlayer(cut, index, ModeratorUiTestIds.PublicGroupPartitionSecondChoice);
		}
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionCommit)).Click();
		var originalRoster = lobby.PlayerRoster.ToArray();
		var acceptedPartition = lobby.AcceptedPublicGroupPartition;
		var originalScenario = lobby.CreateSimulationScenario().ToCanonical();
		var originalStagedBytes = store.Load();
		cut.FindAll("button")
			.Single(button => button.TextContent.Trim() == ClientStrings.Common_Back)
			.Click();

		var movedPlayer = moveUp ? originalRoster[1] : originalRoster[0];
		var moveAriaLabel = string.Format(
			moveUp
				? ClientStrings.LobbyRoster_MoveUpAriaFormat
				: ClientStrings.LobbyRoster_MoveDownAriaFormat,
			movedPlayer.Name);
		cut.FindAll("button")
			.Single(button => button.GetAttribute("aria-label") == moveAriaLabel)
			.Click();

		lobby.PlayerRoster.Select(player => player.Id).Should().Equal(
			originalRoster[1].Id,
			originalRoster[0].Id,
			originalRoster[2].Id,
			originalRoster[3].Id,
			originalRoster[4].Id);
		lobby.AcceptedPublicGroupPartition.Should().BeSameAs(acceptedPartition);
		lobby.AcceptedPublicGroupPartition!.FirstGroupPlayerIds
			.Should().BeEquivalentTo([originalRoster[0].Id]);
		lobby.CreateSimulationScenario().ToCanonical().Should().NotBe(originalScenario);
		store.Load().Should().NotBe(originalStagedBytes);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void RosterMembershipEdit_UsesStagedTransactionAndClearsAcceptedBytes(
		bool addPlayer)
	{
		using var context = new ModeratorComponentTestContext();
		var store = new ToggleFailSaveStore();
		context.Services.AddSingleton<IGameSessionSaveStore>(store);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		OpenNonThiefPrejudicedManipulatorPartition(cut);
		AssignPlayer(cut, playerIndex: 0, ModeratorUiTestIds.PublicGroupPartitionFirstChoice);
		for (var index = 1; index < lobby.PlayerRoster.Count; index++)
		{
			AssignPlayer(cut, index, ModeratorUiTestIds.PublicGroupPartitionSecondChoice);
		}
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionCommit)).Click();
		var originalRoster = lobby.PlayerRoster.ToArray();
		store.Load().Should().NotBeNullOrWhiteSpace();
		cut.FindAll("button")
			.Single(button => button.TextContent.Trim() == ClientStrings.Common_Back)
			.Click();

		if (addPlayer)
		{
			cut.Find("#player-name").Input("Fátima");
			cut.Find("form.ww-roster-form").Submit();
		}
		else
		{
			var removeLabel = string.Format(
				ClientStrings.LobbyRoster_RemoveAriaFormat,
				originalRoster[2].Name);
			cut.FindAll("button")
				.Single(button => button.GetAttribute("aria-label") == removeLabel)
				.Click();
		}

		store.Load().Should().BeNull();
		lobby.PlayerRoster.Should().HaveCount(addPlayer ? 6 : 4);
		lobby.RequiresRoleLockIn.Should().BeTrue();
		lobby.AcceptedPublicGroupPartition.Should().BeNull();
	}

	[Fact]
	public void PartitionPage_UnassignedOrEmptyGroupStaysInlineAndCommitsNothing()
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		OpenNonThiefPrejudicedManipulatorPartition(cut);

		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionFirstChoice))
			.Select(choice => choice.GetAttribute("aria-pressed"))
			.Should().OnlyContain(value => value == "false");
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionSecondChoice))
			.Select(choice => choice.GetAttribute("aria-pressed"))
			.Should().OnlyContain(value => value == "false");
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionCommit)).Click();

		cut.Find("[role='alert']").TextContent.Should().Contain(
			ClientStrings.PublicGroupPartition_IncompleteValidation);
		lobby.AcceptedPublicGroupPartition.Should().BeNull();
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().ContainSingle();

		for (var index = 0; index < lobby.PlayerRoster.Count; index++)
		{
			AssignPlayer(cut, index, ModeratorUiTestIds.PublicGroupPartitionFirstChoice);
		}
		cut.Find(TestId(ModeratorUiTestIds.PublicGroupPartitionCommit)).Click();

		cut.Find("[role='alert']").TextContent.Should().Contain(
			ClientStrings.PublicGroupPartition_IncompleteValidation);
		lobby.AcceptedPublicGroupPartition.Should().BeNull();
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().ContainSingle();
	}

	private static void ClickRole(
		IRenderedComponent<Routes> cut,
		MainRoleType role,
		int count = 1)
	{
		var roleName = role.GetPublicName();
		var addAriaLabel = string.Format(
			ClientStrings.RoleSelection_AddRoleAriaFormat,
			roleName);
		for (var index = 0; index < count; index++)
		{
			cut.FindAll("button")
				.Single(button =>
					button.GetAttribute("aria-label") is { } ariaLabel &&
					(ariaLabel == roleName || ariaLabel == addAriaLabel))
				.Click();
		}
	}

	private static void OpenNonThiefPrejudicedManipulatorPartition(
		IRenderedComponent<Routes> cut)
	{
		foreach (var playerName in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			cut.Find("#player-name").Input(playerName);
			cut.Find("form.ww-roster-form").Submit();
		}
		cut.FindAll("button")
			.Single(button => button.TextContent.Contains(
				ClientStrings.LobbyRoster_ContinueToRolesButton))
			.Click();
		ClickRole(cut, MainRoleType.SimpleWerewolf);
		ClickRole(cut, MainRoleType.PrejudicedManipulator);
		ClickRole(cut, MainRoleType.SimpleVillager, count: 3);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
	}

	private static void AssignPlayer(
		IRenderedComponent<Routes> cut,
		int playerIndex,
		string choiceTestId) =>
		Choice(cut, playerIndex, choiceTestId).Click();

	private static AngleSharp.Dom.IElement Choice(
		IRenderedComponent<Routes> cut,
		int playerIndex,
		string choiceTestId) =>
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPlayer))[playerIndex]
			.QuerySelector(TestId(choiceTestId))!;

	private static void ClickOffer(
		IRenderedComponent<Routes> cut,
		string groupTestId,
		MainRoleType role) =>
		cut.Find(TestId(groupTestId))
			.QuerySelectorAll("button")
			.Single(button => button.TextContent.Trim() == role.GetPublicName())
			.Click();

	private static string TestId(string value) => $"[data-testid='{value}']";

	private sealed class ToggleFailSaveStore : IGameSessionSaveStore
	{
		private string? _payload;

		public bool ThrowOnSave { get; set; }

		public string? Load() => _payload;

		public void Save(string serializedSession)
		{
			if (ThrowOnSave)
			{
				throw new IOException(ClientTestReferences.ExceptionMessages.SaveFailed);
			}

			_payload = serializedSession;
		}

		public void Clear() => _payload = null;
	}
}
