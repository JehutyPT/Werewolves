using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Core;

//todo: move this to its own file once this is finalized.
public interface ISessionMutator
{
	int CurrentTurnNumber { get; }
	void SetModeratorKnownRole(Guid playerId, MainRoleType role);
	void SetPhysicalCharacterCardOwnership(
		long roleLockInVersion,
		Guid playerId,
		Guid cardId,
		MainRoleType printedRole) =>
		throw new NotSupportedException(
			"This Session Mutator does not project Physical Character Card ownership.");
		void SetPhysicalCharacterCardRole(Guid playerId, MainRoleType role);
		void SetPlayerHealth(Guid playerId, PlayerHealth health);
			void SetVotingRight(Guid playerId, bool hasVotingRight);
			void SetDurableVotingPower(Guid playerId, int durableVotingPower);
		void SetPlayerRole(Guid playerId, MainRoleType role);
	void SetPubliclyRevealedRole(Guid playerId, MainRoleType role);
	void SetCurrentPhase(GamePhase newPhase);
	
	/// <summary>
	/// Sets or clears a status effect on a player.
	/// </summary>
	/// <param name="playerId">The player to modify.</param>
	/// <param name="effect">The status effect to set or clear.</param>
	/// <param name="isActive">True to add the effect, false to remove it.</param>
	void SetStatusEffect(Guid playerId, StatusEffectTypes effect, bool isActive);
	
	void ApplyFactionFacts(IFactionFactBatchLogEntry entry);
	void ApplyPermanentRoleSwap(PermanentRoleSwapCommittedLogEntry entry) =>
		throw new NotSupportedException(
			"This Session Mutator does not project Permanent Role Swaps.");

	void AddLogEntry<T>(T entry) where T : GameLogEntryBase;
}

internal partial class GameSessionKernel
{
	private class SessionMutator(GameSessionKernel kernel) : ISessionMutator
	{
		/// <summary>
		/// Represents a key used to allow access to mutate persistent state, player's, game state (i.e. main phase) or game logs.
		/// </summary>
		internal interface IStateMutatorKey{}
		/// <summary>
		/// Private implementation of the state mutator key to restrict access.
		/// </summary>
		private class StateMutatorKey : IStateMutatorKey{}
		private static readonly StateMutatorKey Key = new();

		private PlayerState GetMutablePlayerState(Guid playerId) =>
			kernel.GetMutablePlayerState(Key, playerId);

		public int CurrentTurnNumber => kernel.TurnNumber;

		public void SetModeratorKnownRole(Guid playerId, MainRoleType role)
			=> GetMutablePlayerState(playerId).ModeratorKnownRole = role;

		public void SetPhysicalCharacterCardOwnership(
			long roleLockInVersion,
			Guid playerId,
			Guid cardId,
			MainRoleType printedRole)
		{
			var playerState = GetMutablePlayerState(playerId);
			if (kernel._roleLockIn.Version != roleLockInVersion ||
				playerState.PhysicalCharacterCardId is not null ||
				!kernel._physicalCardStates.TryGetValue(cardId, out var cardState) ||
				cardState.Zone != PhysicalCharacterCardZone.DealPool ||
				cardState.OwnerPlayerId is not null ||
				cardState.Card.PrintedRole != printedRole)
			{
				throw new InvalidOperationException(
					"The Physical Character Card ownership observation is stale or invalid.");
			}

			kernel._physicalCardStates[cardId] = cardState with
			{
				Zone = PhysicalCharacterCardZone.PlayerOwned,
				OwnerPlayerId = playerId
			};
			playerState.PhysicalCharacterCardId = cardId;
			playerState.PhysicalCharacterCardRole = printedRole;
		}

		public void SetPhysicalCharacterCardRole(Guid playerId, MainRoleType role)
			=> GetMutablePlayerState(playerId).PhysicalCharacterCardRole = role;

			public void SetPlayerHealth(Guid playerId, PlayerHealth health)
				=> GetMutablePlayerState(playerId).Health = health;

				public void SetVotingRight(Guid playerId, bool hasVotingRight)
					=> GetMutablePlayerState(playerId).HasVotingRight = hasVotingRight;

			public void SetDurableVotingPower(
				Guid playerId,
				int durableVotingPower)
			{
				if (durableVotingPower < 0)
				throw new ArgumentOutOfRangeException(
					nameof(durableVotingPower));

				GetMutablePlayerState(playerId).DurableVotingPower =
					durableVotingPower;
			}

		public void SetPlayerRole(Guid playerId, MainRoleType role)
			=> GetMutablePlayerState(playerId).MainRole = role;

