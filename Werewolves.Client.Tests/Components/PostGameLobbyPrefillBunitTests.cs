using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Werewolves.Client.Components;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public sealed class PostGameLobbyPrefillBunitTests
{
	[Fact]
	public void DashboardAbandon_RendersPrefilledRosterAndRoleCounts()
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = SeedLobby(context, DashboardRoleCounts);
		context.Services.GetRequiredService<GameClientManager>().StartGame(lobby);
		var cut = context.RenderModeratorComponent<Routes>();

		cut.Find(TestId(ModeratorUiTestIds.LandingContinueButton)).Click();
		cut.Find(TestId(ModeratorUiTestIds.AbandonGameButton)).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.LandingShell)).Should().BeEmpty();
		AssertPrefilledLobby(cut, DashboardRoleCounts);
	}

	[Fact]
	public void VictoryConfirmation_ReturnToLobbyRendersPrefilledRosterAndRoleCounts()
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = SeedLobby(context, VictoryRoleCounts);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var startInstruction = manager.StartGame(lobby);
		PlayToWerewolfVictoryAtDawn(manager, startInstruction);
		var cut = context.RenderModeratorComponent<Routes>();

		cut.Find(TestId(ModeratorUiTestIds.LandingContinueButton)).Click();
		FindButtonByRenderedAccessibleName(
			cut,
			ClientStrings.Victory_ReturnToLobbyButton).Click();

		cut.FindAll(TestId(ModeratorUiTestIds.LandingShell)).Should().BeEmpty();
		AssertPrefilledLobby(cut, VictoryRoleCounts);
	}

	private static (MainRoleType Role, int Count)[] DashboardRoleCounts =>
	[
		(MainRoleType.SimpleWerewolf, 1),
		(MainRoleType.Seer, 1),
		(MainRoleType.SimpleVillager, 3)
	];

	private static (MainRoleType Role, int Count)[] VictoryRoleCounts =>
	[
		(MainRoleType.SimpleWerewolf, 2),
		(MainRoleType.SimpleVillager, 3)
	];

	private static string[] ExpectedPlayerOrder =>
	[
		ClientTestReferences.PlayerNames.Diana,
		ClientTestReferences.PlayerNames.Ana,
		ClientTestReferences.PlayerNames.Eduardo,
		ClientTestReferences.PlayerNames.Bruno,
		ClientTestReferences.PlayerNames.Catarina
	];

	private static LobbySetupState SeedLobby(
		ModeratorComponentTestContext context,
		IReadOnlyList<(MainRoleType Role, int Count)> roleCounts)
	{
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		foreach (var playerName in ExpectedPlayerOrder)
		{
			lobby.AddPlayer(playerName);
		}
		foreach (var (role, count) in roleCounts)
		{
			for (var index = 0; index < count; index++)
			{
				lobby.IncrementRole(role);
			}
		}

		return lobby;
	}

	private static void AssertPrefilledLobby(
		IRenderedComponent<Routes> cut,
		IReadOnlyList<(MainRoleType Role, int Count)> expectedRoleCounts)
	{
		AssertExactPlayerOrder(cut);
		FindButtonByRenderedAccessibleName(
			cut,
			ClientStrings.LobbyRoster_ContinueToRolesButton).Click();
		AssertExactSelectedRoleVector(cut, expectedRoleCounts);
	}

	private static void AssertExactPlayerOrder(IRenderedComponent<Routes> cut)
	{
		var roster = FindElementByAccessibleName(
			cut,
			ClientStrings.LobbyRoster_SeatOrderLabel);
		var expectedPlayerNames = ExpectedPlayerOrder;
		var expectedRemoveLabels = expectedPlayerNames
			.Select(playerName => Format(
				ClientStrings.LobbyRoster_RemoveAriaFormat,
				playerName))
			.ToArray();
		var rows = roster.QuerySelectorAll(ClientTestReferences.Html.Elements.ListItem);

		rows.Should().HaveCount(expectedPlayerNames.Length);
		rows.Select(row => row.QuerySelectorAll("*")
				.Select(element => element.TextContent.Trim())
				.Single(expectedPlayerNames.Contains))
			.Should().Equal(expectedPlayerNames);
		rows.Select(row => row.QuerySelectorAll(ClientTestReferences.Html.Selectors.Button)
				.Select(button => button.GetAttribute(
					ClientTestReferences.Html.Attributes.AriaLabel))
				.Single(label =>
					label is not null && expectedRemoveLabels.Contains(label)))
			.Should().Equal(expectedRemoveLabels);
	}

	private static void AssertExactSelectedRoleVector(
		IRenderedComponent<Routes> cut,
		IReadOnlyList<(MainRoleType Role, int Count)> expectedRoleCounts)
	{
		var expectedStepperCounts = expectedRoleCounts
			.Where(entry => UsesStepper(entry.Role))
			.ToDictionary(
				entry => RoleCountAccessibleName(entry.Role, entry.Count),
				entry => entry.Count);
		var actualStepperCounts = cut
			.FindAll($"[{ClientTestReferences.Html.Attributes.AriaLabel}]")
			.Select(element =>
			{
				int.TryParse(
					element.TextContent.Trim(),
					CultureInfo.CurrentCulture,
					out var count);
				return (
					Name: element.GetAttribute(
						ClientTestReferences.Html.Attributes.AriaLabel)!,
					Count: count);
			})
			.Where(entry => entry.Count > 0)
			.ToDictionary(entry => entry.Name, entry => entry.Count);
		actualStepperCounts.Should().BeEquivalentTo(expectedStepperCounts);

		var expectedSelectedToggles = expectedRoleCounts
			.Where(entry => !UsesStepper(entry.Role))
			.Select(entry => PublicRoleName(entry.Role));
		var actualSelectedToggles = cut
			.FindAll(
				$"{ClientTestReferences.Html.Elements.Button}" +
				$"[{ClientTestReferences.Html.Attributes.AriaPressed}='" +
				$"{ClientTestReferences.Html.AriaValues.True}']")
			.Select(button => button.GetAttribute(
				ClientTestReferences.Html.Attributes.AriaLabel)!)
			.ToArray();
		actualSelectedToggles.Should().BeEquivalentTo(expectedSelectedToggles);
	}

	private static bool UsesStepper(MainRoleType role) => role switch
	{
		MainRoleType.SimpleVillager or MainRoleType.SimpleWerewolf => true,
		MainRoleType.Seer => false,
		_ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
	};

	private static string PublicRoleName(MainRoleType role) => role switch
	{
		MainRoleType.SimpleVillager => GameStrings.SimpleVillagerRoleName,
		MainRoleType.SimpleWerewolf => GameStrings.SimpleWerewolfRoleName,
		MainRoleType.Seer => GameStrings.SeerRoleName,
		_ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
	};

	private static string RoleCountAccessibleName(MainRoleType role, int count) =>
		Format(
			ClientStrings.RoleSelection_RoleCountAriaFormat,
			count,
			PublicRoleName(role));

	private static AngleSharp.Dom.IElement FindElementByAccessibleName(
		IRenderedComponent<Routes> cut,
		string accessibleName) =>
		cut.FindAll($"[{ClientTestReferences.Html.Attributes.AriaLabel}]")
			.Single(element => element.GetAttribute(
				ClientTestReferences.Html.Attributes.AriaLabel) == accessibleName);

	private static AngleSharp.Dom.IElement FindButtonByRenderedAccessibleName(
		IRenderedComponent<Routes> cut,
		string accessibleName) =>
		cut.FindAll(ClientTestReferences.Html.Selectors.Button)
			.Single(button =>
				(button.GetAttribute(ClientTestReferences.Html.Attributes.AriaLabel)
					?? button.TextContent.Trim()) == accessibleName);

	internal static void PlayToWerewolfVictoryAtDawn(
		GameClientManager manager,
		StartGameConfirmationInstruction startInstruction)
	{
		manager.ProcessInput(startInstruction.CreateResponse());
		var players = manager.CurrentSession!.GetPlayers().ToList();
		var werewolfIds = players.Take(2).Select(player => player.Id).ToHashSet();
		var victimId = players[2].Id;

		ConfirmCurrentInstruction(manager);
		SelectCurrentPlayers(manager, werewolfIds);
		SelectCurrentPlayers(manager, [victimId]);
		ConfirmCurrentInstruction(manager);
		ConfirmCurrentInstruction(manager);

		for (var step = 0; step < 20; step++)
		{
			switch (manager.CurrentInstruction)
			{
				case FinishedGameConfirmationInstruction:
					return;
				case AssignRolesInstruction assignRoles:
					manager.ProcessInput(assignRoles.CreateResponse(
						assignRoles.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							_ => MainRoleType.SimpleVillager)));
					break;
				case ConfirmationInstruction confirmation:
					manager.ProcessInput(confirmation.CreateResponse());
					break;
				default:
					throw new InvalidOperationException(
						ClientTestReferences.ExceptionMessages.UnexpectedInstructionWhileReachingVictory(
							manager.CurrentInstruction?.GetType().Name));
			}
		}

		throw new InvalidOperationException(
			ClientTestReferences.ExceptionMessages.VictoryNotReached);
	}

	private static void ConfirmCurrentInstruction(GameClientManager manager)
	{
		var instruction = manager.CurrentInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(instruction.CreateResponse());
	}

	private static void SelectCurrentPlayers(
		GameClientManager manager,
		HashSet<Guid> playerIds)
	{
		var instruction = manager.CurrentInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(instruction.CreateResponse(playerIds));
	}

	private static string TestId(string value) => $"[data-testid='{value}']";

	private static string Format(string format, params object[] args) =>
		string.Format(CultureInfo.CurrentCulture, format, args);
}
