using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

internal sealed record PossibleGameResultInventory(
	Faction[] Factions,
	GameResult[] GameResults)
{
	public static bool TryCreate(
		SimulationScenario scenario,
		SimulatorProfile profile,
		out PossibleGameResultInventory inventory)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(profile);
		var factions = new HashSet<Faction>();
		foreach (var entry in scenario.ToCanonical().RoleComposition.Entries)
		{
			if (!profile.TryGetBeneficiaryFaction(entry.Role, out var faction)
				|| !Enum.IsDefined(faction))
			{
				inventory = null!;
				return false;
			}

			factions.Add(faction);
		}

		var orderedFactions = factions.Order().ToArray();
		inventory = new PossibleGameResultInventory(
			orderedFactions,
			profile.CreatePossibleGameResults(orderedFactions));
		return true;
	}
}
