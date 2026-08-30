using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.RolePowers;

internal sealed record ActorBorrowedRolePowerSpec
{
	internal ActorBorrowedRolePowerSpec(
		MainRoleType sourceRole,
		RolePowerDefinition sourcePower)
	{
		ArgumentNullException.ThrowIfNull(sourcePower);
		if (!sourceRole.IsEligibleActorSetupCard())
		{
			throw new ArgumentException(
				"An Actor borrowed Role Power requires an eligible source Role.",
				nameof(sourceRole));
		}

		SourceRole = sourceRole;
		SourcePower = sourcePower;
	}

	internal MainRoleType SourceRole { get; }
	internal RolePowerDefinition SourcePower { get; }
}

internal abstract record BorrowedPostEliminationRolePowerContext
{
	private BorrowedPostEliminationRolePowerContext() { }

	internal sealed record HunterFinalShot(
		string CascadeScopeId,
		IReadOnlyList<Guid> TriggeringPlayerIds)
		: BorrowedPostEliminationRolePowerContext;

	internal sealed record ElderVillageVoteSuppression(
		int VoteOutcomeLogIndex,
		string CascadeScopeId)
		: BorrowedPostEliminationRolePowerContext;

	internal sealed record KnightRustySwordSchedule(
		int WerewolfAttackEliminationLogIndex,
		string CascadeScopeId)
		: BorrowedPostEliminationRolePowerContext;

	internal sealed record ScapegoatVoterRestriction(
		int TieReplacementPublicMarkerLogIndex,
		string SacrificeCascadeScopeId)
		: BorrowedPostEliminationRolePowerContext;
}

internal static class ActorBorrowedRolePowers
{
	internal static ActorBorrowedRolePowerUse? ResolveActive(
		GameSession session,
		ActorBorrowedRolePowerSpec spec) =>
		ActorBorrowedRolePowerUse.ResolveActive(session, spec);

	internal static ActorBorrowedRolePowerUse? ResolveAfterElimination(
		GameSession session,
		ActorBorrowedRolePowerSpec spec,
		BorrowedPostEliminationRolePowerContext context) =>
		ActorBorrowedRolePowerUse.ResolveAfterElimination(session, spec, context);

	internal sealed class ActorBorrowedRolePowerUse
	{
		private readonly GameSession _session;
		private readonly int _turnNumber;
		private readonly GamePhase _phase;

		internal static ActorBorrowedRolePowerUse? ResolveActive(
			GameSession session,
			ActorBorrowedRolePowerSpec spec)
		{
			ArgumentNullException.ThrowIfNull(session);
			ArgumentNullException.ThrowIfNull(spec);

			var activation =
				session.GetModeratorActiveActorBorrowedRolePowerActivation();
			if (activation is null || activation.SourceRole != spec.SourceRole)
			{
				return null;
			}

			return ResolveUse(
				session,
				spec,
				activation,
				PlayerHealth.Alive);
		}

		internal static ActorBorrowedRolePowerUse? ResolveAfterElimination(
			GameSession session,
			ActorBorrowedRolePowerSpec spec,
			BorrowedPostEliminationRolePowerContext context)
		{
			ArgumentNullException.ThrowIfNull(session);
			ArgumentNullException.ThrowIfNull(spec);
			ArgumentNullException.ThrowIfNull(context);

			var activation =
				session.GetModeratorActiveActorBorrowedRolePowerActivation();
			if (activation is null || activation.SourceRole != spec.SourceRole)
			{
				return null;
			}

			var use = ResolveUse(
				session,
				spec,
				activation,
				PlayerHealth.Dead);
			var validContext = context switch
			{
				BorrowedPostEliminationRolePowerContext.HunterFinalShot hunter =>
					GameSessionQueries.IsValidActorBorrowedHunterFinalShotContext(
						session,
						use,
						hunter),
				BorrowedPostEliminationRolePowerContext
					.ElderVillageVoteSuppression elder =>
					GameSessionQueries
						.IsValidActorBorrowedElderVillageVoteSuppressionContext(
						session,
						use,
						elder),
				BorrowedPostEliminationRolePowerContext
					.KnightRustySwordSchedule knight =>
					GameSessionQueries
						.IsValidActorBorrowedKnightRustySwordScheduleContext(
						session,
						use,
						knight),
				BorrowedPostEliminationRolePowerContext
					.ScapegoatVoterRestriction scapegoat =>
					GameSessionQueries
						.IsValidActorBorrowedScapegoatVoterRestrictionContext(
						session,
						use,
						scapegoat),
				_ => false
			};
			if (!validContext)
			{
				throw new InvalidOperationException(
					"The Actor borrowed post-elimination Role Power context is stale or invalid.");
			}

			return use;
		}

