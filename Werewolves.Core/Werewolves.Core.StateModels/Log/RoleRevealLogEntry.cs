using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records one atomic public Role Reveal exchange.
/// Public reveal establishes current and Moderator-known Role facts, but does not
/// claim ownership of a physical Character Card.
/// </summary>
public sealed record RoleRevealLogEntry : GameLogEntryBase
{
    public required Dictionary<Guid, MainRoleType> RevealedRoles { get; init; }

    protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
    {
        foreach (var (playerId, role) in RevealedRoles)
        {
            mutator.SetPlayerRole(playerId, role);
            mutator.SetModeratorKnownRole(playerId, role);
            mutator.SetPubliclyRevealedRole(playerId, role);
        }

        return this;
    }
}
