using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
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
	private sealed record BorrowedExecutionContext(
		IPlayer ActingPlayer,
		RolePowerInstance PowerInstance);

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
			if (TryResolveBorrowedExecution(session, out var borrowedExecution))
			{
				return GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
						session,
						CreatePowerIdentity(borrowedExecution))
					? HookListenerActionResult.Skip()
					: ExecuteCore(session, input);
			}

			if (session.TurnNumber != 1 ||
			    HasEstablishedSignal(session))
			{
				return HookListenerActionResult.Skip();
			}

			return base.Execute(session, input);
		}

		if (hook == GameHook.OnVoteConducted)
		{
			if (GameSessionQueries.GetCurrentDayVoteOutcome(session) != null)
			{
				return HookListenerActionResult.Skip();
			}

			if (TryResolveBorrowedDayExecution(
					session,
					out var borrowedExecution))
			{
				return GameSessionQueries.HasStutteringJudgeSignalBeenObserved(
							session,
							CreatePowerIdentity(borrowedExecution))
					? HookListenerActionResult.Skip()
					: ExecuteCore(session, input);
			}

			if (!HasEstablishedSignal(session))
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
		if (hook == GameHook.NightMainActionLoop)
		{
			if (pendingInstruction is ConfirmationInstruction
			    {
				    Semantic:
					    ModeratorInstructionSemantic
						    .EstablishStutteringJudgeSignal
			    } &&
			    (HasExpectedAffectedRoleHolders(session, pendingInstruction) ||
			     HasValidBorrowedSignalSetupInstruction(
				     session,
				     pendingInstruction)))
			{
				listenerState =
					StutteringJudgeRoleState.AwaitingSignalSetup.ToString();
				return true;
			}

			if (pendingInstruction is ConfirmationInstruction
			    {
				    Semantic:
					    ModeratorInstructionSemantic.PutRoleToSleep
			    } &&
			    HasValidBorrowedSignalSetupSleep(
				    session,
				    pendingInstruction))
			{
				listenerState =
					StutteringJudgeRoleState.NightComplete.ToString();
				return true;
			}

			return base.TryResolvePendingInstructionContinuation(
				hook,
				session,
				pendingInstruction,
				out listenerState);
		}

		if (hook != GameHook.OnVoteConducted ||
		    pendingInstruction.Semantic !=
			    ModeratorInstructionSemantic.ObserveStutteringJudgeSignal)
		{
			return false;
		}

		if (session.GetCurrentPhase() != GamePhase.Day ||
		    session.GetSubPhase<DaySubPhases>() !=
			    DaySubPhases.NormalVoting ||
		    GameSessionQueries.GetCurrentDayVoteOutcome(session) != null ||
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

		IPlayer signalObserver;
		if (session.GetModeratorActiveActorBorrowedRolePowerActivation()
			    ?.SourceRole == MainRoleType.StutteringJudge)
		{
			if (!TryResolveBorrowedDayExecution(
				    session,
				    out var borrowedExecution))
			{
				throw new InvalidOperationException(
					"The pending Actor borrowed Stuttering Judge signal instruction is stale.");
			}

			signalObserver = borrowedExecution.ActingPlayer;
		}
		else
		{
			var nativeObserver =
				GetAliveRolePlayers(session)?.SingleOrDefault();
			if (nativeObserver is null ||
			    !GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
				    session,
				    nativeObserver.Id))
			{
				throw new InvalidOperationException(
					"The pending Stuttering Judge signal instruction has no valid execution.");
			}

			signalObserver = nativeObserver;
		}

		if (signalInstruction.PublicAnnouncement != null ||
			!StringComparer.Ordinal.Equals(
				signalInstruction.PrivateInstruction,
				GameStrings.StutteringJudgeSignalObservationInstruction) ||
			signalInstruction.AffectedPlayerIds is not [var affectedPlayerId] ||
			affectedPlayerId != signalObserver.Id)
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
			StutteringJudgeRoleState.Awake);
	}

	protected override HookListenerActionResult HandleNightPowerUse_AndId(
		GameSession session,
		ModeratorResponse input) =>
		TryResolveBorrowedExecution(session, out _)
			? HandleNightPowerUse(session, input)
			: base.HandleNightPowerUse_AndId(session, input);

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		var actingPlayer = TryResolveBorrowedExecution(session, out var execution)
			? execution.ActingPlayer
			: GetAliveRolePlayers(session)?.SingleOrDefault()
			  ?? throw new InvalidOperationException(
				  "No living Stuttering Judge is available for signal setup.");

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.EstablishStutteringJudgeSignal,
				privateInstruction:
					GameStrings.StutteringJudgeSignalSetupInstruction,
				affectedPlayerIds: [actingPlayer.Id]),
			StutteringJudgeRoleState.AwaitingSignalSetup);
	}

	private HookListenerActionResult RecordSignalSetup(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedExecution(session, out var execution))
		{
			var powerIdentity = CreatePowerIdentity(execution);
			if (GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
					session,
					powerIdentity))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Stuttering Judge signal is already established for this activation.");
			}

			session.CommitActorBorrowedStutteringJudgeSignalSetup(powerIdentity);
			return HookListenerActionResult.NeedInput(
				new ConfirmationInstruction(
					ModeratorInstructionSemantic.PutRoleToSleep,
					GameStrings.RoleGoesToSleepSingle.Format(
						GameStrings.ActorRoleName),
					affectedPlayerIds: [execution.ActingPlayer.Id]),
				StutteringJudgeRoleState.NightComplete);
		}

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
		var actingPlayer = TryResolveBorrowedDayExecution(
				session,
				out var borrowedExecution)
			? borrowedExecution.ActingPlayer
			: GetAliveRolePlayers(session)?.SingleOrDefault()
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
				affectedPlayerIds: [actingPlayer.Id]),
			StutteringJudgeRoleState.AwaitingSignalObservation);
	}

	private HookListenerActionResult RequestVoteConductConfirmation(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedDayExecution(session, out var borrowedExecution))
		{
			if (!IsSignalOpportunityAvailable(session, borrowedExecution))
			{
				return HookListenerActionResult.Complete(
					StutteringJudgeRoleState.DayComplete);
			}

			return CreateVoteConductConfirmation();
		}

		var judge = GetAliveRolePlayers(session)?.SingleOrDefault();
		if (judge == null ||
		    !IsSignalOpportunityAvailable(session, judge))
		{
			return HookListenerActionResult.Complete(
				StutteringJudgeRoleState.DayComplete);
		}

		return CreateVoteConductConfirmation();
	}

	private static HookListenerActionResult CreateVoteConductConfirmation() =>
		HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.ConductDayVote,
				publicAnnouncement: GameStrings.VoteStartsPublicInstruction,
				privateInstruction: GameStrings.DayVoteConductInstruction),
			StutteringJudgeRoleState.AwaitingVoteConductConfirmation);

	private HookListenerActionResult RecordSignalObservation(
		GameSession session,
		ModeratorResponse input)
	{
		if (TryResolveBorrowedDayExecution(session, out var borrowedExecution))
		{
			if (!IsSignalOpportunityAvailable(session, borrowedExecution))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Stuttering Judge signal opportunity is no longer available.");
			}

			var borrowedOptionId = GetSignalObservationOption(input);
			var signalOccurred = StringComparer.Ordinal.Equals(
				borrowedOptionId,
				StutteringJudgeSignalOptionIds.Occurred);
			session.CommitActorBorrowedStutteringJudgeSignalObservation(
				CreatePowerIdentity(borrowedExecution),
				signalOccurred,
				signalOccurred
					? CreateResourceIdentity(borrowedExecution)
					: null);
			return HookListenerActionResult.Complete(
				StutteringJudgeRoleState.DayComplete);
		}

		var judge = GetAliveRolePlayers(session)?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"No living Stuttering Judge is available for signal observation.");
		if (!IsSignalOpportunityAvailable(session, judge))
		{
			throw new InvalidOperationException(
				"The Stuttering Judge signal opportunity is no longer available.");
		}

		var selectedOptionId = GetSignalObservationOption(input);
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
			CreateResourceIdentity(session, judge));
		return HookListenerActionResult.Complete(
			StutteringJudgeRoleState.DayComplete);
	}

	private static string GetSignalObservationOption(ModeratorResponse input)
	{
		var selectedOptionId = input.SelectedOptionIds?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"Stuttering Judge signal observation requires one semantic option.");
		if (!StringComparer.Ordinal.Equals(
				selectedOptionId,
				StutteringJudgeSignalOptionIds.Occurred) &&
			!StringComparer.Ordinal.Equals(
				selectedOptionId,
				StutteringJudgeSignalOptionIds.DidNotOccur))
		{
			throw new InvalidOperationException(
				"The Stuttering Judge signal option is unknown.");
		}

		return selectedOptionId;
	}

	private bool IsSignalOpportunityAvailable(
		GameSession session,
		IPlayer judge)
	{
		var instance = RolePowerInstance.CreateCurrent(
			session,
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
					session,
					judge,
					MainRoleType.StutteringJudge,
					ConsecutiveVotePower,
					instance,
					new OneUseRolePowerResource(
						ConsecutiveVoteResourceId,
						instance)))
			.AvailabilityResult.IsAvailable;
	}

	private bool IsSignalOpportunityAvailable(
		GameSession session,
		BorrowedExecutionContext execution)
	{
		var powerIdentity = CreatePowerIdentity(execution);
		var resourceIdentity = CreateResourceIdentity(execution);
		if (GameSessionQueries.HasStutteringJudgeSignalBeenObserved(
				session,
				powerIdentity) ||
			GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				session,
				resourceIdentity))
		{
			return false;
		}

		return _availabilityGateway.Evaluate(
				new RolePowerAttempt(
					session,
					execution.ActingPlayer,
					MainRoleType.StutteringJudge,
					ConsecutiveVotePower,
					execution.PowerInstance,
					new OneUseRolePowerResource(
						ConsecutiveVoteResourceId,
						execution.PowerInstance)))
			.AvailabilityResult.IsAvailable;
	}

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		GameSession session,
		IPlayer judge)
	{
		var instance = RolePowerInstance.CreateCurrent(
			session,
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

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		BorrowedExecutionContext execution) =>
		CreateResourceIdentity(execution.ActingPlayer, execution.PowerInstance);

	private static bool TryResolveBorrowedExecution(
		GameSession session,
		out BorrowedExecutionContext execution)
	{
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (activation?.SourceRole != MainRoleType.StutteringJudge)
		{
			execution = null!;
			return false;
		}

		var actor = session.GetPlayer(activation.ActingPlayerId);
		execution = new BorrowedExecutionContext(
			actor,
			RolePowerInstance.CreateBorrowed(
				session,
				actor,
				MainRoleType.StutteringJudge,
				ConsecutiveVotePower));
		return true;
	}

	private static bool TryResolveBorrowedDayExecution(
		GameSession session,
		out BorrowedExecutionContext execution)
	{
		execution = null!;
		return session.GetCurrentPhase() == GamePhase.Day &&
			TryResolveBorrowedExecution(session, out execution) &&
			GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
				session,
				CreatePowerIdentity(execution));
	}

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		BorrowedExecutionContext execution) => new(
			execution.ActingPlayer.Id,
			MainRoleType.StutteringJudge,
			ConsecutiveVotePower.Identifier.Value,
			execution.PowerInstance.Id,
			execution.PowerInstance.Origin);

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

	private bool HasEstablishedSignal(GameSession session)
	{
		var judge = GetAliveRolePlayers(session)?.SingleOrDefault();
		return judge is not null &&
		       GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
			       session,
			       judge.Id);
	}

	internal static bool HasValidEstablishedSignal(GameSession session)
	{
		if (session.GetModeratorActiveActorBorrowedRolePowerActivation()
			?.SourceRole == MainRoleType.StutteringJudge)
		{
			return HasValidBorrowedSignalSetupSleep(
				session,
				session.PendingModeratorInstruction);
		}

		var judges = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.State.CurrentRole == MainRoleType.StutteringJudge)
			.ToArray();
		var entries = session.GameHistoryLog
			.OfType<StutteringJudgeSignalEstablishedLogEntry>()
			.ToArray();
		return judges is [var judge] &&
		       entries is [var entry] &&
		       entry.JudgePlayerId == judge.Id;
	}

	private static bool HasValidBorrowedSignalSetupSleep(
		GameSession session,
		ModeratorInstruction? pendingInstruction)
	{
		var expectedPublicAnnouncement =
			GameStrings.RoleGoesToSleepSingle.Format(
				GameStrings.ActorRoleName);
		if (pendingInstruction is not ConfirmationInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic.PutRoleToSleep,
			    PublicAnnouncement: var publicAnnouncement,
			    PrivateInstruction: null,
			    AffectedPlayerIds: [var affectedPlayerId]
		    } ||
		    !StringComparer.Ordinal.Equals(
			    publicAnnouncement,
			    expectedPublicAnnouncement))
		{
			return false;
		}

		if (!TryResolveBorrowedExecution(session, out var borrowedExecution))
		{
			return false;
		}

		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		var powerIdentity = CreatePowerIdentity(borrowedExecution);
		var matchingSetups = session
			.GetActorBorrowedStutteringJudgeSignalSetupCommits()
			.Where(commit =>
				commit.PowerIdentity == powerIdentity &&
				commit.ActorSetupCardId == activation.SelectedCardId)
			.ToArray();
		return matchingSetups is [var setup] &&
		       setup.TurnNumber == session.TurnNumber &&
		       setup.CurrentPhase == GamePhase.Night &&
		       affectedPlayerId == borrowedExecution.ActingPlayer.Id;
	}

	private static bool HasValidBorrowedSignalSetupInstruction(
		GameSession session,
		ModeratorInstruction pendingInstruction)
	{
		if (session.GetCurrentPhase() != GamePhase.Night ||
		    !TryResolveBorrowedExecution(session, out var borrowedExecution) ||
		    GameSessionQueries.HasStutteringJudgeSignalBeenEstablished(
			    session,
			    CreatePowerIdentity(borrowedExecution)))
		{
			return false;
		}

		return pendingInstruction is ConfirmationInstruction
		       {
			       Semantic:
				       ModeratorInstructionSemantic
					       .EstablishStutteringJudgeSignal,
			       PublicAnnouncement: null,
			       PrivateInstruction: var privateInstruction,
			       AffectedPlayerIds: [var affectedPlayerId]
		       } &&
		       StringComparer.Ordinal.Equals(
			       privateInstruction,
			       GameStrings.StutteringJudgeSignalSetupInstruction) &&
		       affectedPlayerId == borrowedExecution.ActingPlayer.Id;
	}
}
