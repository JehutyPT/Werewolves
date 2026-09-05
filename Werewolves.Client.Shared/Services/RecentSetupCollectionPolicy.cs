using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Client.Services;

public static class RecentSetupCollectionPolicy
{
	public const int MaximumSetupCount = 10;

	public static IReadOnlyList<RecentSetup> Capture(
		IReadOnlyList<RecentSetup> setups,
		IReadOnlyList<string> playerNames,
		IReadOnlyDictionary<MainRoleType, int> roleCounts,
		DateTimeOffset capturedAtUtc)
	{
		ArgumentNullException.ThrowIfNull(setups);
		ArgumentNullException.ThrowIfNull(playerNames);
		ArgumentNullException.ThrowIfNull(roleCounts);

		var normalizedRoleCounts = roleCounts
			.Where(entry => entry.Value > 0)
			.OrderBy(entry => entry.Key)
			.ToDictionary(entry => entry.Key, entry => entry.Value);
		var updatedSetups = setups.ToList();
		var existingIndex = updatedSetups.FindIndex(setup =>
			HasSameContent(setup, playerNames, normalizedRoleCounts));
		if (existingIndex >= 0)
		{
			updatedSetups.RemoveAt(existingIndex);
		}

		updatedSetups.Insert(
			0,
			new RecentSetup(playerNames, normalizedRoleCounts, capturedAtUtc));
		if (updatedSetups.Count > MaximumSetupCount)
		{
			updatedSetups.RemoveAt(MaximumSetupCount);
		}

		return updatedSetups.ToArray();
	}

	public static IReadOnlyList<RecentSetup> Delete(
		IReadOnlyList<RecentSetup> setups,
		RecentSetup setup)
	{
		ArgumentNullException.ThrowIfNull(setups);
		ArgumentNullException.ThrowIfNull(setup);

		var updatedSetups = setups.ToList();
		var index = updatedSetups.FindIndex(candidate =>
			candidate.CapturedAtUtc == setup.CapturedAtUtc &&
			HasSameContent(candidate, setup.PlayerNames, setup.RoleCounts));
		if (index >= 0)
		{
			updatedSetups.RemoveAt(index);
		}

		return updatedSetups.ToArray();
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
}
