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
	private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
	private IReadOnlyList<RecentSetup> _setups = [];

	public IReadOnlyList<RecentSetup> Load() => _setups.ToArray();

	public void Capture(
		IReadOnlyList<string> playerNames,
		IReadOnlyDictionary<MainRoleType, int> roleCounts)
	{
		ArgumentNullException.ThrowIfNull(playerNames);
		ArgumentNullException.ThrowIfNull(roleCounts);

		_setups = RecentSetupCollectionPolicy.Capture(
			_setups,
			playerNames,
			roleCounts,
			_timeProvider.GetUtcNow());
	}

	public void Delete(RecentSetup setup)
	{
		ArgumentNullException.ThrowIfNull(setup);
		_setups = RecentSetupCollectionPolicy.Delete(_setups, setup);
	}
}
