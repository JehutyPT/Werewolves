using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Client.BrowserQaHost;

public sealed class BrowserSafeInstructionAudioPlayback : IInstructionAudioPlayback
{
	public bool IsMuted { get; private set; }

	public Task ReconcileAsync(ModeratorInstruction? instruction, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	public Task SetMutedAsync(
		bool isMuted,
		ModeratorInstruction? instruction,
		CancellationToken cancellationToken = default)
	{
		IsMuted = isMuted;
		return Task.CompletedTask;
	}
}

public sealed class BrowserSafeAudioAssetLoader : IAudioAssetLoader
{
	public Task<Stream?> OpenAsync(string fileName, CancellationToken cancellationToken = default) =>
		Task.FromResult<Stream?>(null);
}

public sealed class BrowserSafeAudioPlayerFactory : IAudioPlayerFactory
{
	public IAudioPlaybackHandle Create(Stream audioStream) => new BrowserSafeAudioPlaybackHandle();
}

public sealed class BrowserSafeAudioPlaybackHandle : IAudioPlaybackHandle
{
	public bool IsPlaying { get; private set; }
	public bool Loop { get; set; }

	public void Play() => IsPlaying = true;

	public void Stop() => IsPlaying = false;

	public void Dispose() => Stop();
}

public sealed class BrowserSafeHapticFeedbackService : IHapticFeedbackService
{
	public void Click()
	{
	}

	public void LongPress()
	{
	}
}

public sealed class BrowserSafeScreenWakeLock : IScreenWakeLock
{
	public bool KeepScreenOn { get; set; }
}

public sealed class BrowserQaInMemoryGameSessionSaveStore : IGameSessionSaveStore
{
	private string? _serializedSession;

	public string? Load() => _serializedSession;

	public void Save(string serializedSession) => _serializedSession = serializedSession;

	public void Clear() => _serializedSession = null;
}
