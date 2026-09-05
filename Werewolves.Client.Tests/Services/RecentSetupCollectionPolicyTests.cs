using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Enums;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public sealed class RecentSetupCollectionPolicyTests
{
	private static readonly DateTimeOffset CapturedAt = new(
		2026,
		8,
		23,
		10,
		0,
		0,
		TimeSpan.Zero);

	[Fact]
	public void Capture_PreservesOrderedNamesAndNormalizesRoleCountsAtTheExplicitInstant()
	{
		var roleCounts = new Dictionary<MainRoleType, int>
		{
			[MainRoleType.SimpleVillager] = 2,
			[MainRoleType.Witch] = 0,
			[MainRoleType.SimpleWerewolf] = 1,
			[MainRoleType.Seer] = -1
		};

		var setups = RecentSetupCollectionPolicy.Capture(
			[],
			["Ana", "ana", "Bruno"],
			roleCounts,
			CapturedAt);

		var setup = setups.Should().ContainSingle().Subject;
		setup.PlayerNames.Should().Equal("Ana", "ana", "Bruno");
		setup.RoleCounts.Should().Equal(roleCounts
			.Where(entry => entry.Value > 0)
			.OrderBy(entry => entry.Key));
		setup.CapturedAtUtc.Should().Be(CapturedAt);
	}

	[Fact]
	public void Capture_EquivalentContentMovesToFrontWithAFreshInstant()
	{
		var initialInstant = CapturedAt;
		var normalizedCounts = RoleCounts();
		var newest = new RecentSetup(["Newest"], normalizedCounts, initialInstant.AddMinutes(2));
		var matching = new RecentSetup(
			["Ana", "Bruno"],
			normalizedCounts,
			initialInstant.AddMinutes(1));
		var oldest = new RecentSetup(["Oldest"], normalizedCounts, initialInstant);
		var recapturedAt = initialInstant.AddMinutes(3);

		var setups = RecentSetupCollectionPolicy.Capture(
			[newest, matching, oldest],
			["Ana", "Bruno"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleVillager] = 2,
				[MainRoleType.Witch] = 0,
				[MainRoleType.SimpleWerewolf] = 1
			},
			recapturedAt);

		setups.Select(setup => string.Join("|", setup.PlayerNames)).Should().Equal(
			"Ana|Bruno",
			"Newest",
			"Oldest");
		setups.Select(setup => setup.CapturedAtUtc).Should().Equal(
			recapturedAt,
			newest.CapturedAtUtc,
			oldest.CapturedAtUtc);
	}

	[Fact]
	public void Capture_RoleCountsOrdinalCasingSpellingAndSeatingOrderRemainDistinct()
	{
		IReadOnlyList<RecentSetup> setups = [];

		setups = RecentSetupCollectionPolicy.Capture(
			setups,
			["Ana", "Bruno"],
			RoleCounts(),
			CapturedAt);
		setups = RecentSetupCollectionPolicy.Capture(
			setups,
			["Ana", "Bruno"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleWerewolf] = 1,
				[MainRoleType.SimpleVillager] = 1
			},
			CapturedAt.AddMinutes(1));
		setups = RecentSetupCollectionPolicy.Capture(
			setups,
			["ana", "Bruno"],
			RoleCounts(),
			CapturedAt.AddMinutes(2));
		setups = RecentSetupCollectionPolicy.Capture(
			setups,
			["Anna", "Bruno"],
			RoleCounts(),
			CapturedAt.AddMinutes(3));
		setups = RecentSetupCollectionPolicy.Capture(
			setups,
			["Bruno", "Ana"],
			RoleCounts(),
			CapturedAt.AddMinutes(4));

		setups.Select(setup => string.Join("|", setup.PlayerNames)).Should().Equal(
			"Bruno|Ana",
			"Anna|Bruno",
			"ana|Bruno",
			"Ana|Bruno",
			"Ana|Bruno");
		setups[^2].RoleCounts[MainRoleType.SimpleVillager].Should().Be(1);
		setups[^1].RoleCounts[MainRoleType.SimpleVillager].Should().Be(2);
	}

	[Fact]
	public void Capture_EleventhDistinctSetupEvictsExactlyTheOldest()
	{
		IReadOnlyList<RecentSetup> setups = [];

		for (var index = 0; index < 11; index++)
		{
			setups = RecentSetupCollectionPolicy.Capture(
				setups,
				[$"Player {index}"],
				RoleCounts(),
				CapturedAt.AddMinutes(index));
		}

		setups.Should().HaveCount(10);
		setups.Select(setup => setup.PlayerNames.Single()).Should().Equal(
			Enumerable.Range(1, 10)
				.Reverse()
				.Select(index => $"Player {index}"));
	}

	[Fact]
	public void Delete_IndependentlyReconstructedCompleteMatchRemovesOnlyThatSetup()
	{
		var matching = new RecentSetup(["Ana", "Bruno"], RoleCounts(), CapturedAt);
		var retained = new RecentSetup(
			["Carla", "Diogo"],
			RoleCounts(),
			CapturedAt.AddMinutes(1));
		var reconstructed = new RecentSetup(
			["Ana", "Bruno"],
			new Dictionary<MainRoleType, int>
			{
				[MainRoleType.SimpleVillager] = 2,
				[MainRoleType.SimpleWerewolf] = 1
			},
			CapturedAt);

		var setups = RecentSetupCollectionPolicy.Delete(
			[matching, retained],
			reconstructed);

		setups.Should().ContainSingle();
		setups[0].PlayerNames.Should().Equal("Carla", "Diogo");
		setups[0].CapturedAtUtc.Should().Be(retained.CapturedAtUtc);
	}

	[Fact]
	public void Delete_PartialAndAbsentMatchesAreNoOps()
	{
		var matching = new RecentSetup(["Ana", "Bruno"], RoleCounts(), CapturedAt);
		var retained = new RecentSetup(
			["Carla", "Diogo"],
			RoleCounts(),
			CapturedAt.AddMinutes(1));
		IReadOnlyList<RecentSetup> setups = [matching, retained];

		setups = RecentSetupCollectionPolicy.Delete(
			setups,
			new RecentSetup(
				matching.PlayerNames,
				matching.RoleCounts,
				CapturedAt.AddHours(1)));
		setups = RecentSetupCollectionPolicy.Delete(
			setups,
			new RecentSetup(["Different"], matching.RoleCounts, CapturedAt));
		setups = RecentSetupCollectionPolicy.Delete(
			setups,
			new RecentSetup(
				matching.PlayerNames,
				new Dictionary<MainRoleType, int>
				{
					[MainRoleType.SimpleWerewolf] = 1
				},
				CapturedAt));
		setups = RecentSetupCollectionPolicy.Delete(
			setups,
			new RecentSetup(["Absent"], matching.RoleCounts, CapturedAt.AddHours(2)));

		setups.Select(setup => string.Join("|", setup.PlayerNames)).Should().Equal(
			"Ana|Bruno",
			"Carla|Diogo");
		setups.Select(setup => setup.CapturedAtUtc).Should().Equal(
			matching.CapturedAtUtc,
			retained.CapturedAtUtc);
	}

	private static IReadOnlyDictionary<MainRoleType, int> RoleCounts() =>
		new Dictionary<MainRoleType, int>
		{
			[MainRoleType.SimpleWerewolf] = 1,
			[MainRoleType.SimpleVillager] = 2
		};
}
