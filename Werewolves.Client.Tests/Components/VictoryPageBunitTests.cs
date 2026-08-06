using System.Globalization;
using Bunit;
using FluentAssertions;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class VictoryPageBunitTests
{
	[Theory]
	[InlineData(VictoryCheckWindow.Dawn)]
	[InlineData(VictoryCheckWindow.PreNight)]
	public void EverySingleFactionOutcome_RendersResourceBackedModeratorCopyAndAction(
		VictoryCheckWindow window)
	{
		using var context = new ModeratorComponentTestContext();

		FactionPresentationTestData.Factions.Should().Equal(Enum.GetValues<Faction>());
		foreach (var faction in Enum.GetValues<Faction>())
		{
			var returnToLobbyRequested = false;
			var cut = context.RenderModeratorComponent<VictoryPage>(parameters =>
				parameters
					.Add(
						component => component.GameResult,
						new SingleFactionGameResult(faction))
					.Add(component => component.VictoryCheckWindow, window)
					.Add(
						component => component.OnReturnToLobby,
						() => returnToLobbyRequested = true));

			var visibleCopy = cut.FindAll("p").Select(element => element.TextContent);
			visibleCopy.Should().Contain(FactionPresentationTestData.Name(faction));
			visibleCopy.Should().Contain(WindowName(window));
			var returnButton = cut.Find("button");
			returnButton.TextContent.Should().Be(ClientStrings.Victory_ReturnToLobbyButton);
			returnButton.Click();
			returnToLobbyRequested.Should().BeTrue();
		}
	}

	[Theory]
	[InlineData(VictoryCheckWindow.Dawn)]
	[InlineData(VictoryCheckWindow.PreNight)]
	public void SharedVictoryAndNoWinnerOutcomes_RenderResourceBackedModeratorCopyAndAction(
		VictoryCheckWindow window)
	{
		using var context = new ModeratorComponentTestContext();
		var sharedResult = new SharedVictoryGameResult(
			[Faction.Angel, Faction.PrejudicedManipulator]);
		var sharedName = string.Format(
			CultureInfo.CurrentCulture,
			ClientStrings.LobbyEvaluation_GameResultSharedFormat,
			ClientStrings.LobbyEvaluation_GameResultShared,
			string.Join(
				ClientStrings.LobbyEvaluation_FactionSeparator,
				sharedResult.Factions.Select(FactionPresentationTestData.Name)));
		var expectedResults = new Dictionary<GameResult, string>
		{
			[sharedResult] = sharedName,
			[new NoWinnerGameResult()] = ClientStrings.LobbyEvaluation_GameResultNoWinner
		};

		foreach (var (result, expectedName) in expectedResults)
		{
			var cut = context.RenderModeratorComponent<VictoryPage>(parameters =>
				parameters
					.Add(component => component.GameResult, result)
					.Add(component => component.VictoryCheckWindow, window));

			var visibleCopy = cut.FindAll("p").Select(element => element.TextContent);
			visibleCopy.Should().Contain(expectedName);
			visibleCopy.Should().Contain(WindowName(window));
			cut.Find("button").TextContent.Should().Be(ClientStrings.Victory_ReturnToLobbyButton);
		}
	}

	private static string WindowName(VictoryCheckWindow window) => window switch
	{
		VictoryCheckWindow.Dawn => ClientStrings.Victory_WindowDawn,
		VictoryCheckWindow.PreNight => ClientStrings.Victory_WindowPreNight,
		_ => throw new ArgumentOutOfRangeException(nameof(window))
	};
}
