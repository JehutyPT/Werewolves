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
	private readonly object _sync = new();
	private EvaluationRequest? _currentRequest;
	private bool _disposed;

	public LobbyEvaluationCoordinator(
		LobbySetupState lobby,
		ITerminalLobbyCacheByteSource bundledCache,
		ILocalTerminalLobbyCacheStore localCache,
		ILobbyTerminalEvaluator evaluator,
		TimeProvider? timeProvider = null)
	{
		_lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
		_bundledCache = bundledCache ?? throw new ArgumentNullException(nameof(bundledCache));
		_localCache = localCache ?? throw new ArgumentNullException(nameof(localCache));
		_evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
		_timeProvider = timeProvider ?? TimeProvider.System;
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
		changed?.Invoke(this, EventArgs.Empty);
		_ = ResolveAsync(retry);
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
			var classification = SimulationScenarioClassifier.Classify(scenario);
			if (!classification.RulesValidity.IsValid
				|| classification.AppSupport is not { IsSupported: true })
			{
				replacement = null;
				nextState = LobbyEvaluationState.NotApplicable();
			}
			else if (classification.SimulatorSupport is not { IsSupported: true } simulatorSupport)
			{
				replacement = null;
				nextState = LobbyEvaluationState.SimulatorUnavailable();
			}
			else
			{
				var identity = new SimulationCompatibilityIdentity(
					scenario.ToCanonical(),
					simulatorSupport.Profile.Identity);
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
		changed?.Invoke(this, EventArgs.Empty);
		if (replacement is not null)
		{
			_ = ResolveAsync(replacement);
		}
	}

	private async Task ResolveAsync(EvaluationRequest request)
	{
		try
		{
			await Task.Yield();
			request.Token.ThrowIfCancellationRequested();
			var bundledBytes = await ReadBundledAsync(request.Token);
			if (TrySelectRecord(bundledBytes, request.Identity, out var bundledRecord))
			{
				PublishIfCurrent(request, LobbyEvaluationState.Terminal(bundledRecord));
				return;
			}

			request.Token.ThrowIfCancellationRequested();
			var localBytes = await ReadLocalAsync(request.Token);
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
			request.Token.ThrowIfCancellationRequested();
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
				await _localCache.WriteAsync(bytes, request.Token);
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
		var delay = Task.Delay(FallbackQuietPeriod, _timeProvider, request.Token);
		await Task.WhenAny(delay, request.AccelerateFallback.Task);
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
			return !_disposed
				&& !request.IsCancellationRequested
				&& ReferenceEquals(_currentRequest, request);
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
		private readonly CancellationTokenSource _cancellation = new();

		public SimulationScenario Scenario { get; } = scenario;
		public SimulationCompatibilityIdentity Identity { get; } = identity;
		public TaskCompletionSource AccelerateFallback { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		public CancellationToken Token => _cancellation.Token;
		public bool IsCancellationRequested => _cancellation.IsCancellationRequested;

		public void Cancel()
		{
			_cancellation.Cancel();
		}
	}
}
