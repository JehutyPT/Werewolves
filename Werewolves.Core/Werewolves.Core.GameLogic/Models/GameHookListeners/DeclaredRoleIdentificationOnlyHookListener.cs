using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Models.GameHookListeners;

internal enum DeclaredRoleIdentificationOnlyState
{
	AwaitingIdentification,
	Complete
}

internal abstract class DeclaredRoleIdentificationOnlyHookListener
	: RoleHookListener,
		IDeclaredRoleWorkflow
{
	private readonly RoleWorkflowRuntime _workflowRuntime;

	protected DeclaredRoleIdentificationOnlyHookListener()
	{
		var identificationWait = RecoverableWait<
			DeclaredRoleIdentificationOnlyState,
			SelectPlayersInstruction>
			.ReplayableWithAcceptedObservationHandoff(
			Id,
			GameHook.NightMainActionLoop,
			startState: null,
			DeclaredRoleIdentificationOnlyState.AwaitingIdentification,
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			ExpectedInputType.PlayerSelection,
			static _ => false,
			static (_, _) => { },
			CreateIdentificationInstruction,
			(_, instruction) =>
				instruction is SelectPlayersInstruction
				{
					RoleIdentification: { } role
				} && role == (MainRoleType)Id,
			ValidateIdentificationInstruction,
			ValidateAcceptedObservationHandoff,
			static _ =>
				DeclaredRoleIdentificationOnlyState.AwaitingIdentification);
		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				identificationWait,
				new RoleWorkflowDecisionStep<
					DeclaredRoleIdentificationOnlyState>(
					Id,
					GameHook.NightMainActionLoop,
					startState: null,
					static _ => true,
					(session, input) =>
						BeginIdentificationOnlySlot(
							session,
							input,
							identificationWait)),
				new RoleWorkflowDecisionStep<
					DeclaredRoleIdentificationOnlyState>(
					Id,
					GameHook.NightMainActionLoop,
					DeclaredRoleIdentificationOnlyState
						.AwaitingIdentification,
					static _ => true,
					CommitIdentification),
				new RoleWorkflowCompletionStep<
					DeclaredRoleIdentificationOnlyState>(
					Id,
					GameHook.NightMainActionLoop,
					DeclaredRoleIdentificationOnlyState.Complete,
					DeclaredRoleIdentificationOnlyState.Complete,
					static _ => true)
			]);
	}

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_workflowRuntime;

	protected RoleWorkflowRuntime IdentificationWorkflowRuntime =>
		_workflowRuntime;

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		_workflowRuntime.Execute(
			session,
			input,
			session.Execution.GetCurrentListenerState<
				DeclaredRoleIdentificationOnlyState>(Id));

	protected virtual void OnIdentificationAccepted(
		GameSession session,
		ModeratorResponse input)
	{
		IdentifyCompleteLivingRoleHolderSet(
			session,
			input.SelectedPlayerIds?.ToHashSet()
			?? throw new InvalidOperationException(
				"Role Identification requires a Player selection."));
	}

	private HookListenerActionResult BeginIdentificationOnlySlot(
		GameSession session,
		ModeratorResponse input,
		RecoverableWait<DeclaredRoleIdentificationOnlyState,
			SelectPlayersInstruction> identificationWait) =>
		session.TurnNumber == 1 && !IsCompleteHolderSetKnown(session)
			? identificationWait.Execute(session, input)
			: HookListenerActionResult.Complete(
				DeclaredRoleIdentificationOnlyState.Complete);

	private HookListenerActionResult CommitIdentification(
		GameSession session,
		ModeratorResponse input)
	{
		OnIdentificationAccepted(session, input);
		return HookListenerActionResult.Complete(
			DeclaredRoleIdentificationOnlyState.Complete);
	}

	private SelectPlayersInstruction CreateIdentificationInstruction(
		GameSession session)
	{
		var expectedCount = GetExpectedLivingRoleHolderCount(session);
		var candidates = GetIdentificationCandidates(session);
		if (expectedCount <= 0 ||
		    GetCommittedLivingRoleHolderIds(session).Count > expectedCount ||
		    candidates.Count < expectedCount)
		{
			throw new InvalidOperationException(
				"Confirmed Role knowledge contradicts the required Living Role Holder count.");
		}

		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			candidates,
			NumberRangeConstraint.Exact(expectedCount),
			publicAnnouncement: GameStrings.RoleWakesUp.Format(PublicName),
			privateInstruction: expectedCount == 1
				? GameStrings.RoleSingleIdentificationPrompt.Format(PublicName)
				: GameStrings.RoleMultipleIdentificationPrompt.Format(PublicName),
			affectedPlayerIds: null,
			roleIdentification: (MainRoleType)Id);
	}

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		var expectedCount = GetExpectedLivingRoleHolderCount(session);
		if (session.TurnNumber != 1 ||
		    instruction.RoleIdentification != (MainRoleType)Id ||
		    instruction.AffectedPlayerIds != null ||
		    instruction.CountConstraint !=
		    NumberRangeConstraint.Exact(expectedCount) ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)))
		{
			throw new InvalidOperationException(
				$"The {PublicName} identification instruction has invalid workflow context.");
		}
	}

	private bool IsCompleteHolderSetKnown(GameSession session) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			(MainRoleType)Id);

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == (MainRoleType)Id ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == null ||
				  player.State.ModeratorKnownRole == (MainRoleType)Id)))
			.ToIdSet();

	private void ValidateAcceptedObservationHandoff(
		GameSession session,
		SelectPlayersInstruction instruction,
		AcceptedObservationRecoveryCursor cursor)
	{
		if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.ContinuationRole != (MainRoleType)Id ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				$"The {PublicName} identification has invalid accepted-observation handoff context.");
		}
	}
}
