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

internal enum BearTamerDawnState
{
	AwaitingGrowl,
	Complete
}

internal sealed class BearTamerRole
	: DeclaredRoleIdentificationOnlyHookListener,
		IDeclaredRoleWorkflow
{
	private const string InvalidBorrowedRecoveryMessage =
		"The pending Actor borrowed Role Power instruction does not match its recovery context.";

	private static readonly RolePowerDefinition GrowlPower = new(
		new RolePowerIdentifier("bear-tamer-growl"),
		RolePowerCategory.Automatic);
	private static readonly ActorBorrowedRolePowerSpec BorrowedPowerSpec = new(
		MainRoleType.BearTamer,
		GrowlPower);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private readonly RoleWorkflowRuntime _dawnWorkflowRuntime;

	internal BearTamerRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;

		var growlWait = RecoverableWait<
			BearTamerDawnState,
			ConfirmationInstruction>.Replayable(
			Id,
			GameHook.DawnMainActionLoop,
			startState: null,
			BearTamerDawnState.AwaitingGrowl,
			ModeratorInstructionSemantic.AnnounceBearTamerGrowl,
			ExpectedInputType.Continue,
			static _ => false,
			static (_, _) => { },
			static _ => CreateGrowlAnnouncement(),
			static (_, instruction) =>
				instruction.Semantic ==
				ModeratorInstructionSemantic.AnnounceBearTamerGrowl,
			ValidateGrowlInstruction);
		_dawnWorkflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.DawnMainActionLoop,
			[
				growlWait,
				new RoleWorkflowDecisionStep<BearTamerDawnState>(
					Id,
					GameHook.DawnMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) =>
						PrepareGrowl(session, input, growlWait)),
				new RoleWorkflowDecisionStep<BearTamerDawnState>(
					Id,
					GameHook.DawnMainActionLoop,
					BearTamerDawnState.AwaitingGrowl,
					static _ => true,
					CommitGrowl),
				new RoleWorkflowCompletionStep<BearTamerDawnState>(
					Id,
					GameHook.DawnMainActionLoop,
					BearTamerDawnState.Complete,
					BearTamerDawnState.Complete,
					static _ => true)
			]);
	}

	internal override string PublicName => GameStrings.BearTamerRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.BearTamer);

	RoleWorkflowRuntime? IDeclaredRoleWorkflow.GetWorkflowRuntime(
		GameHook hook) => hook switch
	{
		GameHook.NightMainActionLoop => IdentificationWorkflowRuntime,
		GameHook.DawnMainActionLoop => _dawnWorkflowRuntime,
		_ => null
	};

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = session.Execution;
		if (execution.CurrentPhase == GamePhase.Dawn &&
		    TryResolveBorrowedExecution(session, out _))
		{
			return ExecuteCore(session, input);
		}

		return execution.CurrentPhase switch
		{
			GamePhase.Night when session.TurnNumber == 1 =>
				base.Execute(session, input),
			GamePhase.Dawn => base.Execute(session, input),
			_ => HookListenerActionResult.Skip()
		};
	}

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input)
	{
		if (!session.Execution.TryGetActiveGameHook(out var hook))
		{
			throw new InvalidOperationException(
				"Bear Tamer requires an active Role hook.");
		}

		return hook switch
		{
			GameHook.NightMainActionLoop => base.ExecuteCore(session, input),
			GameHook.DawnMainActionLoop => _dawnWorkflowRuntime.Execute(
				session,
				input,
				session.Execution.GetCurrentListenerState<BearTamerDawnState>(
					Id)),
			_ => throw new InvalidOperationException(
				$"Bear Tamer does not declare the '{hook}' hook.")
		};
	}

	private HookListenerActionResult PrepareGrowl(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<BearTamerDawnState, ConfirmationInstruction>
			growlWait)
	{
		if (GameSessionQueries.HasBearTamerGrowlOccurredThisDawn(session))
		{
			return HookListenerActionResult.Complete(
				BearTamerDawnState.Complete);
		}

		var execution = ResolveExecution(session);
		var attempt = execution.BorrowedUse?.CreateAttempt() ??
			new RolePowerAttempt(
				session,
				execution.ActingPlayer,
				MainRoleType.BearTamer,
				GrowlPower,
				execution.PowerInstance);
		var availability = _availabilityGateway.Evaluate(attempt);
		if (!availability.AvailabilityResult.IsAvailable ||
		    !HasLivingWerewolfAgentNeighbor(session, execution))
		{
			return HookListenerActionResult.Complete(
				BearTamerDawnState.Complete);
		}

		return growlWait.Execute(session, input);
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
		return HasGrowlInstructionContext(announcement) &&
			StringComparer.Ordinal.Equals(
				announcement.PrivateInstruction,
				expected.PrivateInstruction) &&
			announcement.SoundEffects.SequenceEqual(expected.SoundEffects);
	}

	private void ValidateGrowlInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!HasGrowlInstructionContext(instruction) ||
		    session.Execution.CurrentPhase != GamePhase.Dawn ||
		    GameSessionQueries.HasBearTamerGrowlOccurredThisDawn(session))
		{
			if (HasBorrowedBearTamerCard(session))
			{
				throw new RoleWorkflowInputRejectionException(
					InvalidBorrowedRecoveryMessage);
			}

			throw new InvalidOperationException(
				"The Bear Tamer growl has invalid workflow context.");
		}

		try
		{
			var execution = ResolveExecution(session);
			if (execution.IsBorrowed &&
			    !MatchesGrowlAnnouncement(instruction))
			{
				throw new RoleWorkflowInputRejectionException(
					InvalidBorrowedRecoveryMessage);
			}

			if (!HasLivingWerewolfAgentNeighbor(session, execution))
			{
				throw new InvalidOperationException(
					"The Bear Tamer growl has no living Werewolf Agent neighbor.");
			}
		}
		catch (Exception exception) when (
			exception is not RoleWorkflowInputRejectionException &&
			HasBorrowedBearTamerCard(session))
		{
			throw new RoleWorkflowInputRejectionException(
				InvalidBorrowedRecoveryMessage);
		}
	}

	private static bool HasGrowlInstructionContext(
		ModeratorInstruction instruction) =>
		instruction is ConfirmationInstruction
		{
			Semantic: ModeratorInstructionSemantic.AnnounceBearTamerGrowl,
			PublicAnnouncement: null,
			AffectedPlayerIds: null
		};

	private static bool HasLivingWerewolfAgentNeighbor(
		GameSession session,
		ExecutionContext execution)
	{
		var werewolfAgentIds = session
			.RequireKnownFactionAgents(Faction.Werewolf)
			.Select(player => player.Id)
			.ToHashSet();
		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			execution.ActingPlayer.Id);
		return new[]
			{
				neighbors.Clockwise?.Id,
				neighbors.Counterclockwise?.Id
			}
			.Where(playerId => playerId.HasValue)
			.Select(playerId => playerId!.Value)
			.ToHashSet()
			.Overlaps(werewolfAgentIds);
	}

	private static bool HasBorrowedBearTamerCard(GameSession session) =>
		session.GetModeratorActorSetupCards().Cards.Any(card =>
			card.PrintedRole == MainRoleType.BearTamer);

	private static ExecutionContext ResolveExecution(GameSession session)
		=> TryResolveBorrowedExecution(session, out var borrowedExecution)
			? borrowedExecution
			: ResolveNativeExecution(session);

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		if (!session.GetPlayers().Any(player =>
			    player.State.Health == PlayerHealth.Alive &&
			    player.State.CurrentRole == MainRoleType.Actor))
		{
			execution = null!;
			return false;
		}

		var borrowedUse = ActorBorrowedRolePowers.ResolveActive(
			session,
			BorrowedPowerSpec);
		if (borrowedUse is null)
		{
			execution = null!;
			return false;
		}

		if (GameSessionQueries.HasCorrelatedActorBorrowedBearTamerGrowl(
				session,
				borrowedUse) ||
			GameSessionQueries.HasActorBorrowedBearTamerGrowlForActivation(
				session,
				borrowedUse))
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

	private static ExecutionContext ResolveNativeExecution(GameSession session)
	{
		var holder = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.State.CurrentRole == MainRoleType.BearTamer)
			.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Bear Tamer is available for the Dawn growl.");
		return new ExecutionContext(
			holder,
			RolePowerInstance.CreateCurrent(
				session,
				holder,
				MainRoleType.BearTamer,
				GrowlPower),
			BorrowedUse: null);
	}

	private static HookListenerActionResult CommitGrowl(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = ResolveExecution(session);
		if (execution.BorrowedUse is { } borrowedUse)
		{
			if (GameSessionQueries.HasCorrelatedActorBorrowedBearTamerGrowl(
					session,
					borrowedUse))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Bear Tamer growl is already committed for this Dawn.");
			}

			session.CommitActorBorrowedBearTamerGrowl(
				borrowedUse.PowerIdentity);
		}
		session.CommitGameFact(context =>
			new BearTamerGrowlOccurredLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase
			});
		return HookListenerActionResult.Complete(
			BearTamerDawnState.Complete);
	}

	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		ActorBorrowedRolePowers.ActorBorrowedRolePowerUse? BorrowedUse)
	{
		internal bool IsBorrowed => BorrowedUse is not null;
	}
}