		private static ActorBorrowedRolePowerUse ResolveUse(
			GameSession session,
			ActorBorrowedRolePowerSpec spec,
			ActorBorrowedRolePowerActivation activation,
			PlayerHealth expectedActorHealth)
		{
			var actor = session.GetPlayers().SingleOrDefault(player =>
				player.Id == activation.ActingPlayerId);
			var setupCard = session.GetModeratorSpentActorSetupCards()
				.SingleOrDefault(card => card.Id == activation.SelectedCardId);
			if (actor is null ||
				setupCard is null ||
				actor.State.CurrentRole != MainRoleType.Actor ||
				actor.State.Health != expectedActorHealth ||
				activation.ActingRole != MainRoleType.Actor ||
				activation.SourceRole != spec.SourceRole ||
				setupCard.PrintedRole != spec.SourceRole)
			{
				throw new InvalidOperationException(
					"The active Actor borrowed Role Power lineage is incomplete or stale.");
			}

			var powerInstance = new RolePowerInstance(
				activation.ActivationId,
				spec.SourceRole,
				spec.SourcePower,
				RolePowerInstanceOrigin.Borrowed);
			var powerIdentity = new RolePowerInstanceIdentity(
				actor.Id,
				spec.SourceRole,
				spec.SourcePower.Identifier.Value,
				powerInstance.Id,
				powerInstance.Origin);
			powerIdentity.EnforceValidity();

			return new ActorBorrowedRolePowerUse(
				session,
				actor,
				powerInstance,
				powerIdentity,
				setupCard.Id);
		}


		private ActorBorrowedRolePowerUse(
			GameSession session,
			IPlayer actor,
			RolePowerInstance powerInstance,
			RolePowerInstanceIdentity powerIdentity,
			Guid actorSetupCardId)
		{
			_session = session;
			_turnNumber = session.TurnNumber;
			_phase = session.GetCurrentPhase();
			Actor = actor;
			PowerInstance = powerInstance;
			PowerIdentity = powerIdentity;
			ActorSetupCardId = actorSetupCardId;
		}

		internal IPlayer Actor { get; }
		internal RolePowerInstance PowerInstance { get; }
		internal RolePowerInstanceIdentity PowerIdentity { get; }
		internal Guid ActorSetupCardId { get; }

		internal RolePowerAttempt CreateAttempt() => new(
			_session,
			Actor,
			PowerInstance.SourceRole,
			PowerInstance.SourcePower,
			PowerInstance);

		internal RolePowerAttempt CreateAttempt(Guid oneUseResourceId) => new(
			_session,
			Actor,
			PowerInstance.SourceRole,
			PowerInstance.SourcePower,
			PowerInstance,
			new OneUseRolePowerResource(oneUseResourceId, PowerInstance));

		internal bool Correlates(IActorBorrowedRolePowerCommit commitment)
		{
			ArgumentNullException.ThrowIfNull(commitment);
			var coordinate = commitment.Coordinate;
			coordinate.EnforceValidity();
			var active =
				_session.GetModeratorActiveActorBorrowedRolePowerActivation();
			if (active is null ||
				active.ActivationId != PowerIdentity.PowerInstanceId ||
				active.ActingPlayerId != Actor.Id ||
				active.ActingRole != MainRoleType.Actor ||
				active.SelectedCardId != ActorSetupCardId ||
				active.SourceRole != PowerInstance.SourceRole)
			{
				return false;
			}

			return coordinate.PowerIdentity == PowerIdentity &&
				coordinate.ActorSetupCardId == ActorSetupCardId &&
				coordinate.TurnNumber == _turnNumber &&
				coordinate.CurrentPhase == _phase;
		}
	}
}
