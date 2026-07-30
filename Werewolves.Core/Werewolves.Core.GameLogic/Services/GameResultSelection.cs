using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Services;

internal static class GameResultSelection
{
	public static GameResult? Select(
		IEnumerable<Faction> satisfiedFactions,
		bool allPlayersEliminated)
	{
		ArgumentNullException.ThrowIfNull(satisfiedFactions);
		var factions = satisfiedFactions.Distinct().Order().ToArray();
		if (factions.Any(faction => !Enum.IsDefined(faction)))
		{
			throw new ArgumentOutOfRangeException(nameof(satisfiedFactions));
		}

		return factions.Length switch
		{
			0 when allPlayersEliminated => new NoWinnerGameResult(),
			0 => null,
			1 => new SingleFactionGameResult(factions[0]),
			_ => new SharedVictoryGameResult(factions)
		};
	}
}
