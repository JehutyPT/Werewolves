using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.Services;

public sealed class AsyncTerminalLobbyEvaluator : ILobbyTerminalEvaluator
{
	public static readonly TimeSpan EvaluationTimeout = TimeSpan.FromSeconds(10);

	private readonly Func<SimulationScenario, CancellationToken, LobbyEvaluationResult> _evaluate;
	private readonly TimeProvider _timeProvider;

	public AsyncTerminalLobbyEvaluator(TimeProvider timeProvider)
		: this(new TerminalLobbyEvaluator().Evaluate, timeProvider)
	{
	}

	internal AsyncTerminalLobbyEvaluator(
		Func<SimulationScenario, CancellationToken, LobbyEvaluationResult> evaluate,
		TimeProvider timeProvider)
	{
		_evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
	}

	public async Task<LobbyEvaluationResult> EvaluateAsync(
		SimulationScenario scenario,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		cancellationToken.ThrowIfCancellationRequested();
		using var evaluationCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		using var timeoutCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var evaluation = Task.Run(
			() => _evaluate(scenario, evaluationCancellation.Token),
			CancellationToken.None);
		var timeout = Task.Delay(
			EvaluationTimeout,
			_timeProvider,
			timeoutCancellation.Token);

		var completed = await Task.WhenAny(evaluation, timeout);
		if (completed == timeout)
		{
			evaluationCancellation.Cancel();
			cancellationToken.ThrowIfCancellationRequested();
			ObserveLateCompletion(evaluation);
			return new CouldNotEvaluateLobbyEvaluation();
		}

		try
		{
			timeoutCancellation.Cancel();
			return await evaluation;
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
