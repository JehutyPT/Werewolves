using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public sealed record TerminalLobbyGenerationScenario(
	SimulationScenario Scenario,
	SimulationCompatibilityIdentity Identity,
	bool IsAlreadyDecided);

public static class TerminalLobbyScenarioCatalog
{
	public static IReadOnlyList<TerminalLobbyGenerationScenario> EnumerateCurrentProfile()
	{
		var entries = new List<TerminalLobbyGenerationScenario>();
		for (var playerCount = 5; playerCount <= 30; playerCount++)
		{
			foreach (var seerCount in new[] { 0, 1 })
			{
				foreach (var wildChildCount in new[] { 0, 1 })
				{
					for (var werewolfCount = 1; werewolfCount <= playerCount; werewolfCount++)
					{
						var villagerCount = playerCount
							- werewolfCount
							- seerCount
							- wildChildCount;
						if (villagerCount < 0 || villagerCount + seerCount == 0)
						{
							continue;
						}

						var roles = Enumerable.Repeat(
							MainRoleType.SimpleWerewolf,
							werewolfCount)
							.Concat(Enumerable.Repeat(MainRoleType.Seer, seerCount))
							.Concat(Enumerable.Repeat(MainRoleType.WildChild, wildChildCount))
							.Concat(Enumerable.Repeat(MainRoleType.SimpleVillager, villagerCount));
						var scenario = new SimulationScenario(playerCount, roles);
						var classification = SimulationScenarioClassifier.Classify(scenario);
						if (!classification.RulesValidity.IsValid
							|| classification.AppSupport is not { IsSupported: true }
							|| classification.SimulatorSupport is not { IsSupported: true } support
							|| classification.AlreadyDecided is null)
						{
							continue;
						}

						entries.Add(new TerminalLobbyGenerationScenario(
							scenario,
							new SimulationCompatibilityIdentity(
								scenario.ToCanonical(),
								support.Profile.Identity),
							classification.AlreadyDecided.IsAlreadyDecided));
					}
				}
			}
		}

		return Array.AsReadOnly(entries
			.OrderBy(entry => entry.Identity.ToString(), StringComparer.Ordinal)
			.ToArray());
	}
}
