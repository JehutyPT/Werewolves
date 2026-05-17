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
	private const string TemporarySaveFileSearchPattern = SaveFileName + ".*.tmp";

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
		DeleteTemporaryWriteArtifacts();

		var temporarySaveFilePath = Path.Combine(
			AppDataDirectory,
			$"{SaveFileName}.{Guid.NewGuid():N}.tmp");
		var replacedSave = false;

		try
		{
			File.WriteAllText(temporarySaveFilePath, serializedSession);
			ReplaceSaveFile(temporarySaveFilePath);
			replacedSave = true;
		}
		finally
		{
			if (!replacedSave)
			{
				TryDelete(temporarySaveFilePath);
			}
		}

		DeleteTemporaryWriteArtifacts();
	}

	public void Clear()
	{
		if (File.Exists(_saveFilePath))
		{
			File.Delete(_saveFilePath);
		}

		DeleteTemporaryWriteArtifacts();
	}

	private void ReplaceSaveFile(string temporarySaveFilePath)
	{
		if (!File.Exists(_saveFilePath))
		{
			File.Move(temporarySaveFilePath, _saveFilePath);
			return;
		}

		try
		{
			File.Replace(temporarySaveFilePath, _saveFilePath, destinationBackupFileName: null);
		}
		catch (PlatformNotSupportedException)
		{
			File.Move(temporarySaveFilePath, _saveFilePath, overwrite: true);
		}
		catch (NotSupportedException)
		{
			File.Move(temporarySaveFilePath, _saveFilePath, overwrite: true);
		}
		catch (IOException) when (!File.Exists(_saveFilePath))
		{
			File.Move(temporarySaveFilePath, _saveFilePath);
		}
	}

	private void DeleteTemporaryWriteArtifacts()
	{
		if (!Directory.Exists(AppDataDirectory))
		{
			return;
		}

		foreach (var temporaryFilePath in Directory.GetFiles(AppDataDirectory, TemporarySaveFileSearchPattern))
		{
			TryDelete(temporaryFilePath);
		}
	}

	private static void TryDelete(string filePath)
	{
		try
		{
			if (File.Exists(filePath))
			{
				File.Delete(filePath);
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
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
