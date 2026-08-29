using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class ModeratorResponsePayloadValidationTests
{
	[Fact]
	public void ProcessInstruction_IncompleteRoleAssignments_IsSideEffectFree()
	{
		var firstPlayerId = Guid.NewGuid();
		var secondPlayerId = Guid.NewGuid();
		var (service, gameId, instruction) = CreatePendingAssignmentService(
			firstPlayerId,
			secondPlayerId);
		var response = new ModeratorResponse
		{
			InstructionId = instruction.InstructionId,
			Type = ExpectedInputType.AssignPlayerRoles,
			AssignedPlayerRoles = new Dictionary<Guid, MainRoleType>
			{
				[firstPlayerId] = MainRoleType.SimpleVillager
			}
		};
		var before = service.GetGameStateView(gameId)!.Serialize();

		var act = () => service.ProcessInstruction(gameId, response);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*payload*");
		service.GetGameStateView(gameId)!.Serialize().Should().Be(before);
		service.GetCurrentInstruction(gameId)!.InstructionId
			.Should().Be(instruction.InstructionId);
	}

	[Fact]
	public void ProcessInstruction_ExtraRoleAssignment_IsSideEffectFree()
	{
		var firstPlayerId = Guid.NewGuid();
		var secondPlayerId = Guid.NewGuid();
		var extraPlayerId = Guid.NewGuid();
		var (service, gameId, instruction) = CreatePendingAssignmentService(
			firstPlayerId,
			secondPlayerId);
		var response = new ModeratorResponse
		{
			InstructionId = instruction.InstructionId,
			Type = ExpectedInputType.AssignPlayerRoles,
			AssignedPlayerRoles = new Dictionary<Guid, MainRoleType>
			{
				[firstPlayerId] = MainRoleType.SimpleVillager,
				[secondPlayerId] = MainRoleType.SimpleVillager,
				[extraPlayerId] = MainRoleType.SimpleVillager
			}
		};
		var before = service.GetGameStateView(gameId)!.Serialize();

		var act = () => service.ProcessInstruction(gameId, response);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*payload*");
		service.GetGameStateView(gameId)!.Serialize().Should().Be(before);
		service.GetCurrentInstruction(gameId)!.InstructionId
			.Should().Be(instruction.InstructionId);
	}

	[Fact]
	public void ProcessInstruction_ResponsePayloadMutatedBeforeConsumption_IsSideEffectFree()
	{
		var selectablePlayerId = Guid.NewGuid();
		var invalidPlayerId = Guid.NewGuid();
		var instruction = new SelectPlayersInstruction(
			[selectablePlayerId],
			NumberRangeConstraint.Single,
			privateInstruction: nameof(ProcessInstruction_ResponsePayloadMutatedBeforeConsumption_IsSideEffectFree));
		var session = new GameSession(Guid.NewGuid(), instruction, CreateConfig());
		var service = new GameService();
		var gameId = service.RehydrateSession(session.Serialize());
		var mutableSelection = new HashSet<Guid> { selectablePlayerId };
		var response = new ModeratorResponse
		{
			InstructionId = instruction.InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = mutableSelection
		};
		mutableSelection.Clear();
		mutableSelection.Add(invalidPlayerId);
		var before = service.GetGameStateView(gameId)!.Serialize();

		var act = () => service.ProcessInstruction(gameId, response);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*payload*");
		service.GetGameStateView(gameId)!.Serialize().Should().Be(before);
		service.GetCurrentInstruction(gameId)!.InstructionId
			.Should().Be(instruction.InstructionId);
	}

	[Fact]
	public void ProcessInstruction_OptionIdsOutsideSemanticOrder_IsSideEffectFree()
	{
		var instruction = new SelectOptionsInstruction(
			[
				new ModeratorOption("first", "Primeiro"),
				new ModeratorOption("second", "Segundo")
			],
			NumberRangeConstraint.Exact(2),
			privateInstruction: nameof(ProcessInstruction_OptionIdsOutsideSemanticOrder_IsSideEffectFree));
		var session = new GameSession(Guid.NewGuid(), instruction, CreateConfig());
		var service = new GameService();
		var gameId = service.RehydrateSession(session.Serialize());
		var response = new ModeratorResponse
		{
			InstructionId = instruction.InstructionId,
			Type = ExpectedInputType.OptionSelection,
			SelectedOptionIds = new List<string> { "second", "first" }
		};
		var before = service.GetGameStateView(gameId)!.Serialize();

		var act = () => service.ProcessInstruction(gameId, response);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*payload*");
		service.GetGameStateView(gameId)!.Serialize().Should().Be(before);
		service.GetCurrentInstruction(gameId)!.InstructionId
			.Should().Be(instruction.InstructionId);
	}

	[Fact]
	public void ResponseFactories_CopyCallerOwnedCollections()
	{
		var selectablePlayerId = Guid.NewGuid();
		var replacementPlayerId = Guid.NewGuid();
		var selectablePlayers = new HashSet<Guid> { selectablePlayerId };
		var playerInstruction = new SelectPlayersInstruction(
			selectablePlayers,
			NumberRangeConstraint.Single,
			privateInstruction: nameof(ResponseFactories_CopyCallerOwnedCollections));
		selectablePlayers.Clear();
		selectablePlayers.Add(replacementPlayerId);

		var selectedPlayers = new HashSet<Guid> { selectablePlayerId };
		var playerResponse = playerInstruction.CreateResponse(selectedPlayers);
		selectedPlayers.Clear();
		selectedPlayers.Add(replacementPlayerId);

		var roles = new List<MainRoleType> { MainRoleType.SimpleVillager };
		var roleInstruction = new AssignRolesInstruction(
			[selectablePlayerId],
			roles,
			privateInstruction: nameof(ResponseFactories_CopyCallerOwnedCollections));
		roles[0] = MainRoleType.SimpleWerewolf;
		var assignments = new Dictionary<Guid, MainRoleType>
		{
			[selectablePlayerId] = MainRoleType.SimpleVillager
		};
		var roleResponse = roleInstruction.CreateResponse(assignments);
		assignments[selectablePlayerId] = MainRoleType.SimpleWerewolf;

		playerInstruction.SelectablePlayerIds.Should().BeEquivalentTo([selectablePlayerId]);
		playerResponse.SelectedPlayerIds.Should().BeEquivalentTo([selectablePlayerId]);
		roleInstruction.SelectableRolesForPlayers[selectablePlayerId]
			.Should().Equal(MainRoleType.SimpleVillager);
		roleResponse.AssignedPlayerRoles.Should().Contain(
			selectablePlayerId,
			MainRoleType.SimpleVillager);
	}

	[Fact]
	public void ModeratorInstruction_CopiesCallerOwnedContextCollections()
	{
		var affectedPlayerId = Guid.NewGuid();
		var affectedPlayerIds = new List<Guid> { affectedPlayerId };
		var soundEffects = new List<SoundEffectsEnum> { SoundEffectsEnum.None };
		var instruction = new CollectionInstruction(affectedPlayerIds, soundEffects);

		affectedPlayerIds.Clear();
		soundEffects.Clear();

		instruction.AffectedPlayerIds.Should().Equal(affectedPlayerId);
		instruction.SoundEffects.Should().Equal(SoundEffectsEnum.None);
	}

	private static (
		GameService Service,
		Guid GameId,
		AssignRolesInstruction Instruction) CreatePendingAssignmentService(
			Guid firstPlayerId,
			Guid secondPlayerId)
	{
		var instruction = new AssignRolesInstruction(
			[firstPlayerId, secondPlayerId],
			[MainRoleType.SimpleVillager, MainRoleType.SimpleVillager],
			privateInstruction: nameof(ModeratorResponsePayloadValidationTests));
		var session = new GameSession(
			Guid.NewGuid(),
			instruction,
			CreateConfig());
		var service = new GameService();
		var gameId = service.RehydrateSession(session.Serialize());
		return (service, gameId, instruction);
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

	private sealed record CollectionInstruction : ModeratorInstruction
	{
		public CollectionInstruction(
			IReadOnlyList<Guid> affectedPlayerIds,
			IReadOnlyList<SoundEffectsEnum> soundEffects)
			: base(
				privateInstruction: nameof(CollectionInstruction),
				affectedPlayerIds: affectedPlayerIds,
				soundEffects: soundEffects)
		{
		}
	}
}
