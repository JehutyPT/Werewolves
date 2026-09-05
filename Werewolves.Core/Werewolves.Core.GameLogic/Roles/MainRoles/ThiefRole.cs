using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum ThiefRoleState
{
	Awake,
	AwaitingOfferChoice,
	ReadyToSleep,
	Asleep
}

internal sealed class ThiefRole : RoleHookListener, IDeclaredRoleWorkflow
{
	private readonly RoleWorkflowRuntime _workflowRuntime;

	internal ThiefRole()
	{
		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				RecoverableWait<ThiefRoleState, SelectPlayersInstruction>
					.Replayable(
						Id,
						GameHook.NightMainActionLoop,
						startState: null,
						ThiefRoleState.Awake,
						ModeratorInstructionSemantic.IdentifyRoleHolders,
						ExpectedInputType.PlayerSelection,
						session => !IsCompleteHolderSetKnown(session),
						static (_, _) => { },
						CreateIdentificationInstruction,
						static (_, instruction) =>
							instruction is SelectPlayersInstruction
							{
								Semantic:
									ModeratorInstructionSemantic
										.IdentifyRoleHolders,
								RoleIdentification: MainRoleType.Thief
							},
						ValidateIdentificationInstruction),
				RecoverableWait<ThiefRoleState, ConfirmationInstruction>
					.Replayable(
						Id,
						GameHook.NightMainActionLoop,
						startState: null,
						ThiefRoleState.Awake,
						ModeratorInstructionSemantic.WakeRole,
						ExpectedInputType.Continue,
						IsCompleteHolderSetKnown,
						static (_, _) => { },
						CreateWakeInstruction,
						ClaimsHolderScopedCandidate,
						ValidateWakeInstruction),
				RecoverableWait<ThiefRoleState, SelectOptionsInstruction>
					.ReplayableWithAcceptedObservationHandoff(
						Id,
						GameHook.NightMainActionLoop,
						ThiefRoleState.Awake,
						ThiefRoleState.AwaitingOfferChoice,
						ModeratorInstructionSemantic.ChooseThiefOffer,
						ExpectedInputType.OptionSelection,
						static _ => true,
						AcceptIdentificationIfNeeded,
						CreateOfferInstruction,
						ClaimsHolderScopedCandidate,
						ValidateOfferInstruction,
						ValidateIdentificationRecoveryBoundary,
						static _ => ThiefRoleState.AwaitingOfferChoice),
				RecoverableWait<ThiefRoleState, ConfirmationInstruction>
					.Durable(
						Id,
						GameHook.NightMainActionLoop,
						ThiefRoleState.AwaitingOfferChoice,
						ThiefRoleState.ReadyToSleep,
						ModeratorInstructionSemantic.PutRoleToSleep,
						ExpectedInputType.Continue,
						static _ => true,
						CommitChoice,
						CreateSleepInstruction,
						static (_, _) => false,
						ValidateSleepInstruction,
						ValidateOfferRecoveryBoundary,
						static _ => ThiefRoleState.ReadyToSleep),
				new RoleWorkflowCompletionStep<ThiefRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					ThiefRoleState.ReadyToSleep,
					ThiefRoleState.Asleep,
					static _ => true)
			]);
	}

	internal override string PublicName => GameStrings.ThiefRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Thief);

	RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
		_workflowRuntime;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (session.TurnNumber != 1)
		{
			return HookListenerActionResult.Skip();
		}

		// A committed exchange removes the last active printed Thief card before
		// the pending sleep confirmation is accepted. Resume the already-active
		// listener directly so the hook loop can clear it and continue forward.
		return session.Execution.GetCurrentListenerState<ThiefRoleState>(Id)
			       == null
			? base.Execute(session, input)
			: ExecuteCore(session, input);
	}

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		_workflowRuntime.Execute(
			session,
			input,
			session.Execution.GetCurrentListenerState<ThiefRoleState>(Id));

	private static bool IsCompleteHolderSetKnown(GameSession session) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			MainRoleType.Thief);

	private SelectPlayersInstruction CreateIdentificationInstruction(
		GameSession session)
	{
		var roleCount = GetExpectedLivingRoleHolderCount(session);
		var committedHolderCount =
			GetCommittedLivingRoleHolderIds(session).Count;
		var selectablePlayerIds = GetIdentificationCandidates(session);
		if (roleCount <= 0 ||
		    committedHolderCount > roleCount ||
		    selectablePlayerIds.Count < roleCount)
		{
			throw new InvalidOperationException(
				"Confirmed Role knowledge contradicts the required Living Role Holder count.");
		}

		var privateInstruction = roleCount == 1
			? GameStrings.RoleSingleIdentificationPrompt.Format(PublicName)
			: GameStrings.RoleMultipleIdentificationPrompt.Format(PublicName);
		return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			selectablePlayerIds: selectablePlayerIds,
			countConstraint: NumberRangeConstraint.Exact(roleCount),
			publicAnnouncement: GameStrings.RoleWakesUp.Format(PublicName),
			privateInstruction: privateInstruction,
			affectedPlayerIds: null,
			roleIdentification: MainRoleType.Thief);
	}

	private ConfirmationInstruction CreateWakeInstruction(GameSession session) =>
		new(
			ModeratorInstructionSemantic.WakeRole,
			GameStrings.RoleWakesUp.Format(PublicName),
			affectedPlayerIds: [GetCurrentThief(session).Id]);

	private SelectOptionsInstruction CreateOfferInstruction(
		GameSession session)
	{
		var holder = GetCurrentThief(session);
		return new SelectOptionsInstruction(
			ModeratorInstructionSemantic.ChooseThiefOffer,
			CreateOfferOptions(session),
			NumberRangeConstraint.Single,
			privateInstruction: GameStrings.ThiefOfferSelectionInstruction,
			affectedPlayerIds: [holder.Id]);
	}

	private static List<ModeratorOption> CreateOfferOptions(
		GameSession session)
	{
		var offer1 = session.RoleLockIn.Offer1
			?? throw new InvalidOperationException("Thief requires Offer1.");
		var offer2 = session.RoleLockIn.Offer2
			?? throw new InvalidOperationException("Thief requires Offer2.");
		var options = new List<ModeratorOption>
		{
			new(ThiefOfferOptionIds.Offer1, offer1.PrintedRole.GetPublicName()),
			new(ThiefOfferOptionIds.Offer2, offer2.PrintedRole.GetPublicName())
		};
		if (ThiefOfferRules.IsDeclineLegal(
			    offer1.PrintedRole,
			    offer2.PrintedRole))
		{
			options.Add(new ModeratorOption(
				ThiefOfferOptionIds.Decline,
				GameStrings.DeclineOption));
		}

		return options;
	}

	private ConfirmationInstruction CreateSleepInstruction(
		GameSession session) =>
		new(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleGoesToSleepSingle.Format(PublicName),
			affectedPlayerIds: [ResolveActingThief(session).Id]);

	private void AcceptIdentificationIfNeeded(
		GameSession session,
		ModeratorResponse input)
	{
		if (IsCompleteHolderSetKnown(session))
		{
			return;
		}

		var holderId = input.SelectedPlayerIds?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"Thief identification requires exactly one Player.");
		if (holderId == Guid.Empty)
		{
			throw new InvalidOperationException(
				"Thief identification requires exactly one Player.");
		}

		var holder = session.GetPlayer(holderId);
		if (holder.State.CurrentRole != MainRoleType.Thief &&
		    holder.State.ModeratorKnownRole != MainRoleType.Thief &&
		    holder.State.PhysicalCharacterCardRole != MainRoleType.Thief &&
		    !RoleFactionKnowledge.GetPossibleRoles(session, holderId)
			    .Contains(MainRoleType.Thief))
		{
			throw new InvalidOperationException(
				"Role Identification contradicts committed Role knowledge.");
		}

		if (session.GetPlayerState(holderId).PhysicalCharacterCardId is null)
		{
			var thiefCard = session.GetModeratorPhysicalCharacterCards()
				.FirstOrDefault(state =>
					state.Zone == PhysicalCharacterCardZone.DealPool &&
					state.OwnerPlayerId is null &&
					state.Card.PrintedRole == MainRoleType.Thief)
				?.Card ?? throw new InvalidOperationException(
					"No unowned Thief Physical Character Card is available.");
			if (!session.TryRecordPhysicalCharacterCardOwnership(
					session.RoleLockIn.Version,
					holderId,
					thiefCard.Id))
			{
				throw new InvalidOperationException(
					"The identified Thief Physical Character Card could not be bound.");
			}
		}

		IdentifyCompleteLivingRoleHolderSet(session, [holderId]);
	}

	private void CommitChoice(GameSession session, ModeratorResponse input)
	{
		var holder = GetCurrentThief(session);
		var selectedOptionId = input.SelectedOptionIds?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"The Thief choice requires one semantic option.");
		var offer1 = session.RoleLockIn.Offer1!;
		var offer2 = session.RoleLockIn.Offer2!;
		if (selectedOptionId == ThiefOfferOptionIds.Decline)
		{
			if (!ThiefOfferRules.TryCommitDecline(session, holder.Id))
			{
				throw new InvalidOperationException(
					"The Thief decline could not be committed.");
			}

			return;
		}

		var selected = selectedOptionId switch
		{
			ThiefOfferOptionIds.Offer1 => (Selected: offer1, Other: offer2),
			ThiefOfferOptionIds.Offer2 => (Selected: offer2, Other: offer1),
			_ => throw new InvalidOperationException(
				"The Thief option is unknown.")
		};
		var outgoing = session.GetModeratorPhysicalCharacterCards()
			.Single(state =>
				state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
				state.OwnerPlayerId == holder.Id &&
				state.Card.PrintedRole == MainRoleType.Thief)
			.Card;
		var request = PermanentRoleSwapRules.CreateThiefExchangeRequest(
			session,
			holder.Id,
			outgoing,
			selected.Selected,
			selected.Other);
		if (!PermanentRoleSwapRules.CanCommit(session, request) ||
		    !session.TryCommitPermanentRoleSwap(request))
		{
			throw new InvalidOperationException(
				"The Thief exchange could not be committed.");
		}
	}

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		if (instruction.RoleIdentification != MainRoleType.Thief ||
		    instruction.AffectedPlayerIds != null ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)) ||
		    instruction.CountConstraint != NumberRangeConstraint.Exact(
			    GetExpectedLivingRoleHolderCount(session)))
		{
			throw new InvalidOperationException(
				"The Thief identification instruction has invalid workflow context.");
		}
	}

	private void ValidateWakeInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!StringComparer.Ordinal.Equals(
			    instruction.PublicAnnouncement,
			    GameStrings.RoleWakesUp.Format(PublicName)) ||
		    instruction.PrivateInstruction != null ||
		    !HasExpectedAffectedRoleHolders(session, instruction))
		{
			throw new InvalidOperationException(
				"The Thief wake instruction has invalid workflow context.");
		}
	}

	private void ValidateOfferInstruction(
		GameSession session,
		SelectOptionsInstruction instruction)
	{
		if (instruction.PublicAnnouncement != null ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PrivateInstruction,
			    GameStrings.ThiefOfferSelectionInstruction) ||
		    instruction.SelectionRange != NumberRangeConstraint.Single ||
		    !instruction.Options.SequenceEqual(CreateOfferOptions(session)) ||
		    !HasExpectedAffectedRoleHolders(session, instruction))
		{
			throw new InvalidOperationException(
				"The Thief offer instruction has invalid workflow context.");
		}
	}

	private void ValidateSleepInstruction(
		GameSession session,
		ConfirmationInstruction instruction)
	{
		if (!StringComparer.Ordinal.Equals(
			    instruction.PublicAnnouncement,
			    GameStrings.RoleGoesToSleepSingle.Format(PublicName)) ||
		    instruction.PrivateInstruction != null ||
		    instruction.AffectedPlayerIds is not [var playerId] ||
		    playerId != ResolveActingThief(session).Id ||
		    !ThiefOfferRules.HasValidCommittedChoice(session, playerId))
		{
			throw new InvalidOperationException(
				"The pending Thief sleep instruction is structurally invalid.");
		}
	}

	private void ValidateIdentificationRecoveryBoundary(
		GameSession session,
		SelectOptionsInstruction instruction,
		AcceptedObservationRecoveryCursor cursor)
	{
		ValidateThiefCursor(
			cursor,
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		if (!RoleFactionKnowledge.HasAcceptedRoleIdentification(
			    session,
			    MainRoleType.Thief))
		{
			throw new InvalidOperationException(
				"The Thief offer wait has no committed identification.");
		}
	}

	private static void ValidateOfferRecoveryBoundary(
		GameSession session,
		ConfirmationInstruction instruction,
		AcceptedObservationRecoveryCursor cursor) =>
		ValidateThiefCursor(
			cursor,
			ModeratorInstructionSemantic.ChooseThiefOffer);

	private static void ValidateThiefCursor(
		AcceptedObservationRecoveryCursor cursor,
		ModeratorInstructionSemantic acceptedSemantic)
	{
		if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.AcceptedObservationSemantic != acceptedSemantic ||
		    cursor.ObservedRole != MainRoleType.Thief ||
		    cursor.ContinuationRole != MainRoleType.Thief ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The Thief continuation cursor has invalid workflow context.");
		}
	}

	private bool ClaimsHolderScopedCandidate(
		GameSession session,
		ModeratorInstruction instruction) =>
		HasExpectedAffectedRoleHolders(session, instruction);

	private bool HasExpectedAffectedRoleHolders(
		GameSession session,
		ModeratorInstruction instruction)
	{
		var holders = GetLivingHolderIds(session);
		return holders.Count > 0 &&
		       instruction.AffectedPlayerIds is { } affectedPlayerIds &&
		       affectedPlayerIds.ToHashSet().SetEquals(holders);
	}

	private HashSet<Guid> GetLivingHolderIds(GameSession session) =>
		GetAliveRolePlayers(session)?.Select(player => player.Id).ToHashSet()
		?? [];

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.Thief ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == MainRoleType.Thief ||
				  player.State.ModeratorKnownRole == null &&
				  RoleFactionKnowledge.GetPossibleRoles(session, player.Id)
					  .Contains(MainRoleType.Thief))))
			.ToIdSet();

	private static IPlayer GetCurrentThief(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.SingleOrDefault(player =>
				player.State.CurrentRole == MainRoleType.Thief)
		?? throw new InvalidOperationException("No living Thief is available.");

	/// <summary>
	/// Resolves the Player who acted as the Thief. A committed exchange replaces
	/// that Player's current Role, so the acting holder is recovered from the
	/// committed exchange when no living printed Thief remains.
	/// </summary>
	private static IPlayer ResolveActingThief(GameSession session)
	{
		var holder = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.SingleOrDefault(player =>
				player.State.CurrentRole == MainRoleType.Thief);
		if (holder != null)
		{
			return holder;
		}

		var exchangedPlayerIds = session.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Where(entry =>
				entry.ExpectedCurrentRole == MainRoleType.Thief &&
				entry.RoleLockInVersion == session.RoleLockIn.Version)
			.Select(entry => entry.PlayerId)
			.Distinct()
			.ToArray();
		return exchangedPlayerIds is [var playerId]
			? session.GetPlayers()
				  .WithHealth(PlayerHealth.Alive)
				  .SingleOrDefault(player => player.Id == playerId)
			  ?? throw new InvalidOperationException(
				  "No living Thief is available.")
			: throw new InvalidOperationException(
				"No living Thief is available.");
	}
}
