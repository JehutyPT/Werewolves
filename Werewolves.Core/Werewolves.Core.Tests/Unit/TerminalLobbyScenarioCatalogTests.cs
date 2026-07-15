using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public sealed class TerminalLobbyScenarioCatalogTests
{
	[Fact]
	public void EnumerateCurrentProfile_ReturnsCompleteSupportedIdentitySet()
	{
		var entries = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile();
		var expectedIdentities = IndependentlyEnumerateStructuralIdentities();

		entries.Should().HaveCount(expectedIdentities.Count);
		entries.Select(entry => entry.Identity).Should().OnlyHaveUniqueItems();
		entries.Select(entry => entry.Identity.ToString()).Should().BeInAscendingOrder(
			StringComparer.Ordinal);
		entries.Select(entry => entry.Identity.ToString()).Should().Equal(expectedIdentities);
		entries.Count(entry => entry.IsAlreadyDecided).Should().Be(832);
		entries.Count(entry => !entry.IsAlreadyDecided).Should().Be(832);
		entries.GroupBy(entry => entry.Scenario.PlayerCount)
			.OrderBy(group => group.Key)
			.Select(group => (PlayerCount: group.Key, Count: group.Count()))
			.Should().Equal(Enumerable.Range(5, 26)
				.Select(playerCount => (playerCount, 4 * playerCount - 6)));

		foreach (var entry in entries)
		{
			var classification = SimulationScenarioClassifier.Classify(entry.Scenario);
			classification.RulesValidity.IsValid.Should().BeTrue();
			classification.AppSupport.Should().Match<AppSupportResult>(value => value.IsSupported);
			classification.SimulatorSupport.Should().Match<SimulatorSupportResult>(value => value.IsSupported);
			entry.Identity.Should().Be(new SimulationCompatibilityIdentity(
				entry.Scenario.ToCanonical(),
				SimulatorProfile.Active.Identity));
			entry.IsAlreadyDecided.Should().Be(
				classification.AlreadyDecided!.IsAlreadyDecided);
			if (entry.IsAlreadyDecided)
			{
				classification.Cacheability.Should().BeNull();
			}
			else
			{
				classification.Cacheability.Should().NotBeNull();
				classification.Cacheability!.CompatibilityIdentity.Should().Be(entry.Identity);
			}
		}

		var actualIdentities = entries.Select(entry => entry.Identity.ToString()).ToHashSet(
			StringComparer.Ordinal);
		ExcludedStructuralScenarios()
			.Select(IdentityFor)
			.Should().OnlyContain(identity => !actualIdentities.Contains(identity));
	}

	private static IReadOnlyList<string> IndependentlyEnumerateStructuralIdentities()
	{
		var identities = new List<string>();
		for (var playerCount = 5; playerCount <= 30; playerCount++)
		{
			foreach (var seerCount in new[] { 0, 1 })
			{
				foreach (var wildChildCount in new[] { 0, 1 })
				{
					var maximumWerewolves = playerCount
						- seerCount
						- wildChildCount
						- (seerCount == 0 ? 1 : 0);
					for (var werewolfCount = 1; werewolfCount <= maximumWerewolves; werewolfCount++)
					{
						var villagerCount = playerCount
							- werewolfCount
							- seerCount
							- wildChildCount;
						identities.Add(IdentityFor(Scenario(
							playerCount,
							werewolfCount,
							seerCount,
							wildChildCount,
							villagerCount)));
					}
				}
			}
		}

		return identities.OrderBy(value => value, StringComparer.Ordinal).ToArray();
	}

	private static IEnumerable<SimulationScenario> ExcludedStructuralScenarios()
	{
		yield return Scenario(4, werewolves: 1, seers: 0, wildChildren: 0, villagers: 3);
		yield return Scenario(31, werewolves: 1, seers: 0, wildChildren: 0, villagers: 30);
		yield return Scenario(5, werewolves: 0, seers: 0, wildChildren: 0, villagers: 5);
		yield return Scenario(5, werewolves: 5, seers: 0, wildChildren: 0, villagers: 0);
		yield return Scenario(5, werewolves: 1, seers: 2, wildChildren: 0, villagers: 2);
		yield return Scenario(5, werewolves: 1, seers: 0, wildChildren: 2, villagers: 2);
	}

	private static string IdentityFor(SimulationScenario scenario) =>
		new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorProfile.Active.Identity).ToString();

	private static SimulationScenario Scenario(
		int playerCount,
		int werewolves,
		int seers,
		int wildChildren,
		int villagers) => new(
		playerCount,
		Enumerable.Repeat(MainRoleType.SimpleWerewolf, werewolves)
			.Concat(Enumerable.Repeat(MainRoleType.Seer, seers))
			.Concat(Enumerable.Repeat(MainRoleType.WildChild, wildChildren))
			.Concat(Enumerable.Repeat(MainRoleType.SimpleVillager, villagers)));
}
