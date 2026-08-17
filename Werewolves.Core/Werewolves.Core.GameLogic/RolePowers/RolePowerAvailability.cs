using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.RolePowers;

internal enum RolePowerCategory
{
	Chosen,
	Automatic,
	Reactive,
	Passive,
	Recognition,
	Communication,
}

internal readonly record struct RolePowerIdentifier
{
	internal RolePowerIdentifier(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		Value = value;
	}

	internal string Value { get; }

	public override string ToString() => Value;
}

internal sealed record RolePowerDefinition(
	RolePowerIdentifier Identifier,
	RolePowerCategory Category);

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
}

internal sealed record RolePowerInstance(
	Guid Id,
	MainRoleType SourceRole,
	RolePowerDefinition SourcePower,
	RolePowerInstanceOrigin Origin)
{
	internal static RolePowerInstance CreateNative(
		IPlayer actingPlayer,
		MainRoleType sourceRole,
		RolePowerDefinition sourcePower)
	{
		ArgumentNullException.ThrowIfNull(actingPlayer);
		ArgumentNullException.ThrowIfNull(sourcePower);

		return new RolePowerInstance(
			actingPlayer.Id,
			sourceRole,
			sourcePower,
			RolePowerInstanceOrigin.Native);
	}

	internal static RolePowerInstance CreateCurrent(
		IGameSession session,
		IPlayer actingPlayer,
		MainRoleType sourceRole,
		RolePowerDefinition sourcePower)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(actingPlayer);
		ArgumentNullException.ThrowIfNull(sourcePower);
		var latestSwap = session.GameHistoryLog
			.OfType<IPermanentRoleSwapCommittedLogEntry>()
			.LastOrDefault(entry => entry.PlayerId == actingPlayer.Id);
		if (latestSwap is null)
		{
			return CreateNative(actingPlayer, sourceRole, sourcePower);
		}
		if (latestSwap.NewCurrentRole != sourceRole ||
			actingPlayer.State.CurrentRole != sourceRole)
		{
			throw new InvalidOperationException(
				"The current Role Power does not match the Player's latest Permanent Role Swap.");
		}

		return new RolePowerInstance(
			latestSwap.NewPowerInstanceId,
			sourceRole,
			sourcePower,
			RolePowerInstanceOrigin.Swapped);
	}

	internal static RolePowerInstance CreateBorrowed(
		GameSession session,
		IPlayer actingPlayer,
		MainRoleType sourceRole,
		RolePowerDefinition sourcePower) =>
		CreateBorrowedForActorHealth(
			session,
			actingPlayer,
			sourceRole,
			sourcePower,
			PlayerHealth.Alive);

	internal static RolePowerInstance CreateBorrowedAfterElimination(
		GameSession session,
		IPlayer actingPlayer,
		MainRoleType sourceRole,
		RolePowerDefinition sourcePower,
		BorrowedPostEliminationRolePowerContext context)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(actingPlayer);
		ArgumentNullException.ThrowIfNull(sourcePower);
		ArgumentNullException.ThrowIfNull(context);
		var history = session.GameHistoryLog.ToArray();
		var validContext = context switch
		{
			BorrowedPostEliminationRolePowerContext.HunterFinalShot hunter =>
				sourceRole == MainRoleType.Hunter &&
				sourcePower.Category == RolePowerCategory.Reactive &&
				StringComparer.Ordinal.Equals(
					sourcePower.Identifier.Value,
					"hunter-final-shot") &&
				!string.IsNullOrWhiteSpace(hunter.CascadeScopeId) &&
				hunter.TriggeringPlayerIds is { Count: > 0 } &&
				hunter.TriggeringPlayerIds.Contains(actingPlayer.Id) &&
				hunter.TriggeringPlayerIds.All(playerId =>
					playerId != Guid.Empty) &&
				hunter.TriggeringPlayerIds.Distinct().Count() ==
					hunter.TriggeringPlayerIds.Count &&
				EliminationCascadeStage.IsActiveInteractiveReactionBatch(
					session,
					hunter.CascadeScopeId,
					hunter.TriggeringPlayerIds) &&
				history.OfType<EliminationCascadeBatchResolvedLogEntry>()
					.Any(batch =>
						StringComparer.Ordinal.Equals(
							batch.ScopeId,
							hunter.CascadeScopeId) &&
						batch.CommittedEliminations
							.Select(elimination => elimination.PlayerId)
							.SequenceEqual(hunter.TriggeringPlayerIds)) &&
				!GameSessionQueries.IsEliminationCascadeComplete(
					session,
					hunter.CascadeScopeId),
			BorrowedPostEliminationRolePowerContext
				.ElderVillageVoteSuppression elder =>
				IsValidBorrowedElderSuppressionContext(
					session,
					actingPlayer,
					sourceRole,
					sourcePower,
					elder,
					history),
			BorrowedPostEliminationRolePowerContext.KnightRustySwordSchedule
				knight => IsValidBorrowedKnightScheduleContext(
					session,
					actingPlayer,
					sourceRole,
					sourcePower,
					knight,
					history),
			_ => false
		};
		if (!validContext)
		{
			throw new InvalidOperationException(
				"The borrowed post-elimination Role Power context is stale or invalid.");
		}

		return CreateBorrowedForActorHealth(
			session,
			actingPlayer,
			sourceRole,
			sourcePower,
			PlayerHealth.Dead);
	}

	private static bool IsValidBorrowedElderSuppressionContext(
		GameSession session,
		IPlayer actingPlayer,
		MainRoleType sourceRole,
		RolePowerDefinition sourcePower,
		BorrowedPostEliminationRolePowerContext.ElderVillageVoteSuppression
			context,
		IReadOnlyList<GameLogEntryBase> history)
	{
		var vote = GameSessionQueries.GetCurrentDayVoteOutcome(session);
		if (sourceRole != MainRoleType.Elder ||
			sourcePower.Category != RolePowerCategory.Reactive ||
			!StringComparer.Ordinal.Equals(
				sourcePower.Identifier.Value,
				"elder-village-vote-suppression") ||
			session.Execution.CurrentPhase != GamePhase.Day ||
			actingPlayer.State.PubliclyRevealedRole != MainRoleType.Actor ||
			vote is not { } currentVote ||
			currentVote.LogIndex != context.VoteOutcomeLogIndex ||
			currentVote.PlayerId != actingPlayer.Id ||
			context.VoteOutcomeLogIndex < 0 ||
			context.VoteOutcomeLogIndex >= history.Count ||
			history[context.VoteOutcomeLogIndex] is not
				VoteOutcomeReportedLogEntry
				{
					CurrentPhase: GamePhase.Day,
					ReportedOutcomePlayerId: var votedPlayerId
				} reportedVote ||
			reportedVote.TurnNumber != session.TurnNumber ||
			votedPlayerId != actingPlayer.Id ||
			!StringComparer.Ordinal.Equals(
				context.CascadeScopeId,
				$"Day:{session.TurnNumber}:Vote:{currentVote.VoteOrdinal}") ||
			GameSessionQueries.GetVillagerRolePowerSuppression(session) != null)
		{
			return false;
		}

		var correlatedHistory = history
			.Skip(currentVote.LogIndex + 1)
			.ToArray();
		if (correlatedHistory.Any(entry =>
				entry is VoteOutcomeReportedLogEntry laterVote &&
				laterVote.CurrentPhase == GamePhase.Day &&
				laterVote.TurnNumber == session.TurnNumber))
		{
			return false;
		}

		var revealIndex = Array.FindIndex(
			correlatedHistory,
			entry => entry is RoleRevealLogEntry reveal &&
				reveal.CurrentPhase == GamePhase.Day &&
				reveal.TurnNumber == session.TurnNumber &&
				reveal.RevealedRoles.TryGetValue(
					actingPlayer.Id,
					out var revealedRole) &&
				revealedRole == MainRoleType.Actor);
		var eliminationIndex = Array.FindIndex(
			correlatedHistory,
			entry => entry is PlayerEliminatedLogEntry
			{
				CurrentPhase: GamePhase.Day,
				PlayerId: var eliminatedPlayerId,
				Reason: EliminationReason.DayVote
			} eliminated &&
			eliminated.TurnNumber == session.TurnNumber &&
			eliminatedPlayerId == actingPlayer.Id);
		var expectedElimination = new EliminationCascadeElimination(
			actingPlayer.Id,
			EliminationReason.DayVote);
		var batchIndex = Array.FindIndex(
			correlatedHistory,
			entry => entry is EliminationCascadeBatchResolvedLogEntry batch &&
				batch.CurrentPhase == GamePhase.Day &&
				batch.TurnNumber == session.TurnNumber &&
				StringComparer.Ordinal.Equals(
					batch.ScopeId,
					context.CascadeScopeId) &&
				batch.RequestedEliminations is [var requested] &&
				requested == expectedElimination &&
				batch.CommittedEliminations is [var committed] &&
				committed == expectedElimination);
		var completionIndex = Array.FindIndex(
			correlatedHistory,
			entry => entry is EliminationCascadeCompletedLogEntry
			{
				CurrentPhase: GamePhase.Day,
				ScopeId: var completedScopeId
			} completion &&
			completion.TurnNumber == session.TurnNumber &&
			StringComparer.Ordinal.Equals(
				completedScopeId,
				context.CascadeScopeId));
		return revealIndex >= 0 &&
			eliminationIndex > revealIndex &&
			batchIndex > eliminationIndex &&
			completionIndex > batchIndex;
	}

	private static bool IsValidBorrowedKnightScheduleContext(
		GameSession session,
		IPlayer actingPlayer,
		MainRoleType sourceRole,
		RolePowerDefinition sourcePower,
		BorrowedPostEliminationRolePowerContext.KnightRustySwordSchedule
			context,
		IReadOnlyList<GameLogEntryBase> history)
	{
		if (sourceRole != MainRoleType.KnightWithRustySword ||
			sourcePower.Category != RolePowerCategory.Automatic ||
			!StringComparer.Ordinal.Equals(
				sourcePower.Identifier.Value,
				"knight-rusty-sword-disease") ||
			session.Execution.CurrentPhase != GamePhase.Dawn ||
			!StringComparer.Ordinal.Equals(
				context.CascadeScopeId,
				$"Dawn:{session.TurnNumber}") ||
			context.WerewolfAttackEliminationLogIndex < 0 ||
			context.WerewolfAttackEliminationLogIndex >= history.Count ||
			history[context.WerewolfAttackEliminationLogIndex] is not
				PlayerEliminatedLogEntry
				{
					CurrentPhase: GamePhase.Dawn,
					PlayerId: var eliminatedPlayerId,
					Reason: EliminationReason.WerewolfAttack
				} eliminated ||
			eliminated.TurnNumber != session.TurnNumber ||
			eliminatedPlayerId != actingPlayer.Id)
		{
			return false;
		}

		var determinationIndex = -1;
		for (var index = 0;
			index < context.WerewolfAttackEliminationLogIndex;
			index++)
		{
			if (history[index] is DawnVictimDeterminedLogEntry
				{
					CurrentPhase: GamePhase.Dawn,
					PlayerId: var determinedPlayerId,
					Reason: EliminationReason.WerewolfAttack
				} determination &&
				determination.TurnNumber == session.TurnNumber &&
				determinedPlayerId == actingPlayer.Id)
			{
				determinationIndex = index;
			}
		}

		var expectedElimination = new EliminationCascadeElimination(
			actingPlayer.Id,
			EliminationReason.WerewolfAttack);
		var batchIndex = -1;
		var completionIndex = -1;
		for (var index = context.WerewolfAttackEliminationLogIndex + 1;
			index < history.Count;
			index++)
		{
			if (batchIndex < 0 &&
				history[index] is EliminationCascadeBatchResolvedLogEntry batch &&
				batch.CurrentPhase == GamePhase.Dawn &&
				batch.TurnNumber == session.TurnNumber &&
				StringComparer.Ordinal.Equals(
					batch.ScopeId,
					context.CascadeScopeId) &&
				batch.RequestedEliminations.Contains(expectedElimination) &&
				batch.CommittedEliminations.Contains(expectedElimination))
			{
				batchIndex = index;
				continue;
			}

			if (batchIndex >= 0 &&
				history[index] is EliminationCascadeCompletedLogEntry
				{
					CurrentPhase: GamePhase.Dawn,
					ScopeId: var completedScopeId
				} completion &&
				completion.TurnNumber == session.TurnNumber &&
				StringComparer.Ordinal.Equals(
					completedScopeId,
					context.CascadeScopeId))
			{
				completionIndex = index;
				break;
			}
		}

		return determinationIndex >= 0 &&
			batchIndex > context.WerewolfAttackEliminationLogIndex &&
			completionIndex > batchIndex;
	}

	private static RolePowerInstance CreateBorrowedForActorHealth(
		GameSession session,
		IPlayer actingPlayer,
		MainRoleType sourceRole,
		RolePowerDefinition sourcePower,
		PlayerHealth expectedActorHealth)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(actingPlayer);
		ArgumentNullException.ThrowIfNull(sourcePower);
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		var selectedCard = activation is null
			? null
			: session.GetModeratorSpentActorSetupCards()
				.SingleOrDefault(card => card.Id == activation.SelectedCardId);
		var sessionActor = session.GetPlayers()
			.SingleOrDefault(player => player.Id == actingPlayer.Id);
		if (activation is null ||
		    selectedCard is null ||
		    !ReferenceEquals(sessionActor, actingPlayer) ||
		    actingPlayer.Id != activation.ActingPlayerId ||
		    actingPlayer.State.Health != expectedActorHealth ||
		    actingPlayer.State.CurrentRole != MainRoleType.Actor ||
		    activation.ActingRole != MainRoleType.Actor ||
		    activation.SourceRole != sourceRole ||
		    selectedCard.PrintedRole != sourceRole)
		{
			throw new InvalidOperationException(
				"The borrowed Role Power activation does not match its acting Player and source Role.");
		}

		return new RolePowerInstance(
			activation.ActivationId,
			sourceRole,
			sourcePower,
			RolePowerInstanceOrigin.Borrowed);
	}

	internal static RolePowerInstanceIdentity CreateCurrentIdentity(
		IGameSession session,
		IPlayer actingPlayer,
		MainRoleType sourceRole,
		RolePowerDefinition sourcePower)
	{
		var instance = CreateCurrent(
			session,
			actingPlayer,
			sourceRole,
			sourcePower);
		return new RolePowerInstanceIdentity(
			actingPlayer.Id,
			sourceRole,
			sourcePower.Identifier.Value,
			instance.Id,
			instance.Origin);
	}
}

