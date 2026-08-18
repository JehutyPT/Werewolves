using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Core;

/// <summary>
/// One closed, structural movement from an expected execution cursor to its
/// complete candidate cursor.
/// </summary>
internal abstract class ExecutionTransition
{
	private ExecutionTransition(
		ExecutionView expected,
		ExecutionView candidate)
	{
		Expected = expected;
		Candidate = candidate;
	}

	internal ExecutionView Expected { get; }
	internal ExecutionView Candidate { get; }

	internal static ExecutionTransition ChangeMainPhase(
		ExecutionView expected,
		GamePhase newPhase) =>
		new MainPhaseTransition(
			expected,
			CreateCandidate(
				expected,
				currentPhase: newPhase,
				subPhaseId: null,
				activeSubPhaseStage: null,
				completedSubPhaseStages: [],
				currentListener: null,
				currentListenerState: null));

	internal static ExecutionTransition ChangeSubPhase(
		ExecutionView expected,
		Enum newSubPhase)
	{
		ArgumentNullException.ThrowIfNull(newSubPhase);

		return new SubPhaseTransition(
			expected,
			CreateCandidate(
				expected,
				expected.CurrentPhase,
				newSubPhase.ToString(),
				activeSubPhaseStage: null,
				completedSubPhaseStages: [],
				currentListener: null,
				currentListenerState: null));
	}

	internal static ExecutionTransition EnterStage(
		ExecutionView expected,
		string stageId) =>
		new StageEntryTransition(
			expected,
			CreateCandidate(
				expected,
				expected.CurrentPhase,
				expected.SubPhaseId,
				stageId,
				expected.CompletedSubPhaseStages,
				currentListener: null,
				currentListenerState: null));

	internal static ExecutionTransition CompleteStage(
		ExecutionView expected)
	{
		var activeStage = expected.ActiveSubPhaseStage;
		var completedStages = activeStage == null
			? expected.CompletedSubPhaseStages
			: expected.CompletedSubPhaseStages.Add(activeStage);

		return new StageCompletionTransition(
			expected,
			CreateCandidate(
				expected,
				expected.CurrentPhase,
				expected.SubPhaseId,
				activeSubPhaseStage: null,
				completedStages,
				currentListener: null,
				currentListenerState: null));
	}

	internal static ExecutionTransition PauseOrResumeListener(
		ExecutionView expected,
		ListenerIdentifier listener,
		string listenerState) =>
		new ListenerPauseOrResumeTransition(
			expected,
			CreateCandidate(
				expected,
				expected.CurrentPhase,
				expected.SubPhaseId,
				expected.ActiveSubPhaseStage,
				expected.CompletedSubPhaseStages,
				listener,
				listenerState));

	internal static ExecutionTransition ClearListener(ExecutionView expected) =>
		new ListenerClearTransition(
			expected,
			CreateCandidate(
				expected,
				expected.CurrentPhase,
				expected.SubPhaseId,
				expected.ActiveSubPhaseStage,
				expected.CompletedSubPhaseStages,
				currentListener: null,
				currentListenerState: null));

	internal void EnforceValidAgainst(ExecutionView current)
	{
		ArgumentNullException.ThrowIfNull(current);
		if (!HasSameCursor(current, Expected))
		{
			throw new InvalidOperationException(
				"The execution transition is stale.");
		}

		EnforceStructurallyValid(Expected, "starting");
		EnforceStructurallyValid(Candidate, "candidate");
		if (!IsValidMovement())
		{
			throw new InvalidOperationException(
				"The execution transition is structurally invalid.");
		}
	}

	protected abstract bool IsValidMovement();
	internal abstract void NotifyObserver(IStateChangeObserver? observer);

	private static void EnforceStructurallyValid(
		ExecutionView view,
		string description)
	{
		var hasListener = view.CurrentListener != null;
		var hasListenerState = view.CurrentListenerState != null;
		if (!Enum.IsDefined(view.CurrentPhase) ||
			(view.SubPhaseId != null &&
			 string.IsNullOrWhiteSpace(view.SubPhaseId)) ||
			(view.ActiveSubPhaseStage != null &&
			 string.IsNullOrWhiteSpace(view.ActiveSubPhaseStage)) ||
			view.CompletedSubPhaseStages.Any(string.IsNullOrWhiteSpace) ||
			(view.ActiveSubPhaseStage != null &&
			 view.CompletedSubPhaseStages.Contains(view.ActiveSubPhaseStage)) ||
			hasListener != hasListenerState ||
			(hasListenerState &&
			 string.IsNullOrWhiteSpace(view.CurrentListenerState)) ||
			(hasListener && view.ActiveSubPhaseStage == null))
		{
			throw new InvalidOperationException(
				$"The {description} execution cursor is structurally invalid.");
		}
	}

	private static bool HasSameCursor(
		ExecutionView left,
		ExecutionView right) =>
		HasSameStageCursor(left, right) &&
		left.CurrentListener == right.CurrentListener &&
		StringComparer.Ordinal.Equals(
			left.CurrentListenerState,
			right.CurrentListenerState);

	private static bool HasSameStageCursor(
		ExecutionView left,
		ExecutionView right) =>
		HasSamePhaseAndSubPhase(left, right) &&
		StringComparer.Ordinal.Equals(
			left.ActiveSubPhaseStage,
			right.ActiveSubPhaseStage) &&
		left.CompletedSubPhaseStages.SetEquals(
			right.CompletedSubPhaseStages);

