using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Roles;

internal static class RoleListenerDispatch
{
	internal static HookListenerActionResult? Dispatch(
		ListenerIdentifier listenerId,
		IRoleAdmissionSource admissions,
		Func<ListenerIdentifier, Func<IGameHookListener>, IGameHookListener> getOrCreateListener,
		GameSession session,
		ModeratorResponse input)
	{
		if (admissions.GetAdmission(listenerId) != RoleAdmissionKind.Active)
		{
			return null;
		}

		if (!admissions.TryGetListenerFactory(listenerId, out var listenerFactory))
		{
			throw new InvalidOperationException(
				$"Admitted active listener '{listenerId}' has no listener factory.");
		}

		var listener = getOrCreateListener(listenerId, listenerFactory);
		return listener.Execute(session, input);
	}
}
