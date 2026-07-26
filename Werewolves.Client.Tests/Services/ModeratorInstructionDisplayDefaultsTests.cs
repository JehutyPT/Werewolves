using System.Collections.Immutable;
using System.Reflection;
using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class ModeratorInstructionDisplayDefaultsTests
{
	[Theory]
	[MemberData(nameof(DataEntryInstructions))]
	public void RequiresModeratorDataEntryForDisplay_ReturnsTrueForInputInstructions(ModeratorInstruction instruction)
	{
		ModeratorInstructionDisplayDefaults.RequiresModeratorDataEntryForDisplay(instruction)
			.Should()
			.BeTrue();
	}

	[Theory]
	[MemberData(nameof(PassiveInstructions))]
	public void RequiresModeratorDataEntryForDisplay_ReturnsFalseForPassiveInstructions(ModeratorInstruction instruction)
	{
		ModeratorInstructionDisplayDefaults.RequiresModeratorDataEntryForDisplay(instruction)
			.Should()
			.BeFalse();
	}

	public static IEnumerable<object[]> DataEntryInstructions()
	{
		yield return [CreateSelectPlayersInstruction()];
		yield return [CreateSelectOptionsInstruction()];
		yield return [CreateAssignRolesInstruction()];
	}

	public static IEnumerable<object[]> PassiveInstructions()
	{
		yield return [new StartGameConfirmationInstruction(Guid.NewGuid())];
		yield return [new PassiveInstruction(GameStrings.NightStartsPrompt, GameStrings.ConfirmNightStarted)];
	}

	private static SelectPlayersInstruction CreateSelectPlayersInstruction() =>
		(SelectPlayersInstruction)SelectPlayersConstructor.Invoke(
			[
				new HashSet<Guid> { Guid.NewGuid() },
				NumberRangeConstraint.Single,
				null,
				GameStrings.WerewolvesChooseVictimPrompt,
				null,
				Guid.Empty
			]);

	private static SelectOptionsInstruction CreateSelectOptionsInstruction() =>
		(SelectOptionsInstruction)SelectOptionsConstructor.Invoke(
			[
				new[] { new ModeratorOption("option-alpha", GameStrings.ConfirmNightStarted) },
				NumberRangeConstraint.Single,
				null,
				GameStrings.ConfirmNightStarted,
				null,
				Guid.Empty
			]);

	private static AssignRolesInstruction CreateAssignRolesInstruction() =>
		(AssignRolesInstruction)AssignRolesConstructor.Invoke(
			[
				ImmutableHashSet.Create(Guid.NewGuid()),
				new[] { MainRoleType.SimpleVillager },
				null,
				GameStrings.RevealRolePromptSpecify,
				null,
				Guid.Empty
			]);

	private static readonly ConstructorInfo SelectPlayersConstructor =
		typeof(SelectPlayersInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 6);

	private static readonly ConstructorInfo SelectOptionsConstructor =
		typeof(SelectOptionsInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 6);

	private static readonly ConstructorInfo AssignRolesConstructor =
		typeof(AssignRolesInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 6);

	private sealed record PassiveInstruction : ModeratorInstruction
	{
		public PassiveInstruction(string? publicAnnouncement = null, string? privateInstruction = null)
			: base(publicAnnouncement: publicAnnouncement, privateInstruction: privateInstruction)
		{
		}
	}
}
