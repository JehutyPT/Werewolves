using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.Tests.Helpers;

/// <summary>
/// Factory methods for creating ModeratorResponse instances in tests.
/// </summary>
public static class ResponseFactory
{
    /// <summary>
    /// Helper to get a player by index from the session.
    /// </summary>
    public static IPlayer GetPlayer(IGameSession session, int index)
        => session.GetPlayers().ElementAt(index);

    /// <summary>
    /// Helper to get a player by name from the session.
    /// </summary>
    public static IPlayer GetPlayer(IGameSession session, string name)
        => session.GetPlayers().First(p => p.Name == name);

    /// <summary>
    /// Helper to find the first player with a specific role.
    /// </summary>
    public static IPlayer? GetPlayerByRole(IGameSession session, MainRoleType role)
        => session.GetPlayers().FirstOrDefault(p => p.State.MainRole == role);

    /// <summary>
    /// Helper to find all players with a specific role.
    /// </summary>
    public static IEnumerable<IPlayer> GetPlayersByRole(IGameSession session, MainRoleType role)
        => session.GetPlayers().Where(p => p.State.MainRole == role);
}
