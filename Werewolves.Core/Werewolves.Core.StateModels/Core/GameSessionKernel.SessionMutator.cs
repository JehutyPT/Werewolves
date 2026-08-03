using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;

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
	string ApplyActorBorrowedSeerCheck(
		ActorBorrowedSeerCheckCommandLogEntry entry);
	string ApplyActorBorrowedDefenderProtection(
		ActorBorrowedDefenderProtectionCommandLogEntry entry);
	string ApplyActorBorrowedFoxCheck(
		ActorBorrowedFoxCheckCommandLogEntry entry);
	string ApplyActorBorrowedBearTamerGrowl(
		ActorBorrowedBearTamerGrowlCommandLogEntry entry);
	string ApplyActorBorrowedKnightRustySwordSchedule(
		ActorBorrowedKnightRustySwordScheduleCommandLogEntry entry);
	string ApplyActorBorrowedHunterFinalShot(
		ActorBorrowedHunterFinalShotCommandLogEntry entry);
	string ApplyActorBorrowedElderResistance(
		ActorBorrowedElderResistanceCommandLogEntry entry);
	string ApplyActorBorrowedElderSuppression(
		ActorBorrowedElderSuppressionCommandLogEntry entry);
	string ApplyActorBorrowedScapegoatTieReplacement(
		ActorBorrowedScapegoatTieReplacementCommandLogEntry entry);
	string ApplyActorBorrowedScapegoatVoterRestriction(
		ActorBorrowedScapegoatVoterRestrictionCommandLogEntry entry);
	string ApplyActorBorrowedVillageIdiotPardon(
		ActorBorrowedVillageIdiotPardonCommandLogEntry entry);
	string ApplyActorBorrowedWitchPotionUse(
		ActorBorrowedWitchPotionUseCommandLogEntry entry);
	string ApplyActorBorrowedWitchPotionDecline(
		ActorBorrowedWitchPotionDeclineCommandLogEntry entry);
	string ApplyActorBorrowedCupidLovers(
		ActorBorrowedCupidLoversCommandLogEntry entry);
	void ApplyActorBorrowedCupidInitialBeneficiaryClosure(
		ActorBorrowedCupidInitialBeneficiaryClosureCommandLogEntry entry);
	string ApplyActorBorrowedStutteringJudgeSignalSetup(
		ActorBorrowedStutteringJudgeSignalSetupCommandLogEntry entry);
	string ApplyActorBorrowedStutteringJudgeSignalObservation(
		ActorBorrowedStutteringJudgeSignalObservationCommandLogEntry entry);
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
				var privateCupidFactBatches =
					kernel._actorBorrowedCupidLoversCommits
						.Cast<IFactionFactBatchLogEntry>()
						.ToArray();
				var factBatches = kernel._gameHistoryLog
					.GetAllLogEntries()
					.OfType<IFactionFactBatchLogEntry>()
					.Concat(privateCupidFactBatches);
				if (!privateCupidFactBatches.Any(candidate =>
						ReferenceEquals(candidate, entry)))
				{
					factBatches = factBatches.Append(entry);
				}

				var projection = FactionFactProjection.Create(
					factBatches,
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

		public string ApplyActorBorrowedSeerCheck(
			ActorBorrowedSeerCheckCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var markerLogIndex = kernel._gameHistoryLog.GetAllLogEntries().Count;
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Night ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.Seer ||
				identity.SourceRole != active.SourceRole ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard is null ||
				selectedCard.PrintedRole != MainRoleType.Seer ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(identity.ActingPlayerId, out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Alive,
					CurrentRole: MainRoleType.Actor
				} ||
				!kernel._players.TryGetValue(entry.TargetPlayerId, out var target) ||
				entry.TargetPlayerId == identity.ActingPlayerId ||
				((IPlayer)target).State.Health != PlayerHealth.Alive ||
				((IPlayer)target).State.GetFactionAgentKnowledge(Faction.Werewolf) !=
					entry.TargetAgentKnowledge ||
				kernel._actorBorrowedSeerCheckCommits.Any(commit =>
					commit.PowerIdentity.PowerInstanceId ==
					identity.PowerInstanceId))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Role Power commit is stale or invalid.");
			}

			var commit = new ActorBorrowedSeerCheckCommit(
				identity,
				entry.ActorSetupCardId,
				entry.TargetPlayerId,
				entry.TargetAgentKnowledge,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedSeerCheckCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedDefenderProtection(
			ActorBorrowedDefenderProtectionCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var markerLogIndex = kernel._gameHistoryLog.GetAllLogEntries().Count;
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Night ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.Defender ||
				identity.SourceRole != active.SourceRole ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard is null ||
				selectedCard.PrintedRole != MainRoleType.Defender ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(identity.ActingPlayerId, out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Alive,
					CurrentRole: MainRoleType.Actor
				} ||
				!kernel._players.TryGetValue(entry.TargetPlayerId, out var target) ||
				((IPlayer)target).State.Health != PlayerHealth.Alive ||
				kernel._actorBorrowedDefenderProtectionCommits.Any(commit =>
					commit.PowerIdentity == identity))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Role Power commit is stale or invalid.");
			}

			var commit = new ActorBorrowedDefenderProtectionCommit(
				identity,
				entry.ActorSetupCardId,
				entry.TargetPlayerId,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedDefenderProtectionCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedFoxCheck(
			ActorBorrowedFoxCheckCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var markerLogIndex = kernel._gameHistoryLog.GetAllLogEntries().Count;
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Night ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.Fox ||
				identity.SourceRole != active.SourceRole ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard is null ||
				selectedCard.PrintedRole != MainRoleType.Fox ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(identity.ActingPlayerId, out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Alive,
					CurrentRole: MainRoleType.Actor
				} ||
				!kernel._players.TryGetValue(entry.CenterPlayerId, out var center) ||
				((IPlayer)center).State.Health != PlayerHealth.Alive ||
				kernel._actorBorrowedFoxCheckCommits.Any(commit =>
					commit.PowerIdentity == identity))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Role Power commit is stale or invalid.");
			}

			var commit = new ActorBorrowedFoxCheckCommit(
				identity,
				entry.ActorSetupCardId,
				entry.CenterPlayerId,
				entry.NeighborhoodAgentKnowledge,
				entry.SpentResourceIdentity,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedFoxCheckCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedBearTamerGrowl(
			ActorBorrowedBearTamerGrowlCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var markerLogIndex = kernel._gameHistoryLog.GetAllLogEntries().Count;
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Dawn ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.BearTamer ||
				identity.SourceRole != active.SourceRole ||
				!StringComparer.Ordinal.Equals(
					identity.SourcePowerIdentifier,
					ActorBorrowedBearTamerGrowlCommit
						.ExpectedSourcePowerIdentifier) ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard?.PrintedRole != MainRoleType.BearTamer ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(identity.ActingPlayerId, out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Alive,
					CurrentRole: MainRoleType.Actor
				} ||
				kernel._actorBorrowedBearTamerGrowlCommits.Any(commit =>
					commit.PowerIdentity == identity))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Role Power commit is stale or invalid.");
			}

			var commit = new ActorBorrowedBearTamerGrowlCommit(
				identity,
				entry.ActorSetupCardId,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedBearTamerGrowlCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedKnightRustySwordSchedule(
			ActorBorrowedKnightRustySwordScheduleCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var history = kernel._gameHistoryLog.GetAllLogEntries();
			var markerLogIndex = history.Count;
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Dawn ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.KnightWithRustySword ||
				identity.SourceRole != active.SourceRole ||
				!StringComparer.Ordinal.Equals(
					identity.SourcePowerIdentifier,
					ActorBorrowedKnightRustySwordScheduleCommit
						.ExpectedSourcePowerIdentifier) ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard?.PrintedRole !=
					MainRoleType.KnightWithRustySword ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(
					identity.ActingPlayerId,
					out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Dead,
					CurrentRole: MainRoleType.Actor
				} ||
				!kernel._players.TryGetValue(
					entry.TargetPlayerId,
					out var target) ||
				((IPlayer)target).State.Health != PlayerHealth.Alive ||
				!HasQualifyingActorBorrowedKnightScheduleHistory(
					history,
					identity.ActingPlayerId,
					entry.TurnNumber,
					entry.WerewolfAttackEliminationLogIndex,
					entry.CascadeScopeId,
					markerLogIndex) ||
				kernel._actorBorrowedKnightRustySwordScheduleCommits.Any())
			{
				throw new InvalidOperationException(
					"The Actor borrowed Role Power commit is stale or invalid.");
			}

			var commit = new ActorBorrowedKnightRustySwordScheduleCommit(
				identity,
				entry.ActorSetupCardId,
				entry.TargetPlayerId,
				entry.WerewolfAttackEliminationLogIndex,
				entry.CascadeScopeId,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedKnightRustySwordScheduleCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedHunterFinalShot(
			ActorBorrowedHunterFinalShotCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var markerLogIndex = kernel._gameHistoryLog.GetAllLogEntries().Count;
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase is not (GamePhase.Night or GamePhase.Day) ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.Hunter ||
				identity.SourceRole != active.SourceRole ||
				!StringComparer.Ordinal.Equals(
					identity.SourcePowerIdentifier,
					ActorBorrowedHunterFinalShotCommit
						.ExpectedSourcePowerIdentifier) ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard?.PrintedRole != MainRoleType.Hunter ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(identity.ActingPlayerId, out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Dead,
					CurrentRole: MainRoleType.Actor
				} ||
				entry.TriggeringPlayerIds.Any(playerId =>
					!kernel._players.TryGetValue(playerId, out var triggeringPlayer) ||
					((IPlayer)triggeringPlayer).State.Health != PlayerHealth.Dead) ||
				!kernel._players.TryGetValue(entry.TargetPlayerId, out var target) ||
				((IPlayer)target).State.Health != PlayerHealth.Alive ||
				kernel._actorBorrowedHunterFinalShotCommits.Any(commit =>
					commit.PowerIdentity == identity))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Role Power commit is stale or invalid.");
			}

			var commit = new ActorBorrowedHunterFinalShotCommit(
				identity,
				entry.ActorSetupCardId,
				entry.CascadeScopeId,
				entry.TriggeringPlayerIds.ToArray(),
				entry.TargetPlayerId,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedHunterFinalShotCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedElderResistance(
			ActorBorrowedElderResistanceCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var history = kernel._gameHistoryLog.GetAllLogEntries();
			var markerLogIndex = history.Count;
			var previousCommit = kernel._actorBorrowedElderResistanceCommits
				.LastOrDefault(commit => commit.PowerIdentity == identity);
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Dawn ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.Elder ||
				identity.SourceRole != active.SourceRole ||
				!StringComparer.Ordinal.Equals(
					identity.SourcePowerIdentifier,
					ActorBorrowedElderResistanceCommit
						.ExpectedSourcePowerIdentifier) ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard?.PrintedRole != MainRoleType.Elder ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(
					identity.ActingPlayerId,
					out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Alive,
					CurrentRole: MainRoleType.Actor
				} ||
				entry.TargetPlayerId != identity.ActingPlayerId ||
				entry.TriggeringNightActionLogIndex < 0 ||
				entry.TriggeringNightActionLogIndex >= markerLogIndex ||
				entry.RestoringWitchSaveLogIndex is { } restorationLogIndex &&
					(restorationLogIndex <=
						entry.TriggeringNightActionLogIndex ||
					 restorationLogIndex >= markerLogIndex) ||
				kernel._actorBorrowedElderResistanceCommits.Any(commit =>
					commit.PowerIdentity == identity &&
					commit.TriggeringNightActionLogIndex ==
						entry.TriggeringNightActionLogIndex) ||
				previousCommit is not null &&
					(previousCommit.RestoringWitchSaveLogIndex is null ||
					 previousCommit.PublicMarkerLogIndex >=
						entry.TriggeringNightActionLogIndex))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Role Power commit is stale or invalid.");
			}

			var commit = new ActorBorrowedElderResistanceCommit(
				identity,
				entry.ActorSetupCardId,
				entry.TargetPlayerId,
				entry.TriggeringNightActionLogIndex,
				entry.RestoringWitchSaveLogIndex,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedElderResistanceCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedElderSuppression(
			ActorBorrowedElderSuppressionCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var history = kernel._gameHistoryLog.GetAllLogEntries();
			var markerLogIndex = history.Count;
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Day ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.Elder ||
				identity.SourceRole != active.SourceRole ||
				!StringComparer.Ordinal.Equals(
					identity.SourcePowerIdentifier,
					ActorBorrowedElderSuppressionCommit
						.ExpectedSourcePowerIdentifier) ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard?.PrintedRole != MainRoleType.Elder ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(
					identity.ActingPlayerId,
					out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Dead,
					CurrentRole: MainRoleType.Actor,
					PhysicalCharacterCardRole: MainRoleType.Actor,
					PubliclyRevealedRole: MainRoleType.Actor
				} ||
				!HasQualifyingActorBorrowedElderSuppressionHistory(
					history,
					identity.ActingPlayerId,
					entry.TurnNumber,
					entry.TriggeringVoteOutcomeLogIndex,
					entry.CascadeScopeId,
					markerLogIndex) ||
				kernel._actorBorrowedElderSuppressionCommits.Any() ||
				history.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
					.Any())
			{
				throw new InvalidOperationException(
					"The Actor borrowed Role Power commit is stale or invalid.");
			}

			var commit = new ActorBorrowedElderSuppressionCommit(
				identity,
				entry.ActorSetupCardId,
				entry.TriggeringVoteOutcomeLogIndex,
				entry.CascadeScopeId,
				entry.AnnouncementInstructionId,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedElderSuppressionCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedScapegoatTieReplacement(
			ActorBorrowedScapegoatTieReplacementCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var history = kernel._gameHistoryLog.GetAllLogEntries();
			var markerLogIndex = history.Count;
			var expectedScopeId =
				$"Day:{entry.TurnNumber}:Vote:{entry.VoteOrdinal}";
			var voteOrdinal = entry.TriggeringVoteOutcomeLogIndex < 0 ||
				entry.TriggeringVoteOutcomeLogIndex >= markerLogIndex
					? 0
					: history
						.Take(entry.TriggeringVoteOutcomeLogIndex + 1)
						.OfType<VoteOutcomeReportedLogEntry>()
						.Count(vote =>
							vote.TurnNumber == entry.TurnNumber &&
							vote.CurrentPhase == GamePhase.Day);
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Day ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.Scapegoat ||
				identity.SourceRole != active.SourceRole ||
				!StringComparer.Ordinal.Equals(
					identity.SourcePowerIdentifier,
					ActorBorrowedScapegoatTieReplacementCommit
						.ExpectedSourcePowerIdentifier) ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard?.PrintedRole != MainRoleType.Scapegoat ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(
					identity.ActingPlayerId,
					out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Alive,
					CurrentRole: MainRoleType.Actor,
					PhysicalCharacterCardRole: MainRoleType.Actor,
					PubliclyRevealedRole: MainRoleType.Actor
				} ||
				entry.TriggeringVoteOutcomeLogIndex < 0 ||
				entry.TriggeringVoteOutcomeLogIndex >= markerLogIndex ||
				history[entry.TriggeringVoteOutcomeLogIndex] is not
					VoteOutcomeReportedLogEntry
					{
						ReportedOutcomePlayerId: var reportedOutcome,
						CurrentPhase: GamePhase.Day
					} triggeringVote ||
				reportedOutcome != Guid.Empty ||
				triggeringVote.TurnNumber != entry.TurnNumber ||
				voteOrdinal != entry.VoteOrdinal ||
				!StringComparer.Ordinal.Equals(
					entry.CascadeScopeId,
					expectedScopeId) ||
				kernel._actorBorrowedScapegoatTieReplacementCommits.Any() ||
				kernel._actorBorrowedScapegoatVoterRestrictionCommits.Any() ||
				history.OfType<ScapegoatTieReplacementLogEntry>()
					.Any(replacement =>
						StringComparer.Ordinal.Equals(
							replacement.ScopeId,
							entry.CascadeScopeId)))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Scapegoat tie replacement is stale or invalid.");
			}

			var commit = new ActorBorrowedScapegoatTieReplacementCommit(
				identity,
				entry.ActorSetupCardId,
				entry.TriggeringVoteOutcomeLogIndex,
				entry.VoteOrdinal,
				entry.CascadeScopeId,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedScapegoatTieReplacementCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedScapegoatVoterRestriction(
			ActorBorrowedScapegoatVoterRestrictionCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var history = kernel._gameHistoryLog.GetAllLogEntries();
			var markerLogIndex = history.Count;
			var tieReplacement =
				kernel._actorBorrowedScapegoatTieReplacementCommits
					.SingleOrDefault(commit =>
						commit.PowerIdentity == identity &&
						commit.PublicMarkerLogIndex ==
							entry.TieReplacementPublicMarkerLogIndex &&
						StringComparer.Ordinal.Equals(
							commit.CascadeScopeId,
							entry.CascadeScopeId));
			var livingPlayerIds = kernel._players.Values
				.Where(player =>
					((IPlayer)player).State.Health == PlayerHealth.Alive)
				.Select(player => player.Id)
				.ToHashSet();
			var candidatePlayerIds = entry.CandidatePlayerIds.ToHashSet();
			var permittedVoterIds = entry.PermittedVoterIds.ToHashSet();
			var hasSacrificeElimination = tieReplacement is not null &&
				history
					.Skip(tieReplacement.PublicMarkerLogIndex + 1)
					.OfType<PlayerEliminatedLogEntry>()
					.Any(elimination =>
						elimination.PlayerId == identity.ActingPlayerId &&
						elimination.Reason ==
							EliminationReason.EventElimination);
			var hasSacrificeBatch = history
				.OfType<EliminationCascadeBatchResolvedLogEntry>()
				.Any(batch =>
					StringComparer.Ordinal.Equals(
						batch.ScopeId,
						entry.CascadeScopeId) &&
					batch.CommittedEliminations.Any(elimination =>
						elimination.PlayerId == identity.ActingPlayerId &&
						elimination.Reason ==
							EliminationReason.EventElimination));
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Day ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.Scapegoat ||
				identity.SourceRole != active.SourceRole ||
				!StringComparer.Ordinal.Equals(
					identity.SourcePowerIdentifier,
					ActorBorrowedScapegoatVoterRestrictionCommit
						.ExpectedSourcePowerIdentifier) ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard?.PrintedRole != MainRoleType.Scapegoat ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(
					identity.ActingPlayerId,
					out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Dead,
					CurrentRole: MainRoleType.Actor,
					PhysicalCharacterCardRole: MainRoleType.Actor,
					PubliclyRevealedRole: MainRoleType.Actor
				} ||
				tieReplacement is null ||
				tieReplacement.ActorSetupCardId != entry.ActorSetupCardId ||
				tieReplacement.TurnNumber != entry.TurnNumber ||
				tieReplacement.CurrentPhase != entry.CurrentPhase ||
				entry.TieReplacementPublicMarkerLogIndex >= markerLogIndex ||
				history[entry.TieReplacementPublicMarkerLogIndex] is not
					ActorBorrowedRolePowerCommittedLogEntry ||
				!hasSacrificeElimination ||
				!hasSacrificeBatch ||
				entry.CandidatePlayerIds.Count != candidatePlayerIds.Count ||
				!candidatePlayerIds.SetEquals(livingPlayerIds) ||
				entry.PermittedVoterIds.Count != permittedVoterIds.Count ||
				permittedVoterIds.Count == 0 ||
				!permittedVoterIds.IsSubsetOf(candidatePlayerIds) ||
				entry.AppliesOnTurnNumber != entry.TurnNumber + 1 ||
				kernel._actorBorrowedScapegoatVoterRestrictionCommits.Any() ||
				history.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
					.Any(restriction =>
						StringComparer.Ordinal.Equals(
							restriction.ScopeId,
							entry.CascadeScopeId)))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Scapegoat voter restriction is stale or invalid.");
			}

			var commit = new ActorBorrowedScapegoatVoterRestrictionCommit(
				identity,
				entry.ActorSetupCardId,
				entry.TieReplacementPublicMarkerLogIndex,
				entry.CascadeScopeId,
				entry.CandidatePlayerIds.ToArray(),
				entry.PermittedVoterIds.ToArray(),
				entry.AppliesOnTurnNumber,
				entry.AnnouncementInstructionId,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedScapegoatVoterRestrictionCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedVillageIdiotPardon(
			ActorBorrowedVillageIdiotPardonCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var markerLogIndex = kernel._gameHistoryLog.GetAllLogEntries().Count;
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Day ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.VillageIdiot ||
				identity.SourceRole != active.SourceRole ||
				!StringComparer.Ordinal.Equals(
					identity.SourcePowerIdentifier,
					ActorBorrowedVillageIdiotPardonCommit
						.ExpectedSourcePowerIdentifier) ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard?.PrintedRole != MainRoleType.VillageIdiot ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(identity.ActingPlayerId, out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Alive,
					CurrentRole: MainRoleType.Actor,
					DurableVotingPower: 1
				} ||
				entry.SpentResourceIdentity.ActingPlayerId !=
					identity.ActingPlayerId ||
				entry.SpentResourceIdentity.SourceRole != identity.SourceRole ||
				!StringComparer.Ordinal.Equals(
					entry.SpentResourceIdentity.SourcePowerIdentifier,
					identity.SourcePowerIdentifier) ||
				entry.SpentResourceIdentity.PowerInstanceId !=
					identity.PowerInstanceId ||
				entry.SpentResourceIdentity.PowerInstanceOrigin !=
					identity.PowerInstanceOrigin ||
				entry.SpentResourceIdentity.OneUseResourceId !=
					ActorBorrowedVillageIdiotPardonCommit.ExpectedResourceId ||
				kernel._actorBorrowedVillageIdiotPardonCommits.Any(commit =>
					commit.SpentResourceIdentity == entry.SpentResourceIdentity))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Role Power commit is stale or invalid.");
			}

			var commit = new ActorBorrowedVillageIdiotPardonCommit(
				identity,
				entry.ActorSetupCardId,
				entry.SpentResourceIdentity,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedVillageIdiotPardonCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedWitchPotionUse(
			ActorBorrowedWitchPotionUseCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var markerLogIndex = kernel._gameHistoryLog.GetAllLogEntries().Count;
			var isHealing = entry.SpentResourceIdentity.OneUseResourceId ==
				ActorBorrowedWitchPotionUseCommit.HealingResourceId;
			var isPoison = entry.SpentResourceIdentity.OneUseResourceId ==
				ActorBorrowedWitchPotionUseCommit.PoisonResourceId;
			var physicalAttackTypes = new HashSet<NightActionType>
			{
				NightActionType.WerewolfVictimSelection,
				NightActionType.WhiteWerewolfVictimSelection,
				NightActionType.BigBadWolfVictimSelection
			};
			var isPhysicalAttackTarget = kernel._gameHistoryLog
				.GetAllLogEntries()
				.OfType<NightActionLogEntry>()
				.Any(candidate =>
					candidate.TurnNumber == kernel.TurnNumber &&
					candidate.CurrentPhase == GamePhase.Night &&
					physicalAttackTypes.Contains(candidate.ActionType) &&
					candidate.TargetIds?.Contains(entry.TargetPlayerId) == true);
			var targetWasHealed = kernel._actorBorrowedWitchPotionUseCommits
				.Any(commit =>
					commit.TurnNumber == kernel.TurnNumber &&
					commit.SpentResourceIdentity.OneUseResourceId ==
						ActorBorrowedWitchPotionUseCommit.HealingResourceId &&
					commit.TargetPlayerId == entry.TargetPlayerId);
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Night ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.Witch ||
				identity.SourceRole != active.SourceRole ||
				!StringComparer.Ordinal.Equals(
					identity.SourcePowerIdentifier,
					ActorBorrowedWitchPotionUseCommit
						.ExpectedSourcePowerIdentifier) ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard?.PrintedRole != MainRoleType.Witch ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(identity.ActingPlayerId, out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Alive,
					CurrentRole: MainRoleType.Actor
				} ||
				!kernel._players.TryGetValue(entry.TargetPlayerId, out var target) ||
				((IPlayer)target).State.Health != PlayerHealth.Alive ||
				!isHealing && !isPoison ||
				isHealing && !isPhysicalAttackTarget ||
				isPoison &&
				(entry.TargetPlayerId == identity.ActingPlayerId || targetWasHealed) ||
				kernel._actorBorrowedWitchPotionUseCommits.Any(commit =>
					commit.SpentResourceIdentity == entry.SpentResourceIdentity) ||
				isHealing && kernel._actorBorrowedWitchPotionUseCommits.Any(commit =>
					commit.PowerIdentity == identity &&
					commit.SpentResourceIdentity.OneUseResourceId ==
						ActorBorrowedWitchPotionUseCommit.PoisonResourceId))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Witch potion use is stale or invalid.");
			}

			var commit = new ActorBorrowedWitchPotionUseCommit(
				identity,
				entry.ActorSetupCardId,
				entry.SpentResourceIdentity,
				entry.TargetPlayerId,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedWitchPotionUseCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedWitchPotionDecline(
			ActorBorrowedWitchPotionDeclineCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var markerLogIndex = kernel._gameHistoryLog.GetAllLogEntries().Count;
			var resourceId = entry.OfferedResourceIdentity.OneUseResourceId;
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Night ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.Witch ||
				identity.SourceRole != active.SourceRole ||
				!StringComparer.Ordinal.Equals(
					identity.SourcePowerIdentifier,
					ActorBorrowedWitchPotionUseCommit
						.ExpectedSourcePowerIdentifier) ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard?.PrintedRole != MainRoleType.Witch ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(identity.ActingPlayerId, out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Alive,
					CurrentRole: MainRoleType.Actor
				} ||
				entry.OfferedResourceIdentity.ActingPlayerId !=
					identity.ActingPlayerId ||
				entry.OfferedResourceIdentity.SourceRole != identity.SourceRole ||
				!StringComparer.Ordinal.Equals(
					entry.OfferedResourceIdentity.SourcePowerIdentifier,
					identity.SourcePowerIdentifier) ||
				entry.OfferedResourceIdentity.PowerInstanceId !=
					identity.PowerInstanceId ||
				entry.OfferedResourceIdentity.PowerInstanceOrigin !=
					identity.PowerInstanceOrigin ||
				resourceId !=
					ActorBorrowedWitchPotionUseCommit.HealingResourceId &&
				resourceId !=
					ActorBorrowedWitchPotionUseCommit.PoisonResourceId ||
				kernel._actorBorrowedWitchPotionUseCommits.Any(commit =>
					commit.SpentResourceIdentity ==
						entry.OfferedResourceIdentity) ||
				kernel._actorBorrowedWitchPotionDeclineCommits.Any(commit =>
					commit.OfferedResourceIdentity ==
						entry.OfferedResourceIdentity))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Witch potion decline is stale or invalid.");
			}

			var commit = new ActorBorrowedWitchPotionDeclineCommit(
				identity,
				entry.ActorSetupCardId,
				entry.OfferedResourceIdentity,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedWitchPotionDeclineCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedCupidLovers(
			ActorBorrowedCupidLoversCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var markerLogIndex = kernel._gameHistoryLog.GetAllLogEntries().Count;
			var playerIds = new[] { entry.FirstPlayerId, entry.SecondPlayerId };
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Night ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.Cupid ||
				identity.SourceRole != active.SourceRole ||
				!StringComparer.Ordinal.Equals(
					identity.SourcePowerIdentifier,
					ActorBorrowedCupidLoversCommit
						.ExpectedSourcePowerIdentifier) ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard?.PrintedRole != MainRoleType.Cupid ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(identity.ActingPlayerId, out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Alive,
					CurrentRole: MainRoleType.Actor
				} ||
				entry.FirstPlayerId.CompareTo(entry.SecondPlayerId) >= 0 ||
				playerIds.Any(playerId =>
					!kernel._players.TryGetValue(playerId, out var player) ||
					((IPlayer)player).State.Health != PlayerHealth.Alive) ||
				entry.TurnNumber == 1 &&
				entry.Disposition != ActorBorrowedCupidLoversDisposition
					.DeferredToInitialBeneficiaryClosure ||
				entry.TurnNumber > 1 &&
				entry.Disposition == ActorBorrowedCupidLoversDisposition
					.DeferredToInitialBeneficiaryClosure ||
				kernel._actorBorrowedCupidLoversCommits.Count > 0 ||
				kernel._gameHistoryLog.GetAllLogEntries()
					.OfType<LoversPairCommittedLogEntry>()
					.Any())
			{
				throw new InvalidOperationException(
					"The Actor borrowed Cupid Lovers commit is stale or invalid.");
			}

			var commit = new ActorBorrowedCupidLoversCommit(
				identity,
				entry.ActorSetupCardId,
				entry.FirstPlayerId,
				entry.SecondPlayerId,
				entry.Disposition,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			if (commit.Disposition ==
				ActorBorrowedCupidLoversDisposition.CrossFaction)
			{
				ApplyFactionFacts(commit);
			}
			SetStatusEffect(
				commit.FirstPlayerId,
				StatusEffectTypes.Lovers,
				isActive: true);
			SetStatusEffect(
				commit.SecondPlayerId,
				StatusEffectTypes.Lovers,
				isActive: true);
			kernel._actorBorrowedCupidLoversCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public void ApplyActorBorrowedCupidInitialBeneficiaryClosure(
			ActorBorrowedCupidInitialBeneficiaryClosureCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			entry.EnforceValidity();
			var publicClosure = entry.PublicClosureEntry;
			var expected = entry.ExpectedDeferredCommit;
			var history = kernel._gameHistoryLog.GetAllLogEntries();
			var commitIndex = kernel._actorBorrowedCupidLoversCommits
				.FindIndex(commit => commit == expected);
			if (entry.TurnNumber != kernel.TurnNumber ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != 1 ||
				entry.CurrentPhase != GamePhase.Night ||
				commitIndex < 0 ||
				history.OfType<FactionFactsCommittedLogEntry>().Any(candidate =>
					candidate.Source.Kind ==
					FactionFactSourceKind.InitialBeneficiaryClosure) ||
				entry.ResolvedDisposition ==
					ActorBorrowedCupidLoversDisposition.CrossFaction &&
				publicClosure.Facts.Any(fact =>
					expected.PlayerIds.Contains(fact.PlayerId)))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Cupid Initial Beneficiary Closure transaction is stale or invalid.");
			}

			var resolved = expected with
			{
				Disposition = entry.ResolvedDisposition
			};
			resolved.EnforceValidity();
			var privateCupidFactBatches = kernel
				._actorBorrowedCupidLoversCommits
				.Select((commit, index) =>
					index == commitIndex ? resolved : commit)
				.Cast<IFactionFactBatchLogEntry>()
				.ToArray();
			var projection = FactionFactProjection.Create(
				history
					.OfType<IFactionFactBatchLogEntry>()
					.Concat(privateCupidFactBatches)
					.Append(publicClosure),
				kernel._playerSeatingOrder);
			if (kernel._playerSeatingOrder.Any(playerId =>
				!projection.Beneficiaries[playerId].IsKnown))
			{
				throw new InvalidOperationException(
					"Initial Beneficiary Closure must establish every Player beneficiary.");
			}

			kernel._actorBorrowedCupidLoversCommits[commitIndex] = resolved;
			foreach (var playerId in kernel._playerSeatingOrder)
			{
				GetMutablePlayerState(playerId).ReplaceFactionProjection(
					projection.Beneficiaries[playerId],
					projection.Agents[playerId]);
			}
		}

		public string ApplyActorBorrowedStutteringJudgeSignalSetup(
			ActorBorrowedStutteringJudgeSignalSetupCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var active = kernel._activeActorBorrowedRolePowerActivation;
			var selectedCard = active is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == active.SelectedCardId);
			var markerLogIndex = kernel._gameHistoryLog.GetAllLogEntries().Count;
			if (active is null ||
				entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Night ||
				identity.ActingPlayerId != active.ActingPlayerId ||
				identity.SourceRole != MainRoleType.StutteringJudge ||
				identity.SourceRole != active.SourceRole ||
				identity.PowerInstanceId != active.ActivationId ||
				identity.PowerInstanceOrigin != RolePowerInstanceOrigin.Borrowed ||
				entry.ActorSetupCardId != active.SelectedCardId ||
				selectedCard is null ||
				selectedCard.PrintedRole != MainRoleType.StutteringJudge ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != active.ActivationId ||
				!kernel._players.TryGetValue(identity.ActingPlayerId, out var actor) ||
				((IPlayer)actor).State is not
				{
					Health: PlayerHealth.Alive,
					CurrentRole: MainRoleType.Actor
				} ||
				kernel._actorBorrowedStutteringJudgeSignalSetupCommits.Any(commit =>
					commit.PowerIdentity == identity))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Role Power commit is stale or invalid.");
			}

			var commit = new ActorBorrowedStutteringJudgeSignalSetupCommit(
				identity,
				entry.ActorSetupCardId,
				entry.Timestamp,
				entry.TurnNumber,
				entry.CurrentPhase,
				markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedStutteringJudgeSignalSetupCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
		}

		public string ApplyActorBorrowedStutteringJudgeSignalObservation(
			ActorBorrowedStutteringJudgeSignalObservationCommandLogEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);
			var identity = entry.PowerIdentity;
			var markerLogIndex = kernel._gameHistoryLog.GetAllLogEntries().Count;
			var setup = kernel._actorBorrowedStutteringJudgeSignalSetupCommits
				.SingleOrDefault(commit => commit.PowerIdentity == identity);
			var selectedCard = setup is null
				? null
				: kernel._actorSetupCards.Cards.SingleOrDefault(card =>
					card.Id == setup.ActorSetupCardId);
			var pendingSignalInstruction =
				kernel._pendingModeratorInstruction as SelectOptionsInstruction;
			if (entry.CurrentPhase != kernel.CurrentPhase ||
				entry.TurnNumber != kernel.TurnNumber ||
				kernel.CurrentPhase != GamePhase.Day ||
				setup is null ||
				setup.ActorSetupCardId != entry.ActorSetupCardId ||
				setup.TurnNumber != entry.TurnNumber ||
				setup.CurrentPhase != GamePhase.Night ||
				selectedCard?.PrintedRole != MainRoleType.StutteringJudge ||
				!kernel._actorSetupCardSpendActivationIds.TryGetValue(
					entry.ActorSetupCardId,
					out var spentActivationId) ||
				spentActivationId != identity.PowerInstanceId ||
				!kernel._players.ContainsKey(identity.ActingPlayerId) ||
				kernel._phaseStateCache.GetSubPhaseId() != "NormalVoting" ||
				kernel._phaseStateCache.GetActiveSubPhaseStage() !=
					GameHook.OnVoteConducted.ToString() ||
				kernel._phaseStateCache.GetCurrentListener() !=
					ListenerIdentifier.Listener(MainRoleType.StutteringJudge) ||
				pendingSignalInstruction?.Semantic !=
					ModeratorInstructionSemantic.ObserveStutteringJudgeSignal ||
				pendingSignalInstruction.SelectionRange !=
					NumberRangeConstraint.Single ||
				pendingSignalInstruction.AffectedPlayerIds is not
					[var affectedPlayerId] ||
				affectedPlayerId != identity.ActingPlayerId ||
				kernel._gameHistoryLog.GetAllLogEntries()
					.OfType<VoteOutcomeReportedLogEntry>()
					.Any(outcome =>
						outcome.CurrentPhase == GamePhase.Day &&
						outcome.TurnNumber == entry.TurnNumber) ||
				kernel._actorBorrowedStutteringJudgeSignalObservationCommits
					.Any(commit => commit.PowerIdentity == identity))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Stuttering Judge signal observation is stale or invalid.");
			}

			var commit =
				new ActorBorrowedStutteringJudgeSignalObservationCommit(
					identity,
					entry.ActorSetupCardId,
					entry.SignalOccurred,
					entry.SpentResourceIdentity,
					entry.Timestamp,
					entry.TurnNumber,
					entry.CurrentPhase,
					markerLogIndex);
			commit.EnforceValidity();
			kernel._actorBorrowedStutteringJudgeSignalObservationCommits.Add(commit);
			return ActorBorrowedRolePowerCommitment.Create(
				kernel._actorBorrowedRolePowerCommitmentKey,
				commit);
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
