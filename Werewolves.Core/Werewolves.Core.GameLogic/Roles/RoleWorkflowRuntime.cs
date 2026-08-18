using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Roles;

internal interface IDeclaredRoleWorkflow
{
	RoleWorkflowRuntime WorkflowRuntime { get; }

	RoleWorkflowRuntime? GetWorkflowRuntime(GameHook hook) =>
		WorkflowRuntime.Hook == hook ? WorkflowRuntime : null;
}

internal enum RoleWorkflowRecoveryCandidateKind
{
	Unrelated,
	Authenticated,
	ClaimedButInvalid
}

internal readonly record struct RoleWorkflowRecoveryCandidate(
	RoleWorkflowRecoveryCandidateKind Kind,
	string? ContinuationState,
	string? Failure)
{
	internal static RoleWorkflowRecoveryCandidate Unrelated() =>
		new(RoleWorkflowRecoveryCandidateKind.Unrelated, null, null);

	internal static RoleWorkflowRecoveryCandidate Authenticated(
		string continuationState) =>
		new(
			RoleWorkflowRecoveryCandidateKind.Authenticated,
			continuationState,
			null);

	internal static RoleWorkflowRecoveryCandidate ClaimedButInvalid(
		string failure) =>
		new(
			RoleWorkflowRecoveryCandidateKind.ClaimedButInvalid,
			null,
			failure);
}

internal sealed class RoleWorkflowInputRejectionException(string message)
	: InvalidOperationException(message);

internal sealed class RoleWorkflowRuntime
{
	private readonly ListenerIdentifier _listener;
	private readonly GameHook _hook;
	private readonly IReadOnlyList<IRoleWorkflowStep> _steps;
	private readonly IReadOnlyList<IRecoverableWait> _waits;

	internal GameHook Hook => _hook;

	internal RoleWorkflowRuntime(
		ListenerIdentifier listener,
		GameHook hook,
		IEnumerable<IRoleWorkflowStep> steps)
	{
		ArgumentNullException.ThrowIfNull(steps);
		_listener = listener;
		_hook = hook;
		_steps = steps.ToArray();
		if (_steps.Count == 0 ||
		    _steps.Any(step => step.Listener != listener || step.Hook != hook))
		{
			throw new ArgumentException(
				"A Role workflow requires at least one step owned by its declared listener and hook.",
				nameof(steps));
		}

		_waits = _steps.OfType<IRecoverableWait>().ToArray();
	}

	internal HookListenerActionResult Execute<TState>(
		GameSession session,
		ModeratorResponse input,
		TState? currentState)
		where TState : struct, Enum
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(input);
		if (!session.Execution.TryGetActiveGameHook(out var activeHook) ||
		    activeHook != _hook)
		{
			throw new InvalidOperationException(
				$"Declared Role workflow '{_listener}' requires the '{_hook}' hook.");
		}

		var state = currentState?.ToString();
		if (state != null)
		{
			AuthenticateLiveWait(session, input, state);
		}

		var matchingSteps = _steps
			.Where(step => StringComparer.Ordinal.Equals(step.StartState, state))
			.Where(step => step.CanExecute(session))
			.ToArray();
		if (matchingSteps is not [var step])
		{
			throw new InvalidOperationException(
				$"Declared Role workflow '{_listener}' requires exactly one step from '{state ?? "start"}', but found {matchingSteps.Length}.");
		}

