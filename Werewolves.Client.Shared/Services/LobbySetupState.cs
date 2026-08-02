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
	private bool _roleLockInFinalized;
	private bool _acceptedRoleLockInRequiresReplacement;

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
	public RoleLockIn? AcceptedRoleLockIn { get; private set; }
	public bool RequiresRoleLockIn =>
		GetRoleCount(MainRoleType.Thief) > 0 &&
		(AcceptedRoleLockIn is null || _acceptedRoleLockInRequiresReplacement);
	internal bool AcceptedRoleLockInRequiresReplacement =>
		_acceptedRoleLockInRequiresReplacement;

	public bool HasPlayerConfigIssues(out List<GameConfigValidationError> issues)
	{
		return GameSessionConfig.TryGetPlayerConfigIssues(_playerNames, out issues);
	}

	public bool HasRoleConfigIssues(out List<GameConfigValidationError> issues)
	{
		List<GameConfigValidationError> configIssues;
		if (AcceptedRoleLockIn is { } acceptedRoleLockIn &&
			!_acceptedRoleLockInRequiresReplacement)
		{
			GameSessionConfig.TryGetRoleLockInConfigIssues(
				_playerNames,
				acceptedRoleLockIn,
				ActorSetupCards.None,
				out configIssues);
		}
		else
		{
			var selectedRoles = GetSelectedRoles();
			GameSessionConfig.TryGetConfigIssues(
				_playerNames,
				selectedRoles,
				out configIssues);
			if (selectedRoles.Contains(MainRoleType.Thief) &&
				HasValidThiefRoleLockInPartition(selectedRoles))
			{
				configIssues = [];
			}
		}
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
		OnLobbyDraftChanged();
		return AddPlayerResult.Success;
	}

	public bool RemovePlayerAt(int index)
	{
		if (index < 0 || index >= _playerNames.Count)
		{
			return false;
		}

		_playerNames.RemoveAt(index);
		OnLobbyDraftChanged();
		return true;
	}

	public bool MovePlayerUp(int index)
	{
		if (!CanMovePlayerUp(index))
		{
			return false;
		}

		(_playerNames[index - 1], _playerNames[index]) = (_playerNames[index], _playerNames[index - 1]);
		OnLobbyDraftChanged(simulationScenarioChanged: AcceptedRoleLockIn is not null);
		return true;
	}

	public bool MovePlayerDown(int index)
	{
		if (!CanMovePlayerDown(index))
		{
			return false;
		}

		(_playerNames[index], _playerNames[index + 1]) = (_playerNames[index + 1], _playerNames[index]);
		OnLobbyDraftChanged(simulationScenarioChanged: AcceptedRoleLockIn is not null);
		return true;
	}

	public int GetRoleCount(MainRoleType role)
	{
		return _roleCounts.GetValueOrDefault(role, 0);
	}

	public void IncrementRole(MainRoleType role)
	{
		var constraint = GetAvailableRoleMetadata(role).CountConstraint;
		var (affordance, batchSize, maximum) = GetRoleSelectionPolicy(role, constraint);
		var current = GetRoleCount(role);

		if (affordance == RoleAffordance.Toggle)
		{
			if (current == 0)
			{
				_roleCounts[role] = batchSize;
				OnLobbyDraftChanged();
			}
		}
		else
		{
			if (current < maximum)
			{
				_roleCounts[role] = current + 1;
				OnLobbyDraftChanged();
			}
		}
	}

	public void DecrementRole(MainRoleType role)
	{
		var current = GetRoleCount(role);
		if (current <= 0)
			return;

		var constraint = GetAvailableRoleMetadata(role).CountConstraint;
		var (affordance, _, _) = GetRoleSelectionPolicy(role, constraint);

		if (affordance == RoleAffordance.Toggle)
			_roleCounts[role] = 0;
		else
			_roleCounts[role] = current - 1;

		OnLobbyDraftChanged();
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
		AcceptedRoleLockIn is { } acceptedRoleLockIn &&
			!_acceptedRoleLockInRequiresReplacement
			? new SimulationScenario(acceptedRoleLockIn)
			: new SimulationScenario(_playerNames.Count, GetSelectedRoles());

	public bool TryCreateSimulationScenario(out SimulationScenario scenario)
	{
		if (RequiresRoleLockIn)
		{
			scenario = null!;
			return false;
		}

		scenario = CreateSimulationScenario();
		return true;
	}

	public void Reset()
	{
		if (_playerNames.Count == 0 &&
			_roleCounts.Values.All(count => count == 0) &&
			AcceptedRoleLockIn is null &&
			!_roleLockInFinalized)
		{
			return;
		}

		_playerNames.Clear();
		_roleCounts.Clear();
		AcceptedRoleLockIn = null;
		_roleLockInFinalized = false;
		_acceptedRoleLockInRequiresReplacement = false;
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
		var (affordance, batchSize, maximum) = GetRoleSelectionPolicy(role, constraint);
		var count = GetRoleCount(role);
		var canIncrement = affordance == RoleAffordance.Toggle
			? count == 0
			: count < maximum;
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

	private (RoleAffordance Affordance, int BatchSize, int Maximum)
		GetRoleSelectionPolicy(
			MainRoleType role,
			NumberRangeConstraint constraint)
	{
		if (GetRoleCount(MainRoleType.Thief) > 0 &&
			constraint == NumberRangeConstraint.SingleOptional &&
			RoleLockIn.IsOfferEligible(role))
		{
			return (RoleAffordance.Stepper, BatchSize: 1, Maximum: 2);
		}

		var (affordance, batchSize) = ClassifyConstraint(constraint);
		return (affordance, batchSize, constraint.Maximum);
	}

	private bool HasValidThiefRoleLockInPartition(
		IReadOnlyCollection<MainRoleType> selectedRoles)
	{
		var offerCandidates = selectedRoles
			.Where(RoleLockIn.IsOfferEligible)
			.Distinct()
			.ToArray();
		foreach (var offer1 in offerCandidates)
		{
			foreach (var offer2 in offerCandidates)
			{
				try
				{
					var candidate = RoleLockIn.CreateFromPrintedRoles(
						version: 1,
						_playerNames.Count,
						selectedRoles,
						offer1,
						offer2);
					if (!GameSessionConfig.TryGetRoleLockInConfigIssues(
							_playerNames,
							candidate,
							ActorSetupCards.None,
							out _))
					{
						return true;
					}
				}
				catch (ArgumentException)
				{
				}
			}
		}

		return false;
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

	internal bool CanReplaceRoleLockIn(
		long expectedCurrentVersion,
		RoleLockIn replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);
		var currentVersion = AcceptedRoleLockIn?.Version ?? 0;
		if (_roleLockInFinalized ||
			expectedCurrentVersion != currentVersion ||
			expectedCurrentVersion == long.MaxValue ||
			replacement.Version != expectedCurrentVersion + 1 ||
			replacement.PlayerCount != _playerNames.Count ||
			replacement.RoleComposition.Any(card => !_availableRoleMetadata.ContainsKey(card.PrintedRole)))
		{
			return false;
		}

		return !GameSessionConfig.TryGetRoleLockInConfigIssues(
			_playerNames,
			replacement,
			ActorSetupCards.None,
			out _);
	}

	internal void ApplyAcceptedRoleLockIn(RoleLockIn replacement)
	{
		AcceptedRoleLockIn = replacement;
		ApplyRoleComposition(replacement);
		_acceptedRoleLockInRequiresReplacement = false;
		OnSimulationScenarioChanged();
	}

	internal void RestoreAcceptedRoleLockIn(
		IReadOnlyList<string> playerNames,
		RoleLockIn roleLockIn)
	{
		ArgumentNullException.ThrowIfNull(playerNames);
		ArgumentNullException.ThrowIfNull(roleLockIn);
		var names = playerNames.Select(name => name.Trim()).ToArray();
		if (GameSessionConfig.TryGetPlayerConfigIssues(names.ToList(), out _) ||
			roleLockIn.PlayerCount != names.Length ||
			roleLockIn.RoleComposition.Any(card => !_availableRoleMetadata.ContainsKey(card.PrintedRole)) ||
			GameSessionConfig.TryGetRoleLockInConfigIssues(
				names.ToList(),
				roleLockIn,
				ActorSetupCards.None,
				out _))
		{
			throw new InvalidOperationException("The staged Lobby recovery payload is invalid.");
		}

		_playerNames.Clear();
		_playerNames.AddRange(names);
		ApplyRoleComposition(roleLockIn);
		AcceptedRoleLockIn = roleLockIn;
		_roleLockInFinalized = false;
		_acceptedRoleLockInRequiresReplacement = false;
		OnSimulationScenarioChanged();
	}

	internal void FinalizeRoleLockIn(RoleLockIn roleLockIn)
	{
		ArgumentNullException.ThrowIfNull(roleLockIn);
		if (_acceptedRoleLockInRequiresReplacement)
		{
			throw new InvalidOperationException(
				"Lobby Exit requires a fresh accepted Role Lock-In after Lobby edits.");
		}
		if (AcceptedRoleLockIn is not null && AcceptedRoleLockIn.Version != roleLockIn.Version)
		{
			throw new InvalidOperationException("Lobby Exit must finalize the latest accepted Role Lock-In.");
		}
		AcceptedRoleLockIn = roleLockIn;
		_roleLockInFinalized = true;
	}

	private void ApplyRoleComposition(RoleLockIn roleLockIn)
	{
		_roleCounts.Clear();
		foreach (var roleGroup in roleLockIn.RoleComposition
			.GroupBy(card => card.PrintedRole))
		{
			_roleCounts[roleGroup.Key] = roleGroup.Count();
		}
	}

	private void OnLobbyDraftChanged(bool simulationScenarioChanged = true)
	{
		_acceptedRoleLockInRequiresReplacement = AcceptedRoleLockIn is not null;
		if (simulationScenarioChanged)
		{
			OnSimulationScenarioChanged();
		}
	}

	private void OnSimulationScenarioChanged() =>
		SimulationScenarioChanged?.Invoke(this, EventArgs.Empty);
}
