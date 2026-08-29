using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.Services;

public sealed record LobbyEvaluationSettings
{
	public SimulatorCapability Capability { get; }
	public LobbyEvaluationDepth Depth { get; }

	public LobbyEvaluationSettings(
		SimulatorCapability capability,
		LobbyEvaluationDepth depth)
	{
		Capability = capability ?? throw new ArgumentNullException(nameof(capability));
		if (!Enum.IsDefined(depth))
		{
			throw new ArgumentOutOfRangeException(nameof(depth));
		}
		if (!capability.SupportsEvaluationDepth(depth))
		{
			throw new ArgumentException(
				"The simulator capability does not support the requested evaluation depth.",
				nameof(depth));
		}
		Depth = depth;
	}
}

public interface ILocalTerminalLobbyCacheStore
{
	ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
		CancellationToken cancellationToken = default);

	ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
		ReadOnlyMemory<byte> bytes,
		CancellationToken cancellationToken = default);
}

public interface ILocalTerminalLobbyCacheWrite : IAsyncDisposable
{
	bool TryCommit(Func<Action, bool> commitIfAuthorized);
}

public interface ILobbyTerminalEvaluator
{
	Task<LobbyEvaluationResult> EvaluateAsync(
		SimulationScenario scenario,
		SimulatorCapability capability,
		LobbyEvaluationDepth depth,
		CancellationToken cancellationToken = default);
}

public sealed class InMemoryTerminalLobbyCacheStore : ILocalTerminalLobbyCacheStore
{
	private readonly object _sync = new();
	private byte[]? _bytes;

	public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		lock (_sync)
		{
			return ValueTask.FromResult<ReadOnlyMemory<byte>?>(_bytes?.ToArray());
		}
	}

	public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
		ReadOnlyMemory<byte> bytes,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult<ILocalTerminalLobbyCacheWrite>(
			new InMemoryWrite(this, bytes.ToArray()));
	}

	private sealed class InMemoryWrite(
		InMemoryTerminalLobbyCacheStore owner,
		byte[] bytes) : ILocalTerminalLobbyCacheWrite
	{
		private bool _completed;

		public bool TryCommit(Func<Action, bool> commitIfAuthorized)
		{
			ArgumentNullException.ThrowIfNull(commitIfAuthorized);
			lock (owner._sync)
			{
				ObjectDisposedException.ThrowIf(_completed, this);
				var committed = false;
				var authorized = commitIfAuthorized(() =>
				{
					if (committed)
					{
						throw new InvalidOperationException("A staged write can be committed only once.");
					}
					owner._bytes = bytes.ToArray();
					committed = true;
				});
				if (authorized != committed)
				{
					throw new InvalidOperationException(
						"Commit authorization must return whether it invoked the commit action.");
				}
				_completed = true;
				return committed;
			}
		}

		public ValueTask DisposeAsync()
		{
			lock (owner._sync)
			{
				_completed = true;
			}
			return ValueTask.CompletedTask;
		}
	}
}

public sealed class DisabledLobbyTerminalEvaluator : ILobbyTerminalEvaluator
{
	public static DisabledLobbyTerminalEvaluator Instance { get; } = new();

	private DisabledLobbyTerminalEvaluator()
	{
	}

	public Task<LobbyEvaluationResult> EvaluateAsync(
		SimulationScenario scenario,
		SimulatorCapability capability,
		LobbyEvaluationDepth depth,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(capability);
		if (!Enum.IsDefined(depth))
		{
			throw new ArgumentOutOfRangeException(nameof(depth));
		}
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult<LobbyEvaluationResult>(new CouldNotEvaluateLobbyEvaluation());
	}
}
