using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records one atomic public Role Reveal exchange.
/// Public reveal records public physical history only. It does not establish or
/// overwrite current Role, private Moderator knowledge, or physical ownership.
/// </summary>
public sealed record RoleRevealLogEntry : GameLogEntryBase
{
    public required Dictionary<Guid, MainRoleType> RevealedRoles { get; init; }

    protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
    {
        foreach (var (playerId, role) in RevealedRoles)
        {
            mutator.SetPubliclyRevealedRole(playerId, role);
        }

        return this;
    }
}
