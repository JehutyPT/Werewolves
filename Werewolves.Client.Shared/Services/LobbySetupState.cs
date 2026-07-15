using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.Services;

public enum AddPlayerResult { Success, EmptyName, DuplicateName }

public enum RoleAffordance { Stepper, Toggle }

public record RoleInfo(
	MainRoleType Role,
	string DisplayName,
	RoleGroup Group,
	string GroupDisplayName,
	int Count,
	RoleAffordance Affordance,
	int BatchSize,
	bool CanIncrement,
	bool CanDecrement);

public record RoleSelectionGroupInfo(
	RoleGroup Group,
	string DisplayName,
	IReadOnlyList<RoleInfo> Roles);

public class LobbySetupState
{
	private static readonly RoleGroup[] RoleSelectionGroupOrder =
		[RoleGroup.Villagers, RoleGroup.Werewolves, RoleGroup.Ambiguous, RoleGroup.Loners, RoleGroup.NewMoon];

	private readonly List<string> _playerNames = new();
	private readonly Dictionary<MainRoleType, int> _roleCounts = new();
	private readonly LobbySetupMetadata _setupMetadata;
	private readonly IReadOnlyList<MainRoleType> _availableRoles;
	private readonly Dictionary<MainRoleType, LobbySetupRoleMetadata> _availableRoleMetadata;

	public LobbySetupState(LobbySetupMetadata setupMetadata)
	{
		_setupMetadata = setupMetadata;
		_availableRoles = setupMetadata.AvailableRoles.Select(role => role.Role).ToArray();
		_availableRoleMetadata = setupMetadata.AvailableRoles.ToDictionary(role => role.Role);
	}

	public event EventHandler? SimulationScenarioChanged;

	public int MinimumPlayerCount => _setupMetadata.MinimumPlayerCount;
	public IReadOnlyList<MainRoleType> AvailableRoles => _availableRoles;
	public IReadOnlyList<RoleInfo> AvailableRoleInfos => _availableRoles.Select(GetRoleInfo).ToArray();
	public IReadOnlyList<RoleSelectionGroupInfo> AvailableRoleGroups => GetAvailableRoleGroups();

	public IReadOnlyList<string> PlayerNames => _playerNames;

	public bool HasPlayerConfigIssues(out List<GameConfigValidationError> issues)
	{
		return GameSessionConfig.TryGetPlayerConfigIssues(_playerNames, out issues);
	}

	public bool HasRoleConfigIssues(out List<GameConfigValidationError> issues)
	{
		GameSessionConfig.TryGetConfigIssues(_playerNames, GetSelectedRoles(), out var configIssues);
		issues = configIssues
			.Where(issue => issue.Type is not GameConfigValidationErrorType.TooFewPlayers
				and not GameConfigValidationErrorType.NonUniquePlayerNames)
			.ToList();

		return issues.Count > 0;
	}

	public bool CanMovePlayerUp(int index) => index > 0 && index < _playerNames.Count;

	public bool CanMovePlayerDown(int index) => index >= 0 && index < _playerNames.Count - 1;

	public AddPlayerResult AddPlayer(string playerName)
	{
		var normalizedName = playerName.Trim();
		if (normalizedName.Length == 0)
		{
			return AddPlayerResult.EmptyName;
		}

		if (_playerNames.Any(n => string.Equals(n, normalizedName, StringComparison.OrdinalIgnoreCase)))
		{
			return AddPlayerResult.DuplicateName;
		}

		_playerNames.Add(normalizedName);
		OnSimulationScenarioChanged();
		return AddPlayerResult.Success;
	}

	public bool RemovePlayerAt(int index)
	{
		if (index < 0 || index >= _playerNames.Count)
		{
			return false;
		}

		_playerNames.RemoveAt(index);
		OnSimulationScenarioChanged();
		return true;
	}

	public bool MovePlayerUp(int index)
	{
		if (!CanMovePlayerUp(index))
		{
			return false;
		}

		(_playerNames[index - 1], _playerNames[index]) = (_playerNames[index], _playerNames[index - 1]);
		return true;
	}

	public bool MovePlayerDown(int index)
	{
		if (!CanMovePlayerDown(index))
		{
			return false;
		}

		(_playerNames[index], _playerNames[index + 1]) = (_playerNames[index + 1], _playerNames[index]);
		return true;
	}

