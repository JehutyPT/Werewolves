using FluentAssertions;
using System.Text.Json.Nodes;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public sealed class RecentSetupStoreTests
{
	[Fact]
	public void Capture_EquivalentContentBumpsTheExistingSetupWithANewTimestamp()
	{
		var clock = new ManualTimeProvider(
			new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero));
		var store = new InMemoryRecentSetupStore(clock);

		store.Capture(
			["Ana", "Bruno"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleVillager] = 2,
				[MainRoleType.Witch] = 0
			});
		clock.Advance(TimeSpan.FromMinutes(1));
		store.Capture(
			["Carla", "Diogo"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 1
			});
		clock.Advance(TimeSpan.FromMinutes(1));

		store.Capture(
			["Ana", "Bruno"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleVillager] = 2
			});

		store.Load().Should().SatisfyRespectively(
			mostRecent =>
			{
				mostRecent.PlayerNames.Should().Equal("Ana", "Bruno");
				mostRecent.RoleCounts.Should().BeEquivalentTo(
					new Dictionary<MainRoleType, int>
					{
						[MainRoleType.SimpleVillager] = 2
					});
				mostRecent.CapturedAtUtc.Should().Be(clock.GetUtcNow());
			},
			older => older.PlayerNames.Should().Equal("Carla", "Diogo"));
	}

	[Fact]
	public void Capture_EvictsTheOldestSetupAfterTenDistinctEntries()
	{
		var clock = new ManualTimeProvider(
			new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero));
		var store = new InMemoryRecentSetupStore(clock);
		var roleCounts = new Dictionary<MainRoleType, int>
		{
			[MainRoleType.SimpleWerewolf] = 1,
			[MainRoleType.SimpleVillager] = 4
		};

		for (var index = 0; index < 11; index++)
		{
			store.Capture([$"Player {index}"], roleCounts);
			clock.Advance(TimeSpan.FromMinutes(1));
		}

		store.Load().Select(setup => setup.PlayerNames.Single())
			.Should().Equal(Enumerable.Range(1, 10).Reverse().Select(index => $"Player {index}"));
	}

	[Fact]
	public void Capture_TreatsOrdinalSpellingAndSeatOrderAsDistinctContent()
	{
		var store = new InMemoryRecentSetupStore();
		var roleCounts = new Dictionary<MainRoleType, int>
		{
			[MainRoleType.SimpleWerewolf] = 1,
			[MainRoleType.SimpleVillager] = 1
		};

		store.Capture(["Ana", "Bruno"], roleCounts);
		store.Capture(["ana", "Bruno"], roleCounts);
		store.Capture(["Bruno", "Ana"], roleCounts);

		store.Load().Select(setup => string.Join("|", setup.PlayerNames)).Should().Equal(
			"Bruno|Ana",
			"ana|Bruno",
			"Ana|Bruno");
	}

	[Fact]
	public void FileStore_RoundTripsThroughTwoStoreInstances()
	{
		using var directory = new TemporaryDirectory();
		var capturedAt = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
		var writer = new FileRecentSetupStore(
			directory.Path,
			new ManualTimeProvider(capturedAt));
		writer.Capture(
			["Ana", "Bruno", "Carla"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 2
			});

		var loaded = new FileRecentSetupStore(directory.Path).Load().Should().ContainSingle().Subject;

		loaded.PlayerNames.Should().Equal("Ana", "Bruno", "Carla");
		loaded.RoleCounts.Should().BeEquivalentTo(
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 2
			});
		loaded.CapturedAtUtc.Should().Be(capturedAt);
	}

	[Fact]
	public void FileStore_DeletePersistsRemovalOfOnlyTheSelectedSetup()
	{
		using var directory = new TemporaryDirectory();
		var clock = new ManualTimeProvider(
			new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero));
		var store = new FileRecentSetupStore(directory.Path, clock);
		var roleCounts = new Dictionary<MainRoleType, int>
		{
			[MainRoleType.SimpleWerewolf] = 1,
			[MainRoleType.SimpleVillager] = 1
		};
		store.Capture(["Ana", "Bruno"], roleCounts);
		clock.Advance(TimeSpan.FromMinutes(1));
		store.Capture(["Carla", "Diogo"], roleCounts);
		var selected = store.Load().Single(setup => setup.PlayerNames[0] == "Ana");

		store.Delete(selected);

		new FileRecentSetupStore(directory.Path).Load()
			.Should().ContainSingle()
			.Which.PlayerNames.Should().Equal("Carla", "Diogo");
	}

	[Fact]
	public void FileStore_Delete_WhenStoredPayloadIsCorrupt_ThrowsAndPreservesThePayload()
	{
		using var directory = new TemporaryDirectory();
		var store = new FileRecentSetupStore(directory.Path);
		store.Capture(
			["Ana", "Bruno"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 1
			});
		var selected = store.Load().Single();
		var storePath = System.IO.Path.Combine(
			directory.Path,
			FileRecentSetupStore.StoreFileName);
		const string corruptPayload = "not-json";
		File.WriteAllText(storePath, corruptPayload);

		var delete = () => store.Delete(selected);

		delete.Should().Throw<InvalidDataException>();
		File.ReadAllText(storePath).Should().Be(corruptPayload);
	}

	[Fact]
	public void FileStore_MissingCaptureTimestampYieldsEmptyAndDoesNotPoisonLaterCapture()
	{
		using var directory = new TemporaryDirectory();
		var store = new FileRecentSetupStore(directory.Path);
		store.Capture(
			["Ana", "Bruno"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 1
			});
		var selected = store.Load().Single();
		var storePath = System.IO.Path.Combine(
			directory.Path,
			FileRecentSetupStore.StoreFileName);
		var envelope = JsonNode.Parse(File.ReadAllText(storePath))!;
		envelope["setups"]![0]!.AsObject().Remove("capturedAtUtc");
		var malformedPayload = envelope.ToJsonString();
		File.WriteAllText(storePath, malformedPayload);

		var delete = () => store.Delete(selected);
		delete.Should().Throw<InvalidDataException>();
		File.ReadAllText(storePath).Should().Be(malformedPayload);
		store.Load().Should().BeEmpty();
		store.Capture(
			["Carla", "Diogo"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 1
			});

		new FileRecentSetupStore(directory.Path).Load().Should().ContainSingle()
			.Which.PlayerNames.Should().Equal("Carla", "Diogo");
	}

	[Fact]
	public void FileStore_UnreadablePayloadKeepsLoadTolerantAndLaterCapturePreservesHistoryAcrossReopen()
	{
		using var directory = new TemporaryDirectory();
		var store = new FileRecentSetupStore(directory.Path);
		var roleCounts = new Dictionary<MainRoleType, int>
		{
			[MainRoleType.SimpleWerewolf] = 1,
			[MainRoleType.SimpleVillager] = 1
		};
		store.Capture(["Ana", "Bruno"], roleCounts);
		var storePath = System.IO.Path.Combine(
			directory.Path,
			FileRecentSetupStore.StoreFileName);

		using (File.Open(storePath, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			store.Load().Should().BeEmpty();
			var capture = () => store.Capture(["Carla", "Diogo"], roleCounts);
			capture.Should().Throw<IOException>();
		}

		store.Capture(["Carla", "Diogo"], roleCounts);

		new FileRecentSetupStore(directory.Path).Load().Should().SatisfyRespectively(
			newest => newest.PlayerNames.Should().Equal("Carla", "Diogo"),
			older => older.PlayerNames.Should().Equal("Ana", "Bruno"));
	}

	[Fact]
	public void FileStore_Delete_WhenExistingPayloadCannotBeRead_ThrowsAndPreservesExistingSetup()
	{
		using var directory = new TemporaryDirectory();
		var store = new FileRecentSetupStore(directory.Path);
		store.Capture(
			["Ana", "Bruno"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 1
			});
		var selected = store.Load().Single();
		var storePath = System.IO.Path.Combine(
			directory.Path,
			FileRecentSetupStore.StoreFileName);

		using (File.Open(storePath, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			var delete = () => store.Delete(selected);
			delete.Should().Throw<IOException>();
		}

		new FileRecentSetupStore(directory.Path).Load().Should().ContainSingle()
			.Which.PlayerNames.Should().Equal("Ana", "Bruno");
	}

	[Fact]
	public void FileStore_DeduplicatesBumpsNewestAndCapsTenAcrossReopen()
	{
		using var directory = new TemporaryDirectory();
		var clock = new ManualTimeProvider(
			new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero));
		var store = new FileRecentSetupStore(directory.Path, clock);
		var normalizedCounts = new Dictionary<MainRoleType, int>
		{
			[MainRoleType.SimpleWerewolf] = 1,
			[MainRoleType.SimpleVillager] = 4
		};
		store.Capture(
			["Ana", "Bruno"],
			new Dictionary<MainRoleType, int>(normalizedCounts)
			{
				[MainRoleType.Witch] = 0
			});
		clock.Advance(TimeSpan.FromMinutes(1));
		store.Capture(["Oldest"], normalizedCounts);
		clock.Advance(TimeSpan.FromMinutes(1));

		store.Capture(["Ana", "Bruno"], normalizedCounts);
		var bumpedAt = clock.GetUtcNow();
		for (var index = 2; index <= 10; index++)
		{
			clock.Advance(TimeSpan.FromMinutes(1));
			store.Capture([$"Player {index}"], normalizedCounts);
		}

		var reopened = new FileRecentSetupStore(directory.Path).Load();
		reopened.Should().HaveCount(10);
		reopened.Select(setup => string.Join("|", setup.PlayerNames)).Should().Equal(
			Enumerable.Range(2, 9).Reverse().Select(index => $"Player {index}")
				.Append("Ana|Bruno"));
		var bumped = reopened[^1];
		bumped.PlayerNames.Should().Equal("Ana", "Bruno");
		bumped.RoleCounts.Should().BeEquivalentTo(normalizedCounts);
		bumped.CapturedAtUtc.Should().Be(bumpedAt);
	}

	[Theory]
	[InlineData("not-json")]
	[InlineData("{\"schemaVersion\":999,\"setups\":[]}")]
	public void FileStore_InvalidPayloadYieldsEmptyAndDoesNotPoisonLaterCapture(
		string invalidPayload)
	{
		using var directory = new TemporaryDirectory();
		File.WriteAllText(
			System.IO.Path.Combine(directory.Path, FileRecentSetupStore.StoreFileName),
			invalidPayload);
		var store = new FileRecentSetupStore(directory.Path);

		store.Load().Should().BeEmpty();
		store.Capture(
			["Ana", "Bruno"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 1
			});

		new FileRecentSetupStore(directory.Path).Load().Should().ContainSingle()
			.Which.PlayerNames.Should().Equal("Ana", "Bruno");
	}

	private sealed class TemporaryDirectory : IDisposable
	{
		public TemporaryDirectory()
		{
			Path = System.IO.Path.Combine(
				System.IO.Path.GetTempPath(),
				"Werewolves.Client.Tests",
				Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path);
		}

		public string Path { get; }

		public void Dispose()
		{
			if (Directory.Exists(Path))
			{
				Directory.Delete(Path, recursive: true);
			}
		}
	}
}
