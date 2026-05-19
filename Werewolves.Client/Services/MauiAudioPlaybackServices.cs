using Microsoft.Maui.Storage;
using Plugin.Maui.Audio;

namespace Werewolves.Client.Services;

public sealed class MauiAudioAssetLoader : IAudioAssetLoader
{
	public async Task<Stream?> OpenAsync(string fileName, CancellationToken cancellationToken = default)
	{
		try
		{
			return await FileSystem
				.OpenAppPackageFileAsync($"Audio/{fileName}")
				.ConfigureAwait(false);
		}
		catch (FileNotFoundException)
		{
			return null;
		}
		catch (DirectoryNotFoundException)
		{
			return null;
		}
	}
}

public sealed class PluginAudioPlayerFactory : IAudioPlayerFactory
{
	private readonly IAudioManager _audioManager;

	public PluginAudioPlayerFactory(IAudioManager audioManager)
	{
		_audioManager = audioManager;
	}

	public IAudioPlaybackHandle Create(Stream audioStream) =>
		new PluginAudioPlaybackHandle(_audioManager.CreatePlayer(audioStream));

	private sealed class PluginAudioPlaybackHandle : IAudioPlaybackHandle
	{
		private readonly IAudioPlayer _player;

		public PluginAudioPlaybackHandle(IAudioPlayer player)
		{
			_player = player;
		}

		public bool IsPlaying => _player.IsPlaying;

		public bool Loop
		{
			get => _player.Loop;
			set => _player.Loop = value;
		}

		public void Play() => _player.Play();

		public void Stop() => _player.Stop();

		public void Dispose() => _player.Dispose();
	}
}
