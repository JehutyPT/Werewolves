using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Components;

public class InstructionRendererHapticTests
{
	[Fact]
	public void InstructionRenderer_RemountsInputStateWhenInstructionChanges()
	{
		using var context = new ModeratorComponentTestContext();
		var game = context.Services.GetRequiredService<GameClientManager>();
		var firstInstruction = ReachAssignRolesInstruction(game);
		var secondInstruction = firstInstruction with { };
		var roster = game.CurrentRoster;

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, firstInstruction)
			.Add(component => component.Roster, roster));

		FindRoleButtons(cut).First().Click();

		FindSelectedRoleButtons(cut)
			.Should()
			.ContainSingle();

		cut.Render(parameters => parameters
			.Add(component => component.Instruction, secondInstruction)
			.Add(component => component.Roster, roster));

		FindSelectedRoleButtons(cut)
			.Should()
			.BeEmpty();
	}

	private static AssignRolesInstruction ReachAssignRolesInstruction(GameClientManager game)
	{
		var startInstruction = game.StartGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		game.ProcessInput(startInstruction.CreateResponse(true));
		var players = game.CurrentSession!.GetPlayers().ToList();
		var werewolfIds = players.Take(2).Select(player => player.Id).ToHashSet();
		var victimId = players[2].Id;

		ConfirmCurrentInstruction(game);
		SelectCurrentPlayers(game, werewolfIds);
		SelectCurrentPlayers(game, [victimId]);
		ConfirmCurrentInstruction(game);
		ConfirmCurrentInstruction(game);

		return game.CurrentInstruction.Should().BeOfType<AssignRolesInstruction>().Subject;
	}

	private static void ConfirmCurrentInstruction(GameClientManager game)
	{
		var instruction = game.CurrentInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;
		game.ProcessInput(instruction.CreateResponse(true));
	}

	private static void SelectCurrentPlayers(GameClientManager game, HashSet<Guid> playerIds)
	{
		var instruction = game.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		game.ProcessInput(instruction.CreateResponse(playerIds));
	}

	private static IReadOnlyList<IElement> FindRoleButtons(IRenderedComponent<InstructionRenderer> rendered) =>
		rendered.FindAll(Html.Selectors.ButtonWithClass(ClientTestReferences.Css.Classes.RoleButton));

	private static IReadOnlyList<IElement> FindSelectedRoleButtons(IRenderedComponent<InstructionRenderer> rendered) =>
		FindRoleButtons(rendered)
			.Where(button => button.ClassList.Contains(ClientTestReferences.Css.Classes.RoleButtonSelected))
			.ToArray();
}
