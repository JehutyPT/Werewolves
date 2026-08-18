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
		ArgumentNullException.ThrowIfNull(capability);
		return TryCreate(
			scenario,
			role => capability.TryGetBeneficiaryFaction(role, out var faction)
				? faction
				: null,
			capability.CreatePossibleGameResults,
			out inventory);
	}

	public static bool TryCreate(
		SimulationScenario scenario,
		SimulatorProfile profile,
		out PossibleGameResultInventory inventory)
	{
		ArgumentNullException.ThrowIfNull(profile);
		return TryCreate(
			scenario,
			role => profile.TryGetBeneficiaryFaction(role, out var faction)
				? faction
				: null,
			profile.CreatePossibleGameResults,
			out inventory);
	}

	private static bool TryCreate(
		SimulationScenario scenario,
		Func<MainRoleType, Faction?> getBeneficiaryFaction,
		Func<IEnumerable<Faction>, GameResult[]> createPossibleGameResults,
		out PossibleGameResultInventory inventory)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		var factions = new HashSet<Faction>();
		foreach (var role in scenario.RoleCompositionCards.Distinct())
		{
			if (getBeneficiaryFaction(role) is not { } faction
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
			createPossibleGameResults(orderedFactions));
		return true;
	}
}
