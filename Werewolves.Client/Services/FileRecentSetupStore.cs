using System.Text.Json;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Client.Services;

public sealed class FileRecentSetupStore : IRecentSetupStore
{
	public const string StoreFileName = "recent-setups.json";
	private const int CurrentSchemaVersion = 1;
	private const int MaximumSetupCount = 10;
	private const string TemporaryFileSearchPattern = StoreFileName + ".*.tmp";
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly string _storeFilePath;
	private readonly TimeProvider _timeProvider;

	public FileRecentSetupStore(
		string appDataDirectory,
		TimeProvider? timeProvider = null)
	{
		if (string.IsNullOrWhiteSpace(appDataDirectory))
		{
			throw new ArgumentException(
				"App data directory must be provided.",
				nameof(appDataDirectory));
		}

		AppDataDirectory = appDataDirectory;
		_storeFilePath = Path.Combine(appDataDirectory, StoreFileName);
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	public string AppDataDirectory { get; }

	public static FileRecentSetupStore CreateDefault(
		TimeProvider? timeProvider = null) =>
		new(DefaultAppDataDirectory.GetPath(), timeProvider);

	public IReadOnlyList<RecentSetup> Load()
	{
		try
		{
			return ReadStrict();
		}
		catch (InvalidDataException)
		{
			return [];
		}
		catch (IOException)
		{
			return [];
		}
		catch (UnauthorizedAccessException)
		{
			return [];
		}
	}

	public void Capture(
		IReadOnlyList<string> playerNames,
		IReadOnlyDictionary<MainRoleType, int> roleCounts)
	{
		ArgumentNullException.ThrowIfNull(playerNames);
		ArgumentNullException.ThrowIfNull(roleCounts);

		var normalizedRoleCounts = roleCounts
			.Where(entry => entry.Value > 0)
			.OrderBy(entry => entry.Key)
			.ToDictionary(entry => entry.Key, entry => entry.Value);
		List<RecentSetup> setups;
		try
		{
			setups = ReadStrict().ToList();
		}
		catch (InvalidDataException)
		{
			setups = [];
		}
		var existingIndex = setups.FindIndex(setup =>
			HasSameContent(setup, playerNames, normalizedRoleCounts));
		if (existingIndex >= 0)
		{
			setups.RemoveAt(existingIndex);
		}

		setups.Insert(
			0,
			new RecentSetup(
				playerNames,
				normalizedRoleCounts,
				_timeProvider.GetUtcNow()));
		if (setups.Count > MaximumSetupCount)
		{
			setups.RemoveAt(MaximumSetupCount);
		}

		Write(setups);
	}

	public void Delete(RecentSetup setup)
	{
		ArgumentNullException.ThrowIfNull(setup);
		var setups = ReadStrict().ToList();
		var index = setups.FindIndex(candidate =>
			candidate.CapturedAtUtc == setup.CapturedAtUtc &&
			HasSameContent(candidate, setup.PlayerNames, setup.RoleCounts));
		if (index < 0)
		{
			return;
		}

		setups.RemoveAt(index);
		Write(setups);
	}

	private IReadOnlyList<RecentSetup> ReadStrict()
	{
		if (!File.Exists(_storeFilePath))
		{
			return [];
		}

		RecentSetupsEnvelopeDto? envelope;
		try
		{
			envelope = JsonSerializer.Deserialize<RecentSetupsEnvelopeDto>(
				File.ReadAllText(_storeFilePath),
				JsonOptions);
		}
		catch (JsonException exception)
		{
			throw new InvalidDataException("The recent setup payload is malformed.", exception);
		}

		if (envelope is not
			{
				SchemaVersion: CurrentSchemaVersion,
				Setups: not null
			} ||
			envelope.Setups.Count > MaximumSetupCount)
		{
			throw new InvalidDataException("The recent setup payload is malformed.");
		}

		var setups = new List<RecentSetup>(envelope.Setups.Count);
		foreach (var setup in envelope.Setups)
		{
			if (setup is null ||
				setup.PlayerNames is null ||
				setup.RoleCounts is null ||
				setup.CapturedAtUtc is null ||
				setup.PlayerNames.Count == 0 ||
				setup.PlayerNames.Any(name =>
					string.IsNullOrWhiteSpace(name) ||
					!string.Equals(name, name.Trim(), StringComparison.Ordinal)) ||
				setup.PlayerNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
					setup.PlayerNames.Count ||
				setup.RoleCounts.Count == 0 ||
				setup.RoleCounts.Any(entry =>
					entry.Value <= 0 ||
					!Enum.IsDefined(entry.Key) ||
					entry.Key == MainRoleType.Gypsy))
			{
				throw new InvalidDataException("The recent setup payload is malformed.");
			}

			setups.Add(new RecentSetup(
				setup.PlayerNames,
				setup.RoleCounts,
				setup.CapturedAtUtc.Value));
		}

		return setups;
	}

	private void Write(IReadOnlyList<RecentSetup> setups)
	{
		Directory.CreateDirectory(AppDataDirectory);
		DeleteTemporaryWriteArtifacts();
		var temporaryFilePath = Path.Combine(
			AppDataDirectory,
			$"{StoreFileName}.{Guid.NewGuid():N}.tmp");
		var replacedStore = false;
		try
		{
			var envelope = new RecentSetupsEnvelopeDto(
				CurrentSchemaVersion,
				setups.Select(setup => new RecentSetupDto(
					setup.PlayerNames,
					setup.RoleCounts,
					setup.CapturedAtUtc)).ToArray());
			File.WriteAllText(
				temporaryFilePath,
				JsonSerializer.Serialize(envelope, JsonOptions));
			ReplaceStoreFile(temporaryFilePath);
			replacedStore = true;
		}
		finally
		{
			if (!replacedStore)
			{
				TryDelete(temporaryFilePath);
			}
		}

		DeleteTemporaryWriteArtifacts();
	}

	private void ReplaceStoreFile(string temporaryFilePath)
	{
		if (!File.Exists(_storeFilePath))
		{
			File.Move(temporaryFilePath, _storeFilePath);
			return;
		}

		try
		{
			File.Replace(temporaryFilePath, _storeFilePath, null);
		}
		catch (PlatformNotSupportedException)
		{
			File.Move(temporaryFilePath, _storeFilePath, overwrite: true);
		}
		catch (NotSupportedException)
		{
			File.Move(temporaryFilePath, _storeFilePath, overwrite: true);
		}
		catch (IOException) when (!File.Exists(_storeFilePath))
		{
			File.Move(temporaryFilePath, _storeFilePath);
		}
	}

	private void DeleteTemporaryWriteArtifacts()
	{
		if (!Directory.Exists(AppDataDirectory))
		{
			return;
		}

		foreach (var path in Directory.GetFiles(
			AppDataDirectory,
			TemporaryFileSearchPattern))
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

	private static bool HasSameContent(
		RecentSetup setup,
		IReadOnlyList<string> playerNames,
		IReadOnlyDictionary<MainRoleType, int> roleCounts) =>
		setup.PlayerNames.Count == playerNames.Count &&
		setup.PlayerNames.Zip(playerNames).All(pair =>
			string.Equals(pair.First, pair.Second, StringComparison.Ordinal)) &&
		setup.RoleCounts.Count == roleCounts.Count &&
		setup.RoleCounts.All(entry =>
			roleCounts.TryGetValue(entry.Key, out var count) &&
			count == entry.Value);

	private sealed record RecentSetupsEnvelopeDto(
		int SchemaVersion,
		IReadOnlyList<RecentSetupDto> Setups);

	private sealed record RecentSetupDto(
		IReadOnlyList<string> PlayerNames,
		IReadOnlyDictionary<MainRoleType, int> RoleCounts,
		DateTimeOffset? CapturedAtUtc);
}
