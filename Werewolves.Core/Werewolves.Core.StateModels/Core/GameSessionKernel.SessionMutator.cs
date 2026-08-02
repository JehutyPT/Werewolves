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
	void ApplyThiefOfferDecline(ThiefOfferDeclinedLogEntry entry) =>
		throw new NotSupportedException(
			"This Session Mutator does not project Thief offer declines.");

	void AddLogEntry<T>(T entry) where T : GameLogEntryBase;
}

internal interface IDevotedServantSessionMutator
{
	void ApplyDevotedServantRoleTake(
		DevotedServantRoleTakenCommittedLogEntry entry);
}

internal interface IActorSessionMutator
{
	void ApplyActorSetupCardSpend(
		ActorSetupCardSpendCommandLogEntry entry);
	void ApplyActorBorrowedRolePowerActivationExpiry(
		ActorBorrowedRolePowerActivationExpiryCommandLogEntry entry);
}

internal partial class GameSessionKernel
{
	private class SessionMutator(GameSessionKernel kernel)
		: ISessionMutator,
		  IDevotedServantSessionMutator,
		  IActorSessionMutator
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

		public void ApplyDevotedServantRoleTake(
			DevotedServantRoleTakenCommittedLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var actorState = GetMutablePlayerState(entry.ActingPlayerId);
			var targetState = GetMutablePlayerState(entry.VoteTargetId);
			if (kernel._roleLockIn.Version != entry.RoleLockInVersion ||
				actorState.CurrentRole != MainRoleType.DevotedServant ||
				actorState.ModeratorKnownRole != MainRoleType.DevotedServant ||
				actorState.PubliclyRevealedRole != MainRoleType.DevotedServant ||
				actorState.PhysicalCharacterCardId !=
					entry.PhysicalCards.OutgoingOwnedCardId ||
				targetState.Health != PlayerHealth.Alive ||
				targetState.CurrentRole != entry.ExpectedTargetCurrentRole ||
				targetState.PubliclyRevealedRole is not null ||
				!entry.Policy.IsExplicit ||
				!entry.StateChanges.IsCoherentWith(entry.Policy))
			{
				throw new InvalidOperationException(
					"The Devoted Servant Role take is stale or invalid.");
			}

			var movement = entry.PhysicalCards;
			if (!kernel._physicalCardStates.TryGetValue(
					movement.OutgoingOwnedCardId,
					out var outgoing) ||
				outgoing.Zone != PhysicalCharacterCardZone.PlayerOwned ||
				outgoing.OwnerPlayerId != entry.ActingPlayerId ||
				outgoing.Card.PrintedRole != MainRoleType.DevotedServant ||
				!kernel._physicalCardStates.TryGetValue(
					movement.AcquiredCardId,
					out var acquired) ||
				acquired.Card.PrintedRole != entry.ObservedPrintedRole ||
				!IsExpectedTargetCardState(
					acquired,
					targetState,
					entry.VoteTargetId,
					movement.ExpectedAcquiredCardOwnerPlayerId))
			{
				throw new InvalidOperationException(
					"The Devoted Servant physical-card transfer is stale or invalid.");
			}

			var factionProjection = FactionFactProjection.Create(
				kernel._gameHistoryLog
					.GetAllLogEntries()
					.OfType<IFactionFactBatchLogEntry>()
					.Append(entry),
				kernel._playerSeatingOrder);

			kernel._physicalCardStates[movement.OutgoingOwnedCardId] = outgoing with
			{
				Zone = PhysicalCharacterCardZone.Discarded,
				OwnerPlayerId = null
			};
			kernel._physicalCardStates[movement.AcquiredCardId] = acquired with
			{
				Zone = PhysicalCharacterCardZone.PlayerOwned,
				OwnerPlayerId = entry.ActingPlayerId
			};

			targetState.PhysicalCharacterCardId = null;
			targetState.PhysicalCharacterCardRole = null;
			targetState.CurrentRole = null;
			targetState.ModeratorKnownRole = null;
			actorState.PhysicalCharacterCardId = movement.AcquiredCardId;
			actorState.PhysicalCharacterCardRole = entry.ObservedPrintedRole;
			actorState.CurrentRole = entry.NewCurrentRole;
			actorState.ModeratorKnownRole = entry.NewCurrentRole;
			foreach (var effect in entry.StateChanges.StatusEffectsToClear)
			{
				actorState.RemoveEffect(effect);
			}
			switch (entry.Policy.VotingState)
			{
				case PermanentRoleSwapDisposition.Preserve:
					break;
				case PermanentRoleSwapDisposition.Clear:
					actorState.HasVotingRight = true;
					actorState.DurableVotingPower = 1;
					break;
				case PermanentRoleSwapDisposition.Change:
					actorState.HasVotingRight = entry.StateChanges
						.VotingStateAfterSwap!.HasVotingRight;
					actorState.DurableVotingPower = entry.StateChanges
						.VotingStateAfterSwap.DurableVotingPower;
					break;
				default:
					throw new InvalidOperationException(
						"The Devoted Servant voting-state policy is invalid.");
			}
			foreach (var playerId in kernel._playerSeatingOrder)
			{
				GetMutablePlayerState(playerId).ReplaceFactionProjection(
					factionProjection.Beneficiaries[playerId],
					factionProjection.Agents[playerId]);
			}

			static bool IsExpectedTargetCardState(
				PhysicalCharacterCardState acquired,
				PlayerState targetState,
				Guid targetId,
				Guid? expectedOwnerId) =>
				expectedOwnerId is { } ownerId
					? ownerId == targetId &&
					  acquired.Zone == PhysicalCharacterCardZone.PlayerOwned &&
					  acquired.OwnerPlayerId == targetId &&
					  targetState.PhysicalCharacterCardId == acquired.Card.Id &&
					  targetState.PhysicalCharacterCardRole == acquired.Card.PrintedRole
					: acquired.Zone == PhysicalCharacterCardZone.DealPool &&
					  acquired.OwnerPlayerId is null &&
					  targetState.PhysicalCharacterCardId is null &&
					  targetState.PhysicalCharacterCardRole is null;
		}

