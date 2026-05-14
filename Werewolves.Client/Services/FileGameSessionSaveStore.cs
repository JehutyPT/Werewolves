namespace Werewolves.Client.Services;

public interface IGameSessionSaveStore
{
	string? Load();
	void Save(string serializedSession);
	void Clear();
}

public sealed class FileGameSessionSaveStore : IGameSessionSaveStore
{
	public const string SaveFileName = "active-game-session.json";

	private readonly string _saveFilePath;

	public FileGameSessionSaveStore(string appDataDirectory)
	{
		if (string.IsNullOrWhiteSpace(appDataDirectory))
		{
			throw new ArgumentException("App data directory must be provided.", nameof(appDataDirectory));
		}

		AppDataDirectory = appDataDirectory;
		_saveFilePath = Path.Combine(appDataDirectory, SaveFileName);
	}

	public string AppDataDirectory { get; }

	public static FileGameSessionSaveStore CreateDefault() =>
		new(DefaultAppDataDirectory.GetPath());

	public string? Load()
	{
		return File.Exists(_saveFilePath)
			? File.ReadAllText(_saveFilePath)
			: null;
	}

	public void Save(string serializedSession)
	{
		Directory.CreateDirectory(AppDataDirectory);
		File.WriteAllText(_saveFilePath, serializedSession);
	}

	public void Clear()
	{
		if (File.Exists(_saveFilePath))
		{
			File.Delete(_saveFilePath);
		}
	}
}

internal static class DefaultAppDataDirectory
{
	public static string GetPath()
	{
#if ANDROID || IOS || MACCATALYST || WINDOWS
		return Microsoft.Maui.Storage.FileSystem.Current.AppDataDirectory;
#else
		return Path.Combine(Path.GetTempPath(), "Werewolves.Client", Guid.NewGuid().ToString("N"));
#endif
	}
}
