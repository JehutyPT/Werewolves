using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public sealed class BaselineRandomDecisionStrategy : IModeratorDecisionStrategy
{
	private readonly SimulationStartState _startState;
	private readonly DeterministicRandomSource _random;
	private readonly HeadlessResponsePolicy _policy;

	public static DecisionStrategyIdentity Identity { get; } =
		new("baseline-random", "1-splitmix64");

	public static HeadlessResponsePolicy Policy { get; } = new(
		Identity,
		[
			ModeratorInstructionSemantic.StartGame,
			ModeratorInstructionSemantic.FinishedGame,
			ModeratorInstructionSemantic.StartNight,
			ModeratorInstructionSemantic.FinishNightActions,
			ModeratorInstructionSemantic.WakeRole,
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			ModeratorInstructionSemantic.PutRoleToSleep,
			ModeratorInstructionSemantic.SelectWerewolfVictim,
			ModeratorInstructionSemantic.SelectSeerTarget,
			ModeratorInstructionSemantic.RevealSeerResult,
			ModeratorInstructionSemantic.SelectWildChildModel,
			ModeratorInstructionSemantic.AnnounceDawnVictims,
			ModeratorInstructionSemantic.AssignDawnVictimRoles,
			ModeratorInstructionSemantic.StartDayDebate,
			ModeratorInstructionSemantic.RecordDayVote,
			ModeratorInstructionSemantic.AssignDayVoteTargetRole,
			ModeratorInstructionSemantic.AnnounceLynchingImmunity,
			ModeratorInstructionSemantic.AnnounceDayElimination,
			ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal
		]);

	public BaselineRandomDecisionStrategy(
		RunSeedMaterial material,
		SimulationStartState startState,
		HeadlessResponsePolicy policy)
		: this(material, startState, policy, new DeterministicRandomSource(material))
	{
	}

	internal BaselineRandomDecisionStrategy(
		RunSeedMaterial material,
		SimulationStartState startState,
		HeadlessResponsePolicy policy,
		DeterministicRandomSource random)
	{
		ArgumentNullException.ThrowIfNull(material);
		ArgumentNullException.ThrowIfNull(startState);
		ArgumentNullException.ThrowIfNull(policy);
		ArgumentNullException.ThrowIfNull(random);
		if (!material.DecisionStrategyIdentity.Equals(Identity)
			|| !policy.StrategyIdentity.Equals(Identity)
			|| !material.CompatibilityIdentity.Equals(startState.CompatibilityIdentity)
			|| !random.Material.Equals(material))
		{
			throw new ArgumentException(
				"The strategy, Run Seed Material, and Simulation Start State identities must match.");
		}

		_startState = startState;
		_policy = policy;
		_random = random;
	}

	public ModeratorResponse CreateResponse(
		ModeratorInstruction instruction,
		IGameSession session)
	{
		ArgumentNullException.ThrowIfNull(instruction);
		ArgumentNullException.ThrowIfNull(session);

		if (!_policy.Admits(instruction.Semantic))
		{
			throw new NotSupportedException(
				$"The selected headless response policy does not admit semantic '{instruction.Semantic}'.");
		}

		return instruction switch
		{
			ConfirmationInstruction confirmation => confirmation.CreateResponse(),
			SelectPlayersInstruction { RoleIdentification: not null } selectPlayers =>
				CreateRoleIdentificationResponse(selectPlayers, session),
			SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal
			} selectPlayers =>
				CreateSeededRoleHolderResponse(
					selectPlayers,
					session,
					MainRoleType.VillagerVillager),
			SelectPlayersInstruction selectPlayers =>
				CreatePlayerSelectionResponse(selectPlayers, session),
			AssignRolesInstruction assignRoles =>
				CreateRoleAssignmentResponse(assignRoles, session),
			SelectOptionsInstruction selectOptions =>
				CreateOptionSelectionResponse(selectOptions),
			_ => throw new NotSupportedException(
				$"Unsupported moderator instruction type: {instruction.GetType().Name}")
		};
	}

	private ModeratorResponse CreateRoleIdentificationResponse(
		SelectPlayersInstruction instruction,
		IGameSession session)
	{
		var players = GetPlayersMatchingStartState(session);
		var effectiveRolesByPlayerId = CreateEffectiveRolesByPlayerId(players);
		var selectedPlayerIds = players
			.Where(player => instruction.SelectablePlayerIds.Contains(player.Id))
			.Where(player =>
				effectiveRolesByPlayerId[player.Id] == instruction.RoleIdentification!.Value)
			.Select(player => player.Id)
			.ToHashSet();
		return instruction.CreateResponse(selectedPlayerIds);
	}

	private ModeratorResponse CreateSeededRoleHolderResponse(
		SelectPlayersInstruction instruction,
		IGameSession session,
		MainRoleType role)
	{
		var players = GetPlayersMatchingStartState(session);
		var selectedPlayerIds = _startState.RoleAssignments
			.Where(assignment => assignment.Role == role)
			.Select(assignment => players[assignment.SeatNumber - 1].Id)
			.Where(instruction.SelectablePlayerIds.Contains)
			.ToHashSet();
		return instruction.CreateResponse(selectedPlayerIds);
	}

	private ModeratorResponse CreatePlayerSelectionResponse(
		SelectPlayersInstruction instruction,
		IGameSession session)
	{
		var candidates = session.GetPlayers()
			.Where(player => instruction.SelectablePlayerIds.Contains(player.Id))
			.Select(player => player.Id)
			.ToArray();
		var selected = ChooseUniformValidSubset(candidates, instruction.CountConstraint);
		return instruction.CreateResponse(selected.ToHashSet());
	}

	private ModeratorResponse CreateRoleAssignmentResponse(
		AssignRolesInstruction instruction,
		IGameSession session)
	{
		var players = GetPlayersMatchingStartState(session);
		var effectiveRolesByPlayerId = CreateEffectiveRolesByPlayerId(players);
		if (instruction.PlayersForAssignment.Any(playerId =>
			!effectiveRolesByPlayerId.ContainsKey(playerId)))
		{
			throw new InvalidOperationException(
				"Role assignment instruction refers to a Player outside the Game Session.");
		}

		var assignments = instruction.PlayersForAssignment.ToDictionary(
			playerId => playerId,
			playerId => effectiveRolesByPlayerId[playerId]);
		return instruction.CreateResponse(assignments);
	}

	private IPlayer[] GetPlayersMatchingStartState(IGameSession session)
	{
		var players = session.GetPlayers().ToArray();
		if (players.Length != _startState.PlayerCount)
		{
			throw new InvalidOperationException(
				"The Game Session player count does not match the Simulation Start State.");
		}

		return players;
	}

	private Dictionary<Guid, MainRoleType> CreateEffectiveRolesByPlayerId(
		IReadOnlyList<IPlayer> players) =>
		_startState.RoleAssignments.ToDictionary(
			assignment => players[assignment.SeatNumber - 1].Id,
			assignment => players[assignment.SeatNumber - 1].State.CurrentRole
				?? assignment.Role);

	private ModeratorResponse CreateOptionSelectionResponse(SelectOptionsInstruction instruction)
	{
		var candidates = instruction.Options
			.Select(option => option.Id)
			.ToArray();
		var selected = ChooseUniformValidSubset(candidates, instruction.SelectionRange);
		return instruction.CreateResponse(selected);
	}

	private IReadOnlyList<T> ChooseUniformValidSubset<T>(
		IReadOnlyList<T> candidates,
		NumberRangeConstraint constraint)
	{
		var maximum = Math.Min(constraint.Maximum, candidates.Count);
		var counts = new SortedSet<int>();
		if (constraint.IsOptional)
		{
			counts.Add(0);
		}

		for (var count = Math.Max(0, constraint.Minimum); count <= maximum; count++)
		{
			counts.Add(count);
		}

		if (counts.Count == 0)
		{
			throw new InvalidOperationException(
				"The moderator instruction has no valid response for its available choices.");
		}

		var weightedCounts = counts
			.Select(count => (Count: count, Weight: CombinationCount(candidates.Count, count)))
			.ToArray();
		var totalWeight = weightedCounts.Aggregate(0UL, (total, item) => checked(total + item.Weight));
		var choice = _random.NextUInt64(totalWeight);
		var selectedCount = 0;
		foreach (var item in weightedCounts)
		{
			if (choice < item.Weight)
			{
				selectedCount = item.Count;
				break;
			}

			choice -= item.Weight;
		}

		var shuffled = candidates.ToList();
		_random.Shuffle(shuffled);
		return shuffled.Take(selectedCount).ToArray();
	}

	private static ulong CombinationCount(int population, int selection)
	{
		if (selection < 0 || selection > population)
		{
			return 0;
		}

		selection = Math.Min(selection, population - selection);
		var result = 1UL;
		for (var index = 1; index <= selection; index++)
		{
			result = checked(result * (ulong)(population - selection + index) / (ulong)index);
		}

		return result;
	}
}
