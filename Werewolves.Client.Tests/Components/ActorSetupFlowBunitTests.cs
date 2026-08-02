using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public sealed class ActorSetupFlowBunitTests
{
	[Fact]
	public void ProductionRoute_ActorAndPrejudicedManipulatorOpenActorSetupFirst()
	{
		using var context = CreateActorContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedActorAndPrejudicedManipulatorLobby(lobby);
		var cut = context.RenderModeratorComponent<Routes>();

		OpenRoleSelection(cut);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.ActorSetupPage)).Should().ContainSingle();
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().BeEmpty();

		ClickActorCard(cut, MainRoleType.Seer);
		ClickActorCard(cut, MainRoleType.Cupid);
		ClickActorCard(cut, MainRoleType.Witch);
		cut.Find(TestId(ModeratorUiTestIds.ActorSetupCommit)).Click();
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.ActorSetupPage)).Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.PublicGroupPartitionPage)).Should().ContainSingle();
	}

	[Fact]
	public void ProductionRoute_RecordsReviewsBacksOutAndReplacesActorCards()
	{
		using var context = CreateActorContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedActorLobby(lobby);
		var cut = context.RenderModeratorComponent<Routes>();

		OpenRoleSelection(cut);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		cut.Find(TestId(ModeratorUiTestIds.ActorSetupPage)).TextContent
			.Should().Contain(ClientStrings.ActorSetup_Title);
		var offeredRoles = cut.FindAll(TestId(ModeratorUiTestIds.ActorSetupCard))
			.Select(button => button.TextContent.Trim())
			.ToArray();
		var excludedRoles = new HashSet<string>
		{
			MainRoleType.Actor.GetPublicName(),
			MainRoleType.SimpleVillager.GetPublicName(),
			MainRoleType.Seer.GetPublicName()
		};
		offeredRoles.Should().NotContain(role => excludedRoles.Contains(role));
		cut.Find(TestId(ModeratorUiTestIds.ActorSetupSelectionCount)).TextContent
			.Should().Contain(ActorSelectionCount(0));
		cut.Find(TestId(ModeratorUiTestIds.ActorSetupCommit)).Click();
		cut.Find("[role='alert']").TextContent.Should().Contain(
			ClientStrings.ActorSetup_IncompleteValidation);
		lobby.AcceptedActorSetupCards.Version.Should().Be(0);
		ClickActorCard(cut, MainRoleType.Cupid);
		ClickActorCard(cut, MainRoleType.Witch);
		ClickActorCard(cut, MainRoleType.Hunter);
		cut.Find(TestId(ModeratorUiTestIds.ActorSetupCommit)).Click();

		var first = lobby.AcceptedActorSetupCards;
		first.Version.Should().Be(1);
		first.PrintedRoles.Should().BeEquivalentTo(
			[MainRoleType.Cupid, MainRoleType.Witch, MainRoleType.Hunter]);
		first.Cards.Select(card => card.Id).Should().OnlyHaveUniqueItems();
		first.Cards.Should().OnlyContain(card => card.Id != Guid.Empty);
		cut.Find(TestId(ModeratorUiTestIds.ActorSetupSummary)).TextContent
			.Should().Contain(ClientStrings.ActorSetup_SummaryTitle);
		foreach (var card in first.Cards)
		{
			cut.Markup.Should().NotContain(card.Id.ToString());
		}

		cut.Find(TestId(ModeratorUiTestIds.ActorSetupReview)).Click();
		ActorCard(cut, MainRoleType.Cupid).GetAttribute("aria-pressed").Should().Be("true");
		ActorCard(cut, MainRoleType.Witch).GetAttribute("aria-pressed").Should().Be("true");
		ActorCard(cut, MainRoleType.Hunter).GetAttribute("aria-pressed").Should().Be("true");
		cut.Find(TestId(ModeratorUiTestIds.ActorSetupBack)).Click();

		lobby.AcceptedActorSetupCards.Should().BeSameAs(first);
		cut.Find(TestId(ModeratorUiTestIds.ActorSetupReview)).Click();
		ClickActorCard(cut, MainRoleType.Witch);
		ClickActorCard(cut, MainRoleType.Defender);
		cut.Find(TestId(ModeratorUiTestIds.ActorSetupCommit)).Click();

		lobby.AcceptedActorSetupCards.Version.Should().Be(2);
		lobby.AcceptedActorSetupCards.PrintedRoles.Should().BeEquivalentTo(
			[MainRoleType.Cupid, MainRoleType.Hunter, MainRoleType.Defender]);
	}

	[Fact]
	public void ProductionRoute_ActorReplacementSaveFailureStaysInlineAndPreservesAcceptedCards()
	{
		using var context = CreateActorContext();
		var store = new ToggleFailSaveStore();
		context.Services.AddSingleton<IGameSessionSaveStore>(store);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedActorLobby(lobby);
		var cut = context.RenderModeratorComponent<Routes>();
		OpenRoleSelection(cut);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		ClickActorCard(cut, MainRoleType.Cupid);
		ClickActorCard(cut, MainRoleType.Witch);
		ClickActorCard(cut, MainRoleType.Hunter);
		cut.Find(TestId(ModeratorUiTestIds.ActorSetupCommit)).Click();
		var accepted = lobby.AcceptedActorSetupCards;
		var acceptedBytes = store.Load();

		accepted.Version.Should().Be(1);
		acceptedBytes.Should().NotBeNullOrWhiteSpace();
		cut.Find(TestId(ModeratorUiTestIds.ActorSetupReview)).Click();
		ClickActorCard(cut, MainRoleType.Witch);
		ClickActorCard(cut, MainRoleType.Defender);
		store.ThrowOnSave = true;
		cut.Find(TestId(ModeratorUiTestIds.ActorSetupCommit)).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.ActorSetupPage)).Should().ContainSingle();
		cut.Find("[role='alert']").TextContent.Should().Contain(
			ClientStrings.ActorSetup_SaveFailedValidation);
		lobby.AcceptedActorSetupCards.Should().BeSameAs(accepted);
		store.Load().Should().Be(acceptedBytes);
	}

	private static ModeratorComponentTestContext CreateActorContext()
	{
		var context = new ModeratorComponentTestContext();
		var metadata = LobbySetupMetadataFixture.ForRoles(
			MainRoleType.Actor,
			MainRoleType.PrejudicedManipulator,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer);
		context.Services.AddSingleton(metadata);
		context.Services.AddSingleton<LobbySetupState>();
		return context;
	}

	private static void SeedActorAndPrejudicedManipulatorLobby(LobbySetupState lobby)
	{
		foreach (var playerName in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			lobby.AddPlayer(playerName);
		}

		lobby.IncrementRole(MainRoleType.Actor);
		lobby.IncrementRole(MainRoleType.PrejudicedManipulator);
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
	}

	private static void SeedActorLobby(LobbySetupState lobby)
	{
		foreach (var playerName in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			lobby.AddPlayer(playerName);
		}

		lobby.IncrementRole(MainRoleType.Actor);
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.Seer);
	}

	private static void OpenRoleSelection(IRenderedComponent<Routes> cut) =>
		cut.FindAll("button")
			.Single(button => button.TextContent.Contains(
				ClientStrings.LobbyRoster_ContinueToRolesButton))
			.Click();

	private static void ClickActorCard(IRenderedComponent<Routes> cut, MainRoleType role) =>
		ActorCard(cut, role).Click();

	private static AngleSharp.Dom.IElement ActorCard(
		IRenderedComponent<Routes> cut,
		MainRoleType role) =>
		cut.FindAll(TestId(ModeratorUiTestIds.ActorSetupCard))
			.Single(button => button.TextContent.Trim() == role.GetPublicName());

	private static string ActorSelectionCount(int count) =>
		string.Format(
			System.Globalization.CultureInfo.CurrentCulture,
			ClientStrings.ActorSetup_SelectionCountFormat,
			count,
			3);

	private sealed class ToggleFailSaveStore : IGameSessionSaveStore
	{
		private string? _serializedSession;

		public bool ThrowOnSave { get; set; }

		public string? Load() => _serializedSession;

		public void Save(string serializedSession)
		{
			if (ThrowOnSave)
			{
				throw new InvalidOperationException("Injected save failure.");
			}

			_serializedSession = serializedSession;
		}

		public void Clear() => _serializedSession = null;
	}

	private static string TestId(string value) => $"[data-testid='{value}']";
}
