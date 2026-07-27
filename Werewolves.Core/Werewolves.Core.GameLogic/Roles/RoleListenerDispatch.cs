using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
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
		var listener = GetActiveListener(
			listenerId,
			admissions,
			getOrCreateListener);
		return listener?.Execute(session, input);
	}

	internal static string? ResolvePendingInstructionContinuation(
		ListenerIdentifier listenerId,
		GameHook hook,
		IRoleAdmissionSource admissions,
		Func<ListenerIdentifier, Func<IGameHookListener>, IGameHookListener> getOrCreateListener,
		GameSession session,
		ModeratorInstruction pendingInstruction)
	{
		var listener = GetActiveListener(
			listenerId,
			admissions,
			getOrCreateListener);
		if (listener == null ||
		    !listener.TryResolvePendingInstructionContinuation(
			    hook,
			    session,
			    pendingInstruction,
			    out var listenerState))
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(listenerState))
		{
			throw new InvalidOperationException(
				$"Listener '{listenerId}' resolved a pending instruction without a continuation state.");
		}

		return listenerState;
	}

	private static IGameHookListener? GetActiveListener(
		ListenerIdentifier listenerId,
		IRoleAdmissionSource admissions,
		Func<ListenerIdentifier, Func<IGameHookListener>, IGameHookListener> getOrCreateListener)
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

		return getOrCreateListener(listenerId, listenerFactory);
	}
}
