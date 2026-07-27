using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records the complete exact-Role holder set privately observed by the Moderator.
/// Role Identification establishes current Role knowledge without assigning a
/// physical Character Card or revealing the Role publicly.
/// </summary>
public sealed record RoleIdentificationLogEntry : GameLogEntryBase
{
    public required HashSet<Guid> PlayerIds { get; init; }

    public required MainRoleType Role { get; init; }

    protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
    {
        foreach (var playerId in PlayerIds)
        {
            mutator.SetPlayerRole(playerId, Role);
            mutator.SetModeratorKnownRole(playerId, Role);
        }

        return this;
    }
}