internal sealed record OneUseRolePowerResource(
	Guid Id,
	RolePowerInstance OwningPowerInstance);

internal sealed record RolePowerAttempt(
	IGameSession Session,
	IPlayer ActingPlayer,
	MainRoleType SourceRole,
	RolePowerDefinition SourcePower,
	RolePowerInstance PowerInstance,
	OneUseRolePowerResource? OneUseResource = null);

internal sealed record RolePowerAvailabilityResult(bool IsAvailable)
{
	internal static RolePowerAvailabilityResult Allowed { get; } = new(true);
	internal static RolePowerAvailabilityResult Denied { get; } = new(false);
}

internal sealed record RolePowerExecutionContext(
	RolePowerAttempt Attempt,
	RolePowerAvailabilityResult AvailabilityResult)
{
	internal IGameSession Session => Attempt.Session;
	internal IPlayer ActingPlayer => Attempt.ActingPlayer;
	internal MainRoleType SourceRole => Attempt.SourceRole;
	internal RolePowerDefinition SourcePower => Attempt.SourcePower;
	internal RolePowerInstance PowerInstance => Attempt.PowerInstance;
	internal OneUseRolePowerResource? OneUseResource => Attempt.OneUseResource;
}

internal interface IRolePowerAvailabilityPolicy
{
	RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt);
}

