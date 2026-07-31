using FluentAssertions;
using Microsoft.Extensions.Logging;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Resources;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class InstructionAudioPlaybackTests
{
	private const SoundEffectsEnum FirstEffect = (SoundEffectsEnum)1;
	private const SoundEffectsEnum SecondEffect = (SoundEffectsEnum)2;

	[Fact]
	public async Task ReconcileAsync_WithMappedAvailableEffect_PlaysLoopedAudio()
	{
		var context = PlaybackContext.Create(
			new Dictionary<SoundEffectsEnum, string> { [FirstEffect] = "noite-lobos.mp3" },
			new Dictionary<string, byte[]> { ["noite-lobos.mp3"] = [1, 2, 3] });

		await context.Playback.ReconcileAsync(new TestInstruction(FirstEffect));

		context.AssetLoader.OpenedFileNames.Should().Equal("noite-lobos.mp3");
		context.PlayerFactory.CreatedPlayers.Should().ContainSingle();
		var player = context.PlayerFactory.CreatedPlayers.Single();
		player.Loop.Should().BeTrue();
		player.IsPlaying.Should().BeTrue();
		player.PlayCount.Should().Be(1);
	}

	[Fact]
	public async Task ReconcileAsync_WhenInstructionChanges_StopsPreviousTrackBeforePlayingNext()
	{
		var context = PlaybackContext.Create(
			new Dictionary<SoundEffectsEnum, string>
			{
				[FirstEffect] = "noite-lobos.mp3",
				[SecondEffect] = "amanhecer.mp3"
			},
			new Dictionary<string, byte[]>
			{
				["noite-lobos.mp3"] = [1],
				["amanhecer.mp3"] = [2]
			});

		await context.Playback.ReconcileAsync(new TestInstruction(FirstEffect));
		var firstPlayer = context.PlayerFactory.CreatedPlayers.Single();

		await context.Playback.ReconcileAsync(new TestInstruction(SecondEffect));

		firstPlayer.StopCount.Should().Be(1);
		firstPlayer.IsDisposed.Should().BeTrue();
		context.PlayerFactory.CreatedPlayers.Should().HaveCount(2);
		context.PlayerFactory.CreatedPlayers[1].IsPlaying.Should().BeTrue();
	}

	[Fact]
	public async Task ReconcileAsync_WhenSameTrackIsPausedAfterBackgrounding_ResumesExistingPlayer()
	{
		var context = PlaybackContext.Create(
			new Dictionary<SoundEffectsEnum, string> { [FirstEffect] = "noite-lobos.mp3" },
			new Dictionary<string, byte[]> { ["noite-lobos.mp3"] = [1] });

		await context.Playback.ReconcileAsync(new TestInstruction(FirstEffect));
		var player = context.PlayerFactory.CreatedPlayers.Single();
		player.Pause();

		await context.Playback.ReconcileAsync(new TestInstruction(FirstEffect));

		context.PlayerFactory.CreatedPlayers.Should().ContainSingle();
		player.IsPlaying.Should().BeTrue();
		player.PlayCount.Should().Be(2);
	}

	[Fact]
	public async Task SetMutedAsync_WhenMutedSilencesAudio_AndUnmutedResumesCurrentInstruction()
	{
		var context = PlaybackContext.Create(
			new Dictionary<SoundEffectsEnum, string> { [FirstEffect] = "noite-lobos.mp3" },
			new Dictionary<string, byte[]> { ["noite-lobos.mp3"] = [1] });
		var instruction = new TestInstruction(FirstEffect);

		await context.Playback.ReconcileAsync(instruction);
		var firstPlayer = context.PlayerFactory.CreatedPlayers.Single();

		await context.Playback.SetMutedAsync(isMuted: true, instruction);
		await context.Playback.ReconcileAsync(instruction);

		context.Playback.IsMuted.Should().BeTrue();
		firstPlayer.StopCount.Should().Be(1);
		firstPlayer.IsDisposed.Should().BeTrue();
		context.PlayerFactory.CreatedPlayers.Should().ContainSingle();

		await context.Playback.SetMutedAsync(isMuted: false, instruction);

		context.Playback.IsMuted.Should().BeFalse();
		context.PlayerFactory.CreatedPlayers.Should().HaveCount(2);
		context.PlayerFactory.CreatedPlayers[1].IsPlaying.Should().BeTrue();
	}

	[Fact]
	public async Task ReconcileAsync_WhenMappedFileIsMissing_LogsWarningAndTreatsInstructionAsSilent()
	{
		var context = PlaybackContext.Create(
			new Dictionary<SoundEffectsEnum, string> { [FirstEffect] = "noite-lobos.mp3" },
			new Dictionary<string, byte[]>());

		await context.Playback.ReconcileAsync(new TestInstruction(FirstEffect));

		context.PlayerFactory.CreatedPlayers.Should().BeEmpty();
		context.Logger.Entries.Should().Contain(entry =>
				entry.Level == LogLevel.Warning &&
				entry.Message.Contains("noite-lobos.mp3", StringComparison.Ordinal));
	}

	[Fact]
	public async Task ReconcileAsync_WhenEffectIsUnmapped_TreatsInstructionAsSilentWithoutLoading()
	{
		var context = PlaybackContext.Create(
			new Dictionary<SoundEffectsEnum, string>(),
			new Dictionary<string, byte[]>());

		await context.Playback.ReconcileAsync(new TestInstruction(FirstEffect));

		context.AssetLoader.OpenedFileNames.Should().BeEmpty();
		context.PlayerFactory.CreatedPlayers.Should().BeEmpty();
		context.Logger.Entries.Should().BeEmpty();
	}

	[Fact]
	public async Task ReconcileAsync_WhenAssetLoaderThrows_LogsWarningAndTreatsInstructionAsSilent()
	{
		var context = PlaybackContext.Create(
			new Dictionary<SoundEffectsEnum, string> { [FirstEffect] = "noite-lobos.mp3" },
			new Dictionary<string, byte[]> { ["noite-lobos.mp3"] = [1] });
		context.AssetLoader.OpenException = new IOException("Asset unavailable.");

		await context.Playback.ReconcileAsync(new TestInstruction(FirstEffect));

		context.AssetLoader.OpenedFileNames.Should().Equal("noite-lobos.mp3");
		context.PlayerFactory.CreatedPlayers.Should().BeEmpty();
		context.Logger.Entries.Should().Contain(entry =>
			entry.Level == LogLevel.Warning &&
			entry.Message.Contains("noite-lobos.mp3", StringComparison.Ordinal));
	}

	[Fact]
	public async Task ReconcileAsync_WhenPlayerFactoryThrows_LogsWarningAndTreatsInstructionAsSilent()
	{
		var context = PlaybackContext.Create(
			new Dictionary<SoundEffectsEnum, string> { [FirstEffect] = "noite-lobos.mp3" },
			new Dictionary<string, byte[]> { ["noite-lobos.mp3"] = [1] });
		context.PlayerFactory.CreateException =
			new InvalidOperationException("Player creation failed.");

		await context.Playback.ReconcileAsync(new TestInstruction(FirstEffect));

		context.AssetLoader.OpenedFileNames.Should().Equal("noite-lobos.mp3");
		context.PlayerFactory.CreatedPlayers.Should().BeEmpty();
		context.Logger.Entries.Should().Contain(entry =>
			entry.Level == LogLevel.Warning &&
			entry.Message.Contains("noite-lobos.mp3", StringComparison.Ordinal));
	}

	[Fact]
	public async Task ReconcileAsync_WhenPlayerPlayThrows_LogsWarningAndTreatsInstructionAsSilent()
	{
		var context = PlaybackContext.Create(
			new Dictionary<SoundEffectsEnum, string> { [FirstEffect] = "noite-lobos.mp3" },
			new Dictionary<string, byte[]> { ["noite-lobos.mp3"] = [1] });
		context.PlayerFactory.PlayException =
			new InvalidOperationException("Playback failed.");

		await context.Playback.ReconcileAsync(new TestInstruction(FirstEffect));

		var player = context.PlayerFactory.CreatedPlayers
			.Should().ContainSingle().Subject;
		player.Loop.Should().BeTrue();
		player.PlayCount.Should().Be(1);
		player.IsPlaying.Should().BeFalse();
		context.Logger.Entries.Should().Contain(entry =>
			entry.Level == LogLevel.Warning &&
			entry.Message.Contains("noite-lobos.mp3", StringComparison.Ordinal));
	}

	private sealed record TestInstruction : ModeratorInstruction
	{
		public TestInstruction(params SoundEffectsEnum[] soundEffects)
			: base(privateInstruction: GameStrings.ConfirmNightStarted, soundEffects: soundEffects.ToList())
		{
		}
	}

	private sealed class PlaybackContext
	{
		private PlaybackContext(
			InstructionAudioPlayback playback,
			FakeAudioAssetLoader assetLoader,
			FakeAudioPlayerFactory playerFactory,
			TestLogger<InstructionAudioPlayback> logger)
		{
			Playback = playback;
			AssetLoader = assetLoader;
			PlayerFactory = playerFactory;
			Logger = logger;
		}

		public InstructionAudioPlayback Playback { get; }
		public FakeAudioAssetLoader AssetLoader { get; }
		public FakeAudioPlayerFactory PlayerFactory { get; }
		public TestLogger<InstructionAudioPlayback> Logger { get; }

		public static PlaybackContext Create(
			IReadOnlyDictionary<SoundEffectsEnum, string> mappings,
			IReadOnlyDictionary<string, byte[]> assets)
		{
			var assetLoader = new FakeAudioAssetLoader(assets);
			var playerFactory = new FakeAudioPlayerFactory();
			var logger = new TestLogger<InstructionAudioPlayback>();
			var playback = new InstructionAudioPlayback(
				new AudioMap(mappings),
				assetLoader,
				playerFactory,
				logger);

			return new PlaybackContext(playback, assetLoader, playerFactory, logger);
		}
	}

	private sealed class FakeAudioAssetLoader : IAudioAssetLoader
	{
		private readonly IReadOnlyDictionary<string, byte[]> _assets;

		public FakeAudioAssetLoader(IReadOnlyDictionary<string, byte[]> assets)
		{
			_assets = assets;
		}

		public List<string> OpenedFileNames { get; } = [];
		public Exception? OpenException { get; set; }

		public Task<Stream?> OpenAsync(string fileName, CancellationToken cancellationToken = default)
		{
			OpenedFileNames.Add(fileName);
			if (OpenException is not null)
			{
				return Task.FromException<Stream?>(OpenException);
			}

			return Task.FromResult(_assets.TryGetValue(fileName, out var bytes)
				? new MemoryStream(bytes) as Stream
				: null);
		}
	}

	private sealed class FakeAudioPlayerFactory : IAudioPlayerFactory
	{
		public List<FakeAudioPlayer> CreatedPlayers { get; } = [];
		public Exception? CreateException { get; set; }
		public Exception? PlayException { get; set; }

		public IAudioPlaybackHandle Create(Stream audioStream)
		{
			if (CreateException is not null)
			{
				throw CreateException;
			}

			var player = new FakeAudioPlayer
			{
				PlayException = PlayException
			};
			CreatedPlayers.Add(player);
			return player;
		}
	}

	private sealed class FakeAudioPlayer : IAudioPlaybackHandle
	{
		public bool IsPlaying { get; private set; }
		public bool Loop { get; set; }
		public int PlayCount { get; private set; }
		public int StopCount { get; private set; }
		public bool IsDisposed { get; private set; }
		public Exception? PlayException { get; init; }

		public void Play()
		{
			PlayCount++;
			if (PlayException is not null)
			{
				throw PlayException;
			}

			IsPlaying = true;
		}

		public void Pause()
		{
			IsPlaying = false;
		}

		public void Stop()
		{
			StopCount++;
			IsPlaying = false;
		}

		public void Dispose()
		{
			IsDisposed = true;
		}
	}

	private sealed class TestLogger<T> : ILogger<T>
	{
		public List<LogEntry> Entries { get; } = [];

		public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(
			LogLevel logLevel,
			EventId eventId,
			TState state,
			Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
		}

		private sealed class NullScope : IDisposable
		{
			public static NullScope Instance { get; } = new();

			public void Dispose()
			{
			}
		}
	}

	private sealed record LogEntry(LogLevel Level, string Message);
}
