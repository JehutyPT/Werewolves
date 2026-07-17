using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.Services;

public sealed class AsyncTerminalLobbyEvaluator : ILobbyTerminalEvaluator
{
	public static readonly TimeSpan EvaluationTimeout = TimeSpan.FromSeconds(10);

	private readonly Func<
		SimulationScenario,
		LobbyEvaluationDepth,
		CancellationToken,
		LobbyEvaluationResult> _evaluate;
	private readonly TimeProvider _timeProvider;
	private readonly Action<Task> _observeLate;

	public AsyncTerminalLobbyEvaluator(TimeProvider timeProvider)
		: this(new TerminalLobbyEvaluator().Evaluate, timeProvider)
	{
	}

	internal AsyncTerminalLobbyEvaluator(
		Func<
			SimulationScenario,
			LobbyEvaluationDepth,
			CancellationToken,
			LobbyEvaluationResult> evaluate,
		TimeProvider timeProvider)
		: this(evaluate, timeProvider, ObserveLateCompletion)
	{
	}

	internal AsyncTerminalLobbyEvaluator(
		Func<
			SimulationScenario,
			LobbyEvaluationDepth,
			CancellationToken,
			LobbyEvaluationResult> evaluate,
		TimeProvider timeProvider,
		Action<Task> observeLate)
	{
		_evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
		_observeLate = observeLate ?? throw new ArgumentNullException(nameof(observeLate));
	}

	public async Task<LobbyEvaluationResult> EvaluateAsync(
		SimulationScenario scenario,
		LobbyEvaluationDepth depth,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		if (!Enum.IsDefined(depth))
		{
			throw new ArgumentOutOfRangeException(nameof(depth));
		}
		cancellationToken.ThrowIfCancellationRequested();
		using var evaluationCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		using var timeoutCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var evaluation = Task.Run(
			() => _evaluate(scenario, depth, evaluationCancellation.Token),
			CancellationToken.None);
		var timeout = Task.Delay(
			EvaluationTimeout,
			_timeProvider,
			timeoutCancellation.Token);

		var completed = await Task.WhenAny(evaluation, timeout);
		if (completed == timeout)
		{
			evaluationCancellation.Cancel();
			_observeLate(evaluation);
			cancellationToken.ThrowIfCancellationRequested();
			return new CouldNotEvaluateLobbyEvaluation();
		}

		try
		{
			timeoutCancellation.Cancel();
			var result = await evaluation;
			cancellationToken.ThrowIfCancellationRequested();
			return result;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}
	}

	private static void ObserveLateCompletion(Task evaluation) =>
		_ = evaluation.ContinueWith(
			static completed => _ = completed.Exception,
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
}
