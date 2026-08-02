using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

/// <summary>
/// Seer role implementation using the polymorphic hook listener pattern.
/// Inherits from StandardNightRoleHookListener for standard target selection workflow.
/// </summary>
internal class SeerRole : ImmediateFeedbackNightRoleHookListener,
	ITargetPrivateRolePowerRecoveryCapability
{
	private sealed record ExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance,
		bool IsBorrowed);

	private readonly RolePowerAvailabilityGateway _rolePowerAvailabilityGateway;

	private static readonly RolePowerDefinition WerewolfDetectionPower = new(
		new RolePowerIdentifier("seer-werewolf-detection"),
		RolePowerCategory.Chosen);

	internal SeerRole(RolePowerAvailabilityGateway rolePowerAvailabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(rolePowerAvailabilityGateway);
		_rolePowerAvailabilityGateway = rolePowerAvailabilityGateway;
	}

    public override ListenerIdentifier Id => ListenerIdentifier.Listener(MainRoleType.Seer);
    internal override string PublicName => GameStrings.SeerRoleName;
    protected override bool HasNightPowers => true;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedExecution(session, out _))
		{
			return ExecuteCore(session, input);
		}

		return base.Execute(session, input);
	}

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		listenerState = string.Empty;
		if (hook == GameHook.NightMainActionLoop &&
		    TryResolveBorrowedExecution(session, out var execution))
		{
			switch (pendingInstruction)
			{
				case ConfirmationInstruction
					{
						Semantic: ModeratorInstructionSemantic.WakeRole
					} wake:
					ValidateBorrowedWake(execution, wake);
					listenerState = WokenUpStateEnum.ToString();
					return true;
				case SelectPlayersInstruction
					{
						Semantic: ModeratorInstructionSemantic.SelectSeerTarget
					} selection:
					ValidateBorrowedSelectionInstruction(
						session,
						execution,
						selection);
					listenerState = AwaitingTargetSelectionEnum.ToString();
					return true;
				case ConfirmationInstruction
					{
						Semantic: ModeratorInstructionSemantic.RevealSeerResult
					} feedback:
					ValidateBorrowedFeedback(
						session,
						execution,
						GetBorrowedCommit(session, execution),
						feedback);
					listenerState = AwaitingModeratorFeedbackEnum.ToString();
					return true;
				case ConfirmationInstruction
					{
						Semantic: ModeratorInstructionSemantic.PutRoleToSleep
					} sleep:
					ValidateBorrowedSleep(execution, sleep);
					listenerState = ReadyToSleepStateEnum.ToString();
					return true;
			}
		}

		if (hook == GameHook.NightMainActionLoop &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			switch (pendingInstruction)
			{
				case SelectPlayersInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.SelectSeerTarget
				}:
					listenerState =
						ImmediateFeedbackNightRoleState
							.AwaitingTargetSelection
							.ToString();
					return true;
				case ConfirmationInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.PutRoleToSleep
				}:
					listenerState =
						ImmediateFeedbackNightRoleState
							.AwaitingSleepConfirmation
							.ToString();
					return true;
			}
		}

		return base.TryResolvePendingInstructionContinuation(
			hook,
			session,
			pendingInstruction,
			out listenerState);
	}

	bool ITargetPrivateRolePowerRecoveryCapability
		.TryValidateCommittedRecoveryBoundary(
			GameSession session,
			ModeratorInstruction? startingInstruction,
			ModeratorResponse input,
			TargetPrivateRolePowerRecoveryBoundary committedBoundary,
			ModeratorInstruction nextInstruction)
	{
		if (committedBoundary.ActionType != NightActionType.SeerCheck)
		{
			return false;
		}

		var execution = ResolveBorrowedExecution(session);
		var commit = GetBorrowedCommit(session, execution);
		ValidateBorrowedBoundary(session, execution, commit, committedBoundary);
		if (startingInstruction is not SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.SelectSeerTarget
			} selection ||
			input.InstructionId != selection.InstructionId ||
			input.Type != ExpectedInputType.PlayerSelection ||
			input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds ||
			selectedPlayerIds.Single() != commit.TargetPlayerId ||
			!selection.SelectablePlayerIds.Contains(commit.TargetPlayerId) ||
			nextInstruction is not ConfirmationInstruction
			{
				Semantic: ModeratorInstructionSemantic.RevealSeerResult
			} feedback)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Seer commit does not match its accepted target and feedback continuation.");
		}

		ValidateBorrowedSelectionInstruction(session, execution, selection);
		ValidateBorrowedFeedback(session, execution, commit, feedback);
		return true;
	}

	void ITargetPrivateRolePowerRecoveryCapability.ValidateRecoveryCursorIdentity(
		GameSession session,
		DomainRecoveryCursor cursor)
	{
		ArgumentNullException.ThrowIfNull(cursor);
		var execution = ResolveBorrowedExecution(session);
		var commit = GetBorrowedCommit(session, execution);
		var activation = session.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		if (cursor.Kind != DomainRecoveryCursorKind.TargetPrivateRolePowerCommit ||
			cursor.SourceRole != MainRoleType.Seer ||
			cursor.CommittedActionType != NightActionType.SeerCheck ||
			!StringComparer.Ordinal.Equals(
				cursor.SourcePowerIdentifier,
				WerewolfDetectionPower.Identifier.Value) ||
			cursor.PowerIdentity != CreatePowerIdentity(execution) ||
			cursor.OneUseResourceId != Guid.Empty ||
			cursor.ActorSetupCardId != activation.SelectedCardId ||
			cursor.ActorBorrowedActivationId != activation.ActivationId ||
			cursor.CommittedTargetIds is not { Count: 1 } targetIds ||
			targetIds.Single() != commit.TargetPlayerId ||
			cursor.NextInstructionSemantic !=
				ModeratorInstructionSemantic.RevealSeerResult)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Seer recovery cursor has an invalid target-private Role Power identity.");
		}
	}

	protected override HookListenerActionResult HandleRoleWakeupAndId(
		GameSession session,
		ModeratorResponse input)
	{
		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			return base.HandleRoleWakeupAndId(session, input);
		}

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName),
				affectedPlayerIds: [execution.ActingPlayer.Id]),
			WokenUpStateEnum);
	}

	protected override HookListenerActionResult HandleNightPowerUse_AndId(
		GameSession session,
		ModeratorResponse input) =>
		TryResolveBorrowedExecution(session, out _)
			? HandleNightPowerUse(session, input)
			: base.HandleNightPowerUse_AndId(session, input);

	protected override HookListenerActionResult HandleTargetSelectionRequest(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = ResolveExecution(session);
		var context = _rolePowerAvailabilityGateway.Evaluate(
			new RolePowerAttempt(
				session,
				execution.ActingPlayer,
				MainRoleType.Seer,
				WerewolfDetectionPower,
				execution.PowerInstance));

		if (context.AvailabilityResult.IsAvailable)
		{
			if (execution.IsBorrowed &&
			    GetBorrowedPotentialTargets(
				    session,
				    execution.ActingPlayer.Id).Count == 0)
			{
				return PrepareSleepInstruction(session);
			}

			return base.HandleTargetSelectionRequest(session, input);
		}
		if (execution.IsBorrowed)
		{
			return PrepareSleepInstruction(session);
		}

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(PublicName),
				affectedPlayerIds: [execution.ActingPlayer.Id]),
			ReadyToSleepStateEnum);
	}

    protected override ModeratorInstruction GenerateTargetSelectionInstruction(GameSession session, ModeratorResponse input)
    {
		var execution = ResolveExecution(session);
		if (execution.IsBorrowed)
		{
			return new SelectPlayersInstruction(
				ModeratorInstructionSemantic.SelectSeerTarget,
				publicAnnouncement:
					GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName),
				countConstraint: NumberRangeConstraint.Single,
				selectablePlayerIds:
					GetBorrowedPotentialTargets(session, execution.ActingPlayer.Id),
				privateInstruction: GameStrings.SeerNightActionPrompt,
				affectedPlayerIds: [execution.ActingPlayer.Id]);
		}

		var potentialTargets = GetPotentialTargets(session, false);

        return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectSeerTarget,
            publicAnnouncement: GameStrings.SeerNightActionPrompt,
            countConstraint: NumberRangeConstraint.Single,
            selectablePlayerIds: potentialTargets,
			affectedPlayerIds: new List<Guid> { execution.ActingPlayer.Id }
        );
    }

    protected override ModeratorInstruction ProcessTargetSelectionWithFeedback(GameSession session, ModeratorResponse input)
    {
		var execution = ResolveExecution(session);
		if (execution.IsBorrowed)
		{
			if (input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds)
			{
				throw new InvalidOperationException(
					"The Actor borrowed Seer must select exactly one living Player other than the Actor.");
			}

			var borrowedTargetId = selectedPlayerIds.Single();
			if (!GetBorrowedPotentialTargets(session, execution.ActingPlayer.Id)
				    .Contains(borrowedTargetId))
			{
				throw new InvalidOperationException(
					"The borrowed Role Power response is invalid or no longer available.");
			}

			var targetKnowledge = session.GetFactionAgentKnowledge(
				borrowedTargetId,
				Faction.Werewolf);
			if (targetKnowledge is not
				(FactionAgentKnowledge.KnownAgent or
				 FactionAgentKnowledge.KnownNonAgent))
			{
				throw new InvalidOperationException(
					"The current Werewolf Faction Agent fact is incomplete.");
			}

			var target = session.GetPlayer(borrowedTargetId);
			session.CommitActorBorrowedSeerCheck(
				CreatePowerIdentity(execution),
				borrowedTargetId,
				targetKnowledge);
			return new ConfirmationInstruction(
				ModeratorInstructionSemantic.RevealSeerResult,
				privateInstruction: FormatSeerFeedback(
					target.Name,
					targetKnowledge),
				affectedPlayerIds: [execution.ActingPlayer.Id]);
		}

		var targetId = input.SelectedPlayerIds!.First();
        var targetPlayer = session.GetPlayer(targetId);

        bool targetWakesWithWerewolves =
            session.GetFactionAgentKnowledge(targetId, Faction.Werewolf) ==
            FactionAgentKnowledge.KnownAgent;

        var privateFeedback = (targetWakesWithWerewolves
            ? GameStrings.SeerResultWerewolfTeam
            : GameStrings.SeerResultNotWerewolfTeam).Format(targetPlayer.Name);

        session.PerformNightAction(NightActionType.SeerCheck, targetId);

		return new ConfirmationInstruction(
			ModeratorInstructionSemantic.RevealSeerResult,
			privateInstruction: privateFeedback);
	}

	protected override HookListenerActionResult PrepareSleepInstruction(
		GameSession session)
	{
		if (!TryResolveBorrowedExecution(session, out var execution))
		{
			return base.PrepareSleepInstruction(session);
		}

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(
					GameStrings.ActorRoleName),
				affectedPlayerIds: [execution.ActingPlayer.Id]),
			ReadyToSleepStateEnum);
	}

	private ExecutionContext ResolveExecution(GameSession session) =>
		TryResolveBorrowedExecution(session, out var borrowed)
			? borrowed
			: ResolveNativeExecution(session);

	private ExecutionContext ResolveNativeExecution(GameSession session)
	{
		var seer = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No alive Seer found for Role Power availability.");
		return new ExecutionContext(
			seer,
			RolePowerInstance.CreateCurrent(
				session,
				seer,
				MainRoleType.Seer,
				WerewolfDetectionPower),
			IsBorrowed: false);
	}

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out ExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.Seer)
		{
			execution = null!;
			return false;
		}

		execution = ResolveBorrowedExecution(session);
		return true;
	}

	private static ExecutionContext ResolveBorrowedExecution(
		GameSession session)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.Seer)
		{
			throw new InvalidOperationException(
				"No active Actor borrowed Seer Role Power is available.");
		}

		var actor = session.GetPlayer(activation.ActingPlayerId);
		return new ExecutionContext(
			actor,
			RolePowerInstance.CreateBorrowed(
				session,
				actor,
				MainRoleType.Seer,
				WerewolfDetectionPower),
			IsBorrowed: true);
	}

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		ExecutionContext execution) => new(
		execution.ActingPlayer.Id,
		MainRoleType.Seer,
		WerewolfDetectionPower.Identifier.Value,
		execution.PowerInstance.Id,
		execution.PowerInstance.Origin);

	private static HashSet<Guid> GetBorrowedPotentialTargets(
		GameSession session,
		Guid actorId) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player => player.Id != actorId)
			.ToIdSet();

	private static ActorBorrowedSeerCheckCommit GetBorrowedCommit(
		GameSession session,
		ExecutionContext execution)
	{
		var identity = CreatePowerIdentity(execution);
		var commits = session.GetActorBorrowedSeerCheckCommits()
			.Where(commit =>
				commit.PowerIdentity == identity &&
				commit.TurnNumber == session.TurnNumber &&
				commit.CurrentPhase == GamePhase.Night)
			.ToArray();
		if (commits is not [var commit])
		{
			throw new InvalidOperationException(
				"The Actor borrowed Seer continuation requires exactly one private commit.");
		}

		return commit;
	}

	private static void ValidateBorrowedBoundary(
		GameSession session,
		ExecutionContext execution,
		ActorBorrowedSeerCheckCommit commit,
		TargetPrivateRolePowerRecoveryBoundary boundary)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		if (boundary.CurrentPhase != GamePhase.Night ||
			boundary.TurnNumber != session.TurnNumber ||
			boundary.ActionType != NightActionType.SeerCheck ||
			boundary.PowerIdentity != CreatePowerIdentity(execution) ||
			boundary.PowerIdentity != commit.PowerIdentity ||
			boundary.SpentResourceIdentity is not null ||
			commit.ActorSetupCardId != activation.SelectedCardId)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Seer target-private commit has an invalid Role Power identity.");
		}
	}

	private static void ValidateBorrowedWake(
		ExecutionContext execution,
		ConfirmationInstruction wake)
	{
		if (wake.PublicAnnouncement !=
				GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName) ||
			wake.PrivateInstruction is not null ||
			wake.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Seer wake instruction is invalid.");
		}
	}

	private static void ValidateBorrowedSelectionInstruction(
		GameSession session,
		ExecutionContext execution,
		SelectPlayersInstruction selection)
	{
		var potentialTargets = GetBorrowedPotentialTargets(
			session,
			execution.ActingPlayer.Id);
		if (potentialTargets.Count == 0 ||
			selection.CountConstraint != NumberRangeConstraint.Single ||
			selection.RoleIdentification is not null ||
			selection.PublicAnnouncement !=
				GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName) ||
			selection.PrivateInstruction != GameStrings.SeerNightActionPrompt ||
			selection.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id ||
			!selection.SelectablePlayerIds.ToHashSet().SetEquals(
				potentialTargets))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Seer target instruction is invalid.");
		}
	}

	private static void ValidateBorrowedFeedback(
		GameSession session,
		ExecutionContext execution,
		ActorBorrowedSeerCheckCommit commit,
		ConfirmationInstruction feedback)
	{
		if (feedback.PublicAnnouncement is not null ||
			feedback.PrivateInstruction != FormatSeerFeedback(
				session.GetPlayer(commit.TargetPlayerId).Name,
				commit.TargetAgentKnowledge) ||
			feedback.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Seer feedback does not match its private commit.");
		}
	}

	private static void ValidateBorrowedSleep(
		ExecutionContext execution,
		ConfirmationInstruction sleep)
	{
		if (sleep.PublicAnnouncement !=
				GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName) ||
			sleep.PrivateInstruction is not null ||
			sleep.AffectedPlayerIds is not { Count: 1 } affectedIds ||
			affectedIds.Single() != execution.ActingPlayer.Id)
		{
			throw new InvalidOperationException(
				"The Actor borrowed Seer sleep instruction is invalid.");
		}
	}

	private static string FormatSeerFeedback(
		string targetName,
		FactionAgentKnowledge targetKnowledge) =>
		(targetKnowledge == FactionAgentKnowledge.KnownAgent
			? GameStrings.SeerResultWerewolfTeam
			: GameStrings.SeerResultNotWerewolfTeam).Format(targetName);
}