	private static bool HasSamePhaseAndSubPhase(
		ExecutionView left,
		ExecutionView right) =>
		left.CurrentPhase == right.CurrentPhase &&
		StringComparer.Ordinal.Equals(left.SubPhaseId, right.SubPhaseId);

	private static ExecutionView CreateCandidate(
		ExecutionView expected,
		GamePhase currentPhase,
		string? subPhaseId,
		string? activeSubPhaseStage,
		IEnumerable<string> completedSubPhaseStages,
		ListenerIdentifier? currentListener,
		string? currentListenerState)
	{
		ArgumentNullException.ThrowIfNull(expected);
		ArgumentNullException.ThrowIfNull(completedSubPhaseStages);

		return new ExecutionView(
			currentPhase,
			subPhaseId,
			activeSubPhaseStage,
			completedSubPhaseStages,
			currentListener,
			currentListenerState,
			expected.PendingInstruction,
			expected.AcceptedObservationRecoveryCursor,
			expected.DomainRecoveryCursor);
	}

	private sealed class MainPhaseTransition(
		ExecutionView expected,
		ExecutionView candidate)
		: ExecutionTransition(expected, candidate)
	{
		protected override bool IsValidMovement() =>
			Candidate.CurrentPhase != Expected.CurrentPhase &&
			Candidate.SubPhaseId == null &&
			Candidate.ActiveSubPhaseStage == null &&
			Candidate.CompletedSubPhaseStages.Count == 0 &&
			Candidate.CurrentListener == null &&
			Candidate.CurrentListenerState == null;

		internal override void NotifyObserver(
			IStateChangeObserver? observer) =>
			observer?.OnMainPhaseChanged(Candidate.CurrentPhase);
	}

	private sealed class SubPhaseTransition(
		ExecutionView expected,
		ExecutionView candidate)
		: ExecutionTransition(expected, candidate)
	{
		protected override bool IsValidMovement() =>
			Candidate.CurrentPhase == Expected.CurrentPhase &&
			!StringComparer.Ordinal.Equals(
				Candidate.SubPhaseId,
				Expected.SubPhaseId) &&
			Candidate.ActiveSubPhaseStage == null &&
			Candidate.CompletedSubPhaseStages.Count == 0 &&
			Candidate.CurrentListener == null &&
			Candidate.CurrentListenerState == null;

		internal override void NotifyObserver(
			IStateChangeObserver? observer) =>
			observer?.OnSubPhaseChanged(Candidate.SubPhaseId);
	}

	private sealed class StageEntryTransition(
		ExecutionView expected,
		ExecutionView candidate)
		: ExecutionTransition(expected, candidate)
	{
		protected override bool IsValidMovement() =>
			HasSamePhaseAndSubPhase(Candidate, Expected) &&
			Expected.ActiveSubPhaseStage == null &&
			Candidate.ActiveSubPhaseStage != null &&
			!Expected.CompletedSubPhaseStages.Contains(
				Candidate.ActiveSubPhaseStage) &&
			Candidate.CompletedSubPhaseStages.SetEquals(
				Expected.CompletedSubPhaseStages) &&
			Candidate.CurrentListener == null &&
			Candidate.CurrentListenerState == null;

		internal override void NotifyObserver(
			IStateChangeObserver? observer) =>
			observer?.OnSubPhaseStageChanged(
				Candidate.ActiveSubPhaseStage);
	}

	private sealed class StageCompletionTransition(
		ExecutionView expected,
		ExecutionView candidate)
		: ExecutionTransition(expected, candidate)
	{
		protected override bool IsValidMovement() =>
			HasSamePhaseAndSubPhase(Candidate, Expected) &&
			Expected.ActiveSubPhaseStage != null &&
			!Expected.CompletedSubPhaseStages.Contains(
				Expected.ActiveSubPhaseStage) &&
			Candidate.ActiveSubPhaseStage == null &&
			Candidate.CompletedSubPhaseStages.SetEquals(
				Expected.CompletedSubPhaseStages.Add(
					Expected.ActiveSubPhaseStage)) &&
			Candidate.CurrentListener == null &&
			Candidate.CurrentListenerState == null;

		internal override void NotifyObserver(
			IStateChangeObserver? observer) =>
			observer?.OnSubPhaseStageChanged(
				Candidate.ActiveSubPhaseStage);
	}

	private sealed class ListenerPauseOrResumeTransition(
		ExecutionView expected,
		ExecutionView candidate)
		: ExecutionTransition(expected, candidate)
	{
		protected override bool IsValidMovement() =>
			HasSameStageCursor(Candidate, Expected) &&
			Expected.ActiveSubPhaseStage != null &&
			Candidate.CurrentListener != null &&
			Candidate.CurrentListenerState != null &&
			(Expected.CurrentListener == null ||
			 Expected.CurrentListener == Candidate.CurrentListener);

		internal override void NotifyObserver(
			IStateChangeObserver? observer) =>
			observer?.OnListenerChanged(
				Candidate.CurrentListener,
				Candidate.CurrentListenerState);
	}

	private sealed class ListenerClearTransition(
		ExecutionView expected,
		ExecutionView candidate)
		: ExecutionTransition(expected, candidate)
	{
		protected override bool IsValidMovement() =>
			HasSameStageCursor(Candidate, Expected) &&
			Candidate.CurrentListener == null &&
			Candidate.CurrentListenerState == null;

		internal override void NotifyObserver(
			IStateChangeObserver? observer) =>
			observer?.OnListenerChanged(
				Candidate.CurrentListener,
				Candidate.CurrentListenerState);
	}
}
