using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;

namespace Werewolves.Core.StateModels.Models;

public class GameSessionConfig
{
	public const int MinimumPlayerCount = 5;
	public const int MaximumPlayerCount = 30;

	public IReadOnlyList<string> Players { get; }
	public IReadOnlyList<GameSessionPlayerConfig> PlayerRoster { get; }
	public IReadOnlyList<MainRoleType> Roles { get; }
	public ActorSetupCards ActorSetupCards { get; }
	public RoleLockIn RoleLockIn { get; }
	public PublicGroupPartition? PublicGroupPartition { get; }

	public static Dictionary<MainRoleType, NumberRangeConstraint> RoleCountConstraints { get; } = new()
	{
		// insert all roles with default Any constraint
		[MainRoleType.SimpleWerewolf] = NumberRangeConstraint.Any,
		[MainRoleType.BigBadWolf] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.AccursedWolfFather] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.WhiteWerewolf] = NumberRangeConstraint.SingleOptional,

		[MainRoleType.SimpleVillager] = NumberRangeConstraint.Any,
		[MainRoleType.VillagerVillager] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.Seer] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.Cupid] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.Witch] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.Hunter] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.LittleGirl] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.Defender] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.Elder] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.Scapegoat] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.VillageIdiot] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.TwoSisters] = NumberRangeConstraint.ExactOptional(2),
		[MainRoleType.ThreeBrothers] = NumberRangeConstraint.ExactOptional(3),
		[MainRoleType.Fox] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.BearTamer] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.StutteringJudge] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.KnightWithRustySword] = NumberRangeConstraint.SingleOptional,

		[MainRoleType.Thief] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.DevotedServant] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.Actor] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.WildChild] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.WolfHound] = NumberRangeConstraint.SingleOptional,

		[MainRoleType.Angel] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.Piper] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.PrejudicedManipulator] = NumberRangeConstraint.SingleOptional,

		[MainRoleType.Gypsy] = NumberRangeConstraint.SingleOptional,
	};

	internal static void EnforceValidity(
		List<string> players,
		List<MainRoleType> roles,
		ActorSetupCards? actorSetupCards = null)
	{
		if (TryGetConfigIssues(
			players,
			roles,
			actorSetupCards ?? global::Werewolves.Core.StateModels.Models.ActorSetupCards.None,
			out var issues))
		{
			throw new InvalidOperationException("Game session configuration is invalid:\n" + string.Join(", ", issues));
		}
	}

	internal void EnforceValidity()
	{
		EnforceRoleLockInValidity(
			PlayerRoster.Select(player => player.Name).ToList(),
			RoleLockIn,
			ActorSetupCards);
	}

	public static bool TryGetRoleLockInConfigIssues(
		List<string> players,
		RoleLockIn roleLockIn,
		ActorSetupCards actorSetupCards,
		out List<GameConfigValidationError> issues)
	{
		ArgumentNullException.ThrowIfNull(players);
		ArgumentNullException.ThrowIfNull(roleLockIn);
		ArgumentNullException.ThrowIfNull(actorSetupCards);
		issues = new List<GameConfigValidationError>();
		var collectedIssues = issues;
		if (TryGetPlayerConfigIssues(players, out var playerIssues))
		{
			collectedIssues.AddRange(playerIssues);
		}
		if (roleLockIn.PlayerCount != players.Count)
		{
			collectedIssues.Add(new GameConfigValidationError(
				GameConfigValidationErrorType.RoleCountMismatch,
				"Role Lock-In Player count must match the Game Session roster."));
			return true;
		}

		AddRoleLockInCompositionIssues(
			players.Count,
			roleLockIn.DealPool.Select(card => card.PrintedRole).ToArray(),
			roleLockIn.Offer1?.PrintedRole,
			roleLockIn.Offer2?.PrintedRole,
			actorSetupCards,
			collectedIssues);

		return collectedIssues.Count > 0;
	}

	private static void EnforceRoleLockInValidity(
		List<string> players,
		RoleLockIn roleLockIn,
		ActorSetupCards actorSetupCards)
	{
		if (TryGetRoleLockInConfigIssues(
			players,
			roleLockIn,
			actorSetupCards,
			out var issues))
		{
			throw new InvalidOperationException(
				"Game session configuration is invalid:\n" + string.Join(", ", issues));
		}
	}

	/// <summary>
	/// Used to check specific player-related configuration issues, independently of roles.
	/// </summary>
	/// <param name="players"></param>
	/// <param name="issues"></param>
	/// <returns></returns>
	public static bool TryGetPlayerConfigIssues(List<string> players, out List<GameConfigValidationError> issues)
	{
		issues = new List<GameConfigValidationError>();
		// Non-unique player names
		var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var p in players)
		{
			if (!nameSet.Add(p))
			{
				issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.NonUniquePlayerNames, "Player list contains non-unique names."));
				break;
			}
		}

		AddPlayerCountIssues(players.Count, issues);

		return issues.Count > 0;
	}

	/// <summary>
	/// Helper for UI to display expected vs actual role count.
	/// Returns the number of Role Composition cards expected based on Player count.
	/// Thief contributes two extra Character Cards; Actor Setup Cards are separate setup artifacts.
	/// </summary>
	/// <param name="playerCount">The number of players in the game.</param>
	/// <param name="roles">The list of roles selected for the game.</param>
	/// <returns>The expected total role count.</returns>
	public static int GetExpectedRoleCount(int playerCount, List<MainRoleType> roles) =>
		GetExpectedPhysicalRoleCount(playerCount, roles);

	/// <summary>
	/// The main helper method to validate a game configuration. Use this before trying to create a GameSessionConfig.
	/// </summary>
	/// <param name="players"></param>
	/// <param name="roles"></param>
	/// <param name="issues"></param>
	/// <returns></returns>
	public static bool TryGetConfigIssues(List<string> players, List<MainRoleType> roles, out List<GameConfigValidationError> issues)
	{
		return TryGetConfigIssues(
			players,
			roles,
			global::Werewolves.Core.StateModels.Models.ActorSetupCards.None,
			out issues);
	}

	public static bool TryGetConfigIssues(
		List<string> players,
		List<MainRoleType> roles,
		ActorSetupCards actorSetupCards,
		out List<GameConfigValidationError> issues)
	{
		issues = new List<GameConfigValidationError>();

		if (TryGetPlayerConfigIssues(players, out var playerIssues))
		{
			issues.AddRange(playerIssues);
		}

		AddRoleCompositionIssues(
			players.Count,
			roles,
			issues);
		AddActorSetupIssues(
			roles,
			roles.Contains(MainRoleType.Actor),
			actorSetupCards,
			issues);

		return issues.Count > 0;
	}

	/// <summary>
	/// Validates the physical setup when player identities are not part of the input.
	/// This is the rules boundary used by pre-game scenario classification.
	/// </summary>
	/// <param name="playerCount">The number of Players in the physical game.</param>
	/// <param name="roles">Every physical Role Composition card.</param>
	/// <param name="actorSetupCards">Actor Setup Cards kept outside the Role Composition.</param>
	/// <param name="issues">Structured physical-setup validation failures.</param>
	/// <returns>Whether the physical setup has one or more validation failures.</returns>
	public static bool TryGetPhysicalSetupIssues(
		int playerCount,
		IReadOnlyList<MainRoleType> roles,
		ActorSetupCards actorSetupCards,
		out List<GameConfigValidationError> issues)
	{
		ArgumentNullException.ThrowIfNull(roles);
		ArgumentNullException.ThrowIfNull(actorSetupCards);

		issues = new List<GameConfigValidationError>();
		AddPlayerCountIssues(playerCount, issues);
		AddRoleCompositionIssues(playerCount, roles, issues);
		AddActorSetupIssues(
			roles,
			roles.Contains(MainRoleType.Actor),
			actorSetupCards,
			issues);
		return issues.Count > 0;
	}

	/// <summary>
	/// Validates the reachable printed-role branches of a Role Lock-In when
	/// physical Character Card identities are not part of the input.
	/// </summary>
	public static bool TryGetRoleLockInPhysicalSetupIssues(
		int playerCount,
		IReadOnlyList<MainRoleType> dealPoolRoles,
		MainRoleType? offer1Role,
		MainRoleType? offer2Role,
		ActorSetupCards actorSetupCards,
		out List<GameConfigValidationError> issues)
	{
		ArgumentNullException.ThrowIfNull(dealPoolRoles);
		ArgumentNullException.ThrowIfNull(actorSetupCards);
		if ((offer1Role is null) != (offer2Role is null))
		{
			throw new ArgumentException(
				"A Role Lock-In physical setup requires both ordered offers or neither offer.",
				nameof(offer1Role));
		}

		issues = new List<GameConfigValidationError>();
		AddPlayerCountIssues(playerCount, issues);
		AddRoleLockInCompositionIssues(
			playerCount,
			dealPoolRoles,
			offer1Role,
			offer2Role,
			actorSetupCards,
			issues);
		return issues.Count > 0;
	}

	private static void AddRoleLockInCompositionIssues(
		int playerCount,
		IReadOnlyList<MainRoleType> dealPoolRoles,
		MainRoleType? offer1Role,
		MainRoleType? offer2Role,
		ActorSetupCards actorSetupCards,
		List<GameConfigValidationError> issues)
	{
		var completeRoleComposition = dealPoolRoles
			.Concat(offer1Role is { } firstOffer ? [firstOffer] : [])
			.Concat(offer2Role is { } secondOffer ? [secondOffer] : [])
			.ToArray();
		AddActorSetupIssues(
			completeRoleComposition,
			completeRoleComposition.Contains(MainRoleType.Actor),
			actorSetupCards,
			issues);

		var reachableRoleSets = offer1Role is null
			? new[] { dealPoolRoles.ToArray() }
			: new[]
			{
				dealPoolRoles
					.Where(role => role != MainRoleType.Thief)
					.Append(offer1Role.Value)
					.ToArray(),
				dealPoolRoles
					.Where(role => role != MainRoleType.Thief)
					.Append(offer2Role!.Value)
					.ToArray()
			};
		foreach (var reachableRoles in reachableRoleSets)
		{
			var reachableIssues = new List<GameConfigValidationError>();
			AddRoleCompositionIssues(
				playerCount,
				reachableRoles,
				reachableIssues);
			foreach (var issue in reachableIssues.Where(issue =>
				!issues.Any(existing =>
					existing.Type == issue.Type &&
					existing.Message == issue.Message)))
			{
				issues.Add(issue);
			}
		}
	}

	private static void AddPlayerCountIssues(
		int playerCount,
		List<GameConfigValidationError> issues)
	{
		if (playerCount < MinimumPlayerCount)
		{
			issues.Add(new GameConfigValidationError(
				GameConfigValidationErrorType.TooFewPlayers,
				"At least five players are required."));
		}
		else if (playerCount > MaximumPlayerCount)
		{
			issues.Add(new GameConfigValidationError(
				GameConfigValidationErrorType.TooManyPlayers,
				"At most thirty players are supported."));
		}
	}

	private static void AddRoleCompositionIssues(
		int playerCount,
		IReadOnlyList<MainRoleType> roles,
		List<GameConfigValidationError> issues)
	{
		var actualPlayerRoleCountDiff = roles.Count - playerCount;
		var expectedPlayerRoleCountDiff = GetExpectedPhysicalRoleCount(playerCount, roles) - playerCount;
		if (!roles.Any(role => role.IsHardAlignedWerewolf()))
		{
			issues.Add(new GameConfigValidationError(
				GameConfigValidationErrorType.MissingHardAlignedWerewolf,
				"Role Composition requires at least one hard-aligned Werewolf Role."));
		}

		if (!roles.Any(role => role.IsHardAlignedVillager()))
		{
			issues.Add(new GameConfigValidationError(
				GameConfigValidationErrorType.MissingHardAlignedVillager,
				"Role Composition requires at least one hard-aligned Villager Role."));
		}

		// Role count checks
		if (actualPlayerRoleCountDiff > expectedPlayerRoleCountDiff)
		{
			issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.TooManyRoles, $"Roles in excess: { actualPlayerRoleCountDiff - expectedPlayerRoleCountDiff}"));
		}
		else if (actualPlayerRoleCountDiff < expectedPlayerRoleCountDiff)
		{
			var delta = expectedPlayerRoleCountDiff - actualPlayerRoleCountDiff;

			if (roles.Contains(MainRoleType.Thief))
			{
				issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.MissingExtraThiefRoles, $"Missing extra roles required by Thief (needs two extra roles): {delta}"));
			}
			else
			{
				issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.TooFewRoles, $"Roles lacking: {delta}"));
			}
		}

		//save a RoleCountContraints subset that overlaps with the roles in the config
		var relevantRoleConstraints = RoleCountConstraints
			.Where(kv => roles.Contains(kv.Key))
			.ToDictionary(kv => kv.Key, kv => kv.Value);

		// Per-role constraints
		foreach (var kv in relevantRoleConstraints)
		{
			var role = kv.Key;
			var constraint = kv.Value;
			var rolesOfType = roles.Where(r => r == role).ToList();
			var count = rolesOfType.Count;


			if (constraint.IsValid(rolesOfType) == false)
			{
				var betweenRangeString = constraint.Minimum == constraint.Maximum
					? $"{constraint.Minimum}."
					: $"between {constraint.Minimum} and {constraint.Maximum}";

				if (constraint.IsOptional == false)
				{
					issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.RoleCountMismatch, $"Role count for {role} is {count} but must be {betweenRangeString}"));
				}
				else
				{
					// optional: either zero or within range
					issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.RoleCountMismatch, $"Role {role} count is {count} but must be 0 or {betweenRangeString}."));
				}
			}
		}
	}

	private static int GetExpectedPhysicalRoleCount(
		int playerCount,
		IReadOnlyCollection<MainRoleType> roles) =>
		playerCount + (roles.Contains(MainRoleType.Thief) ? 2 : 0);
	/// <summary>
	/// Should only try to build this after validating the inputs with TryGetConfigIssues.
	/// </summary>
	/// <param name="playerNames"></param>
	/// <param name="roles"></param>
	public GameSessionConfig(
		List<string> playerNames,
		List<MainRoleType> roles,
		ActorSetupCards? actorSetupCards = null)
		: this(
			CreatePlayerRoster(playerNames),
			CreateImplicitNonThiefRoleLockIn(
				playerNames,
				roles,
				actorSetupCards),
			actorSetupCards)
	{
	}

	public GameSessionConfig(
		IReadOnlyList<GameSessionPlayerConfig> playerRoster,
		List<MainRoleType> roles,
		ActorSetupCards? actorSetupCards = null,
		PublicGroupPartition? publicGroupPartition = null)
		: this(
			playerRoster,
			CreateImplicitNonThiefRoleLockIn(
				playerRoster.Select(player => player.Name).ToList(),
				roles,
				actorSetupCards),
			actorSetupCards,
			publicGroupPartition)
	{
	}

	public GameSessionConfig(
		List<string> playerNames,
		RoleLockIn roleLockIn,
		ActorSetupCards? actorSetupCards = null)
		: this(
			CreatePlayerRoster(playerNames),
			roleLockIn,
			actorSetupCards)
	{
	}

	public GameSessionConfig(
		IReadOnlyList<GameSessionPlayerConfig> playerRoster,
		RoleLockIn roleLockIn,
		ActorSetupCards? actorSetupCards = null,
		PublicGroupPartition? publicGroupPartition = null)
	{
		ArgumentNullException.ThrowIfNull(playerRoster);
		ArgumentNullException.ThrowIfNull(roleLockIn);
		var roster = playerRoster.ToArray();
		if (roster.Any(player => player is null) ||
			roster.Select(player => player.Id).Distinct().Count() != roster.Length)
		{
			throw new ArgumentException(
				"A Game Session roster requires distinct stable Player identities.",
				nameof(playerRoster));
		}
		if (roleLockIn.PlayerCount != playerRoster.Count)
		{
			throw new ArgumentException(
				"Role Lock-In Player count must match the Game Session roster.",
				nameof(roleLockIn));
		}

		var playerNames = roster.Select(player => player.Name).ToList();
		var roles = roleLockIn.DealPool
			.Select(card => card.PrintedRole)
			.ToList();
		var normalizedActorSetupCards = actorSetupCards
			?? global::Werewolves.Core.StateModels.Models.ActorSetupCards.None;
		EnforceRoleLockInValidity(
			playerNames,
			roleLockIn,
			normalizedActorSetupCards);
		EnforcePublicGroupPartitionValidity(
			roster,
			roleLockIn,
			publicGroupPartition);

		PlayerRoster = Array.AsReadOnly(roster);
		Players = Array.AsReadOnly(playerNames.ToArray());
		Roles = Array.AsReadOnly(roles.ToArray());
		ActorSetupCards = normalizedActorSetupCards;
		RoleLockIn = roleLockIn;
		PublicGroupPartition = publicGroupPartition;
	}

	private static void EnforcePublicGroupPartitionValidity(
		IReadOnlyCollection<GameSessionPlayerConfig> roster,
		RoleLockIn roleLockIn,
		PublicGroupPartition? publicGroupPartition)
	{
		var prejudicedManipulatorReachable = roleLockIn.DealPool.Any(
				card => card.PrintedRole == MainRoleType.PrejudicedManipulator) ||
			roleLockIn.Offer1?.PrintedRole == MainRoleType.PrejudicedManipulator ||
			roleLockIn.Offer2?.PrintedRole == MainRoleType.PrejudicedManipulator;
		if (prejudicedManipulatorReachable && publicGroupPartition is null)
		{
			throw new InvalidOperationException(
				"A reachable Prejudiced Manipulator requires a Public Group Partition.");
		}
		if (!prejudicedManipulatorReachable && publicGroupPartition is not null)
		{
			throw new ArgumentException(
				"A Public Group Partition is not valid when Prejudiced Manipulator is unreachable.",
				nameof(publicGroupPartition));
		}
		if (publicGroupPartition is not null)
		{
			var partitionPlayerIds = publicGroupPartition.FirstGroupPlayerIds
				.Concat(publicGroupPartition.SecondGroupPlayerIds);
			if (!roster.Select(player => player.Id).ToHashSet()
				.SetEquals(partitionPlayerIds))
			{
				throw new ArgumentException(
					"A Public Group Partition must contain the exact Game Session roster.",
					nameof(publicGroupPartition));
			}
		}
	}

	private static void AddActorSetupIssues(
		IReadOnlyList<MainRoleType> completeRoleComposition,
		bool actorReachable,
		ActorSetupCards actorSetupCards,
		List<GameConfigValidationError> issues)
	{
		if (!actorReachable)
		{
			if (actorSetupCards.Cards.Count > 0)
			{
				issues.Add(new GameConfigValidationError(
					GameConfigValidationErrorType.UnexpectedActorSetupCards,
					"Actor Setup Cards are invalid when Actor is unreachable."));
			}

			return;
		}

		if (actorSetupCards.Cards.Count !=
			global::Werewolves.Core.StateModels.Models.ActorSetupCards.RequiredCount)
		{
			issues.Add(new GameConfigValidationError(
				GameConfigValidationErrorType.ActorSetupCardCountMismatch,
				"Actor requires exactly three separate setup cards."));
		}

		if (actorSetupCards.PrintedRoles.Distinct().Count() !=
			actorSetupCards.PrintedRoles.Count)
		{
			issues.Add(new GameConfigValidationError(
				GameConfigValidationErrorType.DuplicateActorSetupCardSource,
				"Actor requires three distinct source Roles."));
		}

		if (actorSetupCards.PrintedRoles.Intersect(completeRoleComposition).Any())
		{
			issues.Add(new GameConfigValidationError(
				GameConfigValidationErrorType.ActorSetupCardInRoleComposition,
				"Actor Setup Cards must stay outside the full Role Composition."));
		}

		if (actorSetupCards.PrintedRoles.Any(role => !role.IsEligibleActorSetupCard()))
		{
			issues.Add(new GameConfigValidationError(
				GameConfigValidationErrorType.IneligibleActorSetupCard,
				"Actor Setup Cards must use one of the directly allowed source Roles."));
		}
	}

	private static IReadOnlyList<GameSessionPlayerConfig> CreatePlayerRoster(
		List<string> playerNames)
	{
		ArgumentNullException.ThrowIfNull(playerNames);
		return playerNames
			.Select(name => new GameSessionPlayerConfig(Guid.NewGuid(), name))
			.ToArray();
	}

	private static RoleLockIn CreateImplicitNonThiefRoleLockIn(
		List<string> playerNames,
		List<MainRoleType> roles,
		ActorSetupCards? actorSetupCards)
	{
		ArgumentNullException.ThrowIfNull(playerNames);
		ArgumentNullException.ThrowIfNull(roles);
		EnforceValidity(playerNames, roles, actorSetupCards);
		if (roles.Contains(MainRoleType.Thief))
		{
			throw new ArgumentException(
				"Thief requires an explicit Role Lock-In with ordered offer cards.",
				nameof(roles));
		}

		var cards = roles
			.Select(role => new PhysicalCharacterCard(Guid.NewGuid(), role))
			.ToArray();
		return new RoleLockIn(
			version: 1,
			playerCount: playerNames.Count,
			roleComposition: cards,
			dealPoolCardIds: cards.Select(card => card.Id));
	}
}

public enum GameConfigValidationErrorType
{
	TooFewPlayers,
	TooManyPlayers,
	NonUniquePlayerNames,
	TooFewRoles,
	TooManyRoles,
	RoleCountMismatch,
	MissingExtraThiefRoles,
	ActorSetupCardCountMismatch,
	ActorSetupCardInRoleComposition,
	IneligibleActorSetupCard,
	DuplicateActorSetupCardSource,
	UnexpectedActorSetupCards,
	MissingHardAlignedWerewolf,
	MissingHardAlignedVillager,
	PublicGroupPartitionMismatch
}

public class GameConfigValidationError
{
	public GameConfigValidationErrorType Type { get; }
	public string Message { get; }

	public GameConfigValidationError(GameConfigValidationErrorType type, string message)
	{
		Type = type;
		Message = message ?? string.Empty;
	}

}
