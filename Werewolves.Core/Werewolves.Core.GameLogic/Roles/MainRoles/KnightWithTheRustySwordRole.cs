using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
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

		var pendingInstruction = session.PendingModeratorInstruction;
		if (pendingInstruction?.Semantic ==
		    ModeratorInstructionSemantic.AnnounceDawnVictims)
		{
			if (pendingInstruction is not ConfirmationInstruction confirmation ||
			    confirmation.AffectedPlayerIds?.ToHashSet()
				    .SetEquals(eliminatedPlayerIds) != true ||
			    input.InstructionId != confirmation.InstructionId ||
			    input.Type != ExpectedInputType.Continue)
			{
				throw new InvalidOperationException(
					"The Rusty Sword disease announcement received an uncorrelated response.");
			}

			return EliminationCascadeReactionResult.Complete();
		}

		var victimNames = string.Join(
			Environment.NewLine,
			pendingDawnVictims.Select(victim =>
				victim.Reason == EliminationReason.RustySword
					? GameStrings
						.RustySwordDiseaseEliminationAnnouncement
						.Format(victim.Player.Name)
					: victim.Player.Name));
		return EliminationCascadeReactionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.AnnounceDawnVictims,
				publicAnnouncement:
					GameStrings.MultipleVictimEliminatedAnnounce.Format(
						victimNames),
				affectedPlayerIds: eliminatedPlayerIds.ToArray()));
	}

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (!session.TryGetActiveGameHook(out var hook) ||
		    hook is not (GameHook.NightMainActionLoop or
			    GameHook.DawnMainActionLoop) ||
		    session.GetCurrentPhase() is not (GamePhase.Night or GamePhase.Dawn))
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
		if (session.GetCurrentPhase() != GamePhase.Night ||
		    !session.TryGetActiveGameHook(out var hook) ||
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
		if (session.GetCurrentPhase() != GamePhase.Dawn ||
		    !session.TryGetActiveGameHook(out var hook) ||
		    hook != GameHook.DawnMainActionLoop)
		{
			return HookListenerActionResult.Skip();
		}

		var alreadyScheduled =
			GameSessionQueries.HasActiveStatusEffectAppliedThisPhase(
				session,
				StatusEffectTypes.RustySwordDisease);
		if (alreadyScheduled)
		{
			return Complete();
		}

		var directWerewolfAttackVictims =
			GameSessionQueries.GetDirectDawnEliminationPlayerIds(
				session,
				EliminationReason.WerewolfAttack);

		var triggeringKnight = session.GetPlayers()
			.SingleOrDefault(player =>
				player.State.CurrentRole ==
					MainRoleType.KnightWithRustySword &&
				player.State.Health == PlayerHealth.Dead &&
				directWerewolfAttackVictims.Contains(player.Id));
		if (triggeringKnight == null)
		{
			return Complete();
		}

		var cascadeCompleted = GameSessionQueries.IsEliminationCascadeComplete(
			session,
			$"Dawn:{session.TurnNumber}");
		if (!cascadeCompleted)
		{
			throw new InvalidOperationException(
				"The Knight disease cannot be scheduled before the triggering Dawn Elimination Cascade completes.");
		}

		var powerInstance = RolePowerInstance.CreateCurrent(
			session,
			triggeringKnight,
			MainRoleType.KnightWithRustySword,
			DiseasePower);
		var execution = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				triggeringKnight,
				MainRoleType.KnightWithRustySword,
				DiseasePower,
				powerInstance));
		if (!execution.AvailabilityResult.IsAvailable)
		{
			return Complete();
		}

		var target = FindFirstEligibleClockwiseAgent(
			session,
			triggeringKnight.Id);
		if (target == null)
		{
			return Complete();
		}

		session.ApplyStatusEffect(
			StatusEffectTypes.RustySwordDisease,
			target.Id);
		return Complete();
	}

	private static IPlayer? FindFirstEligibleClockwiseAgent(
		GameSession session,
		Guid knightId)
	{
		var currentAgentIds = session
			.RequireKnownFactionAgents(Faction.Werewolf)
			.Select(player => player.Id)
			.ToHashSet();
		currentAgentIds.UnionWith(
			GameSessionQueries.RequireKnownFactionAgentIdsAtBoundary(
				session,
				Faction.Werewolf,
				new FactionFactEffectiveBoundary(
					session.TurnNumber,
					GamePhase.Night,
					int.MaxValue)));

		var visitedPlayerIds = new HashSet<Guid> { knightId };
		var referencePlayerId = knightId;
		while (true)
		{
			var candidate = GameSessionQueries.GetDirectionalLivingNeighbors(
					session,
					referencePlayerId)
				.Clockwise;
			if (candidate == null || !visitedPlayerIds.Add(candidate.Id))
			{
				return null;
			}

			if (currentAgentIds.Contains(candidate.Id))
			{
				return candidate;
			}

			referencePlayerId = candidate.Id;
		}
	}

	private static HookListenerActionResult Complete() =>
		HookListenerActionResult.Complete(
			KnightWithTheRustySwordRoleState.Complete);
}