		public void ApplyActorSetupCardSpend(
			ActorSetupCardSpendCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var activation = entry.Activation;
			if (!kernel._players.TryGetValue(
					activation.ActingPlayerId,
					out var actor))
			{
				throw new InvalidOperationException(
					"The Actor setup-card spend is stale or invalid.");
			}

			var actorState = ((IPlayer)actor).State;
			var selectedCard = kernel._actorSetupCards.Cards
				.SingleOrDefault(card =>
					card.Id == activation.SelectedCardId);
			if (entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Night ||
				kernel._activeActorBorrowedRolePowerActivation is not null ||
				actorState.Health != PlayerHealth.Alive ||
				actorState.CurrentRole != MainRoleType.Actor ||
				activation.ActingRole != MainRoleType.Actor ||
				selectedCard is null ||
				selectedCard.PrintedRole != activation.SourceRole ||
				kernel._actorSetupCardSpendActivationIds.ContainsKey(
					activation.SelectedCardId) ||
				kernel.IsReservedActorBorrowedActivationId(
					activation.ActivationId))
			{
				throw new InvalidOperationException(
					"The Actor setup-card spend is stale or invalid.");
			}

			kernel._actorSetupCardSpendActivationIds.Add(
				activation.SelectedCardId,
				activation.ActivationId);
			kernel._activeActorBorrowedRolePowerActivation = activation;
		}

		public void ApplyActorBorrowedRolePowerActivationExpiry(
			ActorBorrowedRolePowerActivationExpiryCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			if (entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Night ||
				kernel._activeActorBorrowedRolePowerActivation !=
					entry.ExpectedActivation)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Role Power activation expiry is stale or invalid.");
			}

			kernel._activeActorBorrowedRolePowerActivation = null;
		}

		public void ApplyThiefOfferDecline(ThiefOfferDeclinedLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var playerState = GetMutablePlayerState(entry.PlayerId);
			if (kernel._roleLockIn.Version != entry.RoleLockInVersion ||
			    playerState.CurrentRole != MainRoleType.Thief ||
			    playerState.ModeratorKnownRole != MainRoleType.Thief ||
			    playerState.PhysicalCharacterCardId != entry.ThiefCardId ||
			    kernel._roleLockIn.Offer1?.Id != entry.Offer1CardId ||
			    kernel._roleLockIn.Offer2?.Id != entry.Offer2CardId ||
			    !kernel._physicalCardStates.TryGetValue(entry.ThiefCardId, out var thief) ||
			    thief.Zone != PhysicalCharacterCardZone.PlayerOwned ||
			    thief.OwnerPlayerId != entry.PlayerId ||
			    !kernel._physicalCardStates.TryGetValue(entry.Offer1CardId, out var offer1) ||
			    offer1.Zone != PhysicalCharacterCardZone.Offer1 ||
			    offer1.OwnerPlayerId is not null ||
			    !kernel._physicalCardStates.TryGetValue(entry.Offer2CardId, out var offer2) ||
			    offer2.Zone != PhysicalCharacterCardZone.Offer2 ||
			    offer2.OwnerPlayerId is not null)
			{
				throw new InvalidOperationException(
					"The Thief offer decline is stale or invalid.");
			}

			kernel._physicalCardStates[entry.Offer1CardId] = offer1 with
			{
				Zone = PhysicalCharacterCardZone.SetAside
			};
			kernel._physicalCardStates[entry.Offer2CardId] = offer2 with
			{
				Zone = PhysicalCharacterCardZone.SetAside
			};
		}

		public void AddLogEntry<T>(T entry) where T : GameLogEntryBase
		{
			kernel._gameHistoryLog.AddLogEntry(Key, entry);
			kernel._stateChangeObserver?.OnLogEntryApplied(entry);
		}
    }
}
