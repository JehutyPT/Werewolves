using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plugin.Maui.Audio;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Client.Services;

public interface IInstructionAudioPlayback
{
	bool IsMuted { get; }
	Task ReconcileAsync(ModeratorInstruction? instruction, CancellationToken cancellationToken = default);
	Task SetMutedAsync(bool isMuted, ModeratorInstruction? instruction, CancellationToken cancellationToken = default);
}

public interface IAudioAssetLoader
{
	Task<Stream?> OpenAsync(string fileName, CancellationToken cancellationToken = default);
}

public interface IAudioPlayerFactory
{
	IAudioPlayer Create(Stream audioStream);
}

public sealed class DisabledInstructionAudioPlayback : IInstructionAudioPlayback
{
	public static DisabledInstructionAudioPlayback Instance { get; } = new();

	private DisabledInstructionAudioPlayback()
	{
	}

	public bool IsMuted => false;

	public Task ReconcileAsync(ModeratorInstruction? instruction, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	public Task SetMutedAsync(bool isMuted, ModeratorInstruction? instruction, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;
}

public sealed class InstructionAudioPlayback : IInstructionAudioPlayback, IDisposable
{
	private readonly IAudioMap _audioMap;
	private readonly IAudioAssetLoader _assetLoader;
	private readonly IAudioPlayerFactory _playerFactory;
	private readonly ILogger<InstructionAudioPlayback> _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private IAudioPlayer? _activePlayer;
	private string? _activeFileName;

	public InstructionAudioPlayback(
		IAudioMap audioMap,
		IAudioAssetLoader assetLoader,
		IAudioPlayerFactory playerFactory,
		ILogger<InstructionAudioPlayback>? logger = null)
	{
		_audioMap = audioMap;
		_assetLoader = assetLoader;
		_playerFactory = playerFactory;
		_logger = logger ?? NullLogger<InstructionAudioPlayback>.Instance;
	}

	public bool IsMuted { get; private set; }

	public async Task ReconcileAsync(ModeratorInstruction? instruction, CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await ReconcileCoreAsync(instruction, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task SetMutedAsync(
		bool isMuted,
		ModeratorInstruction? instruction,
		CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			IsMuted = isMuted;

			if (IsMuted)
			{
				StopActivePlayer();
				return;
			}

			await ReconcileCoreAsync(instruction, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose()
	{
		StopActivePlayer();
		_gate.Dispose();
	}

	private async Task ReconcileCoreAsync(
		ModeratorInstruction? instruction,
		CancellationToken cancellationToken)
	{
		if (IsMuted)
		{
			StopActivePlayer();
			return;
		}

		var mappedAudio = instruction is null
			? null
			: _audioMap.ResolveFirst(instruction.SoundEffects);

		if (mappedAudio is null)
		{
			StopActivePlayer();
			return;
		}

		if (_activePlayer is not null && _activeFileName == mappedAudio.FileName)
		{
			if (!_activePlayer.IsPlaying)
			{
				_activePlayer.Play();
			}

			return;
		}

		StopActivePlayer();

		Stream? audioStream;
		try
		{
			audioStream = await _assetLoader
				.OpenAsync(mappedAudio.FileName, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.LogWarning(
				exception,
				"Audio file {AudioFileName} mapped from {SoundEffect} could not be opened. Instruction will be treated as silent.",
				mappedAudio.FileName,
				mappedAudio.SoundEffect);
			return;
		}

		if (audioStream is null)
		{
			_logger.LogWarning(
				"Audio file {AudioFileName} mapped from {SoundEffect} was not found. Instruction will be treated as silent.",
				mappedAudio.FileName,
				mappedAudio.SoundEffect);
			return;
		}

		try
		{
			var player = _playerFactory.Create(audioStream);
			player.Loop = true;
			player.Play();
			_activePlayer = player;
			_activeFileName = mappedAudio.FileName;
		}
		catch (Exception exception)
		{
			audioStream.Dispose();
			_logger.LogWarning(
				exception,
				"Audio file {AudioFileName} mapped from {SoundEffect} could not be played. Instruction will be treated as silent.",
				mappedAudio.FileName,
				mappedAudio.SoundEffect);
			StopActivePlayer();
		}
	}

	private void StopActivePlayer()
	{
		var player = _activePlayer;
		_activePlayer = null;
		_activeFileName = null;

		if (player is null)
		{
			return;
		}

		try
		{
			player.Stop();
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Active audio player could not be stopped cleanly.");
		}
		finally
		{
			player.Dispose();
		}
	}
}
