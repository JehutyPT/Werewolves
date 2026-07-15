using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.Services;

public enum LobbyEvaluationStateKind
{
	NotApplicable,
	Pending,
	AlreadyDecided,
	Degenerate,
	Probability,
	SimulatorUnavailable,
	CouldNotEvaluate
}

public sealed record LobbyEvaluationState
{
	public LobbyEvaluationStateKind Kind { get; }

	public SimulationCompatibilityIdentity? Identity { get; }

	public TerminalLobbyCacheRecord? TerminalRecord { get; }

	public bool BlocksLobbyExit => Kind is
		LobbyEvaluationStateKind.Pending or
		LobbyEvaluationStateKind.AlreadyDecided or
		LobbyEvaluationStateKind.Degenerate;

	private LobbyEvaluationState(
		LobbyEvaluationStateKind kind,
		SimulationCompatibilityIdentity? identity = null,
		TerminalLobbyCacheRecord? terminalRecord = null)
	{
		Kind = kind;
		Identity = identity;
		TerminalRecord = terminalRecord;
	}

	internal static LobbyEvaluationState NotApplicable() =>
		new(LobbyEvaluationStateKind.NotApplicable);

	internal static LobbyEvaluationState Pending(SimulationCompatibilityIdentity identity) =>
		new(LobbyEvaluationStateKind.Pending, identity);

	internal static LobbyEvaluationState SimulatorUnavailable() =>
		new(LobbyEvaluationStateKind.SimulatorUnavailable);

	internal static LobbyEvaluationState CouldNotEvaluate(SimulationCompatibilityIdentity identity) =>
		new(LobbyEvaluationStateKind.CouldNotEvaluate, identity);

	internal static LobbyEvaluationState Terminal(TerminalLobbyCacheRecord record) =>
		new(
			record switch
			{
				AlreadyDecidedTerminalCacheRecord => LobbyEvaluationStateKind.AlreadyDecided,
				DegenerateTerminalCacheRecord => LobbyEvaluationStateKind.Degenerate,
				ProbabilityTerminalCacheRecord => LobbyEvaluationStateKind.Probability,
				_ => throw new ArgumentException("Unknown terminal cache record.", nameof(record))
			},
			record.CompatibilityIdentity,
			record);
}

public interface ITerminalLobbyCacheByteSource
{
	ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
		string logicalName,
		CancellationToken cancellationToken = default);
}

public interface ILocalTerminalLobbyCacheStore
{
	ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
		CancellationToken cancellationToken = default);

	ValueTask WriteAsync(
		ReadOnlyMemory<byte> bytes,
		CancellationToken cancellationToken = default);
}

public interface ILobbyTerminalEvaluator
{
	Task<LobbyEvaluationResult> EvaluateAsync(
		SimulationScenario scenario,
		CancellationToken cancellationToken = default);
}

public sealed class EmptyTerminalLobbyCacheByteSource : ITerminalLobbyCacheByteSource
{
	public static EmptyTerminalLobbyCacheByteSource Instance { get; } = new();

	private EmptyTerminalLobbyCacheByteSource()
	{
	}

	public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
		string logicalName,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);
	}
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

	public ValueTask WriteAsync(
		ReadOnlyMemory<byte> bytes,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		lock (_sync)
		{
			_bytes = bytes.ToArray();
		}
		return ValueTask.CompletedTask;
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
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult<LobbyEvaluationResult>(new CouldNotEvaluateLobbyEvaluation());
	}
}