internal sealed class AllowAllRolePowerAvailabilityPolicy : IRolePowerAvailabilityPolicy
{
	internal static AllowAllRolePowerAvailabilityPolicy Instance { get; } = new();

	private AllowAllRolePowerAvailabilityPolicy() { }

	public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt) =>
		RolePowerAvailabilityResult.Allowed;
}

internal sealed class VillagerRolePowerSuppressionPolicy : IRolePowerAvailabilityPolicy
{
	private readonly IRolePowerAvailabilityPolicy _next;

	internal VillagerRolePowerSuppressionPolicy(
		IRolePowerAvailabilityPolicy next)
	{
		ArgumentNullException.ThrowIfNull(next);
		_next = next;
	}

	public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
	{
		if (attempt.SourceRole.GetRoleGroup() == RoleGroup.Villagers &&
		    GameSessionQueries.IsVillagerRolePowerSuppressionActive(
			    attempt.Session))
		{
			return RolePowerAvailabilityResult.Denied;
		}

		return _next.Evaluate(attempt);
	}
}

internal sealed class RolePowerAvailabilityGateway
{
	private readonly IRolePowerAvailabilityPolicy _policy;

	internal RolePowerAvailabilityGateway(IRolePowerAvailabilityPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(policy);
		_policy = policy;
	}

