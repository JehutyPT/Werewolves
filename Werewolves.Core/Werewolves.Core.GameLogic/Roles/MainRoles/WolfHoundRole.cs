using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum WolfHoundRoleState
{
	Awake,
	AwaitingAlignmentChoice,
	AwaitingSleepConfirmation,
	Asleep
}

internal sealed class WolfHoundRole : RoleHookListener, IDeclaredRoleWorkflow
{
	private const string AlignmentChoiceSourceIdentifier =
		"wolf-hound-alignment-choice";

	private readonly RoleWorkflowRuntime _workflowRuntime;

	internal WolfHoundRole()
	{
		_workflowRuntime = new RoleWorkflowRuntime(
			Id,
			GameHook.NightMainActionLoop,
			[
				RecoverableWait<WolfHoundRoleState, SelectPlayersInstruction>
					.Replayable(
						Id,
						GameHook.NightMainActionLoop,
						startState: null,
						WolfHoundRoleState.Awake,
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
								RoleIdentification: MainRoleType.WolfHound
							},
						ValidateIdentificationInstruction),
				RecoverableWait<WolfHoundRoleState, ConfirmationInstruction>
					.Replayable(
						Id,
						GameHook.NightMainActionLoop,
						startState: null,
						WolfHoundRoleState.Awake,
						ModeratorInstructionSemantic.WakeRole,
						ExpectedInputType.Continue,
						IsCompleteHolderSetKnown,
						static (_, _) => { },
						CreateWakeInstruction,
						ClaimsHolderScopedCandidate,
						ValidateWakeInstruction),
				RecoverableWait<WolfHoundRoleState, SelectOptionsInstruction>
					.ReplayableWithAcceptedObservationHandoff(
						Id,
						GameHook.NightMainActionLoop,
						WolfHoundRoleState.Awake,
						WolfHoundRoleState.AwaitingAlignmentChoice,
						ModeratorInstructionSemantic.ChooseWolfHoundAlignment,
						ExpectedInputType.OptionSelection,
						static _ => true,
						AcceptIdentificationIfNeeded,
						CreateAlignmentInstruction,
						ClaimsHolderScopedCandidate,
						ValidateAlignmentInstruction,
						ValidateIdentificationRecoveryBoundary,
						static _ => WolfHoundRoleState.AwaitingAlignmentChoice),
				RecoverableWait<WolfHoundRoleState, ConfirmationInstruction>
					.Durable(
						Id,
						GameHook.NightMainActionLoop,
						WolfHoundRoleState.AwaitingAlignmentChoice,
						WolfHoundRoleState.AwaitingSleepConfirmation,
						ModeratorInstructionSemantic.PutRoleToSleep,
						ExpectedInputType.Continue,
						static _ => true,
						CommitAlignmentChoice,
						CreateSleepInstruction,
						static (_, _) => false,
						ValidateSleepInstruction,
						ValidateAlignmentRecoveryBoundary,
						static _ =>
							WolfHoundRoleState.AwaitingSleepConfirmation),
				new RoleWorkflowCompletionStep<WolfHoundRoleState>(
					Id,
					GameHook.NightMainActionLoop,
					WolfHoundRoleState.AwaitingSleepConfirmation,
					WolfHoundRoleState.Asleep,
					static _ => true)
			]);
	}

	internal override string PublicName => GameStrings.WolfHoundRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.WolfHound);

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

		return base.Execute(session, input);
	}

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		_workflowRuntime.Execute(
			session,
			input,
			session.Execution.GetCurrentListenerState<WolfHoundRoleState>(Id));

	private static bool IsCompleteHolderSetKnown(GameSession session) =>
		GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
			session,
			MainRoleType.WolfHound);

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
			roleIdentification: MainRoleType.WolfHound);
	}

	private ConfirmationInstruction CreateWakeInstruction(GameSession session) =>
		new(
			ModeratorInstructionSemantic.WakeRole,
			GameStrings.RoleWakesUp.Format(PublicName),
			affectedPlayerIds: [GetWolfHound(session).Id]);

	private SelectOptionsInstruction CreateAlignmentInstruction(
		GameSession session) =>
		new(
			ModeratorInstructionSemantic.ChooseWolfHoundAlignment,
			[
				new ModeratorOption(
					WolfHoundAlignmentOptionIds.Villagers,
					GameStrings.VillagersGroupName),
				new ModeratorOption(
					WolfHoundAlignmentOptionIds.Werewolves,
					GameStrings.WerewolvesGroupName)
			],
			NumberRangeConstraint.Single,
			privateInstruction: GameStrings.WolfHoundAlignmentInstruction,
			affectedPlayerIds: [GetWolfHound(session).Id]);

	private ConfirmationInstruction CreateSleepInstruction(
		GameSession session) =>
		new(
			ModeratorInstructionSemantic.PutRoleToSleep,
			GameStrings.RoleGoesToSleepSingle.Format(PublicName),
			affectedPlayerIds: [GetWolfHound(session).Id]);

	private void AcceptIdentificationIfNeeded(
		GameSession session,
		ModeratorResponse input)
	{
		if (IsCompleteHolderSetKnown(session))
		{
			return;
		}

		IdentifyCompleteLivingRoleHolderSet(
			session,
			input.SelectedPlayerIds?.ToHashSet()
			?? throw new InvalidOperationException(
				"Wolf Hound identification requires a Player selection."));
	}

	private void ValidateIdentificationInstruction(
		GameSession session,
		SelectPlayersInstruction instruction)
	{
		if (instruction.RoleIdentification != MainRoleType.WolfHound ||
		    instruction.AffectedPlayerIds != null ||
		    !instruction.SelectablePlayerIds.SetEquals(
			    GetIdentificationCandidates(session)) ||
		    instruction.CountConstraint != NumberRangeConstraint.Exact(
			    GetExpectedLivingRoleHolderCount(session)))
		{
			throw new InvalidOperationException(
				"The Wolf Hound identification instruction has invalid workflow context.");
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
				"The Wolf Hound wake instruction has invalid workflow context.");
		}
	}

	private void ValidateAlignmentInstruction(
		GameSession session,
		SelectOptionsInstruction instruction)
	{
		if (instruction.PublicAnnouncement != null ||
		    !StringComparer.Ordinal.Equals(
			    instruction.PrivateInstruction,
			    GameStrings.WolfHoundAlignmentInstruction) ||
		    instruction.SelectionRange != NumberRangeConstraint.Single ||
		    !instruction.Options.Select(option => option.Id).SequenceEqual(
			    [
				    WolfHoundAlignmentOptionIds.Villagers,
				    WolfHoundAlignmentOptionIds.Werewolves
			    ],
			    StringComparer.Ordinal) ||
		    !HasExpectedAffectedRoleHolders(session, instruction) ||
		    HasCommittedAlignment(session))
		{
			throw new InvalidOperationException(
				"The Wolf Hound alignment instruction has invalid workflow context.");
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
		    !HasExpectedAffectedRoleHolders(session, instruction) ||
		    !HasValidCommittedAlignment(session, GetWolfHound(session).Id))
		{
			throw new InvalidOperationException(
				"The pending Wolf Hound sleep instruction is structurally invalid.");
		}
	}

	private void ValidateIdentificationRecoveryBoundary(
		GameSession session,
		SelectOptionsInstruction instruction,
		AcceptedObservationRecoveryCursor cursor)
	{
		ValidateWolfHoundCursor(
			cursor,
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		var livingHolderIds = GetLivingHolderIds(session);
		if (livingHolderIds.Count == 0 ||
		    !session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Any(
			    entry =>
				    entry.TurnNumber == session.TurnNumber &&
				    entry.CurrentPhase == GamePhase.Night &&
				    entry.Role == MainRoleType.WolfHound &&
				    entry.PlayerIds.SetEquals(livingHolderIds)))
		{
			throw new InvalidOperationException(
				"The Wolf Hound alignment wait has no committed identification.");
		}
	}

	private static void ValidateAlignmentRecoveryBoundary(
		GameSession session,
		ConfirmationInstruction instruction,
		AcceptedObservationRecoveryCursor cursor) =>
		ValidateWolfHoundCursor(
			cursor,
			ModeratorInstructionSemantic.ChooseWolfHoundAlignment);

	private static void ValidateWolfHoundCursor(
		AcceptedObservationRecoveryCursor cursor,
		ModeratorInstructionSemantic acceptedSemantic)
	{
		if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
		    cursor.AcceptedObservationSemantic != acceptedSemantic ||
		    cursor.ObservedRole != MainRoleType.WolfHound ||
		    cursor.ContinuationRole != MainRoleType.WolfHound ||
		    cursor.RetainedLittleGirlGuidanceDecision != null)
		{
			throw new InvalidOperationException(
				"The Wolf Hound continuation cursor has invalid workflow context.");
		}
	}

	private void CommitAlignmentChoice(
		GameSession session,
		ModeratorResponse input)
	{
		var wolfHound = GetWolfHound(session);
		if (HasCommittedAlignment(session))
		{
			throw new InvalidOperationException(
				"The Wolf Hound alignment has already been committed.");
		}

		var selectedOptionId = input.SelectedOptionIds?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"The Wolf Hound alignment requires one semantic option.");
		var (beneficiary, werewolfAgentKnowledge) = selectedOptionId switch
		{
			WolfHoundAlignmentOptionIds.Villagers =>
				(Faction.Villager, FactionAgentKnowledge.KnownNonAgent),
			WolfHoundAlignmentOptionIds.Werewolves =>
				(Faction.Werewolf, FactionAgentKnowledge.KnownAgent),
			_ => throw new InvalidOperationException(
				"The Wolf Hound alignment option is unknown.")
		};

		session.CommitFactionFactBatch(context =>
		{
			var boundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			return new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ExplicitTransition,
					AlignmentChoiceSourceIdentifier),
				Facts =
				[
					FactionFact.Beneficiary(
						wolfHound.Id,
						beneficiary,
						boundary),
					FactionFact.Agent(
						wolfHound.Id,
						Faction.Werewolf,
						werewolfAgentKnowledge,
						boundary)
				]
			};
		});
	}

	private IPlayer GetWolfHound(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException(
			"No living Wolf Hound is available.");

	private HashSet<Guid> GetLivingHolderIds(GameSession session) =>
		GetAliveRolePlayers(session)?.Select(player => player.Id).ToHashSet()
		?? [];

	private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
		session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Where(player =>
				player.State.CurrentRole == MainRoleType.WolfHound ||
				(player.State.CurrentRole == null &&
				 (player.State.ModeratorKnownRole == null ||
				  player.State.ModeratorKnownRole == MainRoleType.WolfHound)))
			.ToIdSet();

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

	private static bool HasCommittedAlignment(GameSession session) =>
		session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Any(IsAlignmentChoice);

	private static bool HasValidCommittedAlignment(
		GameSession session,
		Guid wolfHoundPlayerId)
	{
		var choices = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Where(IsAlignmentChoice)
			.ToArray();
		if (choices.Length != 1)
		{
			return false;
		}

		var facts = choices[0].Facts;
		if (facts.Length != 2 ||
		    facts.Any(fact => fact.PlayerId != wolfHoundPlayerId) ||
		    facts.Select(fact => fact.EffectiveBoundary)
			    .Distinct()
			    .Count() != 1)
		{
			return false;
		}

		var beneficiary = facts.SingleOrDefault(fact =>
			fact.Type == FactionFactType.Beneficiary);
		var werewolfAgency = facts.SingleOrDefault(fact =>
			fact.Type == FactionFactType.Agent &&
			fact.Faction == Faction.Werewolf);
		return (beneficiary?.Faction, werewolfAgency?.AgentKnowledge) is
			(Faction.Villager, FactionAgentKnowledge.KnownNonAgent) or
			(Faction.Werewolf, FactionAgentKnowledge.KnownAgent);
	}

	private static bool IsAlignmentChoice(
		FactionFactsCommittedLogEntry entry) =>
		entry.Source.Kind == FactionFactSourceKind.ExplicitTransition &&
		StringComparer.Ordinal.Equals(
			entry.Source.Identifier,
			AlignmentChoiceSourceIdentifier);
}
