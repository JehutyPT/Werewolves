using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class BearTamerRole : NightRoleIdOnlyHookListener
{
	private static readonly RolePowerDefinition GrowlPower = new(
		new RolePowerIdentifier("bear-tamer-growl"),
		RolePowerCategory.Automatic);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	internal BearTamerRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.BearTamerRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.BearTamer);

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (session.GetCurrentPhase() == GamePhase.Dawn &&
		    TryResolveBorrowedExecution(session, out _))
		{
			return ExecuteCore(session, input);
		}

		return session.GetCurrentPhase() switch
		{
			GamePhase.Night when session.TurnNumber == 1 =>
				base.Execute(session, input),
			GamePhase.Dawn => base.Execute(session, input),
			_ => HookListenerActionResult.Skip()
		};
	}

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		if (hook == GameHook.DawnMainActionLoop &&
		    pendingInstruction is ConfirmationInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.AnnounceBearTamerGrowl
		    })
		{
			listenerState = NightRoleIdOnlyState.Awake.ToString();
			return true;
		}

		return base.TryResolvePendingInstructionContinuation(
			hook,
			session,
			pendingInstruction,
			out listenerState);
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages()
	{
		var stages = base.DefineStateMachineStages();
		stages.Add(
			CreateStage(
				GameHook.DawnMainActionLoop,
				startStage: null,
				[
					NightRoleIdOnlyState.Awake,
					NightRoleIdOnlyState.Asleep
				],
				PrepareGrowl));
		stages.Add(
			CreateStage(
				GameHook.DawnMainActionLoop,
				NightRoleIdOnlyState.Awake,
				NightRoleIdOnlyState.Asleep,
				CommitGrowl));
		stages.Add(
			CreateEndStage(
				GameHook.DawnMainActionLoop,
				NightRoleIdOnlyState.Asleep,
				(_, _) => HookListenerActionResult.Complete(
					NightRoleIdOnlyState.Asleep)));
		return stages;
	}

	private HookListenerActionResult PrepareGrowl(
		GameSession session,
		ModeratorResponse input)
	{
		if (GameSessionQueries.HasBearTamerGrowlOccurredThisDawn(session))
		{
			return HookListenerActionResult.Complete(
				NightRoleIdOnlyState.Asleep);
		}

		var execution = ResolveExecution(session);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				execution.ActingPlayer,
				MainRoleType.BearTamer,
				GrowlPower,
				execution.PowerInstance));
		if (!availability.AvailabilityResult.IsAvailable)
		{
			return HookListenerActionResult.Complete(
				NightRoleIdOnlyState.Asleep);
		}

		var werewolfAgentIds = session
			.RequireKnownFactionAgents(Faction.Werewolf)
			.Select(player => player.Id)
			.ToHashSet();
		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			execution.ActingPlayer.Id);
		var distinctNeighborIds = new[]
			{
				neighbors.Clockwise?.Id,
				neighbors.Counterclockwise?.Id
			}
			.Where(playerId => playerId.HasValue)
			.Select(playerId => playerId!.Value)
			.ToHashSet();
		if (!distinctNeighborIds.Overlaps(werewolfAgentIds))
		{
			return HookListenerActionResult.Complete(
				NightRoleIdOnlyState.Asleep);
		}

		return HookListenerActionResult.NeedInput(
			CreateGrowlAnnouncement(),
			NightRoleIdOnlyState.Awake);
	}

	internal static void ValidateBorrowedPendingGrowlRecoveryInstruction(
		GameSession session)
	{
		var pendingInstruction = session.PendingModeratorInstruction;
		if (pendingInstruction?.Semantic !=
				ModeratorInstructionSemantic.AnnounceBearTamerGrowl ||
			!session.GetModeratorActorSetupCards().Cards.Any(card =>
				card.PrintedRole == MainRoleType.BearTamer))
		{
			return;
		}

		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation is not
			{
				ActingRole: MainRoleType.Actor,
				SourceRole: MainRoleType.BearTamer
			})
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		}

		var actor = session.GetPlayer(activation.ActingPlayerId);
		var selectedCard = session.GetModeratorActorSetupCards().Cards
			.SingleOrDefault(card => card.Id == activation.SelectedCardId);
		var hasCorrelatedCommit = session
			.GetActorBorrowedBearTamerGrowlCommits()
			.Any(commit =>
				commit.PowerIdentity.ActingPlayerId ==
					activation.ActingPlayerId &&
				commit.PowerIdentity.PowerInstanceId ==
					activation.ActivationId &&
				commit.ActorSetupCardId == activation.SelectedCardId);
		if (session.GetCurrentPhase() != GamePhase.Dawn ||
			selectedCard?.PrintedRole != MainRoleType.BearTamer ||
			actor.State.Health != PlayerHealth.Alive ||
			actor.State.CurrentRole != MainRoleType.Actor ||
			GameSessionQueries.HasBearTamerGrowlOccurredThisDawn(session) ||
			hasCorrelatedCommit ||
			!MatchesGrowlAnnouncement(pendingInstruction))
		{
			throw new InvalidOperationException(
				"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		}
	}

	private static ConfirmationInstruction CreateGrowlAnnouncement(
		Guid instructionId = default) =>
		new(
			ModeratorInstructionSemantic.AnnounceBearTamerGrowl,
			publicAnnouncement: null,
			privateInstruction: GameStrings.BearTamerGrowlInstruction,
			affectedPlayerIds: null,
			instructionId: instructionId,
			soundEffects: [SoundEffectsEnum.BearGrowl]);

	internal static bool MatchesGrowlAnnouncement(
		ModeratorInstruction? instruction)
	{
		if (instruction is not ConfirmationInstruction announcement)
		{
			return false;
		}

		var expected = CreateGrowlAnnouncement(announcement.InstructionId);
		return announcement.Semantic == expected.Semantic &&
			announcement.InstructionId == expected.InstructionId &&
			announcement.AffectedPlayerIds is null &&
			StringComparer.Ordinal.Equals(
				announcement.PublicAnnouncement,
				expected.PublicAnnouncement) &&
			StringComparer.Ordinal.Equals(
				announcement.PrivateInstruction,
				expected.PrivateInstruction) &&
			announcement.SoundEffects.SequenceEqual(expected.SoundEffects);
	}

	private ExecutionContext ResolveExecution(GameSession session) =>
		TryResolveBorrowedExecution(session, out var borrowed)
			? borrowed
			: ResolveNativeExecution(session);

	private ExecutionContext ResolveNativeExecution(GameSession session)
	{
		var holder = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Bear Tamer is available for the Dawn growl.");
		return new ExecutionContext(
			holder,
			RolePowerInstance.CreateCurrent(
				session,
				holder,
				MainRoleType.BearTamer,
				GrowlPower),
			IsBorrowed: false);
	}

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.BearTamer)
		{
			execution = null!;
			return false;
		}

		var actor = session.GetPlayer(activation.ActingPlayerId);
		if (actor.State.Health != PlayerHealth.Alive ||
		    actor.State.CurrentRole != MainRoleType.Actor)
		{
			execution = null!;
			return false;
		}

		execution = new ExecutionContext(
			actor,
			RolePowerInstance.CreateBorrowed(
				session,
				actor,
				MainRoleType.BearTamer,
				GrowlPower),
			IsBorrowed: true);
		return true;
	}

	private HookListenerActionResult CommitGrowl(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = ResolveExecution(session);
		if (execution.IsBorrowed)
		{
			session.CommitActorBorrowedBearTamerGrowl(
				CreatePowerIdentity(execution));
		}
		session.CommitGameFact(context =>
			new BearTamerGrowlOccurredLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase
			});
		return HookListenerActionResult.Complete(
			NightRoleIdOnlyState.Asleep);
	}

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		ExecutionContext execution) => new(
			execution.ActingPlayer.Id,
			MainRoleType.BearTamer,
			GrowlPower.Identifier.Value,
			execution.PowerInstance.Id,
			execution.PowerInstance.Origin);

	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		bool IsBorrowed);
}
