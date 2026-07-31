using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class TerminalLobbyCacheAdapterTests
{
	[Fact]
	public async Task FileStore_CommitAuthorizationSerializesOverlappingWritersAndKeepsCurrentBytes()
	{
		var directory = NewTemporaryDirectory();
		try
		{
			var store = new FileTerminalLobbyCacheStore(directory);
			await using var stale = await store.StageWriteAsync("stale"u8.ToArray());
			await using var current = await store.StageWriteAsync("current"u8.ToArray());
			var staleAtBoundary = new TaskCompletionSource(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseStale = new ManualResetEventSlim();
			var staleCommit = Task.Run(() => stale.TryCommit(commit =>
			{
				staleAtBoundary.TrySetResult();
				releaseStale.Wait();
				return false;
			}));
			await staleAtBoundary.Task.WaitAsync(TimeSpan.FromSeconds(5));
			var currentCommit = Task.Run(() => current.TryCommit(commit =>
			{
				commit();
				return true;
			}));

			currentCommit.IsCompleted.Should().BeFalse(
				"the actual commit boundary is serialized across staged writers");
			releaseStale.Set();
			(await staleCommit).Should().BeFalse();
			(await currentCommit).Should().BeTrue();

			var committed = await store.ReadAsync();
			committed!.Value.ToArray().Should().Equal("current"u8.ToArray());
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
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
	public async Task FileStore_CurrentSafetySchemaOneDegenerateDocument_RoundTripsAndResolvesExactIdentity()
	{
		var directory = NewTemporaryDirectory();
		try
		{
			var scenario = new SimulationScenario(
				5,
				[
					MainRoleType.SimpleWerewolf,
					MainRoleType.BearTamer,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager
				]);
			var identity = new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity);
			var villagerVictory = new SingleFactionGameResult(Faction.Villager);
			var record = new DegenerateTerminalCacheRecord(
				identity,
				[
					new TerminalCacheGameResultFrequency(villagerVictory, 1_000, 1_000),
					new TerminalCacheGameResultFrequency(
						new SingleFactionGameResult(Faction.Werewolf),
						0,
						1_000),
					new TerminalCacheGameResultFrequency(new NoWinnerGameResult(), 0, 1_000)
				],
				[
					new TerminalCacheTurnWindowFrequency(
						villagerVictory,
						endingTurn: 1,
						VictoryCheckWindow.Dawn,
						1_000,
						1_000)
				]);
			var bytes = TerminalLobbyCache.Write(
				TerminalLobbyCache.CreateDocument([record]));
			var writer = new FileTerminalLobbyCacheStore(directory);

			await writer.WriteAsync(bytes);

			var reopened = new FileTerminalLobbyCacheStore(directory);
			var persisted = await reopened.ReadAsync();
			var read = TerminalLobbyCache.ReadDocument(persisted!.Value.Span);

			SimulatorCapability.SafetyScreening.Identity.ToString()
				.Should().Be("safety-screening@20");
			persisted.Value.ToArray().Should().Equal(bytes);
			read.IsUsable.Should().BeTrue();
			TerminalLobbyCache.TryGet(read.Document!, identity, out var restored)
				.Should().BeTrue();
			restored.Should().BeOfType<DegenerateTerminalCacheRecord>();
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
