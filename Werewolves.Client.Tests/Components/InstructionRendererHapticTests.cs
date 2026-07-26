using System.Collections.Immutable;
using System.Reflection;
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
		var firstInstruction = CreateAssignRolesInstruction(game);
		var secondInstruction = CreateInstructionWithNewIdentity(firstInstruction);
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

	private static AssignRolesInstruction CreateAssignRolesInstruction(GameClientManager game)
	{
		game.StartGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var playerId = game.CurrentRoster[2].PlayerId;
		return (AssignRolesInstruction)AssignRolesConstructor.Invoke(
			[
				ImmutableHashSet.Create(playerId),
				new[] { MainRoleType.SimpleVillager },
				null,
				nameof(InstructionRenderer_RemountsInputStateWhenInstructionChanges),
				null,
				Guid.Empty
			]);
	}

	private static AssignRolesInstruction CreateInstructionWithNewIdentity(
		AssignRolesInstruction instruction) =>
		(AssignRolesInstruction)AssignRolesConstructor.Invoke(
			[
				instruction.PlayersForAssignment,
				instruction.RolesForAssignment,
				instruction.PublicAnnouncement,
				instruction.PrivateInstruction,
				instruction.AffectedPlayerIds,
				Guid.NewGuid()
			]);

	private static IReadOnlyList<IElement> FindRoleButtons(IRenderedComponent<InstructionRenderer> rendered) =>
		rendered.FindAll(Html.Selectors.ButtonWithClass(ClientTestReferences.Css.Classes.RoleButton));

	private static IReadOnlyList<IElement> FindSelectedRoleButtons(IRenderedComponent<InstructionRenderer> rendered) =>
		FindRoleButtons(rendered)
			.Where(button => button.ClassList.Contains(ClientTestReferences.Css.Classes.RoleButtonSelected))
			.ToArray();

	private static readonly ConstructorInfo AssignRolesConstructor =
		typeof(AssignRolesInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 6);
}
