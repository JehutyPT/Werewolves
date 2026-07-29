using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class WolfHoundConsumerTests : DiagnosticTestBase
{
	public WolfHoundConsumerTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void WerewolfAlignedWolfHound_SeerReportsWerewolfAgentWhileExactRoleRemainsWolfHound()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.WolfHound,
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var wolfHound = players[0];
		var werewolf = players[1];
		var seer = players[2];
		var victim = players[5];
		ChooseWerewolfAlignment(builder, wolfHound.Id);

		var seerIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteWerewolfNightAction(
					[wolfHound.Id, werewolf.Id],
					victim.Id));
		var seerTargetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(
					seerIdentification.CreateResponse([seer.Id])));

		var feedback =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					seerTargetSelection.CreateResponse([wolfHound.Id])));

		feedback.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealSeerResult);
		feedback.PrivateInstruction.Should().Be(
			GameStrings.SeerResultWerewolfTeam.Format(wolfHound.Name));
		builder.GetGameState()!.GetPlayerState(wolfHound.Id)
			.CurrentRole.Should().Be(MainRoleType.WolfHound);
		MarkTestCompleted();
	}

	[Fact]
	public void WerewolfAlignedWolfHound_LaterNightJoinsWerewolfCollectiveWithoutRepeatingAlignmentCall()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.WolfHound,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var wolfHound = players[0];
		var werewolf = players[1];
		var firstVictim = players[5];
		var secondVictim = players[4];
		var werewolfAgents = new[] { wolfHound.Id, werewolf.Id };
		ChooseWerewolfAlignment(builder, wolfHound.Id);

		var finishFirstNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					werewolfAgents.ToHashSet(),
					firstVictim.Id));
		finishFirstNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		builder.Process(finishFirstNight.CreateResponse());
		builder.CompleteDawnPhase(
			new Dictionary<Guid, MainRoleType>
			{
				[firstVictim.Id] = MainRoleType.SimpleVillager
			});
		builder.CompleteDayPhaseWithTie();

		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().BeEquivalentTo(werewolfAgents);
		builder.GetGameState()!.GetPlayerState(wolfHound.Id)
			.Health.Should().Be(PlayerHealth.Alive);

		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		victimSelection.AffectedPlayerIds.Should()
			.BeEquivalentTo(werewolfAgents);
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					victimSelection.CreateResponse([secondVictim.Id])));
		var finishSecondNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));

		new[]
			{
				wake.Semantic,
				victimSelection.Semantic,
				sleep.Semantic,
				finishSecondNight.Semantic
			}
			.Should().NotContain(
				ModeratorInstructionSemantic.ChooseWolfHoundAlignment);
		finishSecondNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		MarkTestCompleted();
	}

	private static void ChooseWerewolfAlignment(
		GameTestBuilder builder,
		Guid wolfHoundId)
	{
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.WolfHound);
		var alignment =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(
					identification.CreateResponse([wolfHoundId])));
		alignment.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseWolfHoundAlignment);
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					alignment.CreateResponse(
						WolfHoundAlignmentOptionIds.Werewolves)));
		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		builder.Process(sleep.CreateResponse());
	}
}
