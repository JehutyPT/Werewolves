using System.Text.Json;
using FluentAssertions;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ExecutionPublicationTests
{
	[Fact]
	public void CandidateSerializationFailure_DoesNotPublishInstructionOrBoundary()
	{
		var reaction = new UnsupportedInstructionReaction();
		var builder = GameTestBuilder.Create()
			.WithEliminationCascadeReaction(
				reaction,
				EliminationCascadeReactionBoundary.Interactive)
			.WithPlayers(
				"Werewolf",
				"Witch",
				"Attack victim",
				"Poison victim",
				"Villager 1",
				"Villager 2")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		reaction.Configure([players[2].Id, players[3].Id]);

		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction(
			[players[0].Id],
			players[2].Id);
		var witchIdentification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var healing = builder.Process(
				witchIdentification.CreateResponse([players[1].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var poison = builder.Process(healing.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var sleep = builder.Process(poison.CreateResponse([players[3].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = builder.Process(sleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var pendingReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var recoveryBeforeFailure = new GameService();
		var recoveryBeforeFailureGameId = recoveryBeforeFailure.RehydrateSession(
			builder.SerializeSession());
		var instructionBeforeFailure = recoveryBeforeFailure
			.GetCurrentInstruction(recoveryBeforeFailureGameId)!;
		var stablePlayerStatesBeforeFailure = recoveryBeforeFailure
			.GetGameStateView(recoveryBeforeFailureGameId)!
			.GetPlayers()
			.Where(player =>
				player.Id == players[2].Id || player.Id == players[3].Id)
			.ToDictionary(
				player => player.Id,
				player => (
					player.State.PubliclyRevealedRole,
					player.State.PhysicalCharacterCardId,
					player.State.PhysicalCharacterCardRole));
		Action process = () => builder.Process(
			pendingReveal.CreateObservedRoleResponse(new Dictionary<Guid, MainRoleType>
			{
				[players[2].Id] = MainRoleType.SimpleVillager,
				[players[3].Id] = MainRoleType.SimpleVillager
			}));

		process.Should().Throw<JsonException>();
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			pendingReveal.InstructionId);

		var failedState = builder.GetGameState()!;
		failedState.GetPlayers()
			.Where(player =>
				player.Id == players[2].Id || player.Id == players[3].Id)
			.Should().AllSatisfy(player =>
			{
				player.State.PubliclyRevealedRole.Should().Be(
					MainRoleType.SimpleVillager);
				player.State.PhysicalCharacterCardId.Should().NotBeNull();
			});

		var recoveryAfterFailure = new GameService();
		var recoveryAfterFailureGameId = recoveryAfterFailure.RehydrateSession(
			builder.SerializeSession());
		var instructionAfterFailure = recoveryAfterFailure.GetCurrentInstruction(
			recoveryAfterFailureGameId);
		instructionAfterFailure.Should().NotBeNull();
		instructionAfterFailure!.GetType().Should().Be(
			instructionBeforeFailure.GetType());
		instructionAfterFailure.InstructionId.Should().Be(
			instructionBeforeFailure.InstructionId);
		instructionAfterFailure.Semantic.Should().Be(
			instructionBeforeFailure.Semantic);
		recoveryAfterFailure.GetGameStateView(recoveryAfterFailureGameId)!
			.GetPlayers()
			.Where(player => stablePlayerStatesBeforeFailure.ContainsKey(player.Id))
			.Should().AllSatisfy(player =>
			{
				var expected = stablePlayerStatesBeforeFailure[player.Id];
				player.State.PubliclyRevealedRole.Should().Be(
					expected.PubliclyRevealedRole);
				player.State.PhysicalCharacterCardId.Should().Be(
					expected.PhysicalCharacterCardId);
				player.State.PhysicalCharacterCardRole.Should().Be(
					expected.PhysicalCharacterCardRole);
			});
	}

	private sealed class UnsupportedInstructionReaction
		: IEliminationCascadeReaction
	{
		private HashSet<Guid> _triggerPlayerIds = [];

		public string ReactionId => nameof(UnsupportedInstructionReaction);

		internal void Configure(IEnumerable<Guid> triggerPlayerIds) =>
			_triggerPlayerIds = triggerPlayerIds.ToHashSet();

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input) =>
			_triggerPlayerIds.SetEquals(eliminatedPlayerIds)
				? EliminationCascadeReactionResult.NeedInput(
					new UnsupportedInstruction())
				: EliminationCascadeReactionResult.Complete();
	}

	private sealed record UnsupportedInstruction : ModeratorInstruction
	{
		internal UnsupportedInstruction()
			: base(privateInstruction: "Unsupported test instruction") { }
	}
}