		public void SetPubliclyRevealedRole(Guid playerId, MainRoleType role)
			=> GetMutablePlayerState(playerId).PubliclyRevealedRole = role;

		public void SetCurrentPhase(GamePhase newPhase)
		{
			kernel._phaseStateCache.TransitionMainPhase(Key, newPhase);
			kernel._stateChangeObserver?.OnMainPhaseChanged(newPhase);

			if (newPhase == GamePhase.Night)
			{
				kernel.IncrementTurnNumber(Key);
				kernel._stateChangeObserver?.OnTurnNumberChanged(kernel.TurnNumber);
			}
		}

		public void SetStatusEffect(Guid playerId, StatusEffectTypes effect, bool isActive)
		{
			var playerState = GetMutablePlayerState(playerId);
			if (isActive)
			{
				playerState.AddEffect(effect);
			}
			else
			{
				playerState.RemoveEffect(effect);
			}
		}

		public void ApplyFactionFacts(IFactionFactBatchLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);

			var projection = FactionFactProjection.Create(
				kernel._gameHistoryLog
					.GetAllLogEntries()
					.OfType<IFactionFactBatchLogEntry>()
					.Append(entry),
				kernel._playerSeatingOrder);

			foreach (var playerId in kernel._playerSeatingOrder)
			{
				GetMutablePlayerState(playerId).ReplaceFactionProjection(
					projection.Beneficiaries[playerId],
					projection.Agents[playerId]);
			}
		}

