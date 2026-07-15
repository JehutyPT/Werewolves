using FluentAssertions;
using Werewolves.Client.Services;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class TerminalLobbyCacheAdapterTests
{
	[Fact]
	public async Task MauiByteSource_ForwardsExactLogicalNameAndPreservesBytes()
	{
		string? opened = null;
		var expected = "semantic-cache"u8.ToArray();
		var source = new MauiTerminalLobbyCacheByteSource((name, token) =>
		{
			token.ThrowIfCancellationRequested();
			opened = name;
			return Task.FromResult<Stream>(new MemoryStream(expected));
		});

		var actual = await source.ReadAsync(LobbyEvaluationCoordinator.BundledCacheLogicalName);

		opened.Should().Be("terminal-lobby-cache.json");
		actual!.Value.ToArray().Should().Equal(expected);
	}

	[Fact]
	public async Task FileStore_ReplacesCommittedBytesAndCleansStaleTemporaryArtifacts()
	{
		var directory = NewTemporaryDirectory();
		try
		{
			var stale = Path.Combine(
				directory,
				$"{FileTerminalLobbyCacheStore.CacheFileName}.stale.tmp");
			Directory.CreateDirectory(directory);
			await File.WriteAllTextAsync(stale, "stale");
			var store = new FileTerminalLobbyCacheStore(directory);

			await store.WriteAsync("first"u8.ToArray());
			await store.WriteAsync("second"u8.ToArray());

			var committed = await store.ReadAsync();
			committed!.Value.ToArray().Should().Equal("second"u8.ToArray());
			Directory.GetFiles(
				directory,
				$"{FileTerminalLobbyCacheStore.CacheFileName}.*.tmp").Should().BeEmpty();
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Fact]
	public async Task FileStore_FailedTemporaryWritePreservesPreviouslyCommittedDocument()
	{
		var directory = NewTemporaryDirectory();
		try
		{
			var initial = new FileTerminalLobbyCacheStore(directory);
			await initial.WriteAsync("committed"u8.ToArray());
			var failing = new FileTerminalLobbyCacheStore(
				directory,
				async (path, bytes, token) =>
				{
					await File.WriteAllBytesAsync(path, bytes.ToArray(), token);
					throw new IOException("injected write failure");
				});

			await failing.Invoking(store => store.WriteAsync("new"u8.ToArray()).AsTask())
				.Should().ThrowAsync<IOException>();

			var committed = await initial.ReadAsync();
			committed!.Value.ToArray().Should().Equal("committed"u8.ToArray());
			Directory.GetFiles(
				directory,
				$"{FileTerminalLobbyCacheStore.CacheFileName}.*.tmp").Should().BeEmpty();
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Fact]
	public async Task FileStore_CancellationBeforeCommitPreservesPreviouslyCommittedDocument()
	{
		var directory = NewTemporaryDirectory();
		try
		{
			var initial = new FileTerminalLobbyCacheStore(directory);
			await initial.WriteAsync("committed"u8.ToArray());
			using var cancellation = new CancellationTokenSource();
			var cancelling = new FileTerminalLobbyCacheStore(
				directory,
				async (path, bytes, token) =>
				{
					await File.WriteAllBytesAsync(path, bytes.ToArray(), token);
					cancellation.Cancel();
				});

			await cancelling.Invoking(store =>
					store.WriteAsync("new"u8.ToArray(), cancellation.Token).AsTask())
				.Should().ThrowAsync<OperationCanceledException>();

			var committed = await initial.ReadAsync();
			committed!.Value.ToArray().Should().Equal("committed"u8.ToArray());
			Directory.GetFiles(
				directory,
				$"{FileTerminalLobbyCacheStore.CacheFileName}.*.tmp").Should().BeEmpty();
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	private static string NewTemporaryDirectory() =>
		Path.Combine(Path.GetTempPath(), $"werewolves-cache-{Guid.NewGuid():N}");
}
