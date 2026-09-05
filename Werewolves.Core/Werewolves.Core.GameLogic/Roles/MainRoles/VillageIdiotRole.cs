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

internal sealed class VillageIdiotRole
	: RoleHookListener,
		IVoteEliminationInterceptor
{
	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		ActorBorrowedRolePowers.ActorBorrowedRolePowerUse? BorrowedUse)
	{
		internal bool IsBorrowed => BorrowedUse is not null;
	}

	private static readonly RolePowerDefinition PardonPower = new(
		new RolePowerIdentifier(
			ActorBorrowedVillageIdiotPardonCommit
				.ExpectedSourcePowerIdentifier),
		RolePowerCategory.Automatic);
	private static readonly ActorBorrowedRolePowerSpec BorrowedPowerSpec = new(
		MainRoleType.VillageIdiot,
		PardonPower);

	private static readonly Guid PardonResourceId =
		ActorBorrowedVillageIdiotPardonCommit.ExpectedResourceId;

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	internal VillageIdiotRole(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.VillageIdiotRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.VillageIdiot);

	public bool TryInterceptVoteElimination(
		GameSession session,
		IPlayer target,
		out ConfirmationInstruction? consequence)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(target);
		consequence = null;

		if (target.State.Health != PlayerHealth.Alive ||
		    target.State.DurableVotingPower != 1 ||
			GameSessionQueries.IsDevotedServantAcquiredRoleDormantForCurrentDay(
				session,
				target.Id,
				session.Execution.CurrentPhase) ||
			!TryResolveExecution(session, target, out var resolved))
		{
			return false;
		}

		var identity = new OneUseRolePowerResourceIdentity(
			resolved.ActingPlayer.Id,
			MainRoleType.VillageIdiot,
			PardonPower.Identifier.Value,
			resolved.PowerInstance.Id,
			resolved.PowerInstance.Origin,
			PardonResourceId);
		var resourceIsCommitted = resolved.BorrowedUse is { } borrowedUse
			? GameSessionQueries
				.IsCorrelatedActorBorrowedVillageIdiotPardonCommitted(
					session,
					borrowedUse,
					identity)
			: GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				session,
				identity);
		if (resourceIsCommitted)
		{
			return false;
		}

		var attempt = resolved.BorrowedUse?.CreateAttempt(PardonResourceId) ??
			new RolePowerAttempt(
				session,
				resolved.ActingPlayer,
				MainRoleType.VillageIdiot,
				PardonPower,
				resolved.PowerInstance,
				new OneUseRolePowerResource(
					PardonResourceId,
					resolved.PowerInstance));
		var execution = _availabilityGateway.Evaluate(attempt);
		if (!execution.AvailabilityResult.IsAvailable)
		{
			return false;
		}

		if (resolved.BorrowedUse is { } committingBorrowedUse)
		{
			session.CommitActorBorrowedVillageIdiotPardon(
				committingBorrowedUse.PowerIdentity,
				identity);
			session.CommitGameFact(context =>
				new VotingRightChangedLogEntry
				{
					Timestamp = context.Timestamp,
					TurnNumber = context.TurnNumber,
					CurrentPhase = context.CurrentPhase,
					PlayerId = identity.ActingPlayerId,
					HasVotingRight = false,
					DurableVotingPower = 0
				});
		}
		else
		{
			session.CommitGameFact(context =>
				new VillageIdiotPardonCommittedLogEntry
				{
					Timestamp = context.Timestamp,
					TurnNumber = context.TurnNumber,
					CurrentPhase = context.CurrentPhase,
					PlayerId = target.Id,
					ActingPlayerId = identity.ActingPlayerId,
					SourceRole = identity.SourceRole,
					SourcePowerIdentifier =
						identity.SourcePowerIdentifier,
					PowerInstanceId = identity.PowerInstanceId,
					PowerInstanceOrigin =
						identity.PowerInstanceOrigin,
					OneUseResourceId = identity.OneUseResourceId
				});
		}

		consequence = CreatePardonAnnouncement(target, resolved.IsBorrowed);
		return true;
	}

	internal static void ValidateBorrowedPendingPardonRecoveryInstruction(
		GameSession session)
	{
		var execution = session.Execution;
		var pendingInstruction = execution.PendingInstruction;
		if (pendingInstruction?.Semantic !=
				ModeratorInstructionSemantic.AnnounceVillageIdiotPardon ||
			!session.GetModeratorActorSetupCards().Cards.Any(card =>
				card.PrintedRole == MainRoleType.VillageIdiot))
		{
			return;
		}

		var borrowedCommits = session
			.GetActorBorrowedVillageIdiotPardonCommits()
			.ToArray();
		if (borrowedCommits is not [var commit])
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		}

		var actor = session.GetPlayer(commit.PowerIdentity.ActingPlayerId);
		if (execution.CurrentPhase != GamePhase.Day ||
			commit.TurnNumber != session.TurnNumber ||
			commit.CurrentPhase != GamePhase.Day ||
			actor.State is not
			{
				Health: PlayerHealth.Alive,
				CurrentRole: MainRoleType.Actor,
				PubliclyRevealedRole: MainRoleType.Actor,
				DurableVotingPower: 0,
				HasVotingRight: false
			} ||
			!MatchesBorrowedPardonAnnouncement(pendingInstruction, actor))
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		}
	}

	private static ConfirmationInstruction CreatePardonAnnouncement(
		IPlayer target,
		bool isBorrowed,
		Guid instructionId = default) =>
		new(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon,
			publicAnnouncement:
				(isBorrowed
					? GameStrings.ActorBorrowedVillageIdiotPardonAnnouncement
					: GameStrings.VillageIdiotPardonAnnouncement).Format(
						target.Name),
			affectedPlayerIds: [target.Id],
			instructionId: instructionId);

	internal static bool MatchesBorrowedPardonAnnouncement(
		ModeratorInstruction? instruction,
		IPlayer actor)
	{
		if (instruction is not ConfirmationInstruction announcement)
		{
			return false;
		}

		var expected = CreatePardonAnnouncement(
			actor,
			isBorrowed: true,
			instructionId: announcement.InstructionId);
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

	private static bool TryResolveExecution(
		GameSession session,
		IPlayer target,
		out ExecutionContext execution)
	{
		if (target.State.CurrentRole == MainRoleType.VillageIdiot)
		{
			execution = new ExecutionContext(
				target,
				RolePowerInstance.CreateCurrent(
					session,
					target,
					MainRoleType.VillageIdiot,
					PardonPower),
				BorrowedUse: null);
			return true;
		}

		var borrowedUse = ActorBorrowedRolePowers.ResolveActive(
			session,
			BorrowedPowerSpec);
		if (target.State.CurrentRole != MainRoleType.Actor ||
			borrowedUse is null ||
			borrowedUse.Actor.Id != target.Id)
		{
			execution = null!;
			return false;
		}

		execution = new ExecutionContext(
			borrowedUse.Actor,
			borrowedUse.PowerInstance,
			borrowedUse);
		return true;
	}

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		HookListenerActionResult.Skip();
}
