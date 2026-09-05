using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public class ModeratorInstructionIdentitySerializationTests
{
	private static readonly JsonSerializerOptions SerializationOptions = new()
	{
		Converters =
		{
			new GameResultConverter(),
			new ModeratorInstructionConverter(),
			new JsonStringEnumConverter()
		}
	};

	[Fact]
	public void Converter_AllInstructionKinds_PreservesIdentity()
	{
		var playerId = Guid.NewGuid();
		ModeratorInstruction[] instructions =
		[
			new ConfirmationInstruction(
				privateInstruction: nameof(Converter_AllInstructionKinds_PreservesIdentity)),
			new StartGameConfirmationInstruction(Guid.NewGuid()),
			new FinishedGameConfirmationInstruction(
				new SingleFactionGameResult(Faction.Villager),
				VictoryCheckWindow.Dawn),
			new SelectPlayersInstruction(
				[playerId],
				NumberRangeConstraint.Single,
				privateInstruction: nameof(Converter_AllInstructionKinds_PreservesIdentity)),
			new AssignRolesInstruction(
				[playerId],
				[MainRoleType.SimpleVillager],
				privateInstruction: nameof(Converter_AllInstructionKinds_PreservesIdentity)),
			new SelectOptionsInstruction(
				[
					new ModeratorOption("first", "Mesmo rótulo"),
					new ModeratorOption("second", "Mesmo rótulo")
				],
				NumberRangeConstraint.Single,
				privateInstruction: nameof(Converter_AllInstructionKinds_PreservesIdentity))
		];

		foreach (var instruction in instructions)
		{
			var json = JsonSerializer.Serialize<ModeratorInstruction>(
				instruction,
				SerializationOptions);

			var restored = JsonSerializer.Deserialize<ModeratorInstruction>(
				json,
				SerializationOptions);

			restored.Should().NotBeNull();
			restored!.GetType().Should().Be(instruction.GetType());
			restored.InstructionId.Should().Be(instruction.InstructionId);
			if (instruction is SelectOptionsInstruction originalOptions)
			{
				((SelectOptionsInstruction)restored).Options
					.Should().Equal(originalOptions.Options);
			}
		}
	}

	[Fact]
	public void Converter_GenericBearGrowlConfirmation_PreservesIdentityAndSound()
	{
		ModeratorInstruction instruction = new ConfirmationInstruction(
			privateInstruction: GameStrings.BearTamerGrowlInstruction,
			soundEffects: [SoundEffectsEnum.BearGrowl]);

		var json = JsonSerializer.Serialize(
			instruction,
			SerializationOptions);
		var restored = JsonSerializer.Deserialize<ModeratorInstruction>(
			json,
			SerializationOptions);

		var confirmation = restored.Should()
			.BeOfType<ConfirmationInstruction>()
			.Subject;
		confirmation.InstructionId.Should().Be(instruction.InstructionId);
		confirmation.PrivateInstruction.Should().Be(GameStrings.BearTamerGrowlInstruction);
		confirmation.SoundEffects.Should().Equal(SoundEffectsEnum.BearGrowl);
	}

	[Fact]
	public void Converter_WhiteWerewolfTerminalInstruction_PreservesTypedOutcomeAndCopy()
	{
		ModeratorInstruction instruction = new FinishedGameConfirmationInstruction(
			new SingleFactionGameResult(Faction.WhiteWerewolf),
			VictoryCheckWindow.PreNight);

		var json = JsonSerializer.Serialize(
			instruction,
			SerializationOptions);
		var restored = JsonSerializer.Deserialize<ModeratorInstruction>(
			json,
			SerializationOptions);

		var finished = restored.Should()
			.BeOfType<FinishedGameConfirmationInstruction>()
			.Subject;
		finished.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.WhiteWerewolf));
		finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.PreNight);
		finished.PublicAnnouncement.Should().Contain(
			GameStrings.VictoryConditionWhiteWerewolfSoleSurvivor);
	}

	[Fact]
	public void Converter_PrejudicedManipulatorTerminalInstruction_PreservesTypedOutcomeAndCopy()
	{
		ModeratorInstruction instruction = new FinishedGameConfirmationInstruction(
			new SingleFactionGameResult(Faction.PrejudicedManipulator),
			VictoryCheckWindow.Dawn);

		var json = JsonSerializer.Serialize(
			instruction,
			SerializationOptions);
		var restored = JsonSerializer.Deserialize<ModeratorInstruction>(
			json,
			SerializationOptions);

		var finished = restored.Should()
			.BeOfType<FinishedGameConfirmationInstruction>()
			.Subject;
		finished.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.PrejudicedManipulator));
		finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		finished.PublicAnnouncement.Should().Contain(
			GameStrings.VictoryConditionPrejudicedManipulatorOpposingGroupEliminated);
	}

	[Theory]
	[InlineData("pt-PT")]
	[InlineData("en-US")]
	public void PrejudicedManipulatorVictoryCopy_IsAvailableInEverySpecificCulture(
		string cultureName)
	{
		var originalCulture = CultureInfo.CurrentUICulture;
		var culture = CultureInfo.GetCultureInfo(cultureName);
		try
		{
			CultureInfo.CurrentUICulture = culture;
			var expected =
				GameStrings.VictoryConditionPrejudicedManipulatorOpposingGroupEliminated;

			GameStrings.ResourceManager.GetString(
					nameof(GameStrings.VictoryConditionPrejudicedManipulatorOpposingGroupEliminated),
					culture)
				.Should().Be(expected);
		}
		finally
		{
			CultureInfo.CurrentUICulture = originalCulture;
		}
	}

	[Fact]
	public void RehydrateSession_PendingResponse_RemainsCorrelated()
	{
		var originalService = new GameService();
		var startInstruction = originalService.StartNewGame(CreateConfig());
		var gameId = startInstruction.GameGuid;
		originalService.ProcessInstruction(gameId, startInstruction.CreateResponse());
		var pendingInstruction = originalService.GetCurrentInstruction(gameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var pendingResponse = pendingInstruction.CreateResponse();
		var serialized = originalService.SerializeSession(gameId);

		var recoveredService = new GameService();
		recoveredService.RehydrateSession(serialized);
		var recoveredInstruction = recoveredService.GetCurrentInstruction(gameId);

		recoveredInstruction!.InstructionId.Should().Be(pendingInstruction.InstructionId);
		recoveredService.ProcessInstruction(gameId, pendingResponse)
			.IsSuccess.Should().BeTrue();
	}

	private static GameSessionConfig CreateConfig() => new(
		["A", "B", "C", "D", "E"],
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		]);
}
