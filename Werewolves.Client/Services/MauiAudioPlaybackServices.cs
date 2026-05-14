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

	public IAudioPlayer Create(Stream audioStream) => _audioManager.CreatePlayer(audioStream);
}