	public int GetRoleCount(MainRoleType role)
	{
		return _roleCounts.GetValueOrDefault(role, 0);
	}

	public void IncrementRole(MainRoleType role)
	{
		var constraint = GetAvailableRoleMetadata(role).CountConstraint;
		var (affordance, batchSize) = ClassifyConstraint(constraint);
		var current = GetRoleCount(role);

		if (affordance == RoleAffordance.Toggle)
		{
			if (current == 0)
			{
				_roleCounts[role] = batchSize;
				OnSimulationScenarioChanged();
			}
		}
		else
		{
			if (current < constraint.Maximum)
			{
				_roleCounts[role] = current + 1;
				OnSimulationScenarioChanged();
			}
		}
	}

	public void DecrementRole(MainRoleType role)
	{
		var current = GetRoleCount(role);
		if (current <= 0)
			return;

		var constraint = GetAvailableRoleMetadata(role).CountConstraint;
		var (affordance, _) = ClassifyConstraint(constraint);

		if (affordance == RoleAffordance.Toggle)
			_roleCounts[role] = 0;
		else
			_roleCounts[role] = current - 1;

		OnSimulationScenarioChanged();
	}

	public List<MainRoleType> GetSelectedRoles()
	{
		var roles = new List<MainRoleType>();
		foreach (var (role, count) in _roleCounts)
		{
			for (var i = 0; i < count; i++)
				roles.Add(role);
		}
		return roles;
	}

	public SimulationScenario CreateSimulationScenario() =>
		new(_playerNames.Count, GetSelectedRoles());

	public void Reset()
	{
		if (_playerNames.Count == 0 && _roleCounts.Values.All(count => count == 0))
		{
			return;
		}

		_playerNames.Clear();
		_roleCounts.Clear();
		OnSimulationScenarioChanged();
	}

	public int TotalSelectedRoleCount => _roleCounts.Values.Sum();

	public int ExpectedRoleCount =>
		GameSessionConfig.GetExpectedRoleCount(_playerNames.Count, GetSelectedRoles());

	public bool CanDecrementRole(MainRoleType role) => GetRoleCount(role) > 0;

	public RoleInfo GetRoleInfo(MainRoleType role)
	{
		var roleMetadata = GetAvailableRoleMetadata(role);
		var constraint = roleMetadata.CountConstraint;
		var (affordance, batchSize) = ClassifyConstraint(constraint);
		var count = GetRoleCount(role);
		var canIncrement = affordance == RoleAffordance.Toggle
			? count == 0
			: count < constraint.Maximum;
		var canDecrement = count > 0;

		return new RoleInfo(
			roleMetadata.Role,
			roleMetadata.DisplayName,
			roleMetadata.Group,
			roleMetadata.GroupDisplayName,
			count,
			affordance,
			batchSize,
			canIncrement,
			canDecrement);
	}

	public bool HasConfigIssues(out List<GameConfigValidationError> issues)
	{
		return GameSessionConfig.TryGetConfigIssues(_playerNames, GetSelectedRoles(), out issues);
	}

	private IReadOnlyList<RoleSelectionGroupInfo> GetAvailableRoleGroups()
	{
		var roleInfosByGroup = AvailableRoleInfos
			.GroupBy(info => info.Group)
			.ToDictionary(group => group.Key, group => (IReadOnlyList<RoleInfo>)group.ToArray());

		return RoleSelectionGroupOrder
			.Where(roleInfosByGroup.ContainsKey)
			.Select(group => new RoleSelectionGroupInfo(
				group,
				group.GetDisplayName(),
				roleInfosByGroup[group]))
			.ToArray();
	}

	private static (RoleAffordance Affordance, int BatchSize) ClassifyConstraint(NumberRangeConstraint constraint)
	{
		if (!constraint.IsOptional)
			return (RoleAffordance.Stepper, 1);
		return (RoleAffordance.Toggle, constraint.Minimum);
	}

	private LobbySetupRoleMetadata GetAvailableRoleMetadata(MainRoleType role)
	{
		if (_availableRoleMetadata.TryGetValue(role, out var metadata))
		{
			return metadata;
		}

		// Developer-facing guard, not rendered UI copy.
		throw new InvalidOperationException($"Role {role} is not available in lobby setup metadata.");
	}

	private void OnSimulationScenarioChanged() =>
		SimulationScenarioChanged?.Invoke(this, EventArgs.Empty);
}