	internal RolePowerExecutionContext Evaluate(RolePowerAttempt attempt)
	{
		ArgumentNullException.ThrowIfNull(attempt);
		ArgumentNullException.ThrowIfNull(attempt.Session);
		ArgumentNullException.ThrowIfNull(attempt.ActingPlayer);
		ArgumentNullException.ThrowIfNull(attempt.SourcePower);
		ArgumentNullException.ThrowIfNull(attempt.PowerInstance);
		ArgumentNullException.ThrowIfNull(attempt.PowerInstance.SourcePower);

		if (attempt.OneUseResource is not null)
		{
			ArgumentNullException.ThrowIfNull(
				attempt.OneUseResource.OwningPowerInstance);
		}

		if (attempt.SourceRole != attempt.PowerInstance.SourceRole)
		{
			throw new ArgumentException(
				"The concrete Role Power instance must belong to the attempt's source Role.",
				nameof(attempt));
		}

		if (attempt.SourcePower != attempt.PowerInstance.SourcePower)
		{
			throw new ArgumentException(
				"The concrete Role Power instance must implement the attempt's source power.",
				nameof(attempt));
		}

		if (attempt.OneUseResource is not null &&
		    attempt.OneUseResource.OwningPowerInstance != attempt.PowerInstance)
		{
			throw new ArgumentException(
				"The One-Use Resource must belong to the attempted concrete Role Power instance.",
				nameof(attempt));
		}

		var result = _policy.Evaluate(attempt) ??
			throw new InvalidOperationException(
				"The Role Power policy must return an availability result.");

		return new RolePowerExecutionContext(attempt, result);
	}
}
