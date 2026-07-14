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
		EnforceValidity(Players, Roles, ActorSetupCards);
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
		if (players.Count < MinimumPlayerCount)
		{
			issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.TooFewPlayers, "At least five players are required."));
		}
		else if (players.Count > MaximumPlayerCount)
		{
			issues.Add(new GameConfigValidationError(GameConfigValidationErrorType.TooManyPlayers, "At most thirty players are supported."));
		}

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
	public static int GetExpectedRoleCount(int playerCount, List<MainRoleType> roles)
	{
		return playerCount + (roles.Contains(MainRoleType.Thief) ? 2 : 0);
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

		var actualPlayerRoleCountDiff = roles.Count - players.Count;
		var expectedPlayerRoleCountDiff = GetExpectedRoleCount(players.Count, roles) - players.Count;

		if (TryGetPlayerConfigIssues(players, out var playerIssues))
		{
			issues.AddRange(playerIssues);
		}

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

		return issues.Count > 0;
	}

	/// <summary>
	/// Should only try to build this after validating the inputs with TryGetConfigIssues.
	/// </summary>
	/// <param name="playerNames"></param>
	/// <param name="roles"></param>
	public GameSessionConfig(
		List<string> playerNames,
		List<MainRoleType> roles,
		ActorSetupCards? actorSetupCards = null)
	{
		var normalizedActorSetupCards = actorSetupCards
			?? global::Werewolves.Core.StateModels.Models.ActorSetupCards.None;
		EnforceValidity(playerNames, roles, normalizedActorSetupCards);

		Players = playerNames;
		Roles = roles;
		ActorSetupCards = normalizedActorSetupCards;
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
