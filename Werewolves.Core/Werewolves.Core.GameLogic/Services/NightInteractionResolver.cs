using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Services;

internal static class NightInteractionResolver
{
	/// <summary>
	/// Analyzes the night's logs to determine who lives, dies, or changes status.
	/// Applies consequences directly to the GameSession.
	/// </summary>
	public static void ResolveNightPhase(GameSession session)
	{
		// 0. Optimization: Pre-fetch actions to avoid querying the log N times per player.
		// Maps PlayerId -> Set of ActionTypes targeting them.
		var nightActionMap = BuildNightActionMap(session);

		var targetedPlayers = session.GetPlayers().IntersectBy(nightActionMap.Keys, player => player.Id);

		foreach (var player in targetedPlayers)
		{
			EliminationReason? eliminationReason = null;

			var incomingActions = nightActionMap[player.Id];

			// 1. Resolve Witch Save (Absolute Defense)
			// If the Witch saved this player, they are safe from wolves.
			bool isProtectedFromWolves = incomingActions.Contains(NightActionType.WitchSave);

			// 2. Resolve Defender Protection

			if (incomingActions.Contains(NightActionType.DefenderProtect))
			{
				// RULE: Little Girl cannot be protected by the Defender.
				if (player.State.MainRole == MainRoleType.LittleGirl)
				{
					// Protection fails silently.
					isProtectedFromWolves = false;
				}
				else
				{
					isProtectedFromWolves = true;
				}
			}

			// 3. Resolve Wolf Faction Actions (Infection & Attacks)
			// These are grouped because Defender blocks all of them.
			if (!isProtectedFromWolves)
			{
				// RULE: Elder with extra life resists infection or wolf attacks automatically, but loses its extra life.
				if (player.State.MainRole == MainRoleType.Elder && !player.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost))
				{
					session.ApplyStatusEffect(StatusEffectTypes.ElderProtectionLost, player.Id);
				}
				// A. Check for Infection (Prioritized over death)
				else if (incomingActions.Contains(NightActionType.AccursedWolfFatherInfection))
				{
					
					session.ApplyStatusEffect(StatusEffectTypes.LycanthropyInfection, player.Id);
					// If infected (or successfully resisted infection), the physical attack is negated.
					// We stop processing wolf actions here.

				}
				// B. Check for Physical Attacks (Simple, BigBad, White)
				else if (incomingActions.Contains(NightActionType.WerewolfVictimSelection) ||
				         incomingActions.Contains(NightActionType.BigBadWolfVictimSelection) ||
				         incomingActions.Contains(NightActionType.WhiteWerewolfVictimSelection))
				{
					eliminationReason = EliminationReason.WerewolfAttack;
				}
			}

			// 4. Resolve Unstoppable Attacks
			// These ignore Defender protection.

			// Witch Death Potion
			if (incomingActions.Contains(NightActionType.WitchKill))
			{
				eliminationReason = EliminationReason.WitchKill;
			}

			// Knight with Rusty Sword (if applicable as a night trigger)
			if (incomingActions.Contains(NightActionType.RustySword))
			{
				eliminationReason = EliminationReason.RustySword;
			}

			// --- Apply Elimination if determined ---
			if (eliminationReason.HasValue)
			{
				session.EliminatePlayer(player.Id, eliminationReason.Value);
			}
		}
	}

	/// <summary>
	/// Helper to flatten the log queries into a dictionary for O(1) lookup during the loop.
	/// </summary>
	private static Dictionary<Guid, HashSet<NightActionType>> BuildNightActionMap(GameSession session)
	{
		var map = new Dictionary<Guid, HashSet<NightActionType>>();

		// Helper to populate the map
		void AddActions(NightActionType type)
		{
			var targets = session.GetPlayersTargetedLastNight(type, NumberRangeConstraint.SingleOptional);
			foreach (var target in targets)
			{
				if (!map.ContainsKey(target.Id))
				{
					map[target.Id] = new HashSet<NightActionType>();
				}
				map[target.Id].Add(type);
			}
		}

		// Register all relevant action types
		AddActions(NightActionType.WerewolfVictimSelection);
		AddActions(NightActionType.BigBadWolfVictimSelection);
		AddActions(NightActionType.WhiteWerewolfVictimSelection);
		AddActions(NightActionType.AccursedWolfFatherInfection);
		AddActions(NightActionType.DefenderProtect);
		AddActions(NightActionType.WitchSave);
		AddActions(NightActionType.WitchKill);
		AddActions(NightActionType.RustySword);

		return map;
	}
}