using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Components;

public sealed class ElderFlowBunitTests
{
	[Fact]
	public async Task VillageVoteSuppression_UnacknowledgedRecoveryRendersOneCorrelatedContinue_AndAcknowledgedRecoveryDoesNotRepeat()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var manager = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		var committedAnnouncement = AdvanceToSuppressionAnnouncement(manager);
		committedAnnouncement.InstructionId.Should().NotBeEmpty();

		var recovered = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		var recoveredAnnouncement = recovered.CurrentInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		recoveredAnnouncement.InstructionId.Should().Be(
			committedAnnouncement.InstructionId);
		recoveredAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillagerRolePowerSuppression);
		recoveredAnnouncement.PublicAnnouncement.Should().Be(
			GameStrings.VillagerRolePowerSuppressionAnnouncement);
		recoveredAnnouncement.PrivateInstruction.Should().BeNull();
		recoveredAnnouncement.AffectedPlayerIds.Should().BeNull();

		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		ModeratorResponse? receivedResponse = null;

		var cut = context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, recoveredAnnouncement)
			.Add(
				component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(
					this,
					response => receivedResponse = response)));

		cut.Find($".{ClientTestReferences.Css.Classes.InstructionAnnouncement}")
			.TextContent.Should().Contain(
				GameStrings.VillagerRolePowerSuppressionAnnouncement);
		cut.FindAll($".{ClientTestReferences.Css.Classes.InstructionPrivate}")
			.Should().BeEmpty();
		var holdButton = cut.FindButtonByAccessibleName(
			ClientStrings.Common_HoldToConfirm);
		holdButton.TextContent.Should().Contain(
			ClientStrings.Dashboard_ContinueButton);

		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);

		receivedResponse.Should().NotBeNull();
		var response = receivedResponse!;
		response.InstructionId.Should().Be(recoveredAnnouncement.InstructionId);
		response.Type.Should().Be(ExpectedInputType.Continue);
		response.SelectedPlayerIds.Should().BeNull();
		response.AssignedPlayerRoles.Should().BeNull();
		response.SelectedOptionIds.Should().BeNull();
		recovered.ProcessInput(response).IsSuccess.Should().BeTrue();

		var afterAcknowledgment = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		afterAcknowledgment.CurrentInstruction.Should().NotBeNull();
		afterAcknowledgment.CurrentInstruction!.InstructionId.Should().NotBe(
			recoveredAnnouncement.InstructionId);
		afterAcknowledgment.CurrentInstruction.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.AnnounceVillagerRolePowerSuppression);
	}

	private static ConfirmationInstruction AdvanceToSuppressionAnnouncement(
		GameClientManager manager)
	{
		var start = manager.StartGame(
			PlayerNames.DefaultFive,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Elder,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var werewolfId = players[0].Id;
		var elderId = players[1].Id;

		for (var step = 0;
		     manager.CurrentInstruction?.Semantic !=
		     ModeratorInstructionSemantic.StartDayDebate && step < 30;
		     step++)
		{
			var instruction = manager.CurrentInstruction
				?? throw new InvalidOperationException(
					"The Elder scenario ended before the Day debate.");
			var response = instruction switch
			{
				ConfirmationInstruction confirmation =>
					confirmation.CreateResponse(),
				SelectPlayersInstruction
				{
					Semantic:
						ModeratorInstructionSemantic
							.ObserveWerewolfFactionAgentGroup
				} observation => observation.CreateResponse([werewolfId]),
				SelectPlayersInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.SelectWerewolfVictim
				} victimSelection => victimSelection.CreateResponse([elderId]),
				SelectPlayersInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.IdentifyRoleHolders,
					RoleIdentification: MainRoleType.Elder
				} identification => identification.CreateResponse([elderId]),
				_ => throw new InvalidOperationException(
					$"Unexpected instruction {instruction.GetType().Name} " +
					$"({instruction.Semantic}) before the Elder vote.")
			};
			manager.ProcessInput(response).IsSuccess.Should().BeTrue();
		}

		var debate = manager.CurrentInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		debate.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		var vote = manager.ProcessInput(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		vote.Semantic.Should().Be(ModeratorInstructionSemantic.RecordDayVote);
		var reveal = manager.ProcessInput(vote.CreateResponse([elderId]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		reveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDayVoteTargetRole);
		reveal.PlayersForAssignment.Should().Equal(elderId);
		var elimination = manager.ProcessInput(reveal.CreateResponse(new()
			{
				[elderId] = MainRoleType.Elder
			}))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		elimination.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		var suppression = manager.ProcessInput(elimination.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		suppression.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillagerRolePowerSuppression);
		return suppression;
	}

	private sealed class TemporaryDirectory : IDisposable
	{
		private TemporaryDirectory(string path)
		{
			Path = path;
		}

		public string Path { get; }

		public static TemporaryDirectory Create() =>
			new(Directory.CreateTempSubdirectory(
				"werewolves-elder-client-").FullName);

		public void Dispose()
		{
			if (Directory.Exists(Path))
			{
				Directory.Delete(Path, recursive: true);
			}
		}
	}
}