		return step.Execute(session, input);
	}

	internal RoleWorkflowRecoveryCandidate ClassifyRecoveryCandidate(
		GameSession session,
		ModeratorInstruction pendingInstruction,
		AcceptedObservationRecoveryCursor? acceptedObservationCursor = null,
		DomainRecoveryCursor? domainCursor = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(pendingInstruction);
		if (acceptedObservationCursor != null && domainCursor != null)
		{
			throw new InvalidOperationException(
				$"Declared Role workflow '{_listener}' cannot recover from multiple cursors.");
		}

		var claims = _waits
			.Select(wait => wait.ClassifyRecoveryCandidate(
				session,
				pendingInstruction,
				acceptedObservationCursor,
				domainCursor))
			.Where(candidate =>
				candidate.Kind != RoleWorkflowRecoveryCandidateKind.Unrelated)
			.ToArray();

		var authenticated = claims
			.Where(candidate =>
				candidate.Kind ==
				RoleWorkflowRecoveryCandidateKind.Authenticated)
			.ToArray();
		if (authenticated.Length > 0)
		{
			return authenticated switch
			{
				[var candidate] => candidate,
				_ => RoleWorkflowRecoveryCandidate.ClaimedButInvalid(
					$"Pending instruction '{pendingInstruction.Semantic}' authenticates multiple waits for '{_listener}'.")
			};
		}

		var invalid = claims.FirstOrDefault(candidate =>
			candidate.Kind ==
			RoleWorkflowRecoveryCandidateKind.ClaimedButInvalid);
		return invalid.Kind == RoleWorkflowRecoveryCandidateKind.ClaimedButInvalid
			? invalid
			: RoleWorkflowRecoveryCandidate.Unrelated();
	}

	internal bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		TargetPrivateRolePowerRecoveryBoundary committedBoundary,
		ModeratorInstruction nextInstruction)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(committedBoundary);
		ArgumentNullException.ThrowIfNull(nextInstruction);
		var claims = _waits
			.Where(wait => wait.TryValidateCommittedRecoveryBoundary(
				session,
				startingInstruction,
				input,
				committedBoundary,
				nextInstruction))
			.ToArray();
		return claims switch
		{
			[] => false,
			[_] => true,
			_ => throw new InvalidOperationException(
				$"Committed target-private boundary authenticates multiple waits for '{_listener}'.")
		};
	}

	internal bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		RecurringRolePowerCommittedLogEntry committedBoundary,
		ModeratorInstruction nextInstruction)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(committedBoundary);
		ArgumentNullException.ThrowIfNull(nextInstruction);
		var claims = _waits
			.Where(wait => wait.TryValidateCommittedRecoveryBoundary(
				session,
				startingInstruction,
				input,
				committedBoundary,
				nextInstruction))
			.ToArray();
		return claims switch
		{
			[] => false,
			[_] => true,
			_ => throw new InvalidOperationException(
				$"Committed recurring boundary authenticates multiple waits for '{_listener}'.")
		};
	}

	private void AuthenticateLiveWait(
		GameSession session,
		ModeratorResponse input,
		string currentState)
	{
		var pendingInstruction = session.Execution.PendingInstruction
			?? throw new InvalidOperationException(
				$"Declared Role workflow '{_listener}' requires one Pending Instruction.");
		var claims = _waits
			.Select(wait => wait.ClassifyLiveCandidate(
				session,
				pendingInstruction,
				input,
				currentState))
			.Where(candidate =>
				candidate.Kind != RoleWorkflowRecoveryCandidateKind.Unrelated)
			.ToArray();
		var authenticatedCount = claims.Count(candidate =>
			candidate.Kind ==
			RoleWorkflowRecoveryCandidateKind.Authenticated);
		if (authenticatedCount == 1)
		{
			return;
		}

		if (authenticatedCount > 1)
		{
			throw new InvalidOperationException(
				$"Pending instruction '{pendingInstruction.Semantic}' does not authenticate exactly one live wait for '{_listener}:{currentState}'.");
		}

		var invalid = claims.FirstOrDefault(candidate =>
			candidate.Kind ==
			RoleWorkflowRecoveryCandidateKind.ClaimedButInvalid);
		throw new InvalidOperationException(
			invalid.Failure ??
			$"Pending instruction '{pendingInstruction.Semantic}' does not authenticate a live wait for '{_listener}:{currentState}'.");
	}
}

internal interface IRoleWorkflowStep
{
	ListenerIdentifier Listener { get; }
	GameHook Hook { get; }
	string? StartState { get; }
	bool CanExecute(GameSession session);
	HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input);
}

