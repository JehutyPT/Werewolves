using Werewolves.Client.Resources;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Client.Tests.Components;

internal static class FactionPresentationTestData
{
	private static readonly IReadOnlyDictionary<Faction, Func<string>> ResourceNames =
		new Dictionary<Faction, Func<string>>
		{
			[Faction.Villager] = () => ClientStrings.LobbyEvaluation_FactionVillager,
			[Faction.Werewolf] = () => ClientStrings.LobbyEvaluation_FactionWerewolf,
			[Faction.WhiteWerewolf] = () =>
				ClientStrings.LobbyEvaluation_FactionWhiteWerewolf,
			[Faction.Piper] = () => ClientStrings.LobbyEvaluation_FactionPiper,
			[Faction.CrossFactionLovers] = () =>
				ClientStrings.LobbyEvaluation_FactionCrossFactionLovers,
			[Faction.Angel] = () => ClientStrings.LobbyEvaluation_FactionAngel,
			[Faction.PrejudicedManipulator] = () =>
				ClientStrings.LobbyEvaluation_FactionPrejudicedManipulator
		};

	public static IEnumerable<Faction> Factions => ResourceNames.Keys;

	public static string Name(Faction faction) => ResourceNames[faction]();
}
