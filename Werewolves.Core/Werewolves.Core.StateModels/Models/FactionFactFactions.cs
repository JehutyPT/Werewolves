using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

internal static class FactionFactFactions
{
    internal static IReadOnlyList<Faction> All { get; } = Array.AsReadOnly(
        new[]
        {
            Faction.Villager,
            Faction.Werewolf,
            Faction.WhiteWerewolf,
            Faction.Piper,
            Faction.CrossFactionLovers,
            Faction.PrejudicedManipulator
        });
}