internal interface IRecoverableWait : IRoleWorkflowStep
{
	RoleWorkflowRecoveryCandidate ClassifyLiveCandidate(
		GameSession session,
		ModeratorInstruction pendingInstruction,
		ModeratorResponse input,
		string currentState);

	RoleWorkflowRecoveryCandidate ClassifyRecoveryCandidate(
		GameSession session,
		ModeratorInstruction pendingInstruction,
		AcceptedObservationRecoveryCursor? acceptedObservationCursor,
		DomainRecoveryCursor? domainCursor);

	bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		TargetPrivateRolePowerRecoveryBoundary committedBoundary,
		ModeratorInstruction nextInstruction);

	bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		RecurringRolePowerCommittedLogEntry committedBoundary,
		ModeratorInstruction nextInstruction);
}

internal enum RecoverableWaitDurability
{
	Replayable,
	ReplayableOrAcceptedObservation,
	AcceptedObservation,
	Domain
}

internal sealed class RecoverableWait<TState, TInstruction> : IRecoverableWait
	where TState : struct, Enum
	where TInstruction : ModeratorInstruction
{
	private readonly TState _continuationState;
	private readonly ModeratorInstructionSemantic _semantic;
	private readonly ExpectedInputType _expectedResponseType;
	private readonly Func<GameSession, bool> _canIssue;
	private readonly Action<GameSession, ModeratorResponse> _beforeIssue;
	private readonly Func<GameSession, TInstruction> _instructionFactory;
	private readonly Func<GameSession, ModeratorInstruction, bool> _claimsCandidate;
	private readonly Action<GameSession, TInstruction> _validateInstructionContext;
	private readonly RecoverableWaitDurability _durability;
	private readonly Action<
		GameSession,
		TInstruction,
		AcceptedObservationRecoveryCursor>? _validateDurableContext;
	private readonly Func<AcceptedObservationRecoveryCursor, TState>?
		_existingCursorContinuationFactory;
	private readonly Action<GameSession, TInstruction, DomainRecoveryCursor>?
		_validateDomainContext;
	private readonly Func<DomainRecoveryCursor, TState>?
		_existingDomainCursorContinuationFactory;
	private readonly Func<
		GameSession,
		ModeratorInstruction?,
		ModeratorResponse,
		TargetPrivateRolePowerRecoveryBoundary,
		TInstruction,
		bool>? _validateTargetPrivateCommittedRecoveryBoundary;
	private readonly Func<
		GameSession,
		ModeratorInstruction?,
		ModeratorResponse,
		RecurringRolePowerCommittedLogEntry,
		TInstruction,
		bool>? _validateRecurringCommittedRecoveryBoundary;

	private RecoverableWait(
		ListenerIdentifier listener,
		GameHook hook,
		TState? startState,
		TState continuationState,
		ModeratorInstructionSemantic semantic,
		ExpectedInputType expectedResponseType,
		Func<GameSession, bool> canIssue,
		Action<GameSession, ModeratorResponse> beforeIssue,
		Func<GameSession, TInstruction> instructionFactory,
		Func<GameSession, ModeratorInstruction, bool> claimsCandidate,
		Action<GameSession, TInstruction> validateInstructionContext,
		RecoverableWaitDurability durability,
		Action<GameSession, TInstruction, AcceptedObservationRecoveryCursor>?
			validateDurableContext,
		Func<AcceptedObservationRecoveryCursor, TState>?
			existingCursorContinuationFactory,
		Action<GameSession, TInstruction, DomainRecoveryCursor>?
			validateDomainContext,
		Func<DomainRecoveryCursor, TState>?
			existingDomainCursorContinuationFactory,
		Func<
			GameSession,
			ModeratorInstruction?,
			ModeratorResponse,
			TargetPrivateRolePowerRecoveryBoundary,
			TInstruction,
			bool>? validateTargetPrivateCommittedRecoveryBoundary,
		Func<
			GameSession,
			ModeratorInstruction?,
			ModeratorResponse,
			RecurringRolePowerCommittedLogEntry,
			TInstruction,
			bool>? validateRecurringCommittedRecoveryBoundary)
	{
		if (!Enum.IsDefined(semantic) || !Enum.IsDefined(expectedResponseType))
		{
			throw new ArgumentOutOfRangeException(nameof(semantic));
		}

		Listener = listener;
		Hook = hook;
		StartState = startState?.ToString();
		_continuationState = continuationState;
		_semantic = semantic;
		_expectedResponseType = expectedResponseType;
		_canIssue = canIssue ?? throw new ArgumentNullException(nameof(canIssue));
		_beforeIssue = beforeIssue ?? throw new ArgumentNullException(nameof(beforeIssue));
		_instructionFactory = instructionFactory ??
			throw new ArgumentNullException(nameof(instructionFactory));
		_claimsCandidate = claimsCandidate ??
			throw new ArgumentNullException(nameof(claimsCandidate));
		_validateInstructionContext = validateInstructionContext ??
			throw new ArgumentNullException(nameof(validateInstructionContext));
		_durability = durability;
		_validateDurableContext = validateDurableContext;
		_existingCursorContinuationFactory =
			existingCursorContinuationFactory;
		_validateDomainContext = validateDomainContext;
		_existingDomainCursorContinuationFactory =
			existingDomainCursorContinuationFactory;
		_validateTargetPrivateCommittedRecoveryBoundary =
			validateTargetPrivateCommittedRecoveryBoundary;
		_validateRecurringCommittedRecoveryBoundary =
			validateRecurringCommittedRecoveryBoundary;
		var hasAcceptedObservationPolicy =
			validateDurableContext != null &&
			existingCursorContinuationFactory != null;
		var hasDomainPolicy =
			validateDomainContext != null &&
			existingDomainCursorContinuationFactory != null &&
			(validateTargetPrivateCommittedRecoveryBoundary != null) !=
			(validateRecurringCommittedRecoveryBoundary != null);
		if (durability switch
		    {
			    RecoverableWaitDurability.Replayable =>
				    !hasAcceptedObservationPolicy && !hasDomainPolicy,
			    RecoverableWaitDurability.ReplayableOrAcceptedObservation =>
				    hasAcceptedObservationPolicy && !hasDomainPolicy,
			    RecoverableWaitDurability.AcceptedObservation =>
				    hasAcceptedObservationPolicy && !hasDomainPolicy,
			    RecoverableWaitDurability.Domain =>
				    !hasAcceptedObservationPolicy && hasDomainPolicy,
			    _ => false
		    } is false)
		{
			throw new ArgumentException(
				"A wait must declare exactly one replayable, accepted-observation, or domain recovery policy.");
		}
	}

	internal static RecoverableWait<TState, TInstruction> Replayable(
		ListenerIdentifier listener,
		GameHook hook,
		TState? startState,
		TState continuationState,
		ModeratorInstructionSemantic semantic,
		ExpectedInputType expectedResponseType,
		Func<GameSession, bool> canIssue,
		Action<GameSession, ModeratorResponse> beforeIssue,
		Func<GameSession, TInstruction> instructionFactory,
		Func<GameSession, ModeratorInstruction, bool> claimsCandidate,
		Action<GameSession, TInstruction> validateInstructionContext) =>
		new(
			listener,
			hook,
			startState,
			continuationState,
			semantic,
			expectedResponseType,
			canIssue,
			beforeIssue,
			instructionFactory,
			claimsCandidate,
			validateInstructionContext,
			RecoverableWaitDurability.Replayable,
			validateDurableContext: null,
			existingCursorContinuationFactory: null,
			validateDomainContext: null,
			existingDomainCursorContinuationFactory: null,
			validateTargetPrivateCommittedRecoveryBoundary: null,
			validateRecurringCommittedRecoveryBoundary: null);

	internal static RecoverableWait<TState, TInstruction>
		ReplayableWithAcceptedObservationHandoff(
			ListenerIdentifier listener,
			GameHook hook,
			TState? startState,
			TState continuationState,
			ModeratorInstructionSemantic semantic,
			ExpectedInputType expectedResponseType,
			Func<GameSession, bool> canIssue,
			Action<GameSession, ModeratorResponse> beforeIssue,
			Func<GameSession, TInstruction> instructionFactory,
			Func<GameSession, ModeratorInstruction, bool> claimsCandidate,
			Action<GameSession, TInstruction> validateInstructionContext,
			Action<GameSession, TInstruction, AcceptedObservationRecoveryCursor>
				validateDurableContext,
			Func<AcceptedObservationRecoveryCursor, TState>
				existingCursorContinuationFactory) =>
		new(
			listener,
			hook,
			startState,
			continuationState,
			semantic,
			expectedResponseType,
			canIssue,
			beforeIssue,
			instructionFactory,
			claimsCandidate,
			validateInstructionContext,
			RecoverableWaitDurability.ReplayableOrAcceptedObservation,
			validateDurableContext,
			existingCursorContinuationFactory,
			validateDomainContext: null,
			existingDomainCursorContinuationFactory: null,
			validateTargetPrivateCommittedRecoveryBoundary: null,
			validateRecurringCommittedRecoveryBoundary: null);

	internal static RecoverableWait<TState, TInstruction> Durable(
		ListenerIdentifier listener,
		GameHook hook,
		TState? startState,
		TState continuationState,
		ModeratorInstructionSemantic semantic,
		ExpectedInputType expectedResponseType,
		Func<GameSession, bool> canIssue,
		Action<GameSession, ModeratorResponse> beforeIssue,
		Func<GameSession, TInstruction> instructionFactory,
		Func<GameSession, ModeratorInstruction, bool> claimsCandidate,
		Action<GameSession, TInstruction> validateInstructionContext,
		Action<GameSession, TInstruction, AcceptedObservationRecoveryCursor>
			validateDurableContext,
		Func<AcceptedObservationRecoveryCursor, TState>
			existingCursorContinuationFactory) =>
		new(
			listener,
			hook,
			startState,
			continuationState,
			semantic,
			expectedResponseType,
			canIssue,
			beforeIssue,
			instructionFactory,
			claimsCandidate,
			validateInstructionContext,
			RecoverableWaitDurability.AcceptedObservation,
			validateDurableContext,
			existingCursorContinuationFactory,
			validateDomainContext: null,
			existingDomainCursorContinuationFactory: null,
			validateTargetPrivateCommittedRecoveryBoundary: null,
			validateRecurringCommittedRecoveryBoundary: null);

	internal static RecoverableWait<TState, TInstruction> DomainDurable(
		ListenerIdentifier listener,
		GameHook hook,
		TState? startState,
		TState continuationState,
		ModeratorInstructionSemantic semantic,
		ExpectedInputType expectedResponseType,
		Func<GameSession, bool> canIssue,
		Action<GameSession, ModeratorResponse> beforeIssue,
		Func<GameSession, TInstruction> instructionFactory,
		Func<GameSession, ModeratorInstruction, bool> claimsCandidate,
		Action<GameSession, TInstruction> validateInstructionContext,
		Action<GameSession, TInstruction, DomainRecoveryCursor>
			validateDomainContext,
		Func<DomainRecoveryCursor, TState>
			existingDomainCursorContinuationFactory,
		Func<
			GameSession,
			ModeratorInstruction?,
			ModeratorResponse,
			TargetPrivateRolePowerRecoveryBoundary,
			TInstruction,
			bool> validateCommittedRecoveryBoundary) =>
		new(
			listener,
			hook,
			startState,
			continuationState,
			semantic,
			expectedResponseType,
			canIssue,
			beforeIssue,
			instructionFactory,
			claimsCandidate,
			validateInstructionContext,
			RecoverableWaitDurability.Domain,
			validateDurableContext: null,
			existingCursorContinuationFactory: null,
			validateDomainContext,
			existingDomainCursorContinuationFactory,
			validateCommittedRecoveryBoundary,
			validateRecurringCommittedRecoveryBoundary: null);

	internal static RecoverableWait<TState, TInstruction>
		RecurringDomainDurable(
			ListenerIdentifier listener,
			GameHook hook,
			TState? startState,
			TState continuationState,
			ModeratorInstructionSemantic semantic,
			ExpectedInputType expectedResponseType,
			Func<GameSession, bool> canIssue,
			Action<GameSession, ModeratorResponse> beforeIssue,
			Func<GameSession, TInstruction> instructionFactory,
			Func<GameSession, ModeratorInstruction, bool> claimsCandidate,
			Action<GameSession, TInstruction> validateInstructionContext,
			Action<GameSession, TInstruction, DomainRecoveryCursor>
				validateDomainContext,
			Func<DomainRecoveryCursor, TState>
				existingDomainCursorContinuationFactory,
			Func<
				GameSession,
				ModeratorInstruction?,
				ModeratorResponse,
				RecurringRolePowerCommittedLogEntry,
				TInstruction,
				bool> validateCommittedRecoveryBoundary) =>
		new(
			listener,
			hook,
			startState,
			continuationState,
			semantic,
			expectedResponseType,
			canIssue,
			beforeIssue,
			instructionFactory,
			claimsCandidate,
			validateInstructionContext,
			RecoverableWaitDurability.Domain,
			validateDurableContext: null,
			existingCursorContinuationFactory: null,
			validateDomainContext,
			existingDomainCursorContinuationFactory,
			validateTargetPrivateCommittedRecoveryBoundary: null,
			validateCommittedRecoveryBoundary);

	public ListenerIdentifier Listener { get; }
	public GameHook Hook { get; }
	public string? StartState { get; }

	public bool CanExecute(GameSession session) => _canIssue(session);

	public HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		_beforeIssue(session, input);
		var instruction = _instructionFactory(session) ??
			throw new InvalidOperationException(
				$"Declared wait '{Listener}:{_semantic}' produced no instruction.");
		ValidateInstruction(session, instruction);
		return HookListenerActionResult.NeedInput(
			instruction,
			_continuationState);
	}

	public RoleWorkflowRecoveryCandidate ClassifyLiveCandidate(
		GameSession session,
		ModeratorInstruction pendingInstruction,
		ModeratorResponse input,
		string currentState)
	{
		if (!Claims(session, pendingInstruction) ||
		    !StringComparer.Ordinal.Equals(
			    _continuationState.ToString(),
			    currentState))
		{
			return RoleWorkflowRecoveryCandidate.Unrelated();
		}

		var invalidInstruction = TryValidateInstruction(
			session,
			pendingInstruction,
			out _);
		if (invalidInstruction != null)
		{
			return Invalid(invalidInstruction);
		}

		if (input.InstructionId != pendingInstruction.InstructionId ||
		    input.Type != _expectedResponseType)
		{
			return Invalid(
				$"Moderator Response does not authenticate the declared '{Listener}:{_semantic}' wait.");
		}

		return RoleWorkflowRecoveryCandidate.Authenticated(currentState);
	}

	public RoleWorkflowRecoveryCandidate ClassifyRecoveryCandidate(
		GameSession session,
		ModeratorInstruction pendingInstruction,
		AcceptedObservationRecoveryCursor? acceptedObservationCursor,
		DomainRecoveryCursor? domainCursor)
	{
		var suppliedDurability = acceptedObservationCursor != null
			? RecoverableWaitDurability.AcceptedObservation
			: domainCursor != null
				? RecoverableWaitDurability.Domain
				: RecoverableWaitDurability.Replayable;
		var acceptsSuppliedDurability = suppliedDurability == _durability ||
			_durability ==
				RecoverableWaitDurability.ReplayableOrAcceptedObservation &&
			suppliedDurability is
				(RecoverableWaitDurability.Replayable or
				 RecoverableWaitDurability.AcceptedObservation);
		if (!acceptsSuppliedDurability)
		{
			if (_durability == RecoverableWaitDurability.Domain &&
			    domainCursor == null &&
			    _claimsCandidate(session, pendingInstruction))
			{
				return Invalid(
					$"Pending instruction '{pendingInstruction.Semantic}' claims durable '{Listener}' context without its domain cursor.");
			}

			return RoleWorkflowRecoveryCandidate.Unrelated();
		}

		var cursorSemantic = acceptedObservationCursor?.NextInstructionSemantic ??
		                     domainCursor?.NextInstructionSemantic;
		var claims = cursorSemantic != null
			? cursorSemantic == _semantic
			: _claimsCandidate(session, pendingInstruction);
		if (!claims)
		{
			return RoleWorkflowRecoveryCandidate.Unrelated();
		}

		if (_durability == RecoverableWaitDurability.Replayable ||
		    _durability ==
		    RecoverableWaitDurability.ReplayableOrAcceptedObservation &&
		    acceptedObservationCursor == null)
		{
			var invalidReplayableInstruction = TryValidateInstruction(
				session,
				pendingInstruction,
				out _);
			return invalidReplayableInstruction == null
				? RoleWorkflowRecoveryCandidate.Authenticated(
					_continuationState.ToString())
				: Invalid(invalidReplayableInstruction);
		}

		var invalidInstruction = TryValidateInstruction(
			session,
			pendingInstruction,
			out var typedInstruction);
		if (invalidInstruction != null)
		{
			return Invalid(invalidInstruction);
		}

		var nextInstructionId = acceptedObservationCursor?.NextInstructionId ??
		                        domainCursor!.NextInstructionId;
		var nextInstructionSemantic =
			acceptedObservationCursor?.NextInstructionSemantic ??
			domainCursor!.NextInstructionSemantic;
		if (pendingInstruction.InstructionId != nextInstructionId ||
		    pendingInstruction.Semantic != nextInstructionSemantic)
		{
			return Invalid(
				$"Pending instruction '{pendingInstruction.Semantic}' does not correlate to the declared '{Listener}' cursor.");
		}

		try
		{
			string continuation;
			if (acceptedObservationCursor != null)
			{
				_validateDurableContext!(
					session,
					typedInstruction!,
					acceptedObservationCursor);
				continuation = _existingCursorContinuationFactory!(
					acceptedObservationCursor).ToString();
			}
			else
			{
				_validateDomainContext!(
					session,
					typedInstruction!,
					domainCursor!);
				continuation = _existingDomainCursorContinuationFactory!(
					domainCursor!).ToString();
			}
			if (!StringComparer.Ordinal.Equals(
				    continuation,
				    _continuationState.ToString()))
			{
				return Invalid(
					$"The declared '{Listener}' cursor resolved an invalid continuation '{continuation}'.");
			}

			return RoleWorkflowRecoveryCandidate.Authenticated(continuation);
		}
		catch (Exception exception) when (
			exception is ArgumentException or InvalidOperationException)
		{
			return Invalid(
				$"Pending instruction '{pendingInstruction.Semantic}' claims invalid durable '{Listener}' context: {exception.Message}");
		}
	}

	public bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		TargetPrivateRolePowerRecoveryBoundary committedBoundary,
		ModeratorInstruction nextInstruction)
	{
		if (_validateTargetPrivateCommittedRecoveryBoundary == null ||
		    _durability != RecoverableWaitDurability.Domain ||
		    nextInstruction.Semantic != _semantic)
		{
			return false;
		}

		if (nextInstruction is not TInstruction typedInstruction)
		{
			throw new InvalidOperationException(
				$"Committed boundary claims '{Listener}:{_semantic}' with invalid instruction type '{nextInstruction.GetType().Name}'.");
		}

		ValidateInstruction(session, typedInstruction);
		return _validateTargetPrivateCommittedRecoveryBoundary(
			session,
			startingInstruction,
			input,
			committedBoundary,
			typedInstruction);
	}

	public bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		RecurringRolePowerCommittedLogEntry committedBoundary,
		ModeratorInstruction nextInstruction)
	{
		if (_validateRecurringCommittedRecoveryBoundary == null ||
		    _durability != RecoverableWaitDurability.Domain ||
		    nextInstruction.Semantic != _semantic)
		{
			return false;
		}

		if (nextInstruction is not TInstruction typedInstruction)
		{
			throw new InvalidOperationException(
				$"Committed boundary claims '{Listener}:{_semantic}' with invalid instruction type '{nextInstruction.GetType().Name}'.");
		}

		ValidateInstruction(session, typedInstruction);
		return _validateRecurringCommittedRecoveryBoundary(
			session,
			startingInstruction,
			input,
			committedBoundary,
			typedInstruction);
	}

	private bool Claims(
		GameSession session,
		ModeratorInstruction pendingInstruction) =>
		pendingInstruction.Semantic == _semantic ||
		_claimsCandidate(session, pendingInstruction);

	private string? TryValidateInstruction(
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out TInstruction? typedInstruction)
	{
		typedInstruction = pendingInstruction as TInstruction;
		if (typedInstruction == null)
		{
			return $"Pending instruction '{pendingInstruction.Semantic}' claims '{Listener}' with invalid type '{pendingInstruction.GetType().Name}'.";
		}

		try
		{
			ValidateInstruction(session, typedInstruction);
			return null;
		}
		catch (RoleWorkflowInputRejectionException exception)
		{
			return exception.Message;
		}
		catch (Exception exception) when (
			exception is ArgumentException or InvalidOperationException)
		{
			return $"Pending instruction '{pendingInstruction.Semantic}' claims invalid '{Listener}' context: {exception.Message}";
		}
	}

	private void ValidateInstruction(
		GameSession session,
		TInstruction instruction)
	{
		if (instruction.Semantic != _semantic)
		{
			throw new InvalidOperationException(
				$"Expected semantic '{_semantic}', found '{instruction.Semantic}'.");
		}

		_validateInstructionContext(session, instruction);
	}

	private static RoleWorkflowRecoveryCandidate Invalid(string failure) =>
		RoleWorkflowRecoveryCandidate.ClaimedButInvalid(failure);
}