		public void ApplyPermanentRoleSwap(PermanentRoleSwapCommittedLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var playerState = GetMutablePlayerState(entry.PlayerId);
			if (kernel._roleLockIn.Version != entry.RoleLockInVersion ||
				playerState.CurrentRole != entry.ExpectedCurrentRole ||
				playerState.PhysicalCharacterCardId != entry.PhysicalCards.OutgoingOwnedCardId ||
				!entry.Policy.IsExplicit ||
				!entry.StateChanges.IsCoherentWith(entry.Policy))
			{
				throw new InvalidOperationException(
					"The Permanent Role Swap is stale or invalid.");
			}

			var expectedAcquiredOwnerId =
				entry.PhysicalCards.ExpectedAcquiredCardOwnerPlayerId;
			if (!kernel._physicalCardStates.TryGetValue(
					entry.PhysicalCards.OutgoingOwnedCardId,
					out var outgoing) ||
				outgoing.Zone != PhysicalCharacterCardZone.PlayerOwned ||
				outgoing.OwnerPlayerId != entry.PlayerId ||
				!kernel._physicalCardStates.TryGetValue(
					entry.PhysicalCards.AcquiredCardId,
					out var acquired) ||
				!IsExpectedAcquiredCardState(acquired, expectedAcquiredOwnerId) ||
				acquired.Card.PrintedRole != entry.NewCurrentRole)
			{
				throw new InvalidOperationException(
					"The Permanent Role Swap physical exchange is invalid.");
			}
			PlayerState? acquiredPriorOwnerState = null;
			if (expectedAcquiredOwnerId is { } acquiredOwnerId)
			{
				if (acquiredOwnerId == entry.PlayerId ||
					!kernel._players.TryGetValue(acquiredOwnerId, out var acquiredOwner))
				{
					throw new InvalidOperationException(
						"The Permanent Role Swap acquired-card owner is invalid.");
				}
				acquiredPriorOwnerState = acquiredOwner.GetMutableState(Key);
				if (acquiredPriorOwnerState.PhysicalCharacterCardId != acquired.Card.Id ||
					acquiredPriorOwnerState.PhysicalCharacterCardRole !=
						acquired.Card.PrintedRole)
				{
					throw new InvalidOperationException(
						"The Permanent Role Swap acquired-card owner projection is stale.");
				}
			}

			var nextCardStates = new Dictionary<Guid, PhysicalCharacterCardState>
			{
				[entry.PhysicalCards.OutgoingOwnedCardId] = outgoing with
				{
					Zone = PhysicalCharacterCardZone.SetAside,
					OwnerPlayerId = null
				},
				[entry.PhysicalCards.AcquiredCardId] = acquired with
				{
					Zone = PhysicalCharacterCardZone.PlayerOwned,
					OwnerPlayerId = entry.PlayerId
				}
			};
			foreach (var cardId in entry.PhysicalCards.AdditionalSetAsideCardIds)
			{
				if (!kernel._physicalCardStates.TryGetValue(cardId, out var cardState) ||
					cardState.OwnerPlayerId is not null ||
					cardState.Zone is PhysicalCharacterCardZone.PlayerOwned or PhysicalCharacterCardZone.SetAside)
				{
					throw new InvalidOperationException(
						"The Permanent Role Swap Set-Aside movement is invalid.");
				}
				nextCardStates[cardId] = cardState with
				{
					Zone = PhysicalCharacterCardZone.SetAside,
					OwnerPlayerId = null
				};
			}

			var factionProjection = FactionFactProjection.Create(
				kernel._gameHistoryLog
					.GetAllLogEntries()
					.OfType<IFactionFactBatchLogEntry>()
					.Append(entry),
				kernel._playerSeatingOrder);
			IReadOnlyList<Guid> relationshipPlayerIdsToClear = [];
			if (entry.StateChanges.RelationshipEffectsToClear.Contains(
					StatusEffectTypes.Lovers))
			{
				var loversPair = kernel._gameHistoryLog
					.GetAllLogEntries()
					.OfType<LoversPairCommittedLogEntry>()
					.SingleOrDefault();
				if (loversPair is null ||
					!loversPair.PlayerIds.Contains(entry.PlayerId) ||
					loversPair.PlayerIds.Any(playerId =>
						!GetMutablePlayerState(playerId).HasStatusEffect(
							StatusEffectTypes.Lovers)))
				{
					throw new InvalidOperationException(
						"The Permanent Role Swap relationship clear is stale.");
				}
				relationshipPlayerIdsToClear = loversPair.PlayerIds;
			}

			foreach (var (cardId, state) in nextCardStates)
			{
				kernel._physicalCardStates[cardId] = state;
			}
			if (acquiredPriorOwnerState is not null)
			{
				acquiredPriorOwnerState.PhysicalCharacterCardId = null;
				acquiredPriorOwnerState.PhysicalCharacterCardRole = null;
			}
			playerState.PhysicalCharacterCardId = entry.PhysicalCards.AcquiredCardId;
			playerState.PhysicalCharacterCardRole = acquired.Card.PrintedRole;
			playerState.CurrentRole = entry.NewCurrentRole;
			playerState.ModeratorKnownRole = entry.Policy.PrivateRoleKnowledge switch
			{
				PermanentRoleSwapDisposition.Preserve => playerState.ModeratorKnownRole,
				PermanentRoleSwapDisposition.Change => entry.NewCurrentRole,
				PermanentRoleSwapDisposition.Clear => null,
				_ => throw new InvalidOperationException(
					"Permanent Role Swap private-knowledge policy is invalid.")
			};
			foreach (var relationshipPlayerId in relationshipPlayerIdsToClear)
			{
				GetMutablePlayerState(relationshipPlayerId).RemoveEffect(
					StatusEffectTypes.Lovers);
			}
			foreach (var effect in entry.StateChanges.StatusEffectsToClear)
			{
				playerState.RemoveEffect(effect);
			}
			switch (entry.Policy.VotingState)
			{
				case PermanentRoleSwapDisposition.Preserve:
					break;
				case PermanentRoleSwapDisposition.Clear:
					playerState.HasVotingRight = true;
					playerState.DurableVotingPower = 1;
					break;
				case PermanentRoleSwapDisposition.Change:
					playerState.HasVotingRight = entry.StateChanges.VotingStateAfterSwap!.HasVotingRight;
					playerState.DurableVotingPower = entry.StateChanges.VotingStateAfterSwap.DurableVotingPower;
					break;
			}
			foreach (var playerId in kernel._playerSeatingOrder)
			{
				GetMutablePlayerState(playerId).ReplaceFactionProjection(
					factionProjection.Beneficiaries[playerId],
					factionProjection.Agents[playerId]);
			}

			static bool IsExpectedAcquiredCardState(
				PhysicalCharacterCardState acquired,
				Guid? expectedOwnerId) =>
				expectedOwnerId is { } ownerId
					? acquired is
					{
						Zone: PhysicalCharacterCardZone.PlayerOwned,
						OwnerPlayerId: var actualOwnerId
					} && actualOwnerId == ownerId
					: acquired.OwnerPlayerId is null &&
						acquired.Zone is PhysicalCharacterCardZone.DealPool or
							PhysicalCharacterCardZone.Offer1 or
							PhysicalCharacterCardZone.Offer2;
		}

		public void AddLogEntry<T>(T entry) where T : GameLogEntryBase
		{
			kernel._gameHistoryLog.AddLogEntry(Key, entry);
			kernel._stateChangeObserver?.OnLogEntryApplied(entry);
		}
    }
}
