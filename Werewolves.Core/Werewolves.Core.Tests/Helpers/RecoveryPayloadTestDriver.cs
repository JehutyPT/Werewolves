using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.Tests.Helpers;

/// <summary>
/// Narrow typed seam for constructing contradictory recovery payloads.
/// Integration tests still enter through opaque Serialize/Rehydrate behavior.
/// </summary>
internal sealed class RecoveryPayloadTestDriver
{
	private static readonly JsonSerializerOptions SerializationOptions = new()
	{
		Converters =
		{
			new GameResultConverter(),
			new GameLogEntryConverter(),
			new ModeratorInstructionConverter(),
			new JsonStringEnumConverter()
		}
	};

	private readonly GameSessionDto _payload;

	private RecoveryPayloadTestDriver(GameSessionDto payload)
	{
		_payload = payload;
	}

	internal static RecoveryPayloadTestDriver Parse(string serializedSession)
	{
		var payload = JsonSerializer.Deserialize<GameSessionDto>(
			serializedSession,
			SerializationOptions)
			?? throw new InvalidOperationException(
				"The recovery test payload could not be deserialized.");
		return new RecoveryPayloadTestDriver(payload);
	}

	internal RecoveryPayloadTestDriver RewriteRolesInPlay(
		IEnumerable<MainRoleType> roles)
	{
		ArgumentNullException.ThrowIfNull(roles);
		_payload.RolesInPlay = roles.ToList();
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteSeatingOrder(
		IEnumerable<Guid> playerIds)
	{
		ArgumentNullException.ThrowIfNull(playerIds);
		_payload.SeatingOrder = playerIds.ToList();
		return this;
	}

	internal RecoveryPayloadTestDriver RemovePublicGroupPartition()
	{
		_payload.PublicGroupPartition = null;
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePublicGroupPartition(
		IEnumerable<Guid> firstGroupPlayerIds,
		IEnumerable<Guid> secondGroupPlayerIds)
	{
		ArgumentNullException.ThrowIfNull(firstGroupPlayerIds);
		ArgumentNullException.ThrowIfNull(secondGroupPlayerIds);
		_payload.PublicGroupPartition = new PublicGroupPartitionDto
		{
			FirstGroupPlayerIds = firstGroupPlayerIds.ToList(),
			SecondGroupPlayerIds = secondGroupPlayerIds.ToList()
		};
		return this;
	}

	internal RecoveryPayloadTestDriver RemoveAngelExpiry()
	{
		var index = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is AngelExpiredLogEntry);
		if (index < 0)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Angel expiry.");
		}

		_payload.GameHistoryLog.RemoveAt(index);
		return this;
	}

