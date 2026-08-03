using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class HunterRole :
	RoleHookListener,
	IEliminationCascadeReaction
{
	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		bool IsBorrowed);

	private static readonly RolePowerDefinition FinalShotPower = new(
		new RolePowerIdentifier(EliminationCascadeReactionIds.HunterFinalShot),
		RolePowerCategory.Reactive);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	internal HunterRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.HunterRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Hunter);

	public string ReactionId => EliminationCascadeReactionIds.HunterFinalShot;

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		HookListenerActionResult.Skip();

	public EliminationCascadeReactionResult Advance(
		GameSession session,
		IReadOnlyCollection<Guid> eliminatedPlayerIds,
		ModeratorResponse input)
	{
		var pendingInstruction = session.PendingModeratorInstruction;
		var cascadeScopeId = EliminationCascadeStage.GetActiveScopeId(session);
		var triggeringPlayerIds = eliminatedPlayerIds.ToArray();
		var triggeringPlayers = triggeringPlayerIds
			.Select(session.GetPlayer)
			.ToArray();
		var durableBorrowedCompletion = session
			.GetActorBorrowedHunterFinalShotCommits()
			.SingleOrDefault(commit =>
				StringComparer.Ordinal.Equals(
					commit.CascadeScopeId,
					cascadeScopeId) &&
				commit.TriggeringPlayerIds.SequenceEqual(
					triggeringPlayerIds));
		if (durableBorrowedCompletion is not null)
		{
			return EliminationCascadeReactionResult.CompletePrivately(
				[
					new EliminationRequest(
						durableBorrowedCompletion.TargetPlayerId,
						EliminationReason.EventElimination)
				]);
		}

		var execution = triggeringPlayers
			.Select(player =>
				TryResolveExecution(
					session,
					player,
					cascadeScopeId,
					triggeringPlayerIds,
					out var resolved)
					? resolved
					: null)
			.Where(resolved => resolved is not null)
			.SingleOrDefault();
		if (execution == null)
		{
			if (pendingInstruction?.Semantic ==
				ModeratorInstructionSemantic.SelectHunterFinalShotTarget)
			{
				throw new InvalidOperationException(
					"The pending Hunter final shot has no matching committed Hunter elimination.");
			}

			return triggeringPlayers.Any(player =>
					player.State.CurrentRole == MainRoleType.Hunter)
				? EliminationCascadeReactionResult.Complete()
				: EliminationCascadeReactionResult.NotApplicable();
		}

		var actingPlayer = execution.ActingPlayer;
		var legalTargetIds = session.GetPlayers()
			.Where(player =>
				player.Id != actingPlayer.Id &&
				player.State.Health == PlayerHealth.Alive)
			.Select(player => player.Id)
			.ToHashSet();
		var expectedPublicAnnouncement = execution.IsBorrowed
			? GameStrings.ActorBorrowedHunterFinalShotSelectionInstruction
			: GameStrings.HunterFinalShotSelectionInstruction;
		if (pendingInstruction?.Semantic ==
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget)
		{
			if (pendingInstruction is not SelectPlayersInstruction pendingSelection ||
				!MatchesFinalShotSelection(
					pendingSelection,
					actingPlayer.Id,
					legalTargetIds,
					expectedPublicAnnouncement))
			{
				throw new InvalidOperationException(
					"The pending Hunter final shot no longer matches its committed elimination context.");
			}

			if (input.InstructionId != pendingSelection.InstructionId ||
				input.SelectedPlayerIds is not { Count: 1 })
			{
				throw new InvalidOperationException(
					"Hunter final shot received an uncorrelated response.");
			}

			var targetId = input.SelectedPlayerIds.Single();
			if (!legalTargetIds.Contains(targetId))
			{
				throw new InvalidOperationException(
					"Hunter final shot must target one other living Player.");
			}

			if (execution.IsBorrowed)
			{
				session.CommitActorBorrowedHunterFinalShot(
					new RolePowerInstanceIdentity(
						actingPlayer.Id,
						execution.PowerInstance.SourceRole,
						execution.PowerInstance.SourcePower.Identifier.Value,
						execution.PowerInstance.Id,
						execution.PowerInstance.Origin),
					cascadeScopeId,
					triggeringPlayerIds,
					targetId);
			}

			var finalShotEliminations = new EliminationRequest[]
			{
				new(
					targetId,
					execution.IsBorrowed
						? EliminationReason.EventElimination
						: EliminationReason.HunterShot)
			};
			return execution.IsBorrowed
				? EliminationCascadeReactionResult.CompletePrivately(
					finalShotEliminations)
				: EliminationCascadeReactionResult.Complete(
					finalShotEliminations);
		}

		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				actingPlayer,
				MainRoleType.Hunter,
				FinalShotPower,
				execution.PowerInstance));
		if (!availability.AvailabilityResult.IsAvailable ||
			legalTargetIds.Count == 0)
		{
			return execution.IsBorrowed
				? EliminationCascadeReactionResult.NotApplicable()
				: EliminationCascadeReactionResult.Complete();
		}

		return EliminationCascadeReactionResult.NeedInput(
			CreateFinalShotSelection(
				actingPlayer.Id,
				legalTargetIds,
				expectedPublicAnnouncement));
	}

	internal static void ValidateBorrowedPendingFinalShotRecoveryInstruction(
		GameSession session)
	{
		var pendingInstruction = session.PendingModeratorInstruction;
		if (pendingInstruction?.Semantic !=
				ModeratorInstructionSemantic.SelectHunterFinalShotTarget ||
			!session.GetModeratorActorSetupCards().Cards.Any(card =>
				card.PrintedRole == MainRoleType.Hunter))
		{
			return;
		}

		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation is not
			{
				ActingRole: MainRoleType.Actor,
				SourceRole: MainRoleType.Hunter
			})
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		}

		var actingPlayer = session.GetPlayer(activation.ActingPlayerId);
		var legalTargetIds = session.GetPlayers()
			.Where(player =>
				player.Id != actingPlayer.Id &&
				player.State.Health == PlayerHealth.Alive)
			.Select(player => player.Id)
			.ToHashSet();
		if (actingPlayer.State.CurrentRole != MainRoleType.Actor ||
			actingPlayer.State.Health != PlayerHealth.Dead ||
			pendingInstruction is not SelectPlayersInstruction pendingSelection ||
			!MatchesFinalShotSelection(
				pendingSelection,
				actingPlayer.Id,
				legalTargetIds,
				GameStrings.ActorBorrowedHunterFinalShotSelectionInstruction))
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		}
	}

	private static SelectPlayersInstruction CreateFinalShotSelection(
		Guid actingPlayerId,
		IReadOnlySet<Guid> legalTargetIds,
		string publicAnnouncement,
		Guid instructionId = default) =>
		new(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget,
			new HashSet<Guid>(legalTargetIds),
			NumberRangeConstraint.Single,
			publicAnnouncement: publicAnnouncement,
			affectedPlayerIds: [actingPlayerId],
			instructionId: instructionId);

	private static bool MatchesFinalShotSelection(
		SelectPlayersInstruction pending,
		Guid actingPlayerId,
		IReadOnlySet<Guid> legalTargetIds,
		string publicAnnouncement)
	{
		var expected = CreateFinalShotSelection(
			actingPlayerId,
			legalTargetIds,
			publicAnnouncement,
			pending.InstructionId);
		return pending.Semantic == expected.Semantic &&
			pending.CountConstraint == expected.CountConstraint &&
			pending.SelectablePlayerIds.SetEquals(
				expected.SelectablePlayerIds) &&
			pending.RoleIdentification == expected.RoleIdentification &&
			(pending.AffectedPlayerIds is null) ==
				(expected.AffectedPlayerIds is null) &&
			(pending.AffectedPlayerIds is null ||
				pending.AffectedPlayerIds.SequenceEqual(
					expected.AffectedPlayerIds!)) &&
			StringComparer.Ordinal.Equals(
				pending.PublicAnnouncement,
				expected.PublicAnnouncement) &&
			StringComparer.Ordinal.Equals(
				pending.PrivateInstruction,
				expected.PrivateInstruction) &&
			pending.SoundEffects.SequenceEqual(expected.SoundEffects) &&
			StringComparer.Ordinal.Equals(
				pending.EmptySelectionOptionLabel,
				expected.EmptySelectionOptionLabel);
	}

	private static bool TryResolveExecution(
		GameSession session,
		IPlayer eliminatedPlayer,
		string cascadeScopeId,
		IReadOnlyList<Guid> triggeringPlayerIds,
		out ExecutionContext execution)
	{
		if (eliminatedPlayer.State.Health != PlayerHealth.Dead ||
			GameSessionQueries
				.IsDevotedServantAcquiredRoleDormantForCurrentDay(
					session,
					eliminatedPlayer.Id))
		{
			execution = null!;
			return false;
		}

		if (eliminatedPlayer.State.CurrentRole == MainRoleType.Hunter)
		{
			execution = new ExecutionContext(
				eliminatedPlayer,
				RolePowerInstance.CreateCurrent(
					session,
					eliminatedPlayer,
					MainRoleType.Hunter,
					FinalShotPower),
				IsBorrowed: false);
			return true;
		}

		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (eliminatedPlayer.State.CurrentRole != MainRoleType.Actor ||
			activation is not
			{
				ActingPlayerId: var actingPlayerId,
				SourceRole: MainRoleType.Hunter
			} ||
			actingPlayerId != eliminatedPlayer.Id)
		{
			execution = null!;
			return false;
		}

		execution = new ExecutionContext(
			eliminatedPlayer,
			RolePowerInstance.CreateBorrowedAfterElimination(
				session,
				eliminatedPlayer,
				MainRoleType.Hunter,
				FinalShotPower,
				new BorrowedPostEliminationRolePowerContext.HunterFinalShot(
					cascadeScopeId,
					triggeringPlayerIds)),
			IsBorrowed: true);
		return true;
	}
}
