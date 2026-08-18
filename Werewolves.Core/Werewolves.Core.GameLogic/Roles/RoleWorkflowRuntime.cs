using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Roles;

internal interface IDeclaredRoleWorkflow
{
	RoleWorkflowRuntime WorkflowRuntime { get; }
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

internal sealed class RoleWorkflowRuntime
{
	private readonly ListenerIdentifier _listener;
	private readonly GameHook _hook;
	private readonly IReadOnlyList<IRoleWorkflowStep> _steps;
	private readonly IReadOnlyList<IRecoverableWait> _waits;

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
		AcceptedObservationRecoveryCursor? cursor)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(pendingInstruction);
		var claims = _waits
			.Select(wait => wait.ClassifyRecoveryCandidate(
				session,
				pendingInstruction,
				cursor))
			.Where(candidate =>
				candidate.Kind != RoleWorkflowRecoveryCandidateKind.Unrelated)
			.ToArray();

		var invalid = claims.FirstOrDefault(candidate =>
			candidate.Kind ==
			RoleWorkflowRecoveryCandidateKind.ClaimedButInvalid);
		if (invalid.Kind == RoleWorkflowRecoveryCandidateKind.ClaimedButInvalid)
		{
			return invalid;
		}

		var authenticated = claims
			.Where(candidate =>
				candidate.Kind ==
				RoleWorkflowRecoveryCandidateKind.Authenticated)
			.ToArray();
		return authenticated switch
		{
			[] => RoleWorkflowRecoveryCandidate.Unrelated(),
			[var candidate] => candidate,
			_ => RoleWorkflowRecoveryCandidate.ClaimedButInvalid(
				$"Pending instruction '{pendingInstruction.Semantic}' authenticates multiple waits for '{_listener}'.")
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
		var invalid = claims.FirstOrDefault(candidate =>
			candidate.Kind ==
			RoleWorkflowRecoveryCandidateKind.ClaimedButInvalid);
		if (invalid.Kind == RoleWorkflowRecoveryCandidateKind.ClaimedButInvalid)
		{
			throw new InvalidOperationException(invalid.Failure);
		}

		if (claims.Count(candidate =>
			    candidate.Kind ==
			    RoleWorkflowRecoveryCandidateKind.Authenticated) != 1)
		{
			throw new InvalidOperationException(
				$"Pending instruction '{pendingInstruction.Semantic}' does not authenticate exactly one live wait for '{_listener}:{currentState}'.");
		}
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
		AcceptedObservationRecoveryCursor? cursor);
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
	private readonly bool _replayable;
	private readonly Action<
		GameSession,
		TInstruction,
		AcceptedObservationRecoveryCursor>? _validateDurableContext;
	private readonly Func<AcceptedObservationRecoveryCursor, TState>?
		_existingCursorContinuationFactory;

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
		bool replayable,
		Action<GameSession, TInstruction, AcceptedObservationRecoveryCursor>?
			validateDurableContext,
		Func<AcceptedObservationRecoveryCursor, TState>?
			existingCursorContinuationFactory)
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
		_replayable = replayable;
		_validateDurableContext = validateDurableContext;
		_existingCursorContinuationFactory =
			existingCursorContinuationFactory;
		if (replayable !=
		    (validateDurableContext == null &&
		     existingCursorContinuationFactory == null))
		{
			throw new ArgumentException(
				"A wait must declare either replayable recovery or a durable validator and existing-cursor continuation.");
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
			replayable: true,
			validateDurableContext: null,
			existingCursorContinuationFactory: null);

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
			replayable: false,
			validateDurableContext,
			existingCursorContinuationFactory);

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
		if (!Claims(session, pendingInstruction))
		{
			return RoleWorkflowRecoveryCandidate.Unrelated();
		}

		if (!StringComparer.Ordinal.Equals(
			    _continuationState.ToString(),
			    currentState))
		{
			return Invalid(
				$"Pending instruction '{pendingInstruction.Semantic}' claims '{Listener}' but not continuation '{currentState}'.");
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
		AcceptedObservationRecoveryCursor? cursor)
	{
		var claims = cursor == null
			? _claimsCandidate(session, pendingInstruction)
			: cursor.NextInstructionSemantic == _semantic ||
			  _claimsCandidate(session, pendingInstruction);
		if (!claims)
		{
			return RoleWorkflowRecoveryCandidate.Unrelated();
		}

		if (cursor == null)
		{
			if (!_replayable)
			{
				return Invalid(
					$"Pending instruction '{pendingInstruction.Semantic}' claims durable '{Listener}' context without its semantic cursor.");
			}

			var invalidReplayableInstruction = TryValidateInstruction(
				session,
				pendingInstruction,
				out _);
			return invalidReplayableInstruction == null
				? RoleWorkflowRecoveryCandidate.Authenticated(
					_continuationState.ToString())
				: Invalid(invalidReplayableInstruction);
		}

		if (_replayable)
		{
			return Invalid(
				$"Replayable wait '{Listener}:{_semantic}' cannot claim a durable continuation.");
		}

		var invalidInstruction = TryValidateInstruction(
			session,
			pendingInstruction,
			out var typedInstruction);
		if (invalidInstruction != null)
		{
			return Invalid(invalidInstruction);
		}

		if (pendingInstruction.InstructionId != cursor.NextInstructionId ||
		    pendingInstruction.Semantic != cursor.NextInstructionSemantic)
		{
			return Invalid(
				$"Pending instruction '{pendingInstruction.Semantic}' does not correlate to the declared '{Listener}' cursor.");
		}

		try
		{
			_validateDurableContext!(session, typedInstruction!, cursor);
			var continuation =
				_existingCursorContinuationFactory!(cursor).ToString();
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
