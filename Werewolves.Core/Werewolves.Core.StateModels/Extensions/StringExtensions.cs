using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Extensions
{
	public static class StringExtensions
	{
		public static string Format(this string value, params object[] arg) => string.Format(value, arg);
	}

	public static class PublicPlayerExtensions
	{
		public static IEnumerable<IPlayer> WithRole(this IEnumerable<IPlayer> players, MainRoleType? roleType) =>
			PlayerExtensionHelpers.WithRole(players, roleType);
		public static IEnumerable<IPlayer> WithoutRole(this IEnumerable<IPlayer> players, MainRoleType? roleType) =>
			PlayerExtensionHelpers.WithoutRole(players, roleType);

		public static IEnumerable<IPlayer> WithHealth(this IEnumerable<IPlayer> players, PlayerHealth health) =>
			PlayerExtensionHelpers.WithHealth(players, health);
		public static IEnumerable<IPlayer> WithHealth(this Dictionary<Guid, IPlayer> players, PlayerHealth health) =>
			PlayerExtensionHelpers.WithHealth(players.Values, health);

		public static IEnumerable<IPlayer> WithRole(this Dictionary<Guid, IPlayer> players, MainRoleType mainRoleType) =>
			PlayerExtensionHelpers.WithRole(players.Values, mainRoleType);

        public static IEnumerable<IPlayer> FromTeam(this IEnumerable<IPlayer> players, Team team) =>
            players.Where(p => p.State.Team == team);

		public static HashSet<Guid> ToIdSet(this Dictionary<Guid, IPlayer> players) =>
			players.Select(p => p.Key).ToHashSet();

		public static HashSet<Guid> ToIdSet(this IEnumerable<IPlayer> players) =>
			players.Select(p => p.Id).ToHashSet();

		/// <summary>
		/// Filters players by those who have a specific status effect.
		/// </summary>
		public static IEnumerable<IPlayer> WithStatusEffect(this IEnumerable<IPlayer> players, StatusEffectTypes effect) =>
			players.Where(p => p.State.HasStatusEffect(effect));

		/// <summary>
		/// Filters players by those who do NOT have a specific status effect.
		/// </summary>
		public static IEnumerable<IPlayer> WithoutStatusEffect(this IEnumerable<IPlayer> players, StatusEffectTypes effect) =>
			players.Where(p => !p.State.HasStatusEffect(effect));
	}

	internal static class PlayerExtensionHelpers
	{
		internal static IEnumerable<T> WithRole<T>(this IEnumerable<T> players, MainRoleType? roleType) where T : IPlayer =>
			players.Where(p => p.State.MainRole == roleType);
		
		internal static IEnumerable<T> WithoutRole<T>(this IEnumerable<T> players, MainRoleType? roleType) where T : IPlayer =>
			players.Where(p => p.State.MainRole != roleType);

		internal static IEnumerable<T> WithStatusEffect<T>(this IEnumerable<T> players, StatusEffectTypes effect)
			where T : IPlayer =>
			players.Where(p => p.State.HasStatusEffect(effect));

		internal static IEnumerable<T> WithoutStatusEffect<T>(this IEnumerable<T> players, StatusEffectTypes effect)
			where T : IPlayer =>
			players.Where(p => !p.State.HasStatusEffect(effect));

		internal static IEnumerable<T> WithHealth<T>(this IEnumerable<T> players, PlayerHealth health) where T : IPlayer =>
			players.Where(p => p.State.Health == health);

            internal static IEnumerable<T> FromTeam<T>(this IEnumerable<T> players, Team team) where T : IPlayer =>
            players.Where(p => p.State.Team == team);
	}

	/*
protected static class InternalPlayerExtensions
{
	internal static IEnumerable<Player> WithRole(this IEnumerable<Player> players, MainRoleType? mainRoleType) =>
		PlayerExtensionHelpers.WithRole(players, mainRoleType);
	internal static IEnumerable<Player> WithoutRole(this IEnumerable<Player> players, MainRoleType? mainRoleType) =>
		PlayerExtensionHelpers.WithoutRole(players, mainRoleType);

	internal static IEnumerable<Player> WithHealth(this IEnumerable<Player> players, PlayerHealth health) =>
		PlayerExtensionHelpers.WithHealth(players, health);
	internal static IEnumerable<Player> WithHealth(this Dictionary<Guid, Player> players, PlayerHealth health) =>
		PlayerExtensionHelpers.WithHealth(players.Values, health);

	internal static IEnumerable<Player> WhereRevealed(this IEnumerable<Player> players, bool isRevealed) =>
		PlayerExtensionHelpers.WhereRevealed(players, isRevealed);

	internal static IEnumerable<Player> WithRole(this Dictionary<Guid, Player> players, MainRoleType mainRoleType) =>
		PlayerExtensionHelpers.WithRole(players.Values, mainRoleType);

	internal static IEnumerable<Player> WhereRevealed(this Dictionary<Guid, Player> players, bool isRevealed) =>
		PlayerExtensionHelpers.WhereRevealed(players.Values, isRevealed);
}
*/
}
