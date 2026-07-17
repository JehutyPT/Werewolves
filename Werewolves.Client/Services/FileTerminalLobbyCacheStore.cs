namespace Werewolves.Client.Services;

public sealed class FileTerminalLobbyCacheStore : ILocalTerminalLobbyCacheStore
{
	public const string CacheFileName = "terminal-lobby-cache.local.json";
	private const string TemporaryFileSearchPattern = CacheFileName + ".*.tmp";

	private readonly string _cacheFilePath;
	private readonly Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> _writeTemporary;
	private readonly object _commitSync = new();
	private readonly HashSet<string> _activeTemporaryPaths = new(StringComparer.Ordinal);

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

	public async ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
		ReadOnlyMemory<byte> bytes,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Directory.CreateDirectory(AppDataDirectory);
		var temporaryPath = Path.Combine(
			AppDataDirectory,
			$"{CacheFileName}.{Guid.NewGuid():N}.tmp");
		lock (_commitSync)
		{
			DeleteAbandonedTemporaryArtifacts();
			_activeTemporaryPaths.Add(temporaryPath);
		}
		try
		{
			await _writeTemporary(temporaryPath, bytes, cancellationToken);
			return new StagedWrite(this, temporaryPath);
		}
		catch
		{
			Abandon(temporaryPath);
			throw;
		}
	}

	public async ValueTask WriteAsync(
		ReadOnlyMemory<byte> bytes,
		CancellationToken cancellationToken = default)
	{
		await using var staged = await StageWriteAsync(bytes, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		staged.TryCommit(commit =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			commit();
			return true;
		});
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

	private bool TryCommit(
		StagedWrite staged,
		Func<Action, bool> commitIfAuthorized)
	{
		ArgumentNullException.ThrowIfNull(commitIfAuthorized);
		lock (_commitSync)
		{
			staged.ThrowIfCompleted();
			var committed = false;
			try
			{
				var authorized = commitIfAuthorized(() =>
				{
					if (committed)
					{
						throw new InvalidOperationException(
							"A staged write can be committed only once.");
					}
					Commit(staged.TemporaryPath);
					committed = true;
				});
				if (authorized != committed)
				{
					throw new InvalidOperationException(
						"Commit authorization must return whether it invoked the commit action.");
				}
				return committed;
			}
			finally
			{
				staged.MarkCompleted();
				_activeTemporaryPaths.Remove(staged.TemporaryPath);
				if (!committed)
				{
					TryDelete(staged.TemporaryPath);
				}
			}
		}
	}

	private void Abandon(string temporaryPath)
	{
		lock (_commitSync)
		{
			_activeTemporaryPaths.Remove(temporaryPath);
			TryDelete(temporaryPath);
		}
	}

	private void DeleteAbandonedTemporaryArtifacts()
	{
		if (!Directory.Exists(AppDataDirectory))
		{
			return;
		}

		foreach (var path in Directory.GetFiles(AppDataDirectory, TemporaryFileSearchPattern))
		{
			if (!_activeTemporaryPaths.Contains(path))
			{
				TryDelete(path);
			}
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

	private sealed class StagedWrite(
		FileTerminalLobbyCacheStore owner,
		string temporaryPath) : ILocalTerminalLobbyCacheWrite
	{
		private bool _completed;

		public string TemporaryPath { get; } = temporaryPath;

		public bool TryCommit(Func<Action, bool> commitIfAuthorized) =>
			owner.TryCommit(this, commitIfAuthorized);

		public ValueTask DisposeAsync()
		{
			lock (owner._commitSync)
			{
				if (_completed)
				{
					return ValueTask.CompletedTask;
				}
				_completed = true;
				owner._activeTemporaryPaths.Remove(TemporaryPath);
				TryDelete(TemporaryPath);
				return ValueTask.CompletedTask;
			}
		}

		public void ThrowIfCompleted() =>
			ObjectDisposedException.ThrowIf(_completed, this);

		public void MarkCompleted() => _completed = true;
	}
}
