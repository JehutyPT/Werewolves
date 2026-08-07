using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.Services;

public sealed class LobbyEvaluationCoordinator : IDisposable
{
	public static readonly TimeSpan FallbackQuietPeriod = TimeSpan.FromMilliseconds(500);

	private readonly LobbySetupState _lobby;
	private readonly ILocalTerminalLobbyCacheStore _localCache;
	private readonly ILobbyTerminalEvaluator _evaluator;
	private readonly SimulatorCapability _capability;
	private readonly LobbyEvaluationDepth _depth;
	private readonly TimeProvider _timeProvider;
	private readonly Func<SimulationScenario, SimulatorCapability, LobbyScenarioSupport> _classify;
	private readonly object _sync = new();
	private EvaluationRequest? _currentRequest;
	private bool _disposed;

	public LobbyEvaluationCoordinator(
		LobbySetupState lobby,
		ILocalTerminalLobbyCacheStore localCache,
		ILobbyTerminalEvaluator evaluator,
		LobbyEvaluationSettings settings,
		TimeProvider? timeProvider = null)
		: this(lobby, localCache, evaluator, settings, timeProvider, ClassifyScenario)
	{
	}

	internal LobbyEvaluationCoordinator(
		LobbySetupState lobby,
		ILocalTerminalLobbyCacheStore localCache,
		ILobbyTerminalEvaluator evaluator,
		LobbyEvaluationSettings settings,
		TimeProvider? timeProvider,
		Func<SimulationScenario, SimulatorCapability, LobbyScenarioSupport> classify)
	{
		_lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
		_localCache = localCache ?? throw new ArgumentNullException(nameof(localCache));
		_evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
		ArgumentNullException.ThrowIfNull(settings);
		_capability = settings.Capability;
		_depth = settings.Depth;
		_timeProvider = timeProvider ?? TimeProvider.System;
		_classify = classify ?? throw new ArgumentNullException(nameof(classify));
		State = LobbyEvaluationState.NotApplicable();
		_lobby.SimulationScenarioChanged += HandleSimulationScenarioChanged;
		RestartForCurrentScenario();
	}

	public event EventHandler? StateChanged;

	public LobbyEvaluationState State { get; private set; }
	public SimulatorCapability Capability => _capability;
	public LobbyEvaluationDepth Depth => _depth;
	public bool EvaluationBlocksLobbyExit => State.BlocksLobbyExit;

