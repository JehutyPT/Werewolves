using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.GameLogic.Services;

internal static class NightInteractionResolver
{
	private static readonly NightActionType[] DawnResolutionActionTypes =
	[
		NightActionType.WerewolfVictimSelection,
		NightActionType.BigBadWolfVictimSelection,
		NightActionType.WhiteWerewolfVictimSelection,
		NightActionType.AccursedWolfFatherInfection,
		NightActionType.DefenderProtect,
		NightActionType.WitchSave,
		NightActionType.WitchKill,
		NightActionType.RustySword
	];

	/// <summary>
	/// Analyzes the night's logs to determine who lives, dies, or changes status.
	/// Applies consequences directly to the GameSession.
	/// </summary>
	public static void ResolveNightPhase(GameSession session)
	{
		var nightActionMap = GameSessionQueries.GetNightActionMap(session, DawnResolutionActionTypes);

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

			// --- Record the consequence so public Role Reveal can precede elimination ---
			if (eliminationReason.HasValue)
			{
				session.DetermineDawnVictim(player.Id, eliminationReason.Value);
			}
		}
	}
}
