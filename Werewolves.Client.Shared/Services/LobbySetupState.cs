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

	private readonly List<GameSessionPlayerConfig> _playerRoster = new();
	private readonly HashSet<Guid> _issuedPlayerIds = new();
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

	public IReadOnlyList<GameSessionPlayerConfig> PlayerRoster => _playerRoster.AsReadOnly();
	public IReadOnlyList<string> PlayerNames => Array.AsReadOnly(
		_playerRoster.Select(player => player.Name).ToArray());
	public RoleLockIn? AcceptedRoleLockIn { get; private set; }
	public PublicGroupPartition? AcceptedPublicGroupPartition { get; private set; }
	public bool RequiresRoleLockIn =>
		_acceptedRoleLockInRequiresReplacement ||
		GetRoleCount(MainRoleType.Thief) > 0 && AcceptedRoleLockIn is null;
	public bool RequiresPublicGroupPartition =>
		AcceptedRoleLockIn is { } acceptedRoleLockIn &&
		!_acceptedRoleLockInRequiresReplacement &&
		IsPrejudicedManipulatorReachable(acceptedRoleLockIn) &&
		AcceptedPublicGroupPartition is null;
	internal bool AcceptedRoleLockInRequiresReplacement =>
		_acceptedRoleLockInRequiresReplacement;

	public bool HasPlayerConfigIssues(out List<GameConfigValidationError> issues)
	{
		return GameSessionConfig.TryGetPlayerConfigIssues(GetPlayerNames(), out issues);
	}

	public bool HasRoleConfigIssues(out List<GameConfigValidationError> issues)
	{
		var playerNames = GetPlayerNames();
		List<GameConfigValidationError> configIssues;
		if (AcceptedRoleLockIn is { } acceptedRoleLockIn &&
			!_acceptedRoleLockInRequiresReplacement)
		{
			GameSessionConfig.TryGetRoleLockInConfigIssues(
				playerNames,
				acceptedRoleLockIn,
				ActorSetupCards.None,
				out configIssues);
		}
		else
		{
			var selectedRoles = GetSelectedRoles();
			GameSessionConfig.TryGetConfigIssues(
				playerNames,
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

	public bool CanMovePlayerUp(int index) => index > 0 && index < _playerRoster.Count;

	public bool CanMovePlayerDown(int index) => index >= 0 && index < _playerRoster.Count - 1;

	public AddPlayerResult AddPlayer(string playerName)
	{
		var normalizedName = playerName.Trim();
		if (normalizedName.Length == 0)
		{
			return AddPlayerResult.EmptyName;
		}

		if (_playerRoster.Any(player => string.Equals(
			player.Name,
			normalizedName,
			StringComparison.OrdinalIgnoreCase)))
		{
			return AddPlayerResult.DuplicateName;
		}

		_playerRoster.Add(CreatePlayerRosterEntry(normalizedName));
		AcceptedPublicGroupPartition = null;
		OnLobbyDraftChanged();
		return AddPlayerResult.Success;
	}

	public bool RemovePlayerAt(int index)
	{
		if (index < 0 || index >= _playerRoster.Count)
		{
			return false;
		}

		_playerRoster.RemoveAt(index);
		AcceptedPublicGroupPartition = null;
		OnLobbyDraftChanged();
		return true;
	}

	public bool MovePlayerUp(int index)
	{
		if (!CanMovePlayerUp(index))
		{
			return false;
		}

		(_playerRoster[index - 1], _playerRoster[index]) =
			(_playerRoster[index], _playerRoster[index - 1]);
		if (AcceptedRoleLockIn is not null)
		{
			OnSimulationScenarioChanged();
		}
		return true;
	}

	public bool MovePlayerDown(int index)
	{
		if (!CanMovePlayerDown(index))
		{
			return false;
		}

		(_playerRoster[index], _playerRoster[index + 1]) =
			(_playerRoster[index + 1], _playerRoster[index]);
		if (AcceptedRoleLockIn is not null)
		{
			OnSimulationScenarioChanged();
		}
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
			? new SimulationScenario(
				acceptedRoleLockIn,
				publicGroupPartition: AcceptedPublicGroupPartition is { } publicGroupPartition
					? CanonicalPublicGroupPartition.Project(
						_playerRoster.Select(player => player.Id).ToArray(),
						publicGroupPartition)
					: null)
			: new SimulationScenario(_playerRoster.Count, GetSelectedRoles());

	public bool TryCreateSimulationScenario(out SimulationScenario scenario)
	{
		if (RequiresRoleLockIn || RequiresPublicGroupPartition)
		{
			scenario = null!;
			return false;
		}

		scenario = CreateSimulationScenario();
		return true;
	}

	public void Reset()
	{
		if (_playerRoster.Count == 0 &&
			_roleCounts.Values.All(count => count == 0) &&
			AcceptedRoleLockIn is null &&
			!_roleLockInFinalized)
		{
			return;
		}

		_playerRoster.Clear();
		_roleCounts.Clear();
		AcceptedRoleLockIn = null;
		AcceptedPublicGroupPartition = null;
		_roleLockInFinalized = false;
		_acceptedRoleLockInRequiresReplacement = false;
		OnSimulationScenarioChanged();
	}

	public int TotalSelectedRoleCount => _roleCounts.Values.Sum();

	public int ExpectedRoleCount =>
		GameSessionConfig.GetExpectedRoleCount(_playerRoster.Count, GetSelectedRoles());

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
		return GameSessionConfig.TryGetConfigIssues(
			GetPlayerNames(),
			GetSelectedRoles(),
			out issues);
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
						_playerRoster.Count,
						selectedRoles,
						offer1,
						offer2);
					if (!GameSessionConfig.TryGetRoleLockInConfigIssues(
							GetPlayerNames(),
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
			replacement.PlayerCount != _playerRoster.Count ||
			replacement.RoleComposition.Any(card => !_availableRoleMetadata.ContainsKey(card.PrintedRole)))
		{
			return false;
		}

		return !GameSessionConfig.TryGetRoleLockInConfigIssues(
			GetPlayerNames(),
			replacement,
			ActorSetupCards.None,
			out _);
	}

	internal void ApplyAcceptedRoleLockIn(RoleLockIn replacement)
	{
		AcceptedRoleLockIn = replacement;
		if (!IsPrejudicedManipulatorReachable(replacement))
		{
			AcceptedPublicGroupPartition = null;
		}
		ApplyRoleComposition(replacement);
		_acceptedRoleLockInRequiresReplacement = false;
		OnSimulationScenarioChanged();
	}

	internal bool CanReplacePublicGroupPartition(PublicGroupPartition replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);
		return !_roleLockInFinalized &&
			AcceptedRoleLockIn is { } acceptedRoleLockIn &&
			!_acceptedRoleLockInRequiresReplacement &&
			IsPrejudicedManipulatorReachable(acceptedRoleLockIn) &&
			HasExactCurrentRoster(replacement);
	}

	internal void ApplyAcceptedPublicGroupPartition(PublicGroupPartition replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);
		if (AcceptedPublicGroupPartition?.Equals(replacement) == true)
		{
			return;
		}

		AcceptedPublicGroupPartition = replacement;
		OnSimulationScenarioChanged();
	}

	internal void RestoreAcceptedRoleLockIn(
		IReadOnlyList<GameSessionPlayerConfig> playerRoster,
		RoleLockIn roleLockIn,
		PublicGroupPartition? publicGroupPartition)
	{
		ArgumentNullException.ThrowIfNull(playerRoster);
		ArgumentNullException.ThrowIfNull(roleLockIn);
		var roster = playerRoster.ToArray();
		try
		{
			if (roleLockIn.RoleComposition.Any(
				card => !_availableRoleMetadata.ContainsKey(card.PrintedRole)))
			{
				throw new ArgumentException(
					"The Role Lock-In contains a Role unavailable to this Lobby.",
					nameof(roleLockIn));
			}

			_ = new GameSessionConfig(
				roster,
				roleLockIn,
				ActorSetupCards.None,
				publicGroupPartition);
		}
		catch (Exception exception) when (
			exception is ArgumentException or InvalidOperationException)
		{
			throw new InvalidOperationException(
				"The staged Lobby recovery payload is invalid.",
				exception);
		}

		_playerRoster.Clear();
		_playerRoster.AddRange(roster);
		_issuedPlayerIds.UnionWith(roster.Select(player => player.Id));
		ApplyRoleComposition(roleLockIn);
		AcceptedRoleLockIn = roleLockIn;
		AcceptedPublicGroupPartition = publicGroupPartition;
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

	private static bool IsPrejudicedManipulatorReachable(RoleLockIn roleLockIn) =>
		roleLockIn.RoleComposition.Any(
			card => card.PrintedRole == MainRoleType.PrejudicedManipulator);

	private bool HasExactCurrentRoster(PublicGroupPartition partition) =>
		_playerRoster.Select(player => player.Id).ToHashSet().SetEquals(
			partition.FirstGroupPlayerIds.Concat(partition.SecondGroupPlayerIds));

	private List<string> GetPlayerNames() =>
		_playerRoster.Select(player => player.Name).ToList();

	private GameSessionPlayerConfig CreatePlayerRosterEntry(string playerName)
	{
		Guid playerId;
		do
		{
			playerId = Guid.NewGuid();
		}
		while (!_issuedPlayerIds.Add(playerId));

		return new GameSessionPlayerConfig(playerId, playerName);
	}
}
