using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;

namespace Werewolves.Core.StateModels.Models;

public class GameSessionConfig
{
	public const int MinimumPlayerCount = 5;
	public const int MaximumPlayerCount = 30;

	public List<string> Players { get; init; } = new();
	public List<MainRoleType> Roles { get; init; } = new();
	public ActorSetupCards ActorSetupCards { get; init; }
	public RoleLockIn RoleLockIn { get; init; } = null!;

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
		EnforceRoleLockInValidity(Players, RoleLockIn, ActorSetupCards);
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

		var reachableRoleSets = roleLockIn.Offer1 is null
			? new[]
			{
				roleLockIn.DealPool.Select(card => card.PrintedRole).ToArray()
			}
			: new[]
			{
				roleLockIn.DealPool
					.Where(card => card.PrintedRole != MainRoleType.Thief)
					.Select(card => card.PrintedRole)
					.Append(roleLockIn.Offer1.PrintedRole)
					.ToArray(),
				roleLockIn.DealPool
					.Where(card => card.PrintedRole != MainRoleType.Thief)
					.Select(card => card.PrintedRole)
					.Append(roleLockIn.Offer2!.PrintedRole)
					.ToArray()
			};
		foreach (var reachableRoles in reachableRoleSets)
		{
			var reachableIssues = new List<GameConfigValidationError>();
			AddRoleCompositionIssues(
				players.Count,
				reachableRoles,
				actorSetupCards,
				reachableIssues);
			foreach (var issue in reachableIssues.Where(issue => !collectedIssues.Contains(issue)))
			{
				collectedIssues.Add(issue);
			}
		}

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
		AddRoleCompositionIssues(playerCount, roles, actorSetupCards, issues);
		return issues.Count > 0;
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
		ActorSetupCards actorSetupCards,
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

		if (roles.Contains(MainRoleType.Actor)
			&& actorSetupCards.Cards.Count != global::Werewolves.Core.StateModels.Models.ActorSetupCards.RequiredCount)
		{
			issues.Add(new GameConfigValidationError(
				GameConfigValidationErrorType.ActorSetupCardCountMismatch,
				"Actor requires exactly three separate setup cards."));
		}

		if (roles.Contains(MainRoleType.Actor))
		{
			var overlappingActorSetupCards = actorSetupCards.Cards
				.Intersect(roles)
				.Distinct()
				.ToArray();

			if (overlappingActorSetupCards.Length > 0)
			{
				issues.Add(new GameConfigValidationError(
					GameConfigValidationErrorType.ActorSetupCardInRoleComposition,
					$"Actor setup cards must stay outside the Role Composition: {string.Join(", ", overlappingActorSetupCards)}."));
			}

			var ineligibleActorSetupCards = actorSetupCards.Cards
				.Where(role => !role.IsEligibleActorSetupCard())
				.Distinct()
				.ToArray();

			if (ineligibleActorSetupCards.Length > 0)
			{
				issues.Add(new GameConfigValidationError(
					GameConfigValidationErrorType.IneligibleActorSetupCard,
					$"Actor setup cards must be hard-aligned Villager Roles with actionable individual powers: {string.Join(", ", ineligibleActorSetupCards)}."));
			}
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
			playerNames,
			CreateImplicitNonThiefRoleLockIn(
				playerNames,
				roles,
				actorSetupCards),
			actorSetupCards)
	{
	}

	public GameSessionConfig(
		List<string> playerNames,
		RoleLockIn roleLockIn,
		ActorSetupCards? actorSetupCards = null)
	{
		ArgumentNullException.ThrowIfNull(playerNames);
		ArgumentNullException.ThrowIfNull(roleLockIn);
		if (roleLockIn.PlayerCount != playerNames.Count)
		{
			throw new ArgumentException(
				"Role Lock-In Player count must match the Game Session roster.",
				nameof(roleLockIn));
		}

		var roles = roleLockIn.RoleComposition
			.Select(card => card.PrintedRole)
			.ToList();
		var normalizedActorSetupCards = actorSetupCards
			?? global::Werewolves.Core.StateModels.Models.ActorSetupCards.None;
		EnforceRoleLockInValidity(
			playerNames,
			roleLockIn,
			normalizedActorSetupCards);

		Players = playerNames.ToList();
		Roles = roles;
		ActorSetupCards = normalizedActorSetupCards;
		RoleLockIn = roleLockIn;
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
	MissingHardAlignedWerewolf,
	MissingHardAlignedVillager
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
