using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

/// <summary>
/// Simple Villager role implementation using polymorphic hook listener pattern.
/// Inherits from RoleHookListener as a stateless role.
/// </summary>
internal class SimpleVillagerRole : RoleHookListener
{
    internal override string PublicName => GameStrings.SimpleVillagerRoleName;
    public override ListenerIdentifier Id => ListenerIdentifier.Listener(MainRoleType.SimpleVillager);

	protected override HookListenerActionResult ExecuteCore(GameSession session, ModeratorResponse input)
    {
        return HookListenerActionResult.Skip();
    }

    
}
