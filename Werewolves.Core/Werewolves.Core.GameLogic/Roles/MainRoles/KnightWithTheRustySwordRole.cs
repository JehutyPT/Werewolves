using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum KnightWithTheRustySwordRoleState
{
	Complete
}

internal sealed class KnightWithTheRustySwordRole
	: RoleHookListener<KnightWithTheRustySwordRoleState>,
	  IEliminationCascadeReaction
{
	private static readonly RolePowerDefinition DiseasePower = new(
		new RolePowerIdentifier("knight-rusty-sword-disease"),
		RolePowerCategory.Automatic);
	private static readonly ActorBorrowedRolePowerSpec BorrowedDiseaseSpec = new(
		MainRoleType.KnightWithRustySword,
		DiseasePower);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	internal KnightWithTheRustySwordRole(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName =>
		GameStrings.KnightWithRustySwordRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.KnightWithRustySword);

	public string ReactionId =>
		EliminationCascadeReactionIds.RustySwordDiseaseAnnouncement;

	public EliminationCascadeReactionResult Advance(
		GameSession session,
		IReadOnlyCollection<Guid> eliminatedPlayerIds,
		ModeratorResponse input)
	{
		var pendingDawnVictims = GameSessionQueries
			.GetPendingDawnEliminations(session)
			.Where(victim => eliminatedPlayerIds.Contains(victim.Player.Id))
			.ToArray();
		if (pendingDawnVictims.All(victim =>
			    victim.Reason != EliminationReason.RustySword))
		{
			return EliminationCascadeReactionResult.Complete();
		}

		var pendingInstruction = session.Execution.PendingInstruction;
		if (pendingInstruction?.Semantic ==
		    ModeratorInstructionSemantic.AnnounceDawnVictims)
		{
			if (!MatchesRustySwordDawnAnnouncement(
					pendingInstruction,
					pendingDawnVictims,
					eliminatedPlayerIds) ||
			    input.InstructionId != pendingInstruction.InstructionId ||
			    input.Type != ExpectedInputType.Continue)
			{
				throw new InvalidOperationException(
					"The Rusty Sword disease announcement received an uncorrelated response.");
			}

			return EliminationCascadeReactionResult.Complete();
		}

		return EliminationCascadeReactionResult.NeedInput(
			CreateRustySwordDawnAnnouncement(
				pendingDawnVictims,
				eliminatedPlayerIds));
	}

	internal static void
		ValidateBorrowedPendingRustySwordRecoveryInstruction(
			GameSession session)
	{
		ValidateBorrowedRustySwordScheduleRecovery(session);

		var execution = session.Execution;
		var pendingInstruction = execution.PendingInstruction;
		if (execution.CurrentPhase != GamePhase.Dawn ||
			pendingInstruction?.Semantic !=
				ModeratorInstructionSemantic.AnnounceDawnVictims)
		{
			return;
		}

		var pendingDawnVictims = GameSessionQueries
			.GetPendingDawnEliminations(session)
			.ToArray();
		if (!HasCorrelatedBorrowedRustySwordSchedule(
				session,
				pendingDawnVictims))
		{
			return;
		}

		var affectedPlayerIds = pendingDawnVictims
			.Select(victim => victim.Player.Id)
			.ToArray();
		if (!MatchesRustySwordDawnAnnouncement(
				pendingInstruction,
				pendingDawnVictims,
				affectedPlayerIds))
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		}
	}

	private static void ValidateBorrowedRustySwordScheduleRecovery(
		GameSession session)
	{
		foreach (var schedule in
			session.GetActorBorrowedKnightRustySwordScheduleCommits())
		{
			if (!IsExpectedBorrowedRustySwordScheduleTarget(
					session,
					schedule))
			{
				throw new InvalidOperationException(
					"The stable recovery snapshot has invalid Actor borrowed Role Power state.");
			}
		}
	}

	private static bool IsExpectedBorrowedRustySwordScheduleTarget(
		GameSession session,
		ActorBorrowedKnightRustySwordScheduleCommit schedule)
	{
		var history = session.GameHistoryLog.ToArray();
		if (!session.GetPlayers().Any(player =>
				player.Id == schedule.PowerIdentity.ActingPlayerId) ||
			!session.GetPlayers().Any(player =>
				player.Id == schedule.TargetPlayerId) ||
			schedule.PublicMarkerLogIndex < 0 ||
			schedule.PublicMarkerLogIndex >= history.Length)
		{
			return false;
		}

		try
		{
			return GameSessionQueries.FindFirstClockwiseLivingKnownFactionAgent(
				session,
				schedule.PowerIdentity.ActingPlayerId,
				Faction.Werewolf,
				new FactionFactEffectiveBoundary(
					schedule.TurnNumber,
					GamePhase.Night,
					int.MaxValue),
				schedule.PublicMarkerLogIndex) == schedule.TargetPlayerId;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}

	private static bool HasCorrelatedBorrowedRustySwordSchedule(
		GameSession session,
		IReadOnlyCollection<PendingDawnElimination> pendingDawnVictims)
	{
		var rustySwordVictims = pendingDawnVictims
			.Where(victim => victim.Reason == EliminationReason.RustySword)
			.ToArray();
		var schedules = session
			.GetActorBorrowedKnightRustySwordScheduleCommits()
			.ToArray();
		if (rustySwordVictims is not [var rustySwordVictim] ||
			schedules is not [var schedule])
		{
			return false;
		}

		return schedule.TargetPlayerId == rustySwordVictim.Player.Id &&
			schedule.TurnNumber == session.TurnNumber - 1 &&
			schedule.CurrentPhase == GamePhase.Dawn &&
			StringComparer.Ordinal.Equals(
				schedule.CascadeScopeId,
				$"Dawn:{schedule.TurnNumber}");
	}

	private static ConfirmationInstruction CreateRustySwordDawnAnnouncement(
		IReadOnlyCollection<PendingDawnElimination> pendingDawnVictims,
		IReadOnlyCollection<Guid> affectedPlayerIds,
		Guid instructionId = default)
	{
		var victimNames = string.Join(
			Environment.NewLine,
			pendingDawnVictims.Select(victim =>
				victim.Reason == EliminationReason.RustySword
					? GameStrings
						.RustySwordDiseaseEliminationAnnouncement
						.Format(victim.Player.Name)
					: victim.Player.Name));
		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.AnnounceDawnVictims,
			publicAnnouncement:
				GameStrings.MultipleVictimEliminatedAnnounce.Format(
					victimNames),
			affectedPlayerIds: affectedPlayerIds.ToArray(),
			instructionId: instructionId);
	}

	private static bool MatchesRustySwordDawnAnnouncement(
		ModeratorInstruction? instruction,
		IReadOnlyCollection<PendingDawnElimination> pendingDawnVictims,
		IReadOnlyCollection<Guid> affectedPlayerIds)
	{
		if (instruction is not ConfirmationInstruction announcement)
		{
			return false;
		}

		var expected = CreateRustySwordDawnAnnouncement(
			pendingDawnVictims,
			affectedPlayerIds,
			announcement.InstructionId);
		return announcement.Semantic == expected.Semantic &&
			announcement.InstructionId == expected.InstructionId &&
			announcement.AffectedPlayerIds is not null &&
			announcement.AffectedPlayerIds.SequenceEqual(
				expected.AffectedPlayerIds!) &&
			StringComparer.Ordinal.Equals(
				announcement.PublicAnnouncement,
				expected.PublicAnnouncement) &&
			StringComparer.Ordinal.Equals(
				announcement.PrivateInstruction,
				expected.PrivateInstruction) &&
			announcement.SoundEffects.SequenceEqual(expected.SoundEffects);
	}

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = session.Execution;
		if (!execution.TryGetActiveGameHook(out var hook) ||
		    hook is not (GameHook.NightMainActionLoop or
			    GameHook.DawnMainActionLoop) ||
		    execution.CurrentPhase is not (GamePhase.Night or GamePhase.Dawn))
		{
			return HookListenerActionResult.Skip();
		}

		// Both owned branches are silent consequences that remain meaningful
		// after the Knight is no longer a living Role holder.
		return ExecuteCore(session, input);
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			startStage: null,
			KnightWithTheRustySwordRoleState.Complete,
			ConvertDueDiseaseToNightAction),
		CreateStage(
			GameHook.DawnMainActionLoop,
			startStage: null,
			KnightWithTheRustySwordRoleState.Complete,
			ScheduleDiseaseAfterQualifyingElimination)
	];

	private static HookListenerActionResult ConvertDueDiseaseToNightAction(
		GameSession session,
		ModeratorResponse _)
	{
		var execution = session.Execution;
		if (execution.CurrentPhase != GamePhase.Night ||
		    !execution.TryGetActiveGameHook(out var hook) ||
		    hook != GameHook.NightMainActionLoop)
		{
			return HookListenerActionResult.Skip();
		}

		var diseasedPlayers = session.GetPlayers()
			.Where(player => player.State.HasStatusEffect(
				StatusEffectTypes.RustySwordDisease))
			.ToArray();
		if (diseasedPlayers.Length == 0)
		{
			return Complete();
		}

		if (diseasedPlayers is not [var diseasedPlayer])
		{
			throw new InvalidOperationException(
				"Only one active Rusty Sword Disease is permitted.");
		}

		var lifecycle = GameSessionQueries.GetLatestStatusEffectLifecycle(
			session,
			diseasedPlayer.Id,
			StatusEffectTypes.RustySwordDisease);
		if (lifecycle is not { IsActive: true })
		{
			throw new InvalidOperationException(
				"The active Rusty Sword Disease has no active status lifecycle.");
		}

		var actionAlreadyCommitted =
			GameSessionQueries.HasNightActionTargetThisNight(
				session,
				NightActionType.RustySword,
				diseasedPlayer.Id);
		if (actionAlreadyCommitted)
		{
			session.RemoveStatusEffect(
				StatusEffectTypes.RustySwordDisease,
				diseasedPlayer.Id);
			return Complete();
		}

		var dueTurn = session.TurnNumber - 1;
		var isDue =
			lifecycle.TurnNumber == dueTurn &&
			lifecycle.CurrentPhase == GamePhase.Dawn;
		if (!isDue)
		{
			if (lifecycle.TurnNumber <= dueTurn)
			{
				session.RemoveStatusEffect(
					StatusEffectTypes.RustySwordDisease,
					diseasedPlayer.Id);
			}

			return Complete();
		}

		if (diseasedPlayer.State.Health == PlayerHealth.Dead)
		{
			session.RemoveStatusEffect(
				StatusEffectTypes.RustySwordDisease,
				diseasedPlayer.Id);
			return Complete();
		}

		session.PerformNightAction(
			NightActionType.RustySword,
			diseasedPlayer.Id);
		session.RemoveStatusEffect(
			StatusEffectTypes.RustySwordDisease,
			diseasedPlayer.Id);
		return Complete();
	}

	private HookListenerActionResult ScheduleDiseaseAfterQualifyingElimination(
		GameSession session,
		ModeratorResponse _)
	{
		var executionView = session.Execution;
		if (executionView.CurrentPhase != GamePhase.Dawn ||
		    !executionView.TryGetActiveGameHook(out var hook) ||
		    hook != GameHook.DawnMainActionLoop)
		{
			return HookListenerActionResult.Skip();
		}

		var alreadyScheduled =
			GameSessionQueries.HasActiveStatusEffectAppliedThisPhase(
				session,
				StatusEffectTypes.RustySwordDisease,
				executionView.CurrentPhase) ||
			session.GetActorBorrowedKnightRustySwordScheduleCommits()
				.Any(commit =>
					commit.TurnNumber == session.TurnNumber &&
					commit.CurrentPhase == GamePhase.Dawn);
		if (alreadyScheduled)
		{
			return Complete();
		}

		var directWerewolfAttackVictims =
			GameSessionQueries.GetDirectDawnEliminationPlayerIds(
				session,
				EliminationReason.WerewolfAttack);
		if (directWerewolfAttackVictims.Count == 0)
		{
			return Complete();
		}

		var cascadeScopeId = $"Dawn:{session.TurnNumber}";
		var cascadeCompleted = GameSessionQueries.IsEliminationCascadeComplete(
			session,
			cascadeScopeId);
		if (!cascadeCompleted)
		{
			throw new InvalidOperationException(
				"The Knight disease cannot be scheduled before the triggering Dawn Elimination Cascade completes.");
		}

		var execution = ResolveDiseaseExecution(
			session,
			directWerewolfAttackVictims,
			cascadeScopeId,
			out var borrowedUse,
			out var borrowedEliminationLogIndex);
		if (execution is null ||
			!execution.AvailabilityResult.IsAvailable)
		{
			return Complete();
		}

		var target = FindFirstEligibleClockwiseAgent(
			session,
			execution.ActingPlayer.Id);
		if (target == null)
		{
			return Complete();
		}

		if (borrowedUse is not null)
		{
			session.CommitActorBorrowedKnightRustySwordSchedule(
				borrowedUse.PowerIdentity,
				target.Id,
				borrowedEliminationLogIndex ??
					throw new InvalidOperationException(
						"The borrowed Rusty Sword schedule has no triggering elimination."),
				cascadeScopeId);
		}
		else
		{
			session.ApplyStatusEffect(
				StatusEffectTypes.RustySwordDisease,
				target.Id);
		}
		return Complete();
	}

	private RolePowerExecutionContext? ResolveDiseaseExecution(
		GameSession session,
		IReadOnlySet<Guid> directWerewolfAttackVictimIds,
		string cascadeScopeId,
		out ActorBorrowedRolePowers.ActorBorrowedRolePowerUse? borrowedUse,
		out int? borrowedEliminationLogIndex)
	{
		borrowedUse = null;
		borrowedEliminationLogIndex = null;
		var nativeKnight = session.GetPlayers()
			.SingleOrDefault(player =>
				player.State.CurrentRole ==
					MainRoleType.KnightWithRustySword &&
				player.State.Health == PlayerHealth.Dead &&
				directWerewolfAttackVictimIds.Contains(player.Id));
		if (nativeKnight != null)
		{
			return Evaluate(
				nativeKnight,
				RolePowerInstance.CreateCurrent(
					session,
					nativeKnight,
					MainRoleType.KnightWithRustySword,
					DiseasePower));
		}

		var history = session.GameHistoryLog.ToArray();
		var eliminationLogIndex = Array.FindLastIndex(
			history,
			entry => entry is PlayerEliminatedLogEntry
			{
				CurrentPhase: GamePhase.Dawn,
				PlayerId: var eliminatedPlayerId,
				Reason: EliminationReason.WerewolfAttack
			} eliminated &&
			eliminated.TurnNumber == session.TurnNumber &&
			directWerewolfAttackVictimIds.Contains(eliminatedPlayerId) &&
			session.GetPlayer(eliminatedPlayerId).State.CurrentRole ==
				MainRoleType.Actor);
		if (eliminationLogIndex < 0)
		{
			return null;
		}

		borrowedUse = ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			BorrowedDiseaseSpec,
			new BorrowedPostEliminationRolePowerContext
				.KnightRustySwordSchedule(
					eliminationLogIndex,
					cascadeScopeId));
		if (borrowedUse is null)
		{
			return null;
		}

		borrowedEliminationLogIndex = eliminationLogIndex;
		return _availabilityGateway.Evaluate(borrowedUse.CreateAttempt());

		RolePowerExecutionContext Evaluate(
			IPlayer actingPlayer,
			RolePowerInstance instance) =>
			_availabilityGateway.Evaluate(
				new RolePowerAttempt(
					session,
					actingPlayer,
					MainRoleType.KnightWithRustySword,
					DiseasePower,
					instance));
	}

	private static IPlayer? FindFirstEligibleClockwiseAgent(
		GameSession session,
		Guid knightId)
	{
		var targetId =
			GameSessionQueries.FindFirstClockwiseLivingKnownFactionAgent(
				session,
				knightId,
				Faction.Werewolf,
				new FactionFactEffectiveBoundary(
					session.TurnNumber,
					GamePhase.Night,
					int.MaxValue));
		return targetId is { } id ? session.GetPlayer(id) : null;
	}

	private static HookListenerActionResult Complete() =>
		HookListenerActionResult.Complete(
			KnightWithTheRustySwordRoleState.Complete);
}
