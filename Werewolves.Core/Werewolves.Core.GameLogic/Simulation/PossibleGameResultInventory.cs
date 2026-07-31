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
		var canonicalScenario = scenario.ToCanonical();
		var factions = new HashSet<Faction>();
		foreach (var entry in canonicalScenario.RoleComposition.Entries)
		{
			if (!profile.TryGetBeneficiaryFaction(entry.Role, out var faction)
				|| !Enum.IsDefined(faction))
			{
				inventory = null!;
				return false;
			}

			factions.Add(faction);
		}
		if (canonicalScenario.RoleComposition.Entries.Any(entry =>
			    entry.Role == MainRoleType.Cupid))
		{
			factions.Add(Faction.CrossFactionLovers);
		}

		var orderedFactions = factions.Order().ToArray();
		inventory = new PossibleGameResultInventory(
			orderedFactions,
			profile.CreatePossibleGameResults(orderedFactions));
		return true;
	}
}
