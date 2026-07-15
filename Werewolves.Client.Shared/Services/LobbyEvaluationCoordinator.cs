using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.Services;

public sealed class LobbyEvaluationCoordinator : IDisposable
{
	public const string BundledCacheLogicalName = "terminal-lobby-cache.json";
	public static readonly TimeSpan FallbackQuietPeriod = TimeSpan.FromMilliseconds(500);

	private readonly LobbySetupState _lobby;
	private readonly ITerminalLobbyCacheByteSource _bundledCache;
	private readonly ILocalTerminalLobbyCacheStore _localCache;
	private readonly ILobbyTerminalEvaluator _evaluator;
	private readonly TimeProvider _timeProvider;
	private readonly Func<SimulationScenario, LobbyScenarioSupport> _classify;
	private readonly object _sync = new();
	private EvaluationRequest? _currentRequest;
	private bool _disposed;

	public LobbyEvaluationCoordinator(
		LobbySetupState lobby,
		ITerminalLobbyCacheByteSource bundledCache,
		ILocalTerminalLobbyCacheStore localCache,
		ILobbyTerminalEvaluator evaluator,
		TimeProvider? timeProvider = null)
		: this(lobby, bundledCache, localCache, evaluator, timeProvider, ClassifyScenario)
	{
	}

	internal LobbyEvaluationCoordinator(
		LobbySetupState lobby,
		ITerminalLobbyCacheByteSource bundledCache,
		ILocalTerminalLobbyCacheStore localCache,
		ILobbyTerminalEvaluator evaluator,
		TimeProvider? timeProvider,
		Func<SimulationScenario, LobbyScenarioSupport> classify)
	{
		_lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
		_bundledCache = bundledCache ?? throw new ArgumentNullException(nameof(bundledCache));
		_localCache = localCache ?? throw new ArgumentNullException(nameof(localCache));
		_evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
		_timeProvider = timeProvider ?? TimeProvider.System;
		_classify = classify ?? throw new ArgumentNullException(nameof(classify));
		State = LobbyEvaluationState.NotApplicable();
		_lobby.SimulationScenarioChanged += HandleSimulationScenarioChanged;
		RestartForCurrentScenario();
	}

	public event EventHandler? StateChanged;

	public LobbyEvaluationState State { get; private set; }
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

		previous.Cancel();
		try
		{
			changed?.Invoke(this, EventArgs.Empty);
		}
		finally
		{
			StartPipeline(retry);
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

			var scenario = _lobby.CreateSimulationScenario();
			var support = _classify(scenario);
			if (!support.RulesValid || !support.AppSupported)
			{
				replacement = null;
				nextState = LobbyEvaluationState.NotApplicable();
			}
			else if (support.SimulatorProfile is not { } simulatorProfile)
			{
				replacement = null;
				nextState = LobbyEvaluationState.SimulatorUnavailable();
			}
			else
			{
				var identity = new SimulationCompatibilityIdentity(
					scenario.ToCanonical(),
					simulatorProfile.Identity);
				replacement = new EvaluationRequest(scenario, identity);
				nextState = LobbyEvaluationState.Pending(identity);
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
		previous?.Cancel();
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

	private static LobbyScenarioSupport ClassifyScenario(SimulationScenario scenario)
	{
		var classification = SimulationScenarioClassifier.Classify(scenario);
		return new LobbyScenarioSupport(
			classification.RulesValidity.IsValid,
			classification.AppSupport is { IsSupported: true },
			classification.SimulatorSupport is { IsSupported: true } simulatorSupport
				? simulatorSupport.Profile
				: null);
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
			request.MarkDrained();
		}
	}

	private async Task ResolveAsync(EvaluationRequest request)
	{
		try
		{
			await Task.Yield();
			request.Token.ThrowIfCancellationRequested();
			var bundledBytes = await ReadBundledAsync(request.Token);
			if (!IsCurrent(request))
			{
				return;
			}
			if (TrySelectRecord(bundledBytes, request.Identity, out var bundledRecord))
			{
				PublishIfCurrent(request, LobbyEvaluationState.Terminal(bundledRecord));
				return;
			}

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
				PublishIfCurrent(request, LobbyEvaluationState.Terminal(localRecord));
				return;
			}

			await WaitForFallbackAsync(request);
			request.Token.ThrowIfCancellationRequested();
			var evaluation = await _evaluator.EvaluateAsync(request.Scenario, request.Token);
			if (!IsCurrent(request))
			{
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
				.Where(candidate => !candidate.CompatibilityIdentity.Equals(request.Identity))
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

			PublishIfCurrent(request, LobbyEvaluationState.Terminal(record));
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

	private async ValueTask<ReadOnlyMemory<byte>?> ReadBundledAsync(
		CancellationToken cancellationToken)
	{
		try
		{
			return await _bundledCache.ReadAsync(BundledCacheLogicalName, cancellationToken);
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

	private static bool TrySelectRecord(
		ReadOnlyMemory<byte>? bytes,
		SimulationCompatibilityIdentity identity,
		out TerminalLobbyCacheRecord record)
	{
		record = null!;
		if (bytes is not { } value)
		{
			return false;
		}

		var document = ReadUsableDocument(value);
		return document is not null
			&& TerminalLobbyCache.TryGet(document, identity, out record);
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

	internal Task CurrentPipelineCompletion
	{
		get
		{
			lock (_sync)
			{
				return _currentRequest?.Drained ?? Task.CompletedTask;
			}
		}
	}

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

	private sealed class EvaluationRequest(
		SimulationScenario scenario,
		SimulationCompatibilityIdentity identity)
	{
		private readonly object _sync = new();
		private readonly CancellationTokenSource _cancellation = new();
		private readonly TaskCompletionSource _drained =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private bool _cancellationRequested;
		private bool _disposed;

		public SimulationScenario Scenario { get; } = scenario;
		public SimulationCompatibilityIdentity Identity { get; } = identity;
		public TaskCompletionSource AccelerateFallback { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		public CancellationToken Token => _cancellation.Token;
		public bool IsCancellationRequested
		{
			get
			{
				lock (_sync)
				{
					return _cancellationRequested;
				}
			}
		}
		public Task Drained => _drained.Task;

		public void Cancel()
		{
			lock (_sync)
			{
				if (_disposed || _cancellationRequested)
				{
					return;
				}
				_cancellationRequested = true;
				_cancellation.Cancel();
			}
		}

		public void DisposeAfterDrain()
		{
			lock (_sync)
			{
				if (_disposed)
				{
					return;
				}
				_disposed = true;
				_cancellation.Dispose();
			}
		}

		public void MarkDrained() => _drained.TrySetResult();
	}
}

internal readonly record struct LobbyScenarioSupport(
	bool RulesValid,
	bool AppSupported,
	SimulatorProfile? SimulatorProfile);
