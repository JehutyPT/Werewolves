using System.Collections.ObjectModel;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Client.Services;

public interface IRecentSetupStore
{
	IReadOnlyList<RecentSetup> Load();
	void Capture(
		IReadOnlyList<string> playerNames,
		IReadOnlyDictionary<MainRoleType, int> roleCounts);
	void Delete(RecentSetup setup);
}

public sealed class DisabledRecentSetupStore : IRecentSetupStore
{
	public static DisabledRecentSetupStore Instance { get; } = new();

	private DisabledRecentSetupStore()
	{
	}

	public IReadOnlyList<RecentSetup> Load() => [];

	public void Capture(
		IReadOnlyList<string> playerNames,
		IReadOnlyDictionary<MainRoleType, int> roleCounts)
	{
	}

	public void Delete(RecentSetup setup)
	{
	}
}

public sealed class RecentSetup
{
	public RecentSetup(
		IReadOnlyList<string> playerNames,
		IReadOnlyDictionary<MainRoleType, int> roleCounts,
		DateTimeOffset capturedAtUtc)
	{
		PlayerNames = Array.AsReadOnly(playerNames.ToArray());
		RoleCounts = new ReadOnlyDictionary<MainRoleType, int>(
			roleCounts.ToDictionary(entry => entry.Key, entry => entry.Value));
		CapturedAtUtc = capturedAtUtc;
	}

	public IReadOnlyList<string> PlayerNames { get; }
	public IReadOnlyDictionary<MainRoleType, int> RoleCounts { get; }
	public DateTimeOffset CapturedAtUtc { get; }
}

public sealed class InMemoryRecentSetupStore(
	TimeProvider? timeProvider = null) : IRecentSetupStore
{
	private const int MaximumSetupCount = 10;
	private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
	private readonly List<RecentSetup> _setups = [];

	public IReadOnlyList<RecentSetup> Load() => _setups.ToArray();

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
		var existingIndex = _setups.FindIndex(setup =>
			HasSameContent(setup, playerNames, normalizedRoleCounts));
		if (existingIndex >= 0)
		{
			_setups.RemoveAt(existingIndex);
		}

		_setups.Insert(
			0,
			new RecentSetup(
				playerNames,
				normalizedRoleCounts,
				_timeProvider.GetUtcNow()));
		if (_setups.Count > MaximumSetupCount)
		{
			_setups.RemoveAt(MaximumSetupCount);
		}
	}

	public void Delete(RecentSetup setup)
	{
		ArgumentNullException.ThrowIfNull(setup);
		var index = _setups.FindIndex(candidate =>
			candidate.CapturedAtUtc == setup.CapturedAtUtc &&
			HasSameContent(candidate, setup.PlayerNames, setup.RoleCounts));
		if (index >= 0)
		{
			_setups.RemoveAt(index);
		}
	}

	private static bool HasSameContent(
		RecentSetup setup,
		IReadOnlyList<string> playerNames,
		IReadOnlyDictionary<MainRoleType, int> roleCounts) =>
		setup.PlayerNames.Count == playerNames.Count &&
		setup.PlayerNames
			.Zip(playerNames)
			.All(pair => string.Equals(
				pair.First,
				pair.Second,
				StringComparison.Ordinal)) &&
		setup.RoleCounts.Count == roleCounts.Count &&
		setup.RoleCounts.All(entry =>
			roleCounts.TryGetValue(entry.Key, out var count) &&
			count == entry.Value);
}
