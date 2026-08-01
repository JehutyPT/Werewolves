using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Log;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class DevotedServantRole
	: RoleHookListener,
	  IEliminationCascadeReaction
{
	private static readonly RolePowerDefinition TakeRolePower = new(
		new RolePowerIdentifier("devoted-servant-take-role"),
		RolePowerCategory.Chosen);

	private static readonly Guid TakeRoleResourceId =
		Guid.Parse("1c04cb89-ffde-4e64-90af-34680c583b8a");

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	internal DevotedServantRole(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.DevotedServantRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.DevotedServant);

	public string ReactionId =>
		EliminationCascadeReactionIds.DevotedServantVoteWindow;

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) => HookListenerActionResult.Skip();

	public EliminationCascadeReactionResult Advance(
		GameSession session,
		IReadOnlyCollection<Guid> eliminatedPlayerIds,
		ModeratorResponse input)
	{
		if (!TryGetVoteTarget(session, eliminatedPlayerIds, out var target) ||
			target.State.PubliclyRevealedRole is not null ||
			GameSessionQueries.GetExpectedLivingRoleHolderCount(
				session,
				MainRoleType.DevotedServant) == 0)
		{
			return EliminationCascadeReactionResult.Complete();
		}

		var selectablePlayerIds = GetSelectablePlayerIds(session, target.Id);
		if (selectablePlayerIds.Count == 0)
		{
			return EliminationCascadeReactionResult.Complete();
		}

		var committedSelfReveal = GameSessionQueries
			.GetCommittedDevotedServantPublicSelfRevealForTarget(
				session,
				target.Id);
		if (committedSelfReveal is not null)
		{
			return AdvanceCommittedSelfReveal(
				session,
				target,
				committedSelfReveal,
				input);
		}

		if (session.PendingModeratorInstruction is
			DevotedServantVoteWindowInstruction pending)
		{
			ValidatePendingWindow(pending, target.Id, selectablePlayerIds);
			if (input.InstructionId != pending.InstructionId)
			{
				throw new InvalidOperationException(
					"The Devoted Servant window received an uncorrelated response.");
			}

			if (input.Type == ExpectedInputType.Continue)
			{
				return EliminationCascadeReactionResult.Complete();
			}

			if (input.Type != ExpectedInputType.PlayerSelection ||
				input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds)
			{
				throw new InvalidOperationException(
					"Devoted Servant Use requires one public self-revealing Player.");
			}

			var actorId = selectedPlayerIds.Single();
			if (!selectablePlayerIds.Contains(actorId))
			{
				throw new InvalidOperationException(
					"The Devoted Servant actor is not eligible for this Vote.");
			}
			var actor = session.GetPlayer(actorId);
			var resourceIdentity = CreateResourceIdentity(session, actor);
			if (!IsUseAvailable(session, actor, resourceIdentity) ||
				!session.TryCommitDevotedServantPublicSelfReveal(
					actorId,
					target.Id,
					resourceIdentity))
			{
				throw new InvalidOperationException(
					"The submitted Player cannot use the Devoted Servant power.");
			}

			var committed = GameSessionQueries
				.GetCommittedDevotedServantPublicSelfReveal(
					session,
					actorId,
					target.Id);
			return EliminationCascadeReactionResult.NeedInput(
				CreateAcquiredCardInstruction(session, committed));
		}

		return EliminationCascadeReactionResult.NeedInput(
			new DevotedServantVoteWindowInstruction(
				target.Id,
				selectablePlayerIds,
				GameStrings.DevotedServantVoteWindowAnnouncement));
	}

	private static EliminationCascadeReactionResult AdvanceCommittedSelfReveal(
		GameSession session,
		IPlayer target,
		DevotedServantPublicSelfRevealCommittedLogEntry committed,
		ModeratorResponse input)
	{
		if (GameSessionQueries.HasCommittedDevotedServantRoleTake(
			session,
			committed.ActingPlayerId,
			committed.VoteTargetId))
		{
			return EliminationCascadeReactionResult.Complete();
		}

		if (session.PendingModeratorInstruction is not AssignRolesInstruction
			{
				Semantic:
					ModeratorInstructionSemantic.RecordDevotedServantAcquiredCard
			} pending ||
			pending.PlayersForAssignment.Count != 1 ||
			!pending.PlayersForAssignment.Contains(target.Id) ||
			pending.AffectedPlayerIds is not { Count: 2 } affected ||
			affected[0] != committed.ActingPlayerId ||
			affected[1] != target.Id)
		{
			throw new InvalidOperationException(
				"The committed Devoted Servant self-reveal has no correlated acquired-card instruction.");
		}

		if (input.InstructionId == Guid.Empty)
		{
			return EliminationCascadeReactionResult.NeedInput(pending);
		}

		if (input.InstructionId != pending.InstructionId ||
			input.Type != ExpectedInputType.AssignPlayerRoles ||
			input.AssignedPlayerRoles is not { Count: 1 } assignments ||
			!assignments.TryGetValue(target.Id, out var observedPrintedRole) ||
			!pending.RolesForAssignment.Contains(observedPrintedRole))
		{
			throw new InvalidOperationException(
				"The Devoted Servant acquired-card response is invalid or uncorrelated.");
		}

		var newCurrentRole =
			observedPrintedRole == MainRoleType.Angel &&
			GameSessionQueries.HasAngelExpired(session)
				? MainRoleType.SimpleVillager
				: observedPrintedRole;
		var request = PermanentRoleSwapRules.CreateDevotedServantRoleTakeRequest(
			session,
			committed.ActingPlayerId,
			target.Id,
			observedPrintedRole,
			newCurrentRole);
		if (!session.TryCommitDevotedServantRoleTake(request))
		{
			throw new InvalidOperationException(
				"The Devoted Servant acquired-card transaction is stale or invalid.");
		}

		return EliminationCascadeReactionResult.Complete();
	}

	private static AssignRolesInstruction CreateAcquiredCardInstruction(
		GameSession session,
		DevotedServantPublicSelfRevealCommittedLogEntry committed)
	{
		var target = session.GetPlayer(committed.VoteTargetId);
		var knownPrintedRole = target.State.PhysicalCharacterCardRole ??
			target.State.ModeratorKnownRole ??
			target.State.CurrentRole;
		IReadOnlyList<MainRoleType> roles =
			knownPrintedRole is { } knownRole
			? new[] { knownRole }
			: GameSessionQueries.GetUnassignedRoles(session);
		return new AssignRolesInstruction(
			ModeratorInstructionSemantic.RecordDevotedServantAcquiredCard,
			playersForAssignment: ImmutableHashSet.Create(target.Id),
			rolesForAssignment: roles,
			privateInstruction:
				GameStrings.DevotedServantAcquiredCardInstruction,
			affectedPlayerIds: [committed.ActingPlayerId, target.Id]);
	}

	private bool IsUseAvailable(
		GameSession session,
		IPlayer actor,
		OneUseRolePowerResourceIdentity resourceIdentity)
	{
		if (GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				session,
				resourceIdentity))
		{
			return false;
		}

		var instance = RolePowerInstance.CreateCurrent(
			session,
			actor,
			MainRoleType.DevotedServant,
			TakeRolePower);
		return _availabilityGateway.Evaluate(
				new RolePowerAttempt(
					actor,
					MainRoleType.DevotedServant,
					TakeRolePower,
					instance,
					new OneUseRolePowerResource(
						TakeRoleResourceId,
						instance)))
			.AvailabilityResult.IsAvailable;
	}

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		GameSession session,
		IPlayer actor)
	{
		var instance = RolePowerInstance.CreateCurrent(
			session,
			actor,
			MainRoleType.DevotedServant,
			TakeRolePower);
		return new OneUseRolePowerResourceIdentity(
			actor.Id,
			MainRoleType.DevotedServant,
			TakeRolePower.Identifier.Value,
			instance.Id,
			instance.Origin,
			TakeRoleResourceId);
	}

	private static bool TryGetVoteTarget(
		GameSession session,
		IReadOnlyCollection<Guid> eliminatedPlayerIds,
		out IPlayer target)
	{
		target = null!;
		if (session.GetCurrentPhase() != GamePhase.Day ||
			eliminatedPlayerIds is not { Count: 1 })
		{
			return false;
		}

		var vote = GameSessionQueries.GetCurrentDayVoteOutcome(session);
		var candidateId = eliminatedPlayerIds.Single();
		if (vote is not { PlayerId: var voteTargetId } ||
			voteTargetId == Guid.Empty ||
			voteTargetId != candidateId)
		{
			return false;
		}

		target = session.GetPlayer(candidateId);
		return target.State.Health == PlayerHealth.Alive;
	}

	private static HashSet<Guid> GetSelectablePlayerIds(
		GameSession session,
		Guid voteTargetId) =>
		session.GetPlayers()
			.Where(player =>
				player.Id != voteTargetId &&
				player.State.Health == PlayerHealth.Alive &&
				!player.State.HasStatusEffect(StatusEffectTypes.Lovers))
			.Select(player => player.Id)
			.ToHashSet();

	private static void ValidatePendingWindow(
		DevotedServantVoteWindowInstruction pending,
		Guid voteTargetId,
		IReadOnlySet<Guid> selectablePlayerIds)
	{
		if (pending.Semantic !=
				ModeratorInstructionSemantic.ResolveDevotedServantVoteWindow ||
			pending.VoteTargetId != voteTargetId ||
			pending.AffectedPlayerIds is not { Count: 1 } affectedPlayerIds ||
			affectedPlayerIds.Single() != voteTargetId ||
			!pending.SelectablePlayerIds.SetEquals(selectablePlayerIds))
		{
			throw new InvalidOperationException(
				"The pending Devoted Servant window no longer matches its Vote.");
		}
	}
}