	public bool TryRequestLobbyExit()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return false;
			}

			if (!State.BlocksLobbyExit)
			{
				return true;
			}

			if (State.Kind == LobbyEvaluationStateKind.Pending
				&& _currentRequest is { } request
				&& request.Identity.Equals(State.Identity))
			{
				request.AccelerateFallback.TrySetResult();
			}

			return false;
		}
	}

	public bool RetryCurrent()
	{
		EvaluationRequest? previous;
		EvaluationRequest retry;
		EventHandler? changed;
		lock (_sync)
		{
			if (_disposed
				|| State.Kind != LobbyEvaluationStateKind.CouldNotEvaluate
				|| State.Identity is not { } currentIdentity
				|| _currentRequest is not { } currentRequest
				|| !currentRequest.Identity.Equals(currentIdentity))
			{
				return false;
			}

			previous = _currentRequest;
			retry = new EvaluationRequest(currentRequest.Scenario, currentIdentity);
			retry.AccelerateFallback.TrySetResult();
			_currentRequest = retry;
			State = LobbyEvaluationState.Pending(currentIdentity);
			changed = StateChanged;
		}

		try
		{
			previous.Cancel();
		}
		finally
		{
			try
			{
				changed?.Invoke(this, EventArgs.Empty);
			}
			finally
			{
				StartPipeline(retry);
			}
		}
		return true;
	}

	private void HandleSimulationScenarioChanged(object? sender, EventArgs args) =>
		RestartForCurrentScenario();

	private void RestartForCurrentScenario()
	{
		EvaluationRequest? previous;
		EvaluationRequest? replacement;
		LobbyEvaluationState nextState;
		EventHandler? changed;
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			if (!_lobby.TryCreateSimulationScenario(out var scenario))
			{
				replacement = null;
				nextState = LobbyEvaluationState.NotApplicable();
			}
			else
			{
				var support = _classify(scenario, _capability);
				if (!support.RulesValid || !support.AppSupported)
				{
					replacement = null;
					nextState = LobbyEvaluationState.NotApplicable();
				}
				else if (!support.SimulatorSupported)
				{
					replacement = null;
					nextState = LobbyEvaluationState.SimulatorUnavailable();
				}
				else
				{
					var identity = new SimulationCompatibilityIdentity(
						scenario.ToCanonical(),
						_capability.Identity);
					if (_currentRequest is { } currentRequest &&
						currentRequest.Identity.Equals(identity))
					{
						return;
					}
					replacement = new EvaluationRequest(scenario, identity);
					nextState = LobbyEvaluationState.Pending(identity);
				}
			}

			previous = _currentRequest;
			_currentRequest = replacement;
			if (State == nextState)
			{
				changed = null;
			}
			else
			{
				State = nextState;
				changed = StateChanged;
			}
		}
		try
		{
			previous?.Cancel();
		}
		finally
		{
			try
			{
				changed?.Invoke(this, EventArgs.Empty);
			}
			finally
			{
				if (replacement is not null)
				{
					StartPipeline(replacement);
				}
			}
		}
	}

	private static LobbyScenarioSupport ClassifyScenario(
		SimulationScenario scenario,
		SimulatorCapability capability)
	{
		var classification = SimulationScenarioClassifier.Classify(scenario, capability);
		return new LobbyScenarioSupport(
			classification.RulesValidity.IsValid,
			classification.AppSupport is { IsSupported: true },
			classification.SimulatorSupport is { IsSupported: true });
	}

	private void StartPipeline(EvaluationRequest request) =>
		_ = RunPipelineAsync(request);

	private async Task RunPipelineAsync(EvaluationRequest request)
	{
		try
		{
			await ResolveAsync(request);
		}
		finally
		{
			request.DisposeAfterDrain();
		}
	}

	private async Task ResolveAsync(EvaluationRequest request)
	{
		try
		{
			await Task.Yield();
			request.Token.ThrowIfCancellationRequested();
			var localBytes = await ReadLocalAsync(request.Token);
			if (!IsCurrent(request))
			{
				return;
			}
			var localDocument = ReadUsableDocument(localBytes);
			if (localDocument is not null
				&& TerminalLobbyCache.TryGet(localDocument, request.Identity, out var localRecord))
			{
				PublishIfCurrent(request, ProjectTerminalRecord(localRecord, request.Identity));
				return;
			}

			await WaitForFallbackAsync(request);
			request.Token.ThrowIfCancellationRequested();
			var evaluation = await _evaluator.EvaluateAsync(
				request.Scenario,
				_capability,
				_depth,
				request.Token);
			if (!IsCurrent(request))
			{
				return;
			}
			if (_depth == LobbyEvaluationDepth.DegenerateScreeningOnly
				&& evaluation is ScreeningPassedLobbyEvaluation or ProbabilityTerminalEvaluation)
			{
				PublishIfCurrent(request, LobbyEvaluationState.ScreeningPassed(request.Identity));
				return;
			}
			if (evaluation is SimulatorUnsupportedLobbyEvaluation)
			{
				PublishIfCurrent(request, LobbyEvaluationState.SimulatorUnavailable());
				return;
			}
			if (evaluation is not TerminalLobbyEvaluation terminal)
			{
				PublishIfCurrent(request, LobbyEvaluationState.CouldNotEvaluate(request.Identity));
				return;
			}

			TerminalLobbyCacheRecord record;
			try
			{
				record = TerminalLobbyCache.Capture(request.Identity, terminal);
			}
			catch (ArgumentException)
			{
				PublishIfCurrent(request, LobbyEvaluationState.CouldNotEvaluate(request.Identity));
				return;
			}

			if (!IsCurrent(request))
			{
				return;
			}

			var records = (localDocument?.Records ?? [])
				.Where(candidate =>
					!candidate.CompatibilityIdentity.Equals(request.Identity))
				.Append(record);
			var bytes = TerminalLobbyCache.Write(TerminalLobbyCache.CreateDocument(records));
			try
			{
				await using var staged = await _localCache.StageWriteAsync(bytes, request.Token);
				if (!IsCurrent(request))
				{
					return;
				}

				staged.TryCommit(commit =>
				{
					lock (_sync)
					{
						if (!IsCurrentUnsafe(request))
						{
							return false;
						}
						commit();
						return true;
					}
				});
			}
			catch (OperationCanceledException) when (request.IsCancellationRequested)
			{
				return;
			}
			catch
			{
				// The terminal meaning remains valid for this app session even if persistence fails.
			}

			PublishIfCurrent(request, ProjectTerminalRecord(record, request.Identity));
		}
		catch (OperationCanceledException) when (request.IsCancellationRequested)
		{
		}
		catch
		{
			PublishIfCurrent(request, LobbyEvaluationState.CouldNotEvaluate(request.Identity));
		}
	}

	private async Task WaitForFallbackAsync(EvaluationRequest request)
	{
		using var delayCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(request.Token);
		var delay = Task.Delay(FallbackQuietPeriod, _timeProvider, delayCancellation.Token);
		var completed = await Task.WhenAny(delay, request.AccelerateFallback.Task);
		if (completed != delay)
		{
			delayCancellation.Cancel();
		}
		request.Token.ThrowIfCancellationRequested();
	}

	private async ValueTask<ReadOnlyMemory<byte>?> ReadLocalAsync(
		CancellationToken cancellationToken)
	{
		try
		{
			return await _localCache.ReadAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			return null;
		}
	}

	private static TerminalLobbyCacheDocument? ReadUsableDocument(
		ReadOnlyMemory<byte>? bytes)
	{
		if (bytes is not { } value)
		{
			return null;
		}

		var read = TerminalLobbyCache.ReadDocument(value.Span);
		return read.IsUsable ? read.Document : null;
	}

	private LobbyEvaluationState ProjectTerminalRecord(
		TerminalLobbyCacheRecord record,
		SimulationCompatibilityIdentity consumerIdentity) =>
		ProjectTerminalMeaning(
			consumerIdentity,
			record switch
			{
				AlreadyDecidedTerminalCacheRecord decided =>
					new AlreadyDecidedTerminalMeaning(decided.GameResult, decided.Reason),
				DegenerateTerminalCacheRecord => new DegenerateTerminalMeaning(),
				ProbabilityTerminalCacheRecord
					when _depth == LobbyEvaluationDepth.DegenerateScreeningOnly =>
					new ScreeningPassedTerminalMeaning(),
				ProbabilityTerminalCacheRecord probability => new ProbabilityTerminalMeaning(
					ProjectProbability(
						probability.GameResultFrequencies,
						probability.GameResultFrequencyByTurn)),
				_ => throw new ArgumentException("Unknown terminal cache record.", nameof(record))
			});

	private static LobbyEvaluationState ProjectTerminalMeaning(
		SimulationCompatibilityIdentity consumerIdentity,
		TerminalMeaning meaning) =>
		meaning switch
		{
			AlreadyDecidedTerminalMeaning decided => LobbyEvaluationState.AlreadyDecided(
				consumerIdentity,
				decided.GameResult,
				decided.Reason),
			DegenerateTerminalMeaning => LobbyEvaluationState.Degenerate(consumerIdentity),
			ScreeningPassedTerminalMeaning => LobbyEvaluationState.ScreeningPassed(consumerIdentity),
			ProbabilityTerminalMeaning probability => LobbyEvaluationState.ProbabilityResult(
				consumerIdentity,
				probability.Probability),
			_ => throw new ArgumentException("Unknown terminal meaning.", nameof(meaning))
		};

	private static LobbyProbabilityData ProjectProbability(
		IEnumerable<TerminalCacheGameResultFrequency> frequencies,
		IEnumerable<TerminalCacheTurnWindowFrequency> frequenciesByTurn)
	{
		var cells = frequenciesByTurn.ToArray();
		return new LobbyProbabilityData(frequencies.Select(row =>
			new LobbyProbabilityOutcomeData(
				row.GameResult,
				row.Numerator,
				row.Denominator,
				cells
					.Where(cell => cell.GameResult.Equals(row.GameResult))
					.GroupBy(cell => cell.EndingTurn)
					.OrderBy(group => group.Key)
					.Select(group => new LobbyProbabilityTurnData(
						group.Key,
						group.Sum(cell => cell.Numerator),
						group.First().Denominator)))));
	}

	private void PublishIfCurrent(
		EvaluationRequest request,
		LobbyEvaluationState state)
	{
		EventHandler? changed;
		lock (_sync)
		{
			if (_disposed
				|| request.IsCancellationRequested
				|| !ReferenceEquals(_currentRequest, request)
				|| State == state)
			{
				return;
			}

			State = state;
			changed = StateChanged;
		}

		changed?.Invoke(this, EventArgs.Empty);
	}

	private bool IsCurrent(EvaluationRequest request)
	{
		lock (_sync)
		{
			return IsCurrentUnsafe(request);
		}
	}

	private bool IsCurrentUnsafe(EvaluationRequest request) =>
		!_disposed
		&& !request.IsCancellationRequested
		&& ReferenceEquals(_currentRequest, request);

	public void Dispose()
	{
		EvaluationRequest? request;
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			request = _currentRequest;
			_currentRequest = null;
		}
		_lobby.SimulationScenarioChanged -= HandleSimulationScenarioChanged;
		request?.Cancel();
	}

	private abstract record TerminalMeaning;

	private sealed record AlreadyDecidedTerminalMeaning(
		GameResult GameResult,
		AlreadyDecidedReason Reason) : TerminalMeaning;

	private sealed record DegenerateTerminalMeaning : TerminalMeaning;

	private sealed record ScreeningPassedTerminalMeaning : TerminalMeaning;

	private sealed record ProbabilityTerminalMeaning(
		LobbyProbabilityData Probability) : TerminalMeaning;

	private sealed class EvaluationRequest(
		SimulationScenario scenario,
		SimulationCompatibilityIdentity identity)
	{
		private readonly object _sync = new();
		private readonly CancellationTokenSource _cancellation = new();
		private int _cancellationRequested;
		private bool _cancellationInProgress;
		private bool _disposeRequested;
		private bool _disposed;

		public SimulationScenario Scenario { get; } = scenario;
		public SimulationCompatibilityIdentity Identity { get; } = identity;
		public TaskCompletionSource AccelerateFallback { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		public CancellationToken Token => _cancellation.Token;
		public bool IsCancellationRequested =>
			Volatile.Read(ref _cancellationRequested) != 0;

		public void Cancel()
		{
			lock (_sync)
			{
				if (_disposed || IsCancellationRequested)
				{
					return;
				}
				Volatile.Write(ref _cancellationRequested, 1);
				_cancellationInProgress = true;
			}

			try
			{
				_cancellation.Cancel();
			}
			catch (AggregateException)
			{
				// Cancellation callback failures cannot prevent installing the latest request.
			}
			finally
			{
				var dispose = false;
				lock (_sync)
				{
					_cancellationInProgress = false;
					if (_disposeRequested && !_disposed)
					{
						_disposed = true;
						dispose = true;
					}
				}
				if (dispose)
				{
					_cancellation.Dispose();
				}
			}
		}

		public void DisposeAfterDrain()
		{
			var dispose = false;
			lock (_sync)
			{
				if (_disposed)
				{
					return;
				}
				if (_cancellationInProgress)
				{
					_disposeRequested = true;
					return;
				}
				_disposed = true;
				dispose = true;
			}
			if (dispose)
			{
				_cancellation.Dispose();
			}
		}
	}
}

internal readonly record struct LobbyScenarioSupport(
	bool RulesValid,
	bool AppSupported,
	bool SimulatorSupported);
