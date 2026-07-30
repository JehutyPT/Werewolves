using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class LittleGirlRole : NightRoleIdOnlyHookListener
{
	internal static readonly RolePowerDefinition SpyingPower = new(
		new RolePowerIdentifier("little-girl-spying"),
		RolePowerCategory.Passive);

	internal override string PublicName => GameStrings.LittleGirlRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.LittleGirl);

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input) =>
		session.TurnNumber == 1
			? base.Execute(session, input)
			: HookListenerActionResult.Skip();
}
