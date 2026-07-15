namespace Werewolves.Client.Services;

public sealed class FileTerminalLobbyCacheStore : ILocalTerminalLobbyCacheStore
{
	public const string CacheFileName = "terminal-lobby-cache.local.json";
	private const string TemporaryFileSearchPattern = CacheFileName + ".*.tmp";

	private readonly string _cacheFilePath;
	private readonly Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> _writeTemporary;

	public FileTerminalLobbyCacheStore(string appDataDirectory)
		: this(appDataDirectory, WriteTemporaryAsync)
	{
	}

	internal FileTerminalLobbyCacheStore(
		string appDataDirectory,
		Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> writeTemporary)
	{
		if (string.IsNullOrWhiteSpace(appDataDirectory))
		{
			throw new ArgumentException(
				"App data directory must be provided.",
				nameof(appDataDirectory));
		}

		AppDataDirectory = appDataDirectory;
		_cacheFilePath = Path.Combine(appDataDirectory, CacheFileName);
		_writeTemporary = writeTemporary ?? throw new ArgumentNullException(nameof(writeTemporary));
	}

	public string AppDataDirectory { get; }

	public static FileTerminalLobbyCacheStore CreateDefault() =>
		new(DefaultAppDataDirectory.GetPath());

	public async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
		CancellationToken cancellationToken = default)
	{
		if (!File.Exists(_cacheFilePath))
		{
			return null;
		}

		return await File.ReadAllBytesAsync(_cacheFilePath, cancellationToken);
	}

	public async ValueTask WriteAsync(
		ReadOnlyMemory<byte> bytes,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Directory.CreateDirectory(AppDataDirectory);
		DeleteTemporaryArtifacts();
		var temporaryPath = Path.Combine(
			AppDataDirectory,
			$"{CacheFileName}.{Guid.NewGuid():N}.tmp");
		var committed = false;
		try
		{
			await _writeTemporary(temporaryPath, bytes, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			Commit(temporaryPath);
			committed = true;
		}
		finally
		{
			if (!committed)
			{
				TryDelete(temporaryPath);
			}
			DeleteTemporaryArtifacts();
		}
	}

	private static Task WriteTemporaryAsync(
		string path,
		ReadOnlyMemory<byte> bytes,
		CancellationToken cancellationToken) =>
		File.WriteAllBytesAsync(path, bytes.ToArray(), cancellationToken);

	private void Commit(string temporaryPath)
	{
		if (!File.Exists(_cacheFilePath))
		{
			File.Move(temporaryPath, _cacheFilePath);
			return;
		}

		try
		{
			File.Replace(temporaryPath, _cacheFilePath, destinationBackupFileName: null);
		}
		catch (PlatformNotSupportedException)
		{
			File.Move(temporaryPath, _cacheFilePath, overwrite: true);
		}
		catch (NotSupportedException)
		{
			File.Move(temporaryPath, _cacheFilePath, overwrite: true);
		}
		catch (IOException) when (!File.Exists(_cacheFilePath))
		{
			File.Move(temporaryPath, _cacheFilePath);
		}
	}

	private void DeleteTemporaryArtifacts()
	{
		if (!Directory.Exists(AppDataDirectory))
		{
			return;
		}

		foreach (var path in Directory.GetFiles(AppDataDirectory, TemporaryFileSearchPattern))
		{
			TryDelete(path);
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
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
