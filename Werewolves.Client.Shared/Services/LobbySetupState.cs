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
	private static readonly object CommitAuthority = new();

	private readonly LobbySetupMetadata _setupMetadata;
	private readonly IReadOnlyList<MainRoleType> _availableRoles;
	private readonly Dictionary<MainRoleType, LobbySetupRoleMetadata> _availableRoleMetadata;
	private LobbySetupAggregate _current;

	public LobbySetupState(LobbySetupMetadata setupMetadata)
	{
		_setupMetadata = setupMetadata;
		_availableRoles = setupMetadata.AvailableRoles.Select(role => role.Role).ToArray();
		_availableRoleMetadata = setupMetadata.AvailableRoles.ToDictionary(role => role.Role);
		_current = new LobbySetupAggregate(
			playerRoster: [],
			issuedPlayerIds: [],
			roleCounts: new Dictionary<MainRoleType, int>(),
			acceptedRoleLockIn: null,
			ActorSetupCards.None,
			acceptedPublicGroupPartition: null,
			roleLockInFinalized: false,
			acceptedRoleLockInRequiresReplacement: false);
	}

	public event EventHandler? SimulationScenarioChanged;

	public int MinimumPlayerCount => _setupMetadata.MinimumPlayerCount;
	public IReadOnlyList<MainRoleType> AvailableRoles => _availableRoles;
	public IReadOnlyList<RoleInfo> AvailableRoleInfos => _availableRoles.Select(GetRoleInfo).ToArray();
	public IReadOnlyList<RoleSelectionGroupInfo> AvailableRoleGroups => GetAvailableRoleGroups();

	public IReadOnlyList<GameSessionPlayerConfig> PlayerRoster => _current.PlayerRoster;
	public IReadOnlyList<string> PlayerNames => Array.AsReadOnly(
		_current.PlayerRoster.Select(player => player.Name).ToArray());
	public RoleLockIn? AcceptedRoleLockIn => _current.AcceptedRoleLockIn;
	public ActorSetupCards AcceptedActorSetupCards =>
		_current.AcceptedActorSetupCards;
	public PublicGroupPartition? AcceptedPublicGroupPartition =>
		_current.AcceptedPublicGroupPartition;
	public bool RequiresRoleLockIn =>
		_current.AcceptedRoleLockInRequiresReplacement ||
		GetRoleCount(MainRoleType.Thief) > 0 && AcceptedRoleLockIn is null;
	public bool RequiresActorSetupCards =>
		AcceptedRoleLockIn is { } acceptedRoleLockIn &&
		!_current.AcceptedRoleLockInRequiresReplacement &&
		IsActorReachable(acceptedRoleLockIn) &&
		AcceptedActorSetupCards.Cards.Count == 0;
	public bool RequiresPublicGroupPartition =>
		AcceptedRoleLockIn is { } acceptedRoleLockIn &&
		!_current.AcceptedRoleLockInRequiresReplacement &&
		!RequiresActorSetupCards &&
		IsPrejudicedManipulatorReachable(acceptedRoleLockIn) &&
		AcceptedPublicGroupPartition is null;
	internal bool AcceptedRoleLockInRequiresReplacement =>
		_current.AcceptedRoleLockInRequiresReplacement;

	public bool HasPlayerConfigIssues(out List<GameConfigValidationError> issues)
	{
		return GameSessionConfig.TryGetPlayerConfigIssues(GetPlayerNames(), out issues);
	}

	public bool HasRoleConfigIssues(out List<GameConfigValidationError> issues)
	{
		var playerNames = GetPlayerNames();
		List<GameConfigValidationError> configIssues;
		if (AcceptedRoleLockIn is { } acceptedRoleLockIn &&
			!_current.AcceptedRoleLockInRequiresReplacement)
		{
			GameSessionConfig.TryGetRoleLockInConfigIssues(
				playerNames,
				acceptedRoleLockIn,
				AcceptedActorSetupCards,
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
				and not GameConfigValidationErrorType.NonUniquePlayerNames
				and not GameConfigValidationErrorType.ActorSetupCardCountMismatch)
			.ToList();

		return issues.Count > 0;
	}

	public bool CanMovePlayerUp(int index) => index > 0 && index < _current.PlayerRoster.Count;

	public bool CanMovePlayerDown(int index) =>
		index >= 0 && index < _current.PlayerRoster.Count - 1;

	public AddPlayerResult AddPlayer(string playerName)
	{
		var normalizedName = playerName.Trim();
		if (normalizedName.Length == 0)
		{
			return AddPlayerResult.EmptyName;
		}

		if (_current.PlayerRoster.Any(player => string.Equals(
			player.Name,
			normalizedName,
			StringComparison.OrdinalIgnoreCase)))
		{
			return AddPlayerResult.DuplicateName;
		}

		var player = CreatePlayerRosterEntry(normalizedName);
		_current = new LobbySetupAggregate(
			_current.PlayerRoster.Append(player),
			_current.IssuedPlayerIds.Append(player.Id),
			_current.RoleCounts,
			AcceptedRoleLockIn,
			AcceptedActorSetupCards,
			acceptedPublicGroupPartition: null,
			_current.RoleLockInFinalized,
			acceptedRoleLockInRequiresReplacement: AcceptedRoleLockIn is not null);
		OnSimulationScenarioChanged();
		return AddPlayerResult.Success;
	}

	public bool RemovePlayerAt(int index)
	{
		if (index < 0 || index >= _current.PlayerRoster.Count)
		{
			return false;
		}

		var roster = _current.PlayerRoster.ToList();
		roster.RemoveAt(index);
		_current = new LobbySetupAggregate(
			roster,
			_current.IssuedPlayerIds,
			_current.RoleCounts,
			AcceptedRoleLockIn,
			AcceptedActorSetupCards,
			acceptedPublicGroupPartition: null,
			_current.RoleLockInFinalized,
			acceptedRoleLockInRequiresReplacement: AcceptedRoleLockIn is not null);
		OnSimulationScenarioChanged();
		return true;
	}

	public bool MovePlayerUp(int index)
	{
		if (!CanMovePlayerUp(index))
		{
			return false;
		}

		var roster = _current.PlayerRoster.ToArray();
		(roster[index - 1], roster[index]) =
			(roster[index], roster[index - 1]);
		_current = new LobbySetupAggregate(
			roster,
			_current.IssuedPlayerIds,
			_current.RoleCounts,
			AcceptedRoleLockIn,
			AcceptedActorSetupCards,
			AcceptedPublicGroupPartition,
			_current.RoleLockInFinalized,
			_current.AcceptedRoleLockInRequiresReplacement);
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

		var roster = _current.PlayerRoster.ToArray();
		(roster[index], roster[index + 1]) =
			(roster[index + 1], roster[index]);
		_current = new LobbySetupAggregate(
			roster,
			_current.IssuedPlayerIds,
			_current.RoleCounts,
			AcceptedRoleLockIn,
			AcceptedActorSetupCards,
			AcceptedPublicGroupPartition,
			_current.RoleLockInFinalized,
			_current.AcceptedRoleLockInRequiresReplacement);
		if (AcceptedRoleLockIn is not null)
		{
			OnSimulationScenarioChanged();
		}
		return true;
	}

	public int GetRoleCount(MainRoleType role)
	{
		return _current.RoleCounts.GetValueOrDefault(role, 0);
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
				ApplyRoleDraftCount(role, batchSize);
			}
		}
		else
		{
			if (current < maximum)
			{
				ApplyRoleDraftCount(role, current + 1);
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

		ApplyRoleDraftCount(
			role,
			affordance == RoleAffordance.Toggle ? 0 : current - 1);
	}

	public List<MainRoleType> GetSelectedRoles()
	{
		var roles = new List<MainRoleType>();
		foreach (var (role, count) in _current.RoleCounts)
		{
			for (var i = 0; i < count; i++)
				roles.Add(role);
		}
		return roles;
	}

	public SimulationScenario CreateSimulationScenario() =>
		AcceptedRoleLockIn is { } acceptedRoleLockIn &&
			!_current.AcceptedRoleLockInRequiresReplacement
			? new SimulationScenario(
				acceptedRoleLockIn,
				AcceptedActorSetupCards,
				publicGroupPartition: AcceptedPublicGroupPartition is { } publicGroupPartition
					? CanonicalPublicGroupPartition.Project(
						_current.PlayerRoster.Select(player => player.Id).ToArray(),
						publicGroupPartition)
					: null)
			: new SimulationScenario(_current.PlayerRoster.Count, GetSelectedRoles());

	public bool TryCreateSimulationScenario(out SimulationScenario scenario)
	{
		if (RequiresRoleLockIn ||
			RequiresConditionalRoleLockIn ||
			RequiresActorSetupCards ||
			RequiresPublicGroupPartition)
		{
			scenario = null!;
			return false;
		}

		scenario = CreateSimulationScenario();
		return true;
	}

	public void Reset()
	{
		if (_current.PlayerRoster.Count == 0 &&
			_current.RoleCounts.Values.All(count => count == 0) &&
			AcceptedRoleLockIn is null &&
			!_current.RoleLockInFinalized)
		{
			return;
		}

		_current = new LobbySetupAggregate(
			playerRoster: [],
			_current.IssuedPlayerIds,
			roleCounts: new Dictionary<MainRoleType, int>(),
			acceptedRoleLockIn: null,
			ActorSetupCards.None,
			acceptedPublicGroupPartition: null,
			roleLockInFinalized: false,
			acceptedRoleLockInRequiresReplacement: false);
		OnSimulationScenarioChanged();
	}

	public int TotalSelectedRoleCount => _current.RoleCounts.Values.Sum();

	public int ExpectedRoleCount =>
		GameSessionConfig.GetExpectedRoleCount(
			_current.PlayerRoster.Count,
			GetSelectedRoles());

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
						_current.PlayerRoster.Count,
						selectedRoles,
						offer1,
						offer2);
					if (!HasBlockingRoleLockInIssues(
							candidate,
							ActorSetupCards.None,
							allowMissingActorSetup: true))
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

	internal LobbyDecision? Decide(LobbyChange? change)
	{
		if (change is null)
		{
			return null;
		}

		return change switch
		{
			LobbyChange.ReplaceRoleLockIn replace =>
				DecideRoleLockInReplacement(replace),
			LobbyChange.AcceptImplicitRoleLockIn implicitRoleLockIn =>
				DecideImplicitRoleLockIn(implicitRoleLockIn),
			LobbyChange.ReplaceActorSetupCards replace =>
				DecideActorSetupCardsReplacement(replace),
			LobbyChange.ReplacePublicGroupPartition replace =>
				DecidePublicGroupPartitionReplacement(replace),
			LobbyChange.MovePlayer move => DecidePlayerMove(move),
			LobbyChange.AddPlayer add => DecidePlayerAddition(add),
			LobbyChange.RemovePlayer remove => DecidePlayerRemoval(remove),
			_ => null
		};
	}

	private LobbyDecision? DecideImplicitRoleLockIn(
		LobbyChange.AcceptImplicitRoleLockIn change)
	{
		if (change.Replacement.RoleComposition.Any(
				card => card.PrintedRole == MainRoleType.Thief) ||
			change.Replacement.Offer1 is not null ||
			change.Replacement.Offer2 is not null)
		{
			return null;
		}

		return DecideRoleLockInReplacement(
			new LobbyChange.ReplaceRoleLockIn(
				change.ExpectedCurrentVersion,
				change.Replacement));
	}

	private LobbyDecision? DecideRoleLockInReplacement(
		LobbyChange.ReplaceRoleLockIn change)
	{
		if (!CanReplaceRoleLockIn(
				change.ExpectedCurrentVersion,
				change.Replacement))
		{
			return null;
		}

		var retainedActorSetupCards =
			GetRetainedActorSetupCardsForRoleLockIn(change.Replacement);
		var actorSetupIsPending = IsActorReachable(change.Replacement) &&
			retainedActorSetupCards.Cards.Count == 0;
		var retainedPartition = !actorSetupIsPending &&
			IsPrejudicedManipulatorReachable(change.Replacement)
				? AcceptedPublicGroupPartition
				: null;
		var roleCounts = change.Replacement.RoleComposition
			.GroupBy(card => card.PrintedRole)
			.ToDictionary(group => group.Key, group => group.Count());
		var nextAggregate = new LobbySetupAggregate(
			_current.PlayerRoster,
			_current.IssuedPlayerIds,
			roleCounts,
			change.Replacement,
			retainedActorSetupCards,
			retainedPartition,
			roleLockInFinalized: false,
			acceptedRoleLockInRequiresReplacement: false);
		return CreateDecision(
			nextAggregate,
			new LobbyPersistenceInstruction.Replace(nextAggregate));
	}

	private LobbyDecision? DecideActorSetupCardsReplacement(
		LobbyChange.ReplaceActorSetupCards change)
	{
		if (!CanReplaceActorSetupCards(
				change.ExpectedCurrentVersion,
				change.Replacement))
		{
			return null;
		}

		var nextAggregate = new LobbySetupAggregate(
			_current.PlayerRoster,
			_current.IssuedPlayerIds,
			_current.RoleCounts,
			AcceptedRoleLockIn,
			change.Replacement,
			AcceptedPublicGroupPartition,
			_current.RoleLockInFinalized,
			_current.AcceptedRoleLockInRequiresReplacement);
		return CreateDecision(
			nextAggregate,
			new LobbyPersistenceInstruction.Replace(nextAggregate));
	}

	private LobbyDecision? DecidePublicGroupPartitionReplacement(
		LobbyChange.ReplacePublicGroupPartition change)
	{
		if (!CanReplacePublicGroupPartition(change.Replacement))
		{
			return null;
		}

		var nextAggregate = AcceptedPublicGroupPartition?.Equals(
			change.Replacement) == true
			? _current
			: new LobbySetupAggregate(
				_current.PlayerRoster,
				_current.IssuedPlayerIds,
				_current.RoleCounts,
				AcceptedRoleLockIn,
				AcceptedActorSetupCards,
				change.Replacement,
				_current.RoleLockInFinalized,
				_current.AcceptedRoleLockInRequiresReplacement);
		var persistence = ReferenceEquals(_current, nextAggregate)
			? (LobbyPersistenceInstruction)new LobbyPersistenceInstruction.Keep()
			: new LobbyPersistenceInstruction.Replace(nextAggregate);
		return CreateDecision(nextAggregate, persistence);
	}

	private LobbyDecision? DecidePlayerMove(LobbyChange.MovePlayer change)
	{
		if (change.FromIndex < 0 ||
			change.ToIndex < 0 ||
			change.FromIndex >= _current.PlayerRoster.Count ||
			change.ToIndex >= _current.PlayerRoster.Count ||
			Math.Abs(change.FromIndex - change.ToIndex) != 1)
		{
			return null;
		}

		var roster = _current.PlayerRoster.ToArray();
		(roster[change.FromIndex], roster[change.ToIndex]) =
			(roster[change.ToIndex], roster[change.FromIndex]);
		var nextAggregate = new LobbySetupAggregate(
			roster,
			_current.IssuedPlayerIds,
			_current.RoleCounts,
			AcceptedRoleLockIn,
			AcceptedActorSetupCards,
			AcceptedPublicGroupPartition,
			_current.RoleLockInFinalized,
			_current.AcceptedRoleLockInRequiresReplacement);
		var persistence = AcceptedRoleLockIn is not null &&
			!_current.AcceptedRoleLockInRequiresReplacement
			? (LobbyPersistenceInstruction)new LobbyPersistenceInstruction.Replace(
				nextAggregate)
			: new LobbyPersistenceInstruction.Keep();
		return CreateDecision(nextAggregate, persistence);
	}

	private LobbyDecision? DecidePlayerAddition(LobbyChange.AddPlayer change)
	{
		var player = change.Player;
		if (_current.RoleLockInFinalized ||
			player is null ||
			player.Id == Guid.Empty ||
			string.IsNullOrWhiteSpace(player.Name) ||
			!string.Equals(player.Name, player.Name.Trim(), StringComparison.Ordinal) ||
			_current.IssuedPlayerIds.Contains(player.Id) ||
			_current.PlayerRoster.Any(existing => string.Equals(
				existing.Name,
				player.Name,
				StringComparison.OrdinalIgnoreCase)))
		{
			return null;
		}

		return DecidePlayerMembershipChange(
			_current.PlayerRoster.Append(player),
			_current.IssuedPlayerIds.Append(player.Id));
	}

	private LobbyDecision? DecidePlayerRemoval(LobbyChange.RemovePlayer change)
	{
		if (_current.RoleLockInFinalized ||
			change.Index < 0 ||
			change.Index >= _current.PlayerRoster.Count)
		{
			return null;
		}

		var roster = _current.PlayerRoster.ToList();
		roster.RemoveAt(change.Index);
		return DecidePlayerMembershipChange(
			roster,
			_current.IssuedPlayerIds);
	}

	private LobbyDecision DecidePlayerMembershipChange(
		IEnumerable<GameSessionPlayerConfig> playerRoster,
		IEnumerable<Guid> issuedPlayerIds)
	{
		var nextAggregate = new LobbySetupAggregate(
			playerRoster,
			issuedPlayerIds,
			_current.RoleCounts,
			AcceptedRoleLockIn,
			AcceptedActorSetupCards,
			acceptedPublicGroupPartition: null,
			_current.RoleLockInFinalized,
			acceptedRoleLockInRequiresReplacement: AcceptedRoleLockIn is not null);
		var persistence = AcceptedRoleLockIn is null
			? (LobbyPersistenceInstruction)new LobbyPersistenceInstruction.Keep()
			: new LobbyPersistenceInstruction.Clear();
		return CreateDecision(nextAggregate, persistence);
	}

	private LobbyDecision CreateDecision(
		LobbySetupAggregate nextAggregate,
		LobbyPersistenceInstruction persistence) =>
		new(
			nextAggregate,
			persistence,
			new CanonicalSimulationScenarioDelta(
				TryCreateCanonicalScenario(_current),
				TryCreateCanonicalScenario(nextAggregate)),
			new Commit(CommitAuthority, nextAggregate));

	private static CanonicalSimulationScenario? TryCreateCanonicalScenario(
		LobbySetupAggregate aggregate)
	{
		if (aggregate.AcceptedRoleLockInRequiresReplacement)
		{
			return null;
		}

		SimulationScenario scenario;
		if (aggregate.AcceptedRoleLockIn is { } roleLockIn)
		{
			var actorIsPending = IsActorReachable(roleLockIn) &&
				aggregate.AcceptedActorSetupCards.Cards.Count == 0;
			var partitionIsPending =
				IsPrejudicedManipulatorReachable(roleLockIn) &&
				aggregate.AcceptedPublicGroupPartition is null;
			if (actorIsPending || partitionIsPending)
			{
				return null;
			}

			scenario = new SimulationScenario(
				roleLockIn,
				aggregate.AcceptedActorSetupCards,
				publicGroupPartition: aggregate.AcceptedPublicGroupPartition is { } partition
					? CanonicalPublicGroupPartition.Project(
						aggregate.PlayerRoster.Select(player => player.Id).ToArray(),
						partition)
					: null);
		}
		else
		{
			var selectedRoles = aggregate.RoleCounts
				.SelectMany(entry => Enumerable.Repeat(entry.Key, entry.Value))
				.ToArray();
			if (selectedRoles.Contains(MainRoleType.Thief) ||
				selectedRoles.Contains(MainRoleType.Actor) ||
				selectedRoles.Contains(MainRoleType.PrejudicedManipulator))
			{
				return null;
			}

			scenario = new SimulationScenario(
				aggregate.PlayerRoster.Count,
				selectedRoles);
		}

		return CanonicalSimulationScenario.Create(scenario);
	}

	internal sealed class Commit
	{
		internal Commit(object authority, LobbySetupAggregate nextAggregate)
		{
			if (!ReferenceEquals(authority, CommitAuthority))
			{
				throw new InvalidOperationException("Only LobbySetupState can create a Lobby commit.");
			}

			NextAggregate = nextAggregate;
		}

		internal LobbySetupAggregate NextAggregate { get; }
	}

	internal void Publish(Commit commit) =>
		_current = commit.NextAggregate;

	internal void NotifySimulationScenarioChanged() =>
		OnSimulationScenarioChanged();

	internal bool CanReplaceRoleLockIn(
		long expectedCurrentVersion,
		RoleLockIn replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);
		var currentVersion = AcceptedRoleLockIn?.Version ?? 0;
		if (_current.RoleLockInFinalized ||
			expectedCurrentVersion != currentVersion ||
			expectedCurrentVersion == long.MaxValue ||
			replacement.Version != expectedCurrentVersion + 1 ||
			replacement.PlayerCount != _current.PlayerRoster.Count ||
			replacement.RoleComposition.Any(card => !_availableRoleMetadata.ContainsKey(card.PrintedRole)))
		{
			return false;
		}

		var retainedActorSetupCards =
			GetRetainedActorSetupCardsForRoleLockIn(replacement);
		return !HasBlockingRoleLockInIssues(
			replacement,
			retainedActorSetupCards,
			allowMissingActorSetup: true);
	}

	internal void ApplyAcceptedRoleLockIn(RoleLockIn replacement)
	{
		var retainedActorSetupCards =
			GetRetainedActorSetupCardsForRoleLockIn(replacement);
		var retainedPartition = AcceptedPublicGroupPartition;
		if (!IsPrejudicedManipulatorReachable(replacement) ||
			IsActorReachable(replacement) && retainedActorSetupCards.Cards.Count == 0)
		{
			retainedPartition = null;
		}
		_current = new LobbySetupAggregate(
			_current.PlayerRoster,
			_current.IssuedPlayerIds,
			GetRoleCounts(replacement),
			replacement,
			retainedActorSetupCards,
			retainedPartition,
			roleLockInFinalized: false,
			acceptedRoleLockInRequiresReplacement: false);
		OnSimulationScenarioChanged();
	}

	internal ActorSetupCards GetRetainedActorSetupCardsForRoleLockIn(
		RoleLockIn replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);
		if (AcceptedActorSetupCards.Cards.Count == 0 ||
			!IsActorReachable(replacement) ||
			HasBlockingRoleLockInIssues(
				replacement,
				AcceptedActorSetupCards,
				allowMissingActorSetup: false))
		{
			return ActorSetupCards.None;
		}

		return AcceptedActorSetupCards;
	}

	internal bool CanReplaceActorSetupCards(
		long expectedCurrentVersion,
		ActorSetupCards replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);
		return !_current.RoleLockInFinalized &&
			AcceptedRoleLockIn is { } acceptedRoleLockIn &&
			!_current.AcceptedRoleLockInRequiresReplacement &&
			IsActorReachable(acceptedRoleLockIn) &&
			expectedCurrentVersion == AcceptedActorSetupCards.Version &&
			expectedCurrentVersion != long.MaxValue &&
			replacement.Version == expectedCurrentVersion + 1 &&
			!HasBlockingRoleLockInIssues(
				acceptedRoleLockIn,
				replacement,
				allowMissingActorSetup: false);
	}

	internal void ApplyAcceptedActorSetupCards(ActorSetupCards replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);
		_current = new LobbySetupAggregate(
			_current.PlayerRoster,
			_current.IssuedPlayerIds,
			_current.RoleCounts,
			AcceptedRoleLockIn,
			replacement,
			AcceptedPublicGroupPartition,
			_current.RoleLockInFinalized,
			_current.AcceptedRoleLockInRequiresReplacement);
		OnSimulationScenarioChanged();
	}

	internal bool CanReplacePublicGroupPartition(PublicGroupPartition replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);
		return !_current.RoleLockInFinalized &&
			AcceptedRoleLockIn is { } acceptedRoleLockIn &&
			!_current.AcceptedRoleLockInRequiresReplacement &&
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

		_current = new LobbySetupAggregate(
			_current.PlayerRoster,
			_current.IssuedPlayerIds,
			_current.RoleCounts,
			AcceptedRoleLockIn,
			AcceptedActorSetupCards,
			replacement,
			_current.RoleLockInFinalized,
			_current.AcceptedRoleLockInRequiresReplacement);
		OnSimulationScenarioChanged();
	}

	internal void RestoreAcceptedRoleLockIn(
		IReadOnlyList<GameSessionPlayerConfig> playerRoster,
		RoleLockIn roleLockIn,
		ActorSetupCards actorSetupCards,
		PublicGroupPartition? publicGroupPartition)
	{
		ArgumentNullException.ThrowIfNull(playerRoster);
		ArgumentNullException.ThrowIfNull(roleLockIn);
		ArgumentNullException.ThrowIfNull(actorSetupCards);
		var roster = playerRoster.ToArray();
		try
		{
			if (roster.Any(player => player is null) ||
				roster.Select(player => player.Id).Distinct().Count() != roster.Length)
			{
				throw new ArgumentException(
					"A staged Lobby roster requires distinct stable Player identities.",
					nameof(playerRoster));
			}
			if (roleLockIn.RoleComposition.Any(
				card => !_availableRoleMetadata.ContainsKey(card.PrintedRole)))
			{
				throw new ArgumentException(
					"The Role Lock-In contains a Role unavailable to this Lobby.",
					nameof(roleLockIn));
			}

			var actorSetupIsPending =
				IsActorReachable(roleLockIn) && actorSetupCards.Cards.Count == 0;
			GameSessionConfig.TryGetRoleLockInConfigIssues(
				roster.Select(player => player.Name).ToList(),
				roleLockIn,
				actorSetupCards,
				out var configIssues);
			var blockingIssues = configIssues
				.Where(issue =>
					!actorSetupIsPending ||
					issue.Type != GameConfigValidationErrorType.ActorSetupCardCountMismatch)
				.ToArray();
			if (blockingIssues.Length > 0)
			{
				throw new InvalidOperationException(
					"The staged Role Lock-In is invalid: " +
					string.Join(", ", blockingIssues.Select(issue => issue.Message)));
			}

			var prejudicedManipulatorReachable =
				IsPrejudicedManipulatorReachable(roleLockIn);
			if (publicGroupPartition is not null &&
				(!prejudicedManipulatorReachable || actorSetupIsPending ||
				!roster.Select(player => player.Id).ToHashSet().SetEquals(
					publicGroupPartition.FirstGroupPlayerIds.Concat(
						publicGroupPartition.SecondGroupPlayerIds))))
			{
				throw new ArgumentException(
					"The staged Public Group Partition is invalid or out of order.",
					nameof(publicGroupPartition));
			}
		}
		catch (Exception exception) when (
			exception is ArgumentException or InvalidOperationException)
		{
			throw new InvalidOperationException(
				"The staged Lobby recovery payload is invalid.",
				exception);
		}

		_current = new LobbySetupAggregate(
			roster,
			_current.IssuedPlayerIds.Concat(
				roster.Select(player => player.Id)).Distinct(),
			GetRoleCounts(roleLockIn),
			roleLockIn,
			actorSetupCards,
			publicGroupPartition,
			roleLockInFinalized: false,
			acceptedRoleLockInRequiresReplacement: false);
		OnSimulationScenarioChanged();
	}

	internal void FinalizeRoleLockIn(RoleLockIn roleLockIn)
	{
		ArgumentNullException.ThrowIfNull(roleLockIn);
		if (_current.AcceptedRoleLockInRequiresReplacement)
		{
			throw new InvalidOperationException(
				"Lobby Exit requires a fresh accepted Role Lock-In after Lobby edits.");
		}
		if (AcceptedRoleLockIn is not null && AcceptedRoleLockIn.Version != roleLockIn.Version)
		{
			throw new InvalidOperationException("Lobby Exit must finalize the latest accepted Role Lock-In.");
		}
		_current = new LobbySetupAggregate(
			_current.PlayerRoster,
			_current.IssuedPlayerIds,
			_current.RoleCounts,
			roleLockIn,
			AcceptedActorSetupCards,
			AcceptedPublicGroupPartition,
			roleLockInFinalized: true,
			acceptedRoleLockInRequiresReplacement: false);
	}

	private static IReadOnlyDictionary<MainRoleType, int> GetRoleCounts(
		RoleLockIn roleLockIn) =>
		roleLockIn.RoleComposition
			.GroupBy(card => card.PrintedRole)
			.ToDictionary(group => group.Key, group => group.Count());

	private void ApplyRoleDraftCount(MainRoleType role, int count)
	{
		var roleCounts = _current.RoleCounts.ToDictionary(
			entry => entry.Key,
			entry => entry.Value);
		roleCounts[role] = count;
		_current = new LobbySetupAggregate(
			_current.PlayerRoster,
			_current.IssuedPlayerIds,
			roleCounts,
			AcceptedRoleLockIn,
			AcceptedActorSetupCards,
			AcceptedPublicGroupPartition,
			_current.RoleLockInFinalized,
			acceptedRoleLockInRequiresReplacement: AcceptedRoleLockIn is not null);
		OnSimulationScenarioChanged();
	}

	private void OnSimulationScenarioChanged() =>
		SimulationScenarioChanged?.Invoke(this, EventArgs.Empty);

	private static bool IsPrejudicedManipulatorReachable(RoleLockIn roleLockIn) =>
		roleLockIn.RoleComposition.Any(
			card => card.PrintedRole == MainRoleType.PrejudicedManipulator);

	private static bool IsActorReachable(RoleLockIn roleLockIn) =>
		roleLockIn.RoleComposition.Any(
			card => card.PrintedRole == MainRoleType.Actor);

	private bool RequiresConditionalRoleLockIn =>
		AcceptedRoleLockIn is null &&
		(GetRoleCount(MainRoleType.Actor) > 0 ||
			GetRoleCount(MainRoleType.PrejudicedManipulator) > 0);

	private bool HasBlockingRoleLockInIssues(
		RoleLockIn roleLockIn,
		ActorSetupCards actorSetupCards,
		bool allowMissingActorSetup)
	{
		GameSessionConfig.TryGetRoleLockInConfigIssues(
			GetPlayerNames(),
			roleLockIn,
			actorSetupCards,
			out var issues);
		return issues.Any(issue =>
			!allowMissingActorSetup ||
			issue.Type != GameConfigValidationErrorType.ActorSetupCardCountMismatch);
	}

	private bool HasExactCurrentRoster(PublicGroupPartition partition) =>
		_current.PlayerRoster.Select(player => player.Id).ToHashSet().SetEquals(
			partition.FirstGroupPlayerIds.Concat(partition.SecondGroupPlayerIds));

	private List<string> GetPlayerNames() =>
		_current.PlayerRoster.Select(player => player.Name).ToList();

	private GameSessionPlayerConfig CreatePlayerRosterEntry(string playerName)
	{
		Guid playerId;
		do
		{
			playerId = Guid.NewGuid();
		}
		while (_current.IssuedPlayerIds.Contains(playerId));

		return new GameSessionPlayerConfig(playerId, playerName);
	}
}
