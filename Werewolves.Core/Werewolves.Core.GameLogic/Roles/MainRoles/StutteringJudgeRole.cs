using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum StutteringJudgeRoleState
{
	Awake,
	AwaitingSignalSetup,
	NightComplete,
	AwaitingVoteConductConfirmation,
	AwaitingSignalObservation,
	DayComplete
}

internal sealed class StutteringJudgeRole
	: NightRoleHookListener<StutteringJudgeRoleState>
{
	private static readonly RolePowerDefinition ConsecutiveVotePower = new(
		new RolePowerIdentifier("stuttering-judge-consecutive-vote"),
		RolePowerCategory.Chosen);

	private static readonly Guid ConsecutiveVoteResourceId =
		Guid.Parse("85ff5eb7-61cf-4b33-894c-b9c37d58bace");

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	internal StutteringJudgeRole(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.StutteringJudgeRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.StutteringJudge);

	protected override StutteringJudgeRoleState WokenUpStateEnum =>
		StutteringJudgeRoleState.Awake;

	protected override StutteringJudgeRoleState ReadyToSleepStateEnum =>
		StutteringJudgeRoleState.NightComplete;

	protected override StutteringJudgeRoleState AsleepStateEnum =>
		StutteringJudgeRoleState.NightComplete;

	protected override bool HasNightPowers => false;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (!session.TryGetActiveGameHook(out var hook))
		{
			return HookListenerActionResult.Skip();
		}

		if (hook == GameHook.NightMainActionLoop)
		{
			if (session.TurnNumber != 1 ||
			    HasEstablishedSignal(session))
			{
				return HookListenerActionResult.Skip();
			}

			return base.Execute(session, input);
		}

		if (hook == GameHook.OnVoteConducted)
		{
			if (!HasEstablishedSignal(session) ||
			    GameSessionQueries.GetCurrentDayVoteOutcome(session) != null)
			{
				return HookListenerActionResult.Skip();
			}

			return base.Execute(session, input);
		}

		return HookListenerActionResult.Skip();
	}

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		listenerState = string.Empty;
		if (hook != GameHook.OnVoteConducted ||
		    pendingInstruction.Semantic !=
			    ModeratorInstructionSemantic.ObserveStutteringJudgeSignal)
		{
			return false;
		}

		if (session.GetCurrentPhase() != GamePhase.Day ||
		    session.GetSubPhase<DaySubPhases>() !=
			    DaySubPhases.NormalVoting ||
		    pendingInstruction is not
			    SelectOptionsInstruction signalInstruction ||
		    signalInstruction.SelectionRange !=
			    NumberRangeConstraint.Single ||
		    !signalInstruction.Options
			    .Select(option => option.Id)
			    .SequenceEqual(
				    [
					    StutteringJudgeSignalOptionIds.Occurred,
					    StutteringJudgeSignalOptionIds.DidNotOccur
				    ],
				    StringComparer.Ordinal))
		{
			throw new InvalidOperationException(
				"The pending Stuttering Judge signal instruction is structurally invalid.");
		}

		listenerState =
			StutteringJudgeRoleState.AwaitingSignalObservation.ToString();
		return true;
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			null,
			[
				StutteringJudgeRoleState.Awake,
				StutteringJudgeRoleState.NightComplete
			],
			HandleRoleWakeupAndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			StutteringJudgeRoleState.Awake,
			StutteringJudgeRoleState.AwaitingSignalSetup,
			HandleNightPowerUse_AndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			StutteringJudgeRoleState.AwaitingSignalSetup,
			StutteringJudgeRoleState.NightComplete,
			RecordSignalSetup),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			StutteringJudgeRoleState.NightComplete,
			(_, _) => HookListenerActionResult.Complete(
				StutteringJudgeRoleState.NightComplete)),
			CreateStage(
				GameHook.OnVoteConducted,
				null,
				[
					StutteringJudgeRoleState.AwaitingVoteConductConfirmation,
					StutteringJudgeRoleState.AwaitingSignalObservation,
					StutteringJudgeRoleState.DayComplete
				],
				RequestVoteConductConfirmation),
			CreateStage(
				GameHook.OnVoteConducted,
				StutteringJudgeRoleState.AwaitingVoteConductConfirmation,
				StutteringJudgeRoleState.AwaitingSignalObservation,
				RequestSignalObservation),
		CreateStage(
			GameHook.OnVoteConducted,
			StutteringJudgeRoleState.AwaitingSignalObservation,
			StutteringJudgeRoleState.DayComplete,
			RecordSignalObservation),
		CreateEndStage(
			GameHook.OnVoteConducted,
			StutteringJudgeRoleState.DayComplete,
			(_, _) => HookListenerActionResult.Complete(
				StutteringJudgeRoleState.DayComplete))
	];

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		var judge = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Stuttering Judge is available for signal setup.");

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.EstablishStutteringJudgeSignal,
				privateInstruction:
					GameStrings.StutteringJudgeSignalSetupInstruction,
				affectedPlayerIds: [judge.Id]),
			StutteringJudgeRoleState.AwaitingSignalSetup);
	}

	private HookListenerActionResult RecordSignalSetup(
		GameSession session,
		ModeratorResponse input)
	{
		var judge = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Stuttering Judge is available to complete signal setup.");
		RecordSignalEstablished(session, judge.Id);
		return HookListenerActionResult.Complete(
			StutteringJudgeRoleState.NightComplete);
	}

	private HookListenerActionResult RequestSignalObservation(
		GameSession session,
		ModeratorResponse input)
	{
		var judge = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Stuttering Judge is available for signal observation.");

		return HookListenerActionResult.NeedInput(
			new SelectOptionsInstruction(
				ModeratorInstructionSemantic.ObserveStutteringJudgeSignal,
				[
					new ModeratorOption(
						StutteringJudgeSignalOptionIds.Occurred,
						GameStrings.StutteringJudgeSignalOccurredOption),
					new ModeratorOption(
						StutteringJudgeSignalOptionIds.DidNotOccur,
						GameStrings.StutteringJudgeSignalDidNotOccurOption)
				],
				NumberRangeConstraint.Single,
				privateInstruction:
					GameStrings.StutteringJudgeSignalObservationInstruction,
				affectedPlayerIds: [judge.Id]),
			StutteringJudgeRoleState.AwaitingSignalObservation);
	}

	private HookListenerActionResult RequestVoteConductConfirmation(
		GameSession session,
		ModeratorResponse input)
	{
		var judge = GetAliveRolePlayers(session)?.SingleOrDefault();
		if (judge == null ||
		    !IsSignalOpportunityAvailable(session, judge))
		{
			return HookListenerActionResult.Complete(
				StutteringJudgeRoleState.DayComplete);
		}

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.ConductDayVote,
				publicAnnouncement: GameStrings.VoteStartsPublicInstruction,
				privateInstruction: GameStrings.DayVoteConductInstruction),
			StutteringJudgeRoleState.AwaitingVoteConductConfirmation);
	}

	private HookListenerActionResult RecordSignalObservation(
		GameSession session,
		ModeratorResponse input)
	{
		var judge = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Stuttering Judge is available for signal observation.");
		if (!IsSignalOpportunityAvailable(session, judge))
		{
			throw new InvalidOperationException(
				"The Stuttering Judge signal opportunity is no longer available.");
		}

		var selectedOptionId = input.SelectedOptionIds?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"Stuttering Judge signal observation requires one semantic option.");
		if (StringComparer.Ordinal.Equals(
			    selectedOptionId,
			    StutteringJudgeSignalOptionIds.DidNotOccur))
		{
			RecordSignalDidNotOccur(session, judge.Id);
			return HookListenerActionResult.Complete(
				StutteringJudgeRoleState.DayComplete);
		}

		if (!StringComparer.Ordinal.Equals(
			    selectedOptionId,
			    StutteringJudgeSignalOptionIds.Occurred))
		{
			throw new InvalidOperationException(
				"The Stuttering Judge signal option is unknown.");
		}

		DayVoteRules.CommitOneUseDayAction(
			session,
			DayPowerType.JudgeExtraVote,
			CreateResourceIdentity(judge));
		return HookListenerActionResult.Complete(
			StutteringJudgeRoleState.DayComplete);
	}

	private bool IsSignalOpportunityAvailable(
		GameSession session,
		IPlayer judge)
	{
		var instance = RolePowerInstance.CreateNative(
			judge,
			MainRoleType.StutteringJudge,
			ConsecutiveVotePower);
		if (GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			    session,
			    CreateResourceIdentity(judge, instance)))
		{
			return false;
		}
		return _availabilityGateway.Evaluate(
				new RolePowerAttempt(
					judge,
					MainRoleType.StutteringJudge,
					ConsecutiveVotePower,
					instance,
					new OneUseRolePowerResource(
						ConsecutiveVoteResourceId,
						instance)))
			.AvailabilityResult.IsAvailable;
	}

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		IPlayer judge)
	{
		var instance = RolePowerInstance.CreateNative(
			judge,
			MainRoleType.StutteringJudge,
			ConsecutiveVotePower);
		return CreateResourceIdentity(judge, instance);
	}

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		IPlayer judge,
		RolePowerInstance instance) => new(
		judge.Id,
		MainRoleType.StutteringJudge,
		ConsecutiveVotePower.Identifier.Value,
		instance.Id,
		instance.Origin,
		ConsecutiveVoteResourceId);

	private static void RecordSignalEstablished(
		GameSession session,
		Guid judgePlayerId)
	{
		EnsureJudgePlayerId(judgePlayerId);
		session.CommitGameFact(context =>
			new StutteringJudgeSignalEstablishedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				JudgePlayerId = judgePlayerId
			});
	}

	private static void RecordSignalDidNotOccur(
		GameSession session,
		Guid judgePlayerId)
	{
		EnsureJudgePlayerId(judgePlayerId);
		session.CommitGameFact(context =>
			new StutteringJudgeSignalDidNotOccurLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				JudgePlayerId = judgePlayerId
			});
	}

	private static void EnsureJudgePlayerId(Guid judgePlayerId)
	{
		if (judgePlayerId == Guid.Empty)
		{
			throw new ArgumentException(
				"The Stuttering Judge holder identity is required.",
				nameof(judgePlayerId));
		}
	}

	private static bool HasEstablishedSignal(GameSession session) =>
		GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(session);
}
