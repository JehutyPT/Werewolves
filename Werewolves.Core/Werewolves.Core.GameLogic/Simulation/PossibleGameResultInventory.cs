using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

internal sealed record PossibleGameResultInventory(
	Faction[] Factions,
	GameResult[] GameResults)
{
	public static bool TryCreate(
		SimulationScenario scenario,
		SimulatorCapability capability,
		out PossibleGameResultInventory inventory)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(capability);
		var factions = new HashSet<Faction>();
		foreach (var role in scenario.RoleCompositionCards.Distinct())
		{
			if (!capability.TryGetBeneficiaryFaction(role, out var faction)
				|| !Enum.IsDefined(faction))
			{
				inventory = null!;
				return false;
			}

			factions.Add(faction);
		}
		if (scenario.RoleCompositionCards.Contains(MainRoleType.Cupid))
		{
			factions.Add(Faction.CrossFactionLovers);
		}
		if (scenario.RoleCompositionCards.Contains(MainRoleType.Angel))
		{
			factions.Add(Faction.Angel);
		}

		var orderedFactions = factions.Order().ToArray();
		inventory = new PossibleGameResultInventory(
			orderedFactions,
			capability.CreatePossibleGameResults(orderedFactions));
		return true;
	}
}