internal sealed class RoleWorkflowDecisionStep<TState> : IRoleWorkflowStep
	where TState : struct, Enum
{
	private readonly Func<GameSession, bool> _canExecute;
	private readonly Func<
		GameSession,
		ModeratorResponse,
		HookListenerActionResult> _execute;

	internal RoleWorkflowDecisionStep(
		ListenerIdentifier listener,
		GameHook hook,
		TState? startState,
		Func<GameSession, bool> canExecute,
		Func<GameSession, ModeratorResponse, HookListenerActionResult> execute)
	{
		Listener = listener;
		Hook = hook;
		StartState = startState?.ToString();
		_canExecute = canExecute ?? throw new ArgumentNullException(nameof(canExecute));
		_execute = execute ?? throw new ArgumentNullException(nameof(execute));
	}

	public ListenerIdentifier Listener { get; }
	public GameHook Hook { get; }
	public string? StartState { get; }

	public bool CanExecute(GameSession session) => _canExecute(session);

	public HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input) =>
		_execute(session, input);
}

internal sealed class RoleWorkflowCompletionStep<TState> : IRoleWorkflowStep
	where TState : struct, Enum
{
	private readonly Func<GameSession, bool> _canComplete;
	private readonly TState _completedState;

	internal RoleWorkflowCompletionStep(
		ListenerIdentifier listener,
		GameHook hook,
		TState? startState,
		TState completedState,
		Func<GameSession, bool> canComplete)
	{
		Listener = listener;
		Hook = hook;
		StartState = startState?.ToString();
		_completedState = completedState;
		_canComplete = canComplete ?? throw new ArgumentNullException(nameof(canComplete));
	}

	public ListenerIdentifier Listener { get; }
	public GameHook Hook { get; }
	public string? StartState { get; }

	public bool CanExecute(GameSession session) => _canComplete(session);

	public HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input) =>
		HookListenerActionResult.Complete(_completedState);
}
