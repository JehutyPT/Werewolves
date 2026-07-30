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
	public void TypedOutcomeAndWindow_RenderResourceBackedModeratorCopy(
		VictoryCheckWindow window)
	{
		using var context = new ModeratorComponentTestContext();
		GameResult[] results =
		[
			new SingleFactionGameResult(Faction.Villager),
			new SharedVictoryGameResult([Faction.Villager, Faction.Werewolf]),
			new NoWinnerGameResult()
		];

		foreach (var result in results)
		{
			var cut = context.RenderModeratorComponent<VictoryPage>(parameters =>
				parameters
					.Add(component => component.GameResult, result)
					.Add(component => component.VictoryCheckWindow, window));

			cut.Markup.Should().Contain(
				LobbyEvaluationPresentation.GameResultName(result));
			cut.Markup.Should().Contain(window == VictoryCheckWindow.Dawn
				? ClientStrings.Victory_WindowDawn
				: ClientStrings.Victory_WindowPreNight);
		}
	}
}
