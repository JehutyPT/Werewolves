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

internal static class ActorBorrowedRolePowers
{
	internal static ActorBorrowedRolePowerUse? ResolveActive(
		GameSession session,
		ActorBorrowedRolePowerSpec spec) =>
		ActorBorrowedRolePowerUse.ResolveActive(session, spec);

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

			var actor = session.GetPlayers().SingleOrDefault(player =>
				player.Id == activation.ActingPlayerId);
			var setupCard = session.GetModeratorSpentActorSetupCards()
				.SingleOrDefault(card => card.Id == activation.SelectedCardId);
			if (actor is null || setupCard is null)
			{
				throw new InvalidOperationException(
					"The active Actor borrowed Role Power lineage is incomplete.");
			}

			var powerInstance = RolePowerInstance.CreateBorrowed(
				session,
				actor,
				spec.SourceRole,
				spec.SourcePower);
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
