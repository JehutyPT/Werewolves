using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Services;

internal static class NightInteractionResolver
{
	private readonly record struct CommittedNightAttempt(
		NightActionType ActionType,
		Guid TargetId);

	private static readonly NightActionType[] DawnResolutionActionTypes =
	[
		NightActionType.WerewolfVictimSelection,
		NightActionType.AccursedWolfFatherInfection,
		NightActionType.WhiteWerewolfVictimSelection,
		NightActionType.BigBadWolfVictimSelection,
		NightActionType.DefenderProtect,
		NightActionType.WitchSave,
		NightActionType.WitchKill,
		NightActionType.RustySword
	];

	internal static bool RequiresElderRoleIdentification(
		GameSession session)
	{
		if (GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
				session,
				MainRoleType.Elder) ||
		    GameSessionQueries.IsVillagerRolePowerSuppressionActive(session))
		{
			return false;
		}

		var committedAttempts = GetCommittedNightAttempts(session);
		var defenderTargets = GetDefenderTargets(committedAttempts);
		return GetCanonicalWerewolfAttackAttempts(committedAttempts)
			.Any(attempt =>
				CanTargetBeElder(session, attempt.TargetId) &&
				(attempt.ActionType ==
					 NightActionType.AccursedWolfFatherInfection ||
				 !DefenderBlocksPhysicalAttack(
					 session,
					 defenderTargets,
					 attempt.TargetId)));
	}

	/// <summary>
	/// Analyzes the night's logs to determine who lives, dies, or changes status.
	/// Applies consequences directly to the GameSession.
	/// </summary>
	public static void ResolveNightPhase(GameSession session)
	{
		var nightActions =
			GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				DawnResolutionActionTypes);
			var infectionLogs = nightActions
				.Where(entry =>
					entry.ActionType ==
						NightActionType.AccursedWolfFatherInfection)
				.ToArray();
			FactionFactEffectiveBoundary? infectionEffectiveBoundary = null;
			if (infectionLogs.Length > 0)
			{
				if (infectionLogs is not [var infection] ||
				    infection.TargetIds is not [var infectionTarget] ||
				    !GameSessionQueries.TryGetRetainedWerewolfVictimThisNight(
					    session,
					    out var retainedVictimId) ||
				    infectionTarget != retainedVictimId)
				{
					throw new InvalidOperationException(
						"The Accursed Wolf-Father infection intent does not match one retained collective victim.");
				}

				infectionEffectiveBoundary = new FactionFactEffectiveBoundary(
					infection.TurnNumber,
					infection.CurrentPhase,
					GameSessionQueries.GetCommittedLogIndex(
						session,
						infection));
			}

		var committedAttempts = GetCommittedNightAttempts(nightActions);
		var defenderTargets = GetDefenderTargets(committedAttempts);
		var elderRole = GetElderRole(session);
		var elderProtectionConsumedThisResolution = new HashSet<Guid>();
		var lethalPhysicalTargets = new HashSet<Guid>();
		var orderedLethalTargets = new List<Guid>();

		void ResolveAttackAttempt(
			CommittedNightAttempt attempt,
			bool isInfection)
		{
			var player = session.GetPlayer(attempt.TargetId);
			var defenderBlocksPhysical = DefenderBlocksPhysicalAttack(
				session,
				defenderTargets,
				player.Id);
			if (!isInfection && defenderBlocksPhysical)
			{
				return;
			}

			if (player.State.MainRole == MainRoleType.Elder &&
			    !player.State.HasStatusEffect(
				    StatusEffectTypes.ElderProtectionLost) &&
			    elderRole.IsResistanceAvailable(session, player))
			{
				session.ApplyStatusEffect(
					StatusEffectTypes.ElderProtectionLost,
					player.Id);
				elderProtectionConsumedThisResolution.Add(player.Id);
				return;
			}

				if (isInfection)
				{
					var boundary = infectionEffectiveBoundary
						?? throw new InvalidOperationException(
							"The Accursed Wolf-Father infection requires its historical effective boundary.");
					session.ApplyStatusEffect(
						StatusEffectTypes.LycanthropyInfection,
						player.Id);
					session.CommitFactionFactBatch(context =>
					{
						return new FactionFactsCommittedLogEntry
						{
							Timestamp = context.Timestamp,
							TurnNumber = context.TurnNumber,
							CurrentPhase = context.CurrentPhase,
							Source = new FactionFactSource(
								FactionFactSourceKind.ExplicitTransition,
								AccursedWolfFatherRole
									.InfectionPowerIdentifier.Value),
							Facts =
							[
								FactionFact.Beneficiary(
									player.Id,
									Faction.Werewolf,
									boundary,
									beneficiaryPrecedence: 0),
								FactionFact.Agent(
									player.Id,
									Faction.Werewolf,
									FactionAgentKnowledge.KnownAgent,
									boundary)
							]
						};
					});
				}
			else
			{
				if (lethalPhysicalTargets.Add(player.Id))
				{
					orderedLethalTargets.Add(player.Id);
				}
			}
		}

		// Infection globally replaces the collective physical slot. Attempts
		// within each canonical slot retain their committed log/target order.
		var infectionAttempts = committedAttempts
			.Where(attempt =>
				attempt.ActionType ==
				NightActionType.AccursedWolfFatherInfection)
			.ToArray();
		var collectiveAttempts = infectionAttempts.Length > 0
			? infectionAttempts
			: committedAttempts
				.Where(attempt =>
					attempt.ActionType ==
					NightActionType.WerewolfVictimSelection)
				.ToArray();
		foreach (var attempt in collectiveAttempts)
		{
			ResolveAttackAttempt(
				attempt,
				isInfection:
					attempt.ActionType ==
					NightActionType.AccursedWolfFatherInfection);
		}

		foreach (var attempt in committedAttempts.Where(attempt =>
			         attempt.ActionType ==
			         NightActionType.WhiteWerewolfVictimSelection))
		{
			ResolveAttackAttempt(attempt, isInfection: false);
		}

		foreach (var attempt in committedAttempts.Where(attempt =>
			         attempt.ActionType ==
			         NightActionType.BigBadWolfVictimSelection))
		{
			ResolveAttackAttempt(attempt, isInfection: false);
		}

		foreach (var attempt in committedAttempts.Where(attempt =>
			         attempt.ActionType == NightActionType.WitchSave))
		{
			var lethalPhysicalBeforeHealing =
				lethalPhysicalTargets.Remove(attempt.TargetId);
			if (lethalPhysicalBeforeHealing)
			{
				orderedLethalTargets.Remove(attempt.TargetId);
			}
			var protectionConsumed =
				elderProtectionConsumedThisResolution.Remove(
					attempt.TargetId);
			if (protectionConsumed && !lethalPhysicalBeforeHealing)
			{
				session.RemoveStatusEffect(
					StatusEffectTypes.ElderProtectionLost,
					attempt.TargetId);
			}
		}

		var independentEliminationReasons =
			new Dictionary<Guid, EliminationReason>();

		void ResolveIndependentAttempts(
			NightActionType actionType,
			EliminationReason eliminationReason)
		{
			foreach (var attempt in committedAttempts.Where(attempt =>
				         attempt.ActionType == actionType))
			{
				if (!lethalPhysicalTargets.Contains(attempt.TargetId) &&
				    !independentEliminationReasons.ContainsKey(attempt.TargetId))
				{
					orderedLethalTargets.Add(attempt.TargetId);
				}

				independentEliminationReasons[attempt.TargetId] =
					eliminationReason;
			}
		}

		ResolveIndependentAttempts(
			NightActionType.WitchKill,
			EliminationReason.WitchKill);
		ResolveIndependentAttempts(
			NightActionType.RustySword,
			EliminationReason.RustySword);

		foreach (var targetId in orderedLethalTargets)
		{
			if (!independentEliminationReasons.TryGetValue(
				    targetId,
				    out var eliminationReason))
			{
				if (!lethalPhysicalTargets.Contains(targetId))
				{
					continue;
				}

				eliminationReason = EliminationReason.WerewolfAttack;
			}

			// --- Record the consequence so public Role Reveal can precede elimination ---
			session.DetermineDawnVictim(targetId, eliminationReason);
		}
	}

	internal static ElderRole GetElderRole(GameSession session)
	{
		var listenerId = ListenerIdentifier.Listener(MainRoleType.Elder);
		if (session.TryGetExistingListener<ElderRole>(
				listenerId,
				out var elderRole))
		{
			return elderRole;
		}

		if (!GameFlowManager.ListenerFactories.TryGetValue(
				listenerId,
				out var factory) ||
		    session.GetOrCreateListener(listenerId, factory) is not
			    ElderRole createdElderRole)
		{
			throw new InvalidOperationException(
				"The admitted Elder Role listener is unavailable.");
		}

		return createdElderRole;
	}

	private static CommittedNightAttempt[] GetCommittedNightAttempts(
		GameSession session) =>
		GetCommittedNightAttempts(
			GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				DawnResolutionActionTypes));

	private static CommittedNightAttempt[] GetCommittedNightAttempts(
		IEnumerable<NightActionLogEntry> nightActions) =>
		nightActions
			.SelectMany(log =>
				(log.TargetIds ?? []).Select(targetId =>
					new CommittedNightAttempt(log.ActionType, targetId)))
			.ToArray();

	private static HashSet<Guid> GetDefenderTargets(
		IEnumerable<CommittedNightAttempt> attempts) =>
		attempts
			.Where(attempt =>
				attempt.ActionType == NightActionType.DefenderProtect)
			.Select(attempt => attempt.TargetId)
			.ToHashSet();

	private static IEnumerable<CommittedNightAttempt>
		GetCanonicalWerewolfAttackAttempts(
		IReadOnlyCollection<CommittedNightAttempt> committedAttempts)
	{
		var infectionAttempts = committedAttempts
			.Where(attempt =>
				attempt.ActionType ==
				NightActionType.AccursedWolfFatherInfection)
			.ToArray();
		foreach (var attempt in infectionAttempts.Length > 0
			         ? infectionAttempts
			         : committedAttempts.Where(attempt =>
				         attempt.ActionType ==
				         NightActionType.WerewolfVictimSelection))
		{
			yield return attempt;
		}

		foreach (var attempt in committedAttempts.Where(attempt =>
			         attempt.ActionType ==
			         NightActionType.WhiteWerewolfVictimSelection))
		{
			yield return attempt;
		}

		foreach (var attempt in committedAttempts.Where(attempt =>
			         attempt.ActionType ==
			         NightActionType.BigBadWolfVictimSelection))
		{
			yield return attempt;
		}
	}

	private static bool DefenderBlocksPhysicalAttack(
		GameSession session,
		IReadOnlySet<Guid> defenderTargets,
		Guid targetId) =>
		defenderTargets.Contains(targetId) &&
		session.GetPlayerState(targetId).MainRole !=
		MainRoleType.LittleGirl;

	private static bool CanTargetBeElder(
		GameSession session,
		Guid targetId)
	{
		var state = session.GetPlayerState(targetId);
		return state.CurrentRole is null or MainRoleType.Elder &&
		       state.ModeratorKnownRole is null or MainRoleType.Elder;
	}
}
