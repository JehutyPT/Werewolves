using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
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
