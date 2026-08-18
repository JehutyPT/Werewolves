using System.Collections.Immutable;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.StateModels.Core;

/// <summary>
/// One read-only snapshot of the transient Game Session execution point.
/// </summary>
internal sealed class ExecutionView
{
	private readonly AcceptedObservationRecoveryCursor?
		_acceptedObservationRecoveryCursor;
	private readonly DomainRecoveryCursor? _domainRecoveryCursor;

	internal ExecutionView(
		GamePhase currentPhase,
		string? subPhaseId,
		string? activeSubPhaseStage,
		IEnumerable<string> completedSubPhaseStages,
		ListenerIdentifier? currentListener,
		string? currentListenerState,
		ModeratorInstruction? pendingInstruction,
		AcceptedObservationRecoveryCursor? acceptedObservationRecoveryCursor,
		DomainRecoveryCursor? domainRecoveryCursor)
	{
		CurrentPhase = currentPhase;
		SubPhaseId = subPhaseId;
		ActiveSubPhaseStage = activeSubPhaseStage;
		CompletedSubPhaseStages = completedSubPhaseStages
			.ToImmutableHashSet(StringComparer.Ordinal);
		CurrentListener = currentListener;
		CurrentListenerState = currentListenerState;
		PendingInstruction = pendingInstruction;
		_acceptedObservationRecoveryCursor =
			Copy(acceptedObservationRecoveryCursor);
		_domainRecoveryCursor = Copy(domainRecoveryCursor);
	}

	internal GamePhase CurrentPhase { get; }
	internal string? SubPhaseId { get; }
	internal string? ActiveSubPhaseStage { get; }
	internal ImmutableHashSet<string> CompletedSubPhaseStages { get; }
	internal ListenerIdentifier? CurrentListener { get; }
	internal string? CurrentListenerState { get; }
	internal ModeratorInstruction? PendingInstruction { get; }

	internal AcceptedObservationRecoveryCursor?
		AcceptedObservationRecoveryCursor =>
		Copy(_acceptedObservationRecoveryCursor);

	internal DomainRecoveryCursor? DomainRecoveryCursor =>
		Copy(_domainRecoveryCursor);

	internal T? GetSubPhase<T>() where T : struct, Enum =>
		SubPhaseId != null && Enum.TryParse<T>(SubPhaseId, out var result)
			? result
			: null;

	internal T? GetCurrentListenerState<T>(ListenerIdentifier listener)
		where T : struct, Enum =>
		CurrentListener?.Equals(listener) == true &&
		CurrentListenerState != null &&
		Enum.TryParse<T>(CurrentListenerState, out var result)
			? result
			: null;

	internal bool TryGetActiveGameHook(out GameHook hook) =>
		Enum.TryParse(ActiveSubPhaseStage, out hook);

	internal bool HasSubPhaseStageCompleted(string subPhaseStageId) =>
		CompletedSubPhaseStages.Contains(subPhaseStageId);

	internal bool HasSameRecoveryCursors(ExecutionView other)
	{
		ArgumentNullException.ThrowIfNull(other);
		return AcceptedObservationCursorEquals(
			_acceptedObservationRecoveryCursor,
			other._acceptedObservationRecoveryCursor) &&
			DomainCursorEquals(
				_domainRecoveryCursor,
				other._domainRecoveryCursor);
	}

	private static bool AcceptedObservationCursorEquals(
		AcceptedObservationRecoveryCursor? left,
		AcceptedObservationRecoveryCursor? right) =>
		ReferenceEquals(left, right) ||
		left != null &&
		right != null &&
		left.Version == right.Version &&
		left.AcceptedObservationSemantic == right.AcceptedObservationSemantic &&
		left.ObservedRole == right.ObservedRole &&
		left.ContinuationRole == right.ContinuationRole &&
		left.RetainedLittleGirlGuidanceDecision ==
			right.RetainedLittleGirlGuidanceDecision &&
		left.NextInstructionSemantic == right.NextInstructionSemantic &&
		left.NextInstructionId == right.NextInstructionId;

	private static bool DomainCursorEquals(
		DomainRecoveryCursor? left,
		DomainRecoveryCursor? right) =>
		ReferenceEquals(left, right) ||
		left != null &&
		right != null &&
		left.Version == right.Version &&
		left.Kind == right.Kind &&
		left.SourceRole == right.SourceRole &&
		left.CommittedActionType == right.CommittedActionType &&
		left.CommittedDayActionType == right.CommittedDayActionType &&
		left.ActingPlayerId == right.ActingPlayerId &&
		StringComparer.Ordinal.Equals(
			left.SourcePowerIdentifier,
			right.SourcePowerIdentifier) &&
		left.PowerInstanceId == right.PowerInstanceId &&
		left.PowerInstanceOrigin == right.PowerInstanceOrigin &&
		left.OneUseResourceId == right.OneUseResourceId &&
		left.ActorSetupCardId == right.ActorSetupCardId &&
		left.ActorBorrowedActivationId == right.ActorBorrowedActivationId &&
		left.CommittedTargetIds.SequenceEqual(right.CommittedTargetIds) &&
		left.NextInstructionSemantic == right.NextInstructionSemantic &&
		left.NextInstructionId == right.NextInstructionId;

	private static AcceptedObservationRecoveryCursor? Copy(
		AcceptedObservationRecoveryCursor? cursor) =>
		cursor == null
			? null
			: new AcceptedObservationRecoveryCursor
			{
				Version = cursor.Version,
				AcceptedObservationSemantic =
					cursor.AcceptedObservationSemantic,
				ObservedRole = cursor.ObservedRole,
				ContinuationRole = cursor.ContinuationRole,
				RetainedLittleGirlGuidanceDecision =
					cursor.RetainedLittleGirlGuidanceDecision,
				NextInstructionSemantic = cursor.NextInstructionSemantic,
				NextInstructionId = cursor.NextInstructionId
			};

	private static DomainRecoveryCursor? Copy(DomainRecoveryCursor? cursor) =>
		cursor == null
			? null
			: new DomainRecoveryCursor
			{
				Version = cursor.Version,
				Kind = cursor.Kind,
				SourceRole = cursor.SourceRole,
				CommittedActionType = cursor.CommittedActionType,
				CommittedDayActionType = cursor.CommittedDayActionType,
				ActingPlayerId = cursor.ActingPlayerId,
				SourcePowerIdentifier = cursor.SourcePowerIdentifier,
				PowerInstanceId = cursor.PowerInstanceId,
				PowerInstanceOrigin = cursor.PowerInstanceOrigin,
				OneUseResourceId = cursor.OneUseResourceId,
				ActorSetupCardId = cursor.ActorSetupCardId,
				ActorBorrowedActivationId = cursor.ActorBorrowedActivationId,
				CommittedTargetIds = cursor.CommittedTargetIds.ToList(),
				NextInstructionSemantic = cursor.NextInstructionSemantic,
				NextInstructionId = cursor.NextInstructionId
			};
}
