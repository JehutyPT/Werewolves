using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

public class GameSessionConfig
{
	public List<string> Players { get; init; } = new();
	public List<MainRoleType> Roles { get; init; } = new();

	public static Dictionary<MainRoleType, NumberRangeConstraint> RoleCountConstraints { get; } = new()
	{
		// insert all roles with default Any constraint
		[MainRoleType.SimpleWerewolf] = NumberRangeConstraint.AtLeast(1),
		[MainRoleType.BigBadWolf] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.AccursedWolfFather] = NumberRangeConstraint.SingleOptional,
		[MainRoleType.WhiteWerewolf] = NumberRangeConstraint.SingleOptional,

		[MainRoleType.SimpleVillager] = NumberRangeConstraint.AtLeast(1),
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

	internal static void EnforceValidity(List<string> players, List<MainRoleType> roles)
	{
		if (TryGetConfigIssues(players, roles, out var issues))
		{
			throw new InvalidOperationException("Game session configuration is invalid:\n" + string.Join(", ", issues));
		}
	}

	internal void EnforceValidity()
	{
		EnforceValidity(Players, Roles);
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

		// Player count sanity
		if (players.Count < 5)
		{
			issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.TooFewPlayers, "At least five players are required."));
		}

		return issues.Count > 0;
	}

	/// <summary>
	/// Helper for UI to display expected vs actual role count.
	/// Returns the number of roles expected based on player count and special roles (Thief needs +2, Actor needs +3).
	/// </summary>
	/// <param name="playerCount">The number of players in the game.</param>
	/// <param name="roles">The list of roles selected for the game.</param>
	/// <returns>The expected total role count.</returns>
	public static int GetExpectedRoleCount(int playerCount, List<MainRoleType> roles)
	{
		return playerCount + (roles.Contains(MainRoleType.Thief) ? 2 : 0) + (roles.Contains(MainRoleType.Actor) ? 3 : 0);
	}

	/// <summary>
	/// The main helper method to validate a game configuration. Use this before trying to create a GameSessionConfig.
	/// </summary>
	/// <param name="players"></param>
	/// <param name="roles"></param>
	/// <param name="issues"></param>
	/// <returns></returns>
	public static bool TryGetConfigIssues(List<string> players, List<MainRoleType> roles, out List<GameConfigValidationError> issues)
	{
		issues = new List<GameConfigValidationError>();

		var actualPlayerRoleCountDiff = roles.Count - players.Count;
		var expectedPlayerRoleCountDiff = GetExpectedRoleCount(players.Count, roles) - players.Count;

		if (TryGetPlayerConfigIssues(players, out var playerIssues))
		{
			issues.AddRange(playerIssues);
		}

		// Role count checks
		if (actualPlayerRoleCountDiff > expectedPlayerRoleCountDiff)
		{
			issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.TooManyRoles, $"Roles in excess: { actualPlayerRoleCountDiff - expectedPlayerRoleCountDiff}"));
		}
		else if (actualPlayerRoleCountDiff < expectedPlayerRoleCountDiff)
		{
			var delta = expectedPlayerRoleCountDiff - actualPlayerRoleCountDiff;

			// Missing extras for Thief/Actor
			if (roles.Contains(MainRoleType.Thief) && roles.Contains(MainRoleType.Actor))
			{
				issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.MissingExtraThiefActorRoles, $"Missing extra roles required by Thief and Actor: {delta}"));
			}
			else if (roles.Contains(MainRoleType.Thief))
			{
				issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.MissingExtraThiefRoles, $"Missing extra roles required by Thief (needs two extra roles): {delta}"));
			}
			else if (roles.Contains(MainRoleType.Actor))
			{
				issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.MissingExtraActorRoles, $"Missing 3 extra roles required by Actor: {delta}"));
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

		return issues.Count > 0;
	}

	/// <summary>
	/// Should only try to build this after validating the inputs with TryGetConfigIssues.
	/// </summary>
	/// <param name="playerNames"></param>
	/// <param name="roles"></param>
	public GameSessionConfig(List<string> playerNames, List<MainRoleType> roles)
	{
		EnforceValidity(playerNames, roles);

		Players = playerNames;
		Roles = roles;
	}
}

public enum GameConfigValidationErrorType
{
	TooFewPlayers,
	NonUniquePlayerNames,
	TooFewRoles,
	TooManyRoles,
	RoleCountMismatch,
	MissingExtraActorRoles,
	MissingExtraThiefRoles,
	MissingExtraThiefActorRoles
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