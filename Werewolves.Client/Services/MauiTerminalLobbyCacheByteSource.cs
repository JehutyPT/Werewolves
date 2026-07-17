namespace Werewolves.Client.Services;

public sealed class MauiTerminalLobbyCacheByteSource : ITerminalLobbyCacheByteSource
{
	private readonly Func<string, CancellationToken, Task<Stream>> _open;

	public MauiTerminalLobbyCacheByteSource()
		: this(OpenAppPackageFileAsync)
	{
	}

	internal MauiTerminalLobbyCacheByteSource(
		Func<string, CancellationToken, Task<Stream>> open)
	{
		_open = open ?? throw new ArgumentNullException(nameof(open));
	}

	public async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
		string logicalName,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			await using var stream = await _open(logicalName, cancellationToken);
			using var bytes = new MemoryStream();
			await stream.CopyToAsync(bytes, cancellationToken);
			return bytes.ToArray();
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

	private static Task<Stream> OpenAppPackageFileAsync(
		string logicalName,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
#if ANDROID || IOS || MACCATALYST || WINDOWS
		return Microsoft.Maui.Storage.FileSystem.Current.OpenAppPackageFileAsync(logicalName);
#else
		return Task.FromException<Stream>(new PlatformNotSupportedException());
#endif
	}
}
