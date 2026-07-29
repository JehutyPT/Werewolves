using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum WolfHoundRoleState
{
	Awake,
	AwaitingAlignmentChoice,
	AwaitingSleepConfirmation,
	Asleep
}

internal sealed class WolfHoundRole : NightRoleHookListener<WolfHoundRoleState>
{
	private const string AlignmentChoiceSourceIdentifier =
		"wolf-hound-alignment-choice";

	internal override string PublicName => GameStrings.WolfHoundRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.WolfHound);

	protected override WolfHoundRoleState WokenUpStateEnum =>
		WolfHoundRoleState.Awake;

	protected override WolfHoundRoleState ReadyToSleepStateEnum =>
		WolfHoundRoleState.AwaitingSleepConfirmation;

	protected override WolfHoundRoleState AsleepStateEnum =>
		WolfHoundRoleState.Asleep;

	protected override bool HasNightPowers => false;

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

	protected override HookListenerActionResult HandleRoleWakeupAndId(
		GameSession session,
		ModeratorResponse input)
	{
		var result = base.HandleRoleWakeupAndId(session, input);
		if (result.Instruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.WakeRole
		    })
		{
			return result;
		}

		var wolfHound = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Wolf Hound is available to wake.");
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName),
				affectedPlayerIds: [wolfHound.Id]),
			WolfHoundRoleState.Awake);
	}

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		listenerState = string.Empty;
		if (hook != GameHook.NightMainActionLoop ||
		    pendingInstruction.Semantic !=
			    ModeratorInstructionSemantic.PutRoleToSleep)
		{
			return false;
		}

		var wolfHound = GetAliveRolePlayers(session)?.SingleOrDefault();
		if (wolfHound == null ||
		    pendingInstruction is not ConfirmationInstruction ||
		    pendingInstruction.AffectedPlayerIds is not { Count: 1 } affected ||
		    affected.Single() != wolfHound.Id)
		{
			return false;
		}

		var subPhase = session.GetSubPhase<NightSubPhases>();
		if (session.TurnNumber != 1 ||
		    session.GetCurrentPhase() != GamePhase.Night ||
		    (subPhase ?? NightSubPhases.Start) != NightSubPhases.Start ||
		    !HasValidCommittedAlignment(session, wolfHound.Id))
		{
			throw new InvalidOperationException(
				"The pending Wolf Hound sleep instruction is structurally invalid.");
		}

		listenerState =
			WolfHoundRoleState.AwaitingSleepConfirmation.ToString();
		return true;
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			null,
			[
				WolfHoundRoleState.Awake,
				WolfHoundRoleState.Asleep
			],
			HandleRoleWakeupAndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			WolfHoundRoleState.Awake,
			WolfHoundRoleState.AwaitingAlignmentChoice,
			HandleNightPowerUse_AndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			WolfHoundRoleState.AwaitingAlignmentChoice,
			WolfHoundRoleState.AwaitingSleepConfirmation,
			CommitAlignmentChoice),
		CreateStage(
			GameHook.NightMainActionLoop,
			WolfHoundRoleState.AwaitingSleepConfirmation,
			WolfHoundRoleState.Asleep,
			HandleAsleepConfirmation),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			WolfHoundRoleState.Asleep,
			(_, _) => HookListenerActionResult.Complete(
				WolfHoundRoleState.Asleep))
	];

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		var wolfHound = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Wolf Hound is available to choose an alignment.");
		if (HasCommittedAlignment(session))
		{
			throw new InvalidOperationException(
				"The Wolf Hound alignment has already been committed.");
		}

		return HookListenerActionResult.NeedInput(
			new SelectOptionsInstruction(
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
				privateInstruction:
					GameStrings.WolfHoundAlignmentInstruction,
				affectedPlayerIds: [wolfHound.Id]),
			WolfHoundRoleState.AwaitingAlignmentChoice);
	}

	private HookListenerActionResult CommitAlignmentChoice(
		GameSession session,
		ModeratorResponse input)
	{
		var wolfHound = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Wolf Hound is available to choose an alignment.");
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

		return PrepareSleepInstruction(session);
	}

	protected override HookListenerActionResult PrepareSleepInstruction(
		GameSession session)
	{
		var wolfHound = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Wolf Hound is available to return to sleep.");
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(PublicName),
				affectedPlayerIds: [wolfHound.Id]),
			WolfHoundRoleState.AwaitingSleepConfirmation);
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
