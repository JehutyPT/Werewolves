using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Serialization;

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
		ModeratorInstruction pendingInstruction,
		AcceptedObservationRecoveryCursor? acceptedObservationRecoveryCursor = null,
		DomainRecoveryCursor? domainRecoveryCursor = null)
	{
		var listener = GetActiveListener(
			listenerId,
			admissions,
			getOrCreateListener);
		if (listener == null)
		{
			return null;
		}

		if (listener is IDeclaredRoleWorkflow declaredWorkflow)
		{
			var runtime = declaredWorkflow.GetWorkflowRuntime(hook);
			if (runtime == null)
			{
				return null;
			}

			var candidate = runtime.ClassifyRecoveryCandidate(
						session,
						pendingInstruction,
						acceptedObservationRecoveryCursor,
						domainRecoveryCursor);
			return candidate.Kind switch
			{
				RoleWorkflowRecoveryCandidateKind.Unrelated => null,
				RoleWorkflowRecoveryCandidateKind.Authenticated
					when !string.IsNullOrWhiteSpace(candidate.ContinuationState) =>
					candidate.ContinuationState,
				RoleWorkflowRecoveryCandidateKind.Authenticated =>
					throw new InvalidOperationException(
						$"Declared workflow '{listenerId}' authenticated no continuation state."),
				RoleWorkflowRecoveryCandidateKind.ClaimedButInvalid =>
					throw new InvalidOperationException(candidate.Failure),
				_ => throw new InvalidOperationException(
					$"Unknown declared workflow recovery result for '{listenerId}'.")
			};
		}

		if (!listener.TryResolvePendingInstructionContinuation(
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

	internal static string? ResolveDeclaredPendingInstructionContinuation(
		ListenerIdentifier listenerId,
		GameHook hook,
		IRoleAdmissionSource admissions,
		Func<ListenerIdentifier, Func<IGameHookListener>, IGameHookListener>
			getOrCreateListener,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		AcceptedObservationRecoveryCursor? acceptedObservationRecoveryCursor,
		DomainRecoveryCursor? domainRecoveryCursor)
	{
		var listener = GetActiveListener(
			listenerId,
			admissions,
			getOrCreateListener);
		if (listener is not IDeclaredRoleWorkflow declaredWorkflow)
		{
			return null;
		}

		var runtime = declaredWorkflow.GetWorkflowRuntime(hook);
		if (runtime == null)
		{
			return null;
		}

		var candidate = runtime.ClassifyRecoveryCandidate(
				session,
				pendingInstruction,
				acceptedObservationRecoveryCursor,
				domainRecoveryCursor);
		return candidate.Kind switch
		{
			RoleWorkflowRecoveryCandidateKind.Unrelated => null,
			RoleWorkflowRecoveryCandidateKind.Authenticated
				when !string.IsNullOrWhiteSpace(candidate.ContinuationState) =>
				candidate.ContinuationState,
			RoleWorkflowRecoveryCandidateKind.Authenticated =>
				throw new InvalidOperationException(
					$"Declared workflow '{listenerId}' authenticated no continuation state."),
			RoleWorkflowRecoveryCandidateKind.ClaimedButInvalid =>
				throw new InvalidOperationException(candidate.Failure),
			_ => throw new InvalidOperationException(
				$"Unknown declared workflow recovery result for '{listenerId}'.")
		};
	}

	internal static bool ValidateDeclaredWorkflowRecovery(
		ListenerIdentifier listenerId,
		GameHook hook,
		IRoleAdmissionSource admissions,
		Func<ListenerIdentifier, Func<IGameHookListener>, IGameHookListener>
			getOrCreateListener,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		AcceptedObservationRecoveryCursor cursor)
	{
		var listener = GetActiveListener(
			listenerId,
			admissions,
			getOrCreateListener);
		if (listener is not IDeclaredRoleWorkflow declaredWorkflow)
		{
			return false;
		}

		var runtime = declaredWorkflow.GetWorkflowRuntime(hook);
		if (runtime == null)
		{
			return false;
		}

		var candidate = runtime.ClassifyRecoveryCandidate(
				session,
				pendingInstruction,
				acceptedObservationCursor: cursor);
		if (candidate.Kind == RoleWorkflowRecoveryCandidateKind.Authenticated)
		{
			return true;
		}

		throw new InvalidOperationException(
			candidate.Failure ??
			$"Pending instruction '{pendingInstruction.Semantic}' does not authenticate declared workflow '{listenerId}:{hook}'.");
	}

	internal static bool TryValidateTargetPrivateCommittedRecoveryBoundary(
		ListenerIdentifier listenerId,
		IRoleAdmissionSource admissions,
		Func<ListenerIdentifier, Func<IGameHookListener>, IGameHookListener>
			getOrCreateListener,
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		TargetPrivateRolePowerRecoveryBoundary committedBoundary,
		ModeratorInstruction nextInstruction)
	{
		var listener = GetActiveListener(
			listenerId,
			admissions,
			getOrCreateListener);
		if (listener is IDeclaredRoleWorkflow declaredWorkflow)
		{
			if (!session.Execution.TryGetActiveGameHook(out var hook))
			{
				throw new InvalidOperationException(
					$"Declared workflow '{listenerId}' has no active hook for committed recovery validation.");
			}

			var runtime = declaredWorkflow.GetWorkflowRuntime(hook);
			return runtime?.TryValidateCommittedRecoveryBoundary(
					session,
					startingInstruction,
					input,
					committedBoundary,
					nextInstruction) ?? false;
		}

		return listener is ITargetPrivateRolePowerRecoveryCapability capability &&
		       capability.TryValidateCommittedRecoveryBoundary(
			       session,
			       startingInstruction,
			       input,
			       committedBoundary,
			       nextInstruction);
	}

	internal static bool TryValidateRecurringCommittedRecoveryBoundary(
		ListenerIdentifier listenerId,
		IRoleAdmissionSource admissions,
		Func<ListenerIdentifier, Func<IGameHookListener>, IGameHookListener>
			getOrCreateListener,
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		RecurringRolePowerCommittedLogEntry committedBoundary,
		ModeratorInstruction nextInstruction)
	{
		var listener = GetActiveListener(
			listenerId,
			admissions,
			getOrCreateListener);
		if (listener is not IDeclaredRoleWorkflow declaredWorkflow ||
		    !session.Execution.TryGetActiveGameHook(out var hook))
		{
			return false;
		}

		var runtime = declaredWorkflow.GetWorkflowRuntime(hook);
		return runtime?.TryValidateCommittedRecoveryBoundary(
			session,
			startingInstruction,
			input,
			committedBoundary,
			nextInstruction) ?? false;
	}

	internal static bool TryValidateOneUseCommittedRecoveryBoundary(
		ListenerIdentifier listenerId,
		IRoleAdmissionSource admissions,
		Func<ListenerIdentifier, Func<IGameHookListener>, IGameHookListener>
			getOrCreateListener,
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		OneUseRolePowerCommittedLogEntry committedBoundary,
		ModeratorInstruction nextInstruction)
	{
		var listener = GetActiveListener(
			listenerId,
			admissions,
			getOrCreateListener);
		if (listener is not IDeclaredRoleWorkflow declaredWorkflow ||
		    !session.Execution.TryGetActiveGameHook(out var hook))
		{
			return false;
		}

		var runtime = declaredWorkflow.GetWorkflowRuntime(hook);
		return runtime?.TryValidateCommittedRecoveryBoundary(
			session,
			startingInstruction,
			input,
			committedBoundary,
			nextInstruction) ?? false;
	}

	internal static bool TryValidateDeclaredDomainRecoveryCursorIdentity(
		GameSession session,
		ListenerIdentifier listenerId,
		GameHook hook,
		IRoleAdmissionSource admissions,
		Func<ListenerIdentifier, Func<IGameHookListener>, IGameHookListener>
			getOrCreateListener,
		ModeratorInstruction pendingInstruction,
		DomainRecoveryCursor cursor)
	{
		var listener = GetActiveListener(
			listenerId,
			admissions,
			getOrCreateListener);
		if (listener is not IDeclaredRoleWorkflow declaredWorkflow)
		{
			return false;
		}

		var runtime = declaredWorkflow.GetWorkflowRuntime(hook);
		if (runtime == null)
		{
			return false;
		}

		var candidate = runtime.ClassifyRecoveryCandidate(
			session,
			pendingInstruction,
			domainCursor: cursor);
		if (candidate.Kind == RoleWorkflowRecoveryCandidateKind.Authenticated)
		{
			return true;
		}

		throw new InvalidOperationException(
			candidate.Failure ??
			$"Pending instruction '{pendingInstruction.Semantic}' does not authenticate declared workflow '{listenerId}:{hook}'.");
	}

	internal static bool TryValidateTargetPrivateRecoveryCursorIdentity(
		GameSession session,
		ListenerIdentifier listenerId,
		IRoleAdmissionSource admissions,
		Func<ListenerIdentifier, Func<IGameHookListener>, IGameHookListener>
			getOrCreateListener,
		ModeratorInstruction pendingInstruction,
		DomainRecoveryCursor cursor)
	{
		var listener = GetActiveListener(
			listenerId,
			admissions,
			getOrCreateListener);
		if (listener is IDeclaredRoleWorkflow declaredWorkflow)
		{
			var runtime = declaredWorkflow.GetWorkflowRuntime(
				GameHook.NightMainActionLoop);
			if (runtime == null)
			{
				return false;
			}

			var candidate = runtime.ClassifyRecoveryCandidate(
					session,
					pendingInstruction,
					domainCursor: cursor);
			if (candidate.Kind ==
			    RoleWorkflowRecoveryCandidateKind.Authenticated)
			{
				return true;
			}

			throw new InvalidOperationException(
				candidate.Failure ??
				$"Pending instruction '{pendingInstruction.Semantic}' does not authenticate declared workflow '{listenerId}'.");
		}

		if (listener is not ITargetPrivateRolePowerRecoveryCapability capability)
		{
			return false;
		}

		capability.ValidateRecoveryCursorIdentity(session, cursor);
		return true;
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