	internal RecoveryPayloadTestDriver DuplicateAngelExpiry()
	{
		var index = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is AngelExpiredLogEntry);
		if (index < 0)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Angel expiry.");
		}

		_payload.GameHistoryLog.Insert(
			index + 1,
			_payload.GameHistoryLog[index]);
		return this;
	}

	internal RecoveryPayloadTestDriver DuplicatePostExpirySimpleVillagerProjection()
	{
		var expiryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is AngelExpiredLogEntry);
		var projectionIndex = _payload.GameHistoryLog.FindIndex(
			expiryIndex + 1,
			entry => entry is AssignRoleLogEntry
			{
				AssignedMainRole: MainRoleType.SimpleVillager
			});
		if (expiryIndex < 0 || projectionIndex < 0)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no post-expiry Simple Villager projection.");
		}

		_payload.GameHistoryLog.Insert(
			projectionIndex + 1,
			_payload.GameHistoryLog[projectionIndex]);
		return this;
	}

	internal RecoveryPayloadTestDriver MoveKnownAngelProjectionToHistoryTail()
	{
		var expiryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is AngelExpiredLogEntry);
		if (expiryIndex < 0 ||
			expiryIndex + 1 >= _payload.GameHistoryLog.Count ||
			_payload.GameHistoryLog[expiryIndex + 1] is not AssignRoleLogEntry
			{
				AssignedMainRole: MainRoleType.SimpleVillager
			} projection)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no immediate known-holder Angel projection.");
		}

		_payload.GameHistoryLog.RemoveAt(expiryIndex + 1);
		_payload.GameHistoryLog.Add(projection);
		return this;
	}

	internal RecoveryPayloadTestDriver AppendAngelVictory(
		int turnNumber,
		GamePhase phase,
		VictoryCheckWindow window)
	{
		_payload.GameHistoryLog.Add(new VictoryConditionMetLogEntry
		{
			Timestamp = _payload.GameHistoryLog[^1].Timestamp.AddTicks(1),
			TurnNumber = turnNumber,
			CurrentPhase = phase,
			GameResult = new SingleFactionGameResult(Faction.Angel),
			VictoryCheckWindow = window
		});
		return this;
	}

	internal RecoveryPayloadTestDriver MismatchOneUseResource(
		Guid cursorResourceId)
	{
		RequireDomainCursor().OneUseResourceId = cursorResourceId;
		return this;
	}

	internal RecoveryPayloadTestDriver OmitSourceRole()
	{
		RequireDomainCursor().SourceRole = null;
		return this;
	}

	internal RecoveryPayloadTestDriver OmitPowerInstanceOrigin()
	{
		RequireDomainCursor().PowerInstanceOrigin = null;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestOneUseAction(
		NightActionType actionType)
	{
		RequireDomainCursor().CommittedActionType = actionType;
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is OneUseRolePowerCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    OneUseRolePowerCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed One-Use Resource action.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			ActionType = actionType
		};
		return this;
	}

	internal RecoveryPayloadTestDriver RetargetLatestOneUseActionAndCursor(
		Guid targetId)
	{
		if (targetId == Guid.Empty)
		{
			throw new ArgumentException(
				"A recovery test target cannot be empty.",
				nameof(targetId));
		}

		RequireDomainCursor().CommittedTargetIds = [targetId];
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is OneUseRolePowerCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    OneUseRolePowerCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed One-Use Resource action.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			TargetIds = [targetId]
		};
		return this;
	}

	internal RecoveryPayloadTestDriver
		RetargetLatestRecurringNightActionAndCursor(Guid targetId)
	{
		if (targetId == Guid.Empty)
		{
			throw new ArgumentException(
				"A recovery test target cannot be empty.",
				nameof(targetId));
		}

		RequireDomainCursor().CommittedTargetIds = [targetId];
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is RecurringRolePowerCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    RecurringRolePowerCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed recurring Night action.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			TargetIds = [targetId]
		};
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringCursorTargets(
		params Guid[] targetIds)
	{
		ArgumentNullException.ThrowIfNull(targetIds);
		if (targetIds.Length == 0 ||
		    targetIds.Any(targetId => targetId == Guid.Empty) ||
		    targetIds.Distinct().Count() != targetIds.Length)
		{
			throw new ArgumentException(
				"Recovery test targets must contain one or more distinct, non-empty GUIDs.",
				nameof(targetIds));
		}

		RequireDomainCursor().CommittedTargetIds = targetIds.ToList();
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringCursorSourceRole(
		MainRoleType sourceRole)
	{
		RequireDomainCursor().SourceRole = sourceRole;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringActorAndCursor(
		Guid actingPlayerId)
	{
		var cursor = RequireDomainCursor();
		cursor.ActingPlayerId = actingPlayerId;
		cursor.PowerInstanceId = actingPlayerId;
		RewriteLatestRecurringEntry(entry => entry with
		{
			ActingPlayerId = actingPlayerId,
			PowerInstanceId = actingPlayerId
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringSourceRoleAndCursor(
		MainRoleType sourceRole)
	{
		RequireDomainCursor().SourceRole = sourceRole;
		RewriteLatestRecurringEntry(entry => entry with
		{
			SourceRole = sourceRole
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringPowerAndCursor(
		string sourcePowerIdentifier)
	{
		RequireDomainCursor().SourcePowerIdentifier =
			sourcePowerIdentifier;
		RewriteLatestRecurringEntry(entry => entry with
		{
			SourcePowerIdentifier = sourcePowerIdentifier
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringInstanceAndCursor(
		Guid powerInstanceId)
	{
		RequireDomainCursor().PowerInstanceId = powerInstanceId;
		RewriteLatestRecurringEntry(entry => entry with
		{
			PowerInstanceId = powerInstanceId
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringOriginAndCursor(
		RolePowerInstanceOrigin powerInstanceOrigin)
	{
		RequireDomainCursor().PowerInstanceOrigin = powerInstanceOrigin;
		RewriteLatestRecurringEntry(entry => entry with
		{
			PowerInstanceOrigin = powerInstanceOrigin
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringActionAndCursor(
		NightActionType actionType)
	{
		RequireDomainCursor().CommittedActionType = actionType;
		RewriteLatestRecurringEntry(entry => entry with
		{
			ActionType = actionType
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringPhase(
		GamePhase phase)
	{
		RewriteLatestRecurringEntry(entry => entry with
		{
			CurrentPhase = phase
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringTurnNumber(
		int turnNumber)
	{
		RewriteLatestRecurringEntry(entry => entry with
		{
			TurnNumber = turnNumber
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteSessionTurnNumber(
		int turnNumber)
	{
		_payload.TurnNumber = turnNumber;
		return this;
	}

	internal RecoveryPayloadTestDriver RemoveDomainRecoveryCursor()
	{
		_payload.DomainRecoveryCursor = null;
		return this;
	}

	internal RecoveryPayloadTestDriver
		DowngradeLatestRecurringCommitToLegacyNightAction()
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is RecurringRolePowerCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    RecurringRolePowerCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed recurring Night action.");
		}

		_payload.GameHistoryLog[entryIndex] = new NightActionLogEntry
		{
			Timestamp = entry.Timestamp,
			TurnNumber = entry.TurnNumber,
			CurrentPhase = entry.CurrentPhase,
			ActionType = entry.ActionType,
			TargetIds = entry.TargetIds
		};
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringNextSemantic(
		ModeratorInstructionSemantic semantic)
	{
		RequireDomainCursor().NextInstructionSemantic = semantic;
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePendingConfirmationSemantic(
		ModeratorInstructionSemantic semantic)
	{
		if (_payload.PendingInstruction is not
		    ConfirmationInstruction pending)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no pending confirmation.");
		}

		_payload.PendingInstruction = new ConfirmationInstruction(
			semantic,
			pending.PublicAnnouncement,
			pending.PrivateInstruction,
			pending.AffectedPlayerIds,
			pending.InstructionId);
		_payload.PendingInstructionSemantic = semantic;
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewritePendingConfirmationAffectedPlayer(Guid playerId)
	{
		if (_payload.PendingInstruction is not
		    ConfirmationInstruction pending)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no pending confirmation.");
		}

		_payload.PendingInstruction = new ConfirmationInstruction(
			pending.Semantic,
			pending.PublicAnnouncement,
			pending.PrivateInstruction,
			[playerId],
			pending.InstructionId);
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePendingConfirmationPresentation(
		string? privateInstruction,
		IReadOnlyList<SoundEffectsEnum>? soundEffects)
	{
		if (_payload.PendingInstruction is not
		    ConfirmationInstruction pending)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no pending confirmation.");
		}

		_payload.PendingInstruction = new ConfirmationInstruction(
			pending.Semantic,
			pending.PublicAnnouncement,
			privateInstruction,
			pending.AffectedPlayerIds,
			pending.InstructionId,
			soundEffects);
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestStutteringJudgeAction(
		DayPowerType actionType)
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is
				OneUseRolePowerDayActionCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			OneUseRolePowerDayActionCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed Stuttering Judge vote.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			ActionType = actionType
		};
		return this;
	}

	internal RecoveryPayloadTestDriver TargetLatestStutteringJudgeAction()
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is OneUseRolePowerDayActionCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    OneUseRolePowerDayActionCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed Stuttering Judge vote.");
		}

		var targetId = _payload.Players
			.Select(player => player.Id)
			.First(id => id != entry.ResourceIdentity.ActingPlayerId);
		_payload.GameHistoryLog[entryIndex] = entry with
		{
			TargetIds = [targetId]
		};
		return this;
	}

	internal RecoveryPayloadTestDriver AddCrossTypeDuplicateOfStutteringJudgeResource()
	{
		var judgeCommit = _payload.GameHistoryLog
			.OfType<OneUseRolePowerDayActionCommittedLogEntry>()
			.LastOrDefault()
			?? throw new InvalidOperationException(
				"The recovery test payload has no committed Stuttering Judge vote.");
		var resourceIdentity = judgeCommit.ResourceIdentity;
		var targetId = _payload.Players
			.Select(player => player.Id)
			.First(id => id != resourceIdentity.ActingPlayerId);
		_payload.GameHistoryLog.Add(new OneUseRolePowerCommittedLogEntry
		{
			Timestamp = judgeCommit.Timestamp.AddTicks(1),
			TurnNumber = judgeCommit.TurnNumber,
			CurrentPhase = judgeCommit.CurrentPhase,
			ActionType = NightActionType.WitchKill,
			TargetIds = [targetId],
			ActingPlayerId = resourceIdentity.ActingPlayerId,
			SourceRole = resourceIdentity.SourceRole,
			SourcePowerIdentifier =
				resourceIdentity.SourcePowerIdentifier,
			PowerInstanceId = resourceIdentity.PowerInstanceId,
			PowerInstanceOrigin =
				resourceIdentity.PowerInstanceOrigin,
			OneUseResourceId = resourceIdentity.OneUseResourceId
		});
		return this;
	}

	internal RecoveryPayloadTestDriver
		InvalidateLatestVoterEligibilityRestrictionTurn()
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is VoterEligibilityRestrictionCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			VoterEligibilityRestrictionCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed voter-eligibility restriction.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			AppliesOnTurnNumber = entry.TurnNumber
		};
		return this;
	}

	internal RecoveryPayloadTestDriver RemoveInitialBeneficiaryClosureFact(
		Guid playerId)
	{
		if (playerId == Guid.Empty)
		{
			throw new ArgumentException(
				"A recovery test Player cannot be empty.",
				nameof(playerId));
		}

		var entryIndex = _payload.GameHistoryLog.FindLastIndex(entry =>
			entry is FactionFactsCommittedLogEntry facts &&
			facts.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    FactionFactsCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Initial Beneficiary Closure.");
		}

		var matchingFacts = entry.Facts
			.Where(fact =>
				fact.Type == FactionFactType.Beneficiary &&
				fact.PlayerId == playerId)
			.ToArray();
		if (matchingFacts.Length != 1)
		{
			throw new InvalidOperationException(
				"The recovery test payload must have exactly one matching Beneficiary fact.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			Facts = entry.Facts
				.Where(fact =>
					fact.Type != FactionFactType.Beneficiary ||
					fact.PlayerId != playerId)
				.ToImmutableArray()
		};
		var player = _payload.Players.SingleOrDefault(
			candidate => candidate.Id == playerId)
			?? throw new InvalidOperationException(
				"The recovery test payload has no matching Player.");
		player.FactionBeneficiary = FactionBeneficiaryKnowledge.Unknown;
		return this;
	}

	internal RecoveryPayloadTestDriver
		SwapInitialBeneficiaryClosureAssignmentsAndCaches(
			Guid firstPlayerId,
			Guid secondPlayerId)
	{
		if (firstPlayerId == Guid.Empty ||
		    secondPlayerId == Guid.Empty ||
		    firstPlayerId == secondPlayerId)
		{
			throw new ArgumentException(
				"Recovery test Players must be distinct and non-empty.");
		}

		var entryIndex = _payload.GameHistoryLog.FindLastIndex(entry =>
			entry is FactionFactsCommittedLogEntry facts &&
			facts.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    FactionFactsCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Initial Beneficiary Closure.");
		}

		var firstFact = RequireSingleBeneficiaryFact(
			entry,
			firstPlayerId);
		var secondFact = RequireSingleBeneficiaryFact(
			entry,
			secondPlayerId);
		_payload.GameHistoryLog[entryIndex] = entry with
		{
			Facts = entry.Facts
				.Select(fact =>
				{
					if (fact.PlayerId == firstPlayerId)
					{
						return FactionFact.Beneficiary(
							firstPlayerId,
							secondFact.Faction,
							fact.EffectiveBoundary,
							fact.BeneficiaryPrecedence!.Value);
					}

					return fact.PlayerId == secondPlayerId
						? FactionFact.Beneficiary(
							secondPlayerId,
							firstFact.Faction,
							fact.EffectiveBoundary,
							fact.BeneficiaryPrecedence!.Value)
						: fact;
				})
				.ToImmutableArray()
		};
		_payload.Players.Single(player => player.Id == firstPlayerId)
			.FactionBeneficiary =
			FactionBeneficiaryKnowledge.Known(secondFact.Faction);
		_payload.Players.Single(player => player.Id == secondPlayerId)
			.FactionBeneficiary =
			FactionBeneficiaryKnowledge.Known(firstFact.Faction);
		return this;
	}

	internal RecoveryPayloadTestDriver ReplacePendingInstructionWithConfirmation()
	{
		var pending = _payload.PendingInstruction
			?? throw new InvalidOperationException(
				"The recovery test payload has no pending instruction.");
		_payload.PendingInstruction = new ConfirmationInstruction(
			pending.Semantic,
			pending.PublicAnnouncement,
			pending.PrivateInstruction,
			pending.AffectedPlayerIds,
			pending.InstructionId);
		return this;
	}

	internal StatusEffectTypes GetActiveEffects(Guid playerId) =>
		_payload.Players.Single(player => player.Id == playerId).ActiveEffects;

	internal RecoveryPayloadTestDriver RewriteActiveEffects(
		Guid playerId,
		StatusEffectTypes activeEffects)
	{
		_payload.Players.Single(player => player.Id == playerId).ActiveEffects =
			activeEffects;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteCurrentRole(
		Guid playerId,
		MainRoleType currentRole)
	{
		_payload.Players.Single(player => player.Id == playerId).MainRole =
			currentRole;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteModeratorKnownRole(
		Guid playerId,
		MainRoleType? role)
	{
		_payload.Players.Single(player => player.Id == playerId)
			.ModeratorKnownRole = role;
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePubliclyRevealedRole(
		Guid playerId,
		MainRoleType? role)
	{
		_payload.Players.Single(player => player.Id == playerId)
			.PubliclyRevealedRole = role;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteVotingState(
		Guid playerId,
		bool hasVotingRight,
		int durableVotingPower)
	{
		var player = _payload.Players.Single(player => player.Id == playerId);
		player.HasVotingRight = hasVotingRight;
		player.DurableVotingPower = durableVotingPower;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestPermanentRoleSwapSource(
		FactionFactSourceKind sourceKind)
	{
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			Source = new FactionFactSource(
				sourceKind,
				entry.Source.Identifier)
		});
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteLatestPermanentRoleSwapSourceIdentifier(string identifier)
	{
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			Source = new FactionFactSource(entry.Source.Kind, identifier)
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestPermanentRoleSwapFacts(
		Func<ImmutableArray<FactionFact>, ImmutableArray<FactionFact>> rewrite)
	{
		ArgumentNullException.ThrowIfNull(rewrite);
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			Facts = rewrite(entry.Facts)
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestPermanentRoleSwapPolicy(
		Func<PermanentRoleSwapPolicy, PermanentRoleSwapPolicy> rewrite)
	{
		ArgumentNullException.ThrowIfNull(rewrite);
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			Policy = rewrite(entry.Policy)
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestThiefSwapAcquiredCard(
		Guid replacementCardId)
	{
		var swap = RequireLatestPermanentRoleSwap();
		var replacementCard = RequireRoleLockInCard(replacementCardId);
		var previousAcquiredCardId = swap.PhysicalCards.AcquiredCardId;
		var previousAcquired = RequirePhysicalCardState(previousAcquiredCardId);
		var replacement = RequirePhysicalCardState(replacementCardId);
		if (replacement.Zone != PhysicalCharacterCardZone.DealPool ||
		    replacement.OwnerPlayerId is not null)
		{
			throw new InvalidOperationException(
				"The replacement acquired card must be unused in the Deal Pool.");
		}

		previousAcquired.Zone = RequireOfferZone(previousAcquiredCardId);
		previousAcquired.OwnerPlayerId = null;
		replacement.Zone = PhysicalCharacterCardZone.PlayerOwned;
		replacement.OwnerPlayerId = swap.PlayerId;
		var player = _payload.Players.Single(player => player.Id == swap.PlayerId);
		player.MainRole = replacementCard.PrintedRole;
		player.ModeratorKnownRole = replacementCard.PrintedRole;
		player.PhysicalCharacterCardId = replacementCardId;
		player.PhysicalCharacterCardRole = replacementCard.PrintedRole;
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			NewCurrentRole = replacementCard.PrintedRole,
			PhysicalCards = new PermanentRoleSwapCardMovement(
				entry.PhysicalCards.OutgoingOwnedCardId,
				replacementCardId,
				entry.PhysicalCards.AdditionalSetAsideCardIds)
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestThiefSwapUnchosenCard(
		Guid replacementCardId)
	{
		var swap = RequireLatestPermanentRoleSwap();
		var previousUnchosenCardId = swap.PhysicalCards
			.AdditionalSetAsideCardIds.Single();
		var previousUnchosen = RequirePhysicalCardState(previousUnchosenCardId);
		var replacement = RequirePhysicalCardState(replacementCardId);
		if (replacement.Zone != PhysicalCharacterCardZone.DealPool ||
		    replacement.OwnerPlayerId is not null)
		{
			throw new InvalidOperationException(
				"The replacement unchosen card must be unused in the Deal Pool.");
		}

		previousUnchosen.Zone = RequireOfferZone(previousUnchosenCardId);
		previousUnchosen.OwnerPlayerId = null;
		replacement.Zone = PhysicalCharacterCardZone.SetAside;
		replacement.OwnerPlayerId = null;
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			PhysicalCards = new PermanentRoleSwapCardMovement(
				entry.PhysicalCards.OutgoingOwnedCardId,
				entry.PhysicalCards.AcquiredCardId,
				[replacementCardId])
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteThiefOfferPrintedRoles(
		MainRoleType offer1Role,
		MainRoleType offer2Role)
	{
		var lockIn = _payload.RoleLockIn
			?? throw new InvalidOperationException(
				"The recovery test payload has no Role Lock-In.");
		if (lockIn.Offer1CardId is not { } offer1CardId ||
		    lockIn.Offer2CardId is not { } offer2CardId)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Thief offers.");
		}

		RewriteCard(offer1CardId, offer1Role);
		RewriteCard(offer2CardId, offer2Role);
		return this;

		void RewriteCard(Guid cardId, MainRoleType printedRole)
		{
			var index = lockIn.RoleComposition.FindIndex(card => card.Id == cardId);
			if (index < 0)
			{
				throw new InvalidOperationException(
					"The recovery test payload is missing a Thief offer card.");
			}

			lockIn.RoleComposition[index] =
				new PhysicalCharacterCard(cardId, printedRole);
			var persistedState = RequirePhysicalCardState(cardId);
			persistedState.Zone = PhysicalCharacterCardZone.SetAside;
			persistedState.OwnerPlayerId = null;
		}
	}

	internal RecoveryPayloadTestDriver
		RewriteLatestPermanentRoleSwapBeneficiaryAndCache(Faction faction)
	{
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			Facts = entry.Facts.Select(fact =>
				fact.Type == FactionFactType.Beneficiary
					? FactionFact.Beneficiary(
						fact.PlayerId,
						faction,
						fact.EffectiveBoundary,
						fact.BeneficiaryPrecedence!.Value)
					: fact).ToImmutableArray()
		});
		var swap = _payload.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Last();
		_payload.Players.Single(player => player.Id == swap.PlayerId)
			.FactionBeneficiary = FactionBeneficiaryKnowledge.Known(faction);
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteLatestPermanentRoleSwapAgentAndCache(
			Faction faction,
			FactionAgentKnowledge agentKnowledge)
	{
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			Facts = entry.Facts.Select(fact =>
				fact.Type == FactionFactType.Agent && fact.Faction == faction
					? FactionFact.Agent(
						fact.PlayerId,
						faction,
						agentKnowledge,
						fact.EffectiveBoundary)
					: fact).ToImmutableArray()
		});
		var swap = _payload.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Last();
		_payload.Players.Single(player => player.Id == swap.PlayerId)
			.FactionAgentKnowledge![faction] = agentKnowledge;
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteLatestPermanentRoleSwapPowerInstanceId(Guid powerInstanceId)
	{
		if (powerInstanceId == Guid.Empty)
		{
			throw new ArgumentException(
				"A recovery test power instance cannot be empty.",
				nameof(powerInstanceId));
		}

		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			NewPowerInstanceId = powerInstanceId,
			Source = new FactionFactSource(
				FactionFactSourceKind.ExplicitTransition,
				$"permanent-role-swap:{entry.PlayerId:N}:{powerInstanceId:N}")
		});
		return this;
	}

	internal string Serialize() =>
		JsonSerializer.Serialize(_payload, SerializationOptions);

	private void RewriteLatestRecurringEntry(
		Func<
			RecurringRolePowerCommittedLogEntry,
			RecurringRolePowerCommittedLogEntry> rewrite)
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is RecurringRolePowerCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    RecurringRolePowerCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed recurring Night action.");
		}

		_payload.GameHistoryLog[entryIndex] = rewrite(entry);
	}

	private void RewriteLatestPermanentRoleSwap(
		Func<
			PermanentRoleSwapCommittedLogEntry,
			PermanentRoleSwapCommittedLogEntry> rewrite)
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is PermanentRoleSwapCommittedLogEntry);
		if (entryIndex < 0 ||
			_payload.GameHistoryLog[entryIndex] is not
				PermanentRoleSwapCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Permanent Role Swap.");
		}

		_payload.GameHistoryLog[entryIndex] = rewrite(entry);
	}

	private PermanentRoleSwapCommittedLogEntry RequireLatestPermanentRoleSwap() =>
		_payload.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.LastOrDefault()
		?? throw new InvalidOperationException(
			"The recovery test payload has no Permanent Role Swap.");

	private PhysicalCharacterCard RequireRoleLockInCard(Guid cardId) =>
		_payload.RoleLockIn?.RoleComposition
			.SingleOrDefault(card => card.Id == cardId)
		?? throw new InvalidOperationException(
			"The recovery test payload is missing a Physical Character Card.");

	private PhysicalCharacterCardStateDto RequirePhysicalCardState(Guid cardId) =>
		_payload.PhysicalCharacterCards
			.SingleOrDefault(state => state.CardId == cardId)
		?? throw new InvalidOperationException(
			"The recovery test payload is missing Physical Character Card state.");

	private PhysicalCharacterCardZone RequireOfferZone(Guid cardId)
	{
		var lockIn = _payload.RoleLockIn
			?? throw new InvalidOperationException(
				"The recovery test payload has no Role Lock-In.");
		return cardId == lockIn.Offer1CardId
			? PhysicalCharacterCardZone.Offer1
			: cardId == lockIn.Offer2CardId
				? PhysicalCharacterCardZone.Offer2
				: throw new InvalidOperationException(
					"The recovery test card is not a locked Thief offer.");
	}

	private static FactionFact RequireSingleBeneficiaryFact(
		FactionFactsCommittedLogEntry entry,
		Guid playerId)
	{
		var facts = entry.Facts
			.Where(fact =>
				fact.Type == FactionFactType.Beneficiary &&
				fact.PlayerId == playerId)
			.ToArray();
		return facts.Length == 1
			? facts[0]
			: throw new InvalidOperationException(
				"The recovery test payload must have exactly one matching Beneficiary fact.");
	}

	private DomainRecoveryCursor RequireDomainCursor() =>
		_payload.DomainRecoveryCursor
		?? throw new InvalidOperationException(
			"The recovery test payload has no committed domain continuation.");
}
