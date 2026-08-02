using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;
using static Werewolves.Core.GameLogic.Models.InternalMessages.MainPhaseHandlerResult;
using static Werewolves.Core.GameLogic.Models.InternalMessages.SubPhaseHandlerResult;
using static Werewolves.Core.GameLogic.Models.StateMachine.HookSubPhaseStage;
using static Werewolves.Core.GameLogic.Models.StateMachine.EliminationCascadeStage;
using static Werewolves.Core.GameLogic.Models.StateMachine.LogicSubPhaseStage;
using static Werewolves.Core.GameLogic.Models.StateMachine.NavigationSubPhaseStage;
using static Werewolves.Core.StateModels.Enums.GameHook;
using static Werewolves.Core.StateModels.Enums.MainRoleType;
using static Werewolves.Core.StateModels.Enums.StatusEffectTypes;
using static Werewolves.Core.StateModels.Models.ListenerIdentifier;

namespace Werewolves.Core.GameLogic.Services;

/// <summary>
/// Holds the state machine configuration and provides access to phase definitions.
/// </summary>
internal static class GameFlowManager
{
    private class GameFlowManagerKey : IGameFlowManagerKey;

    private static readonly GameFlowManagerKey Key = new();

    private enum AcceptedObservationInstructionShape
    {
        PlayerSelection,
        Confirmation
    }

    private readonly record struct AcceptedObservationContinuation(
        string ActiveSubPhaseStage,
        ListenerIdentifier Listener,
        string ListenerState,
        AcceptedObservationInstructionShape InstructionShape)
    {
        internal bool Matches(ModeratorInstruction? instruction)
            => InstructionShape switch
            {
                AcceptedObservationInstructionShape.PlayerSelection =>
                    instruction?.GetType() == typeof(SelectPlayersInstruction),
                AcceptedObservationInstructionShape.Confirmation =>
                    instruction?.GetType() == typeof(ConfirmationInstruction),
                _ => false
            };
    }

    #region Static Flow Definitions
    internal static readonly Dictionary<GameHook, List<ListenerIdentifier>> HookListeners = new()
    {
        // Define hook-to-listener mappings here.
        // ORDER MATTERS!!!!
        [NightMainActionLoop] = 
		[
			Listener(Thief),                //first night only
			Listener(PrejudicedManipulator), //first night only
			Listener(Actor),
            Listener(LittleGirl),           //first night only
            Listener(Cupid),                //first night only
            Listener(Lovers),              //first night only
            Listener(StutteringJudge),      //first night only
            Listener(Elder),                //first night only, required to enable disregarding wolf infection
            Listener(TwoSisters),
            Listener(ThreeBrothers),
            Listener(WildChild),            //first night only
            Listener(WolfHound),            //first night only
            Listener(BearTamer),            //first night only
            Listener(Defender),
            Listener(SimpleWerewolf),
            Listener(Fox),
            Listener(AccursedWolfFather),
            Listener(WhiteWerewolf),
            Listener(BigBadWolf),
			Listener(Seer),
            Listener(Witch),
            Listener(Gypsy),
            Listener(Piper),
            Listener(Charmed),
            Listener(KnightWithRustySword)  //should not wake the player, just check if the knight was killed the previous day by werewolves
                                            //and applies the effect if so
		],

        [DawnMainActionLoop] =
        [
            Listener(KnightWithRustySword),
            Listener(BearTamer),
            Listener(Gypsy),
            Listener(TownCrier),
        ],

        [OnVoteConcluded] =
        [
            Listener(Scapegoat),            // in case of a tie, scapegoat ability triggers
			Listener(Elder),                // after a complete village-Vote Elimination Cascade
        ],

        [OnVoteConducted] =
        [
            Listener(StutteringJudge),      // signal is observed before the Vote result
        ],
	};

	// Elimination reaction boundaries and dispatch order are correctness
	// properties and stay centralized here, beside the hook ordering table.
	internal static readonly IReadOnlyList<
		EliminationCascadeReactionRegistration>
	EliminationCascadeReactionRegistrations =
	[
		new(
			EliminationCascadeReactionIds.DevotedServantVoteWindow,
			EliminationCascadeReactionBoundary.PreReveal,
			Listener(DevotedServant)),
		new(
			EliminationCascadeReactionIds.RustySwordDiseaseAnnouncement,
			EliminationCascadeReactionBoundary.PreReveal,
			Listener(KnightWithRustySword)),
		new(
			EliminationCascadeReactionIds.WildChildModelEliminated,
			EliminationCascadeReactionBoundary.Forced,
			Listener(WildChild)),
		new(
			EliminationCascadeReactionIds.LoversHeartbreak,
			EliminationCascadeReactionBoundary.Forced,
			Listener(Cupid)),
		new(
			EliminationCascadeReactionIds.HunterFinalShot,
			EliminationCascadeReactionBoundary.Interactive,
			Listener(Hunter))
	];

    /// <summary>
    /// Factory functions for creating listener instances. Each game session gets its own fresh instances.
    /// This ensures listener state machines are isolated between games (fixing test isolation bugs).
    /// </summary>
    internal static IReadOnlyDictionary<ListenerIdentifier, Func<IGameHookListener>> ListenerFactories =>
        SupportedRoleCatalog.ListenerFactories;

    internal static readonly Dictionary<GamePhase, IPhaseDefinition> PhaseDefinitions = new()
    {
        [GamePhase.Night] = CreateNightPhase(),
        [GamePhase.Dawn] = CreateDawnPhase(),
        [GamePhase.Day] = CreateDayPhase()
    };

    private static IPhaseDefinition CreateNightPhase()
        => new PhaseManager<NightSubPhases>(
            entrySubPhase: NightSubPhases.Start,
            subPhaseList:
            [
                new(
                    subPhase: NightSubPhases.Start,
                    subPhaseStages:
                    [
                        LogicStage(
                            NightSubPhaseStage.RequestVillagerVillagerPublicFromDealObservation,
                            RoleKnowledgeHandlers.RequestVillagerVillagerPublicFromDealObservation),
                        LogicStage(
                            NightSubPhaseStage.RecordVillagerVillagerPublicFromDealObservation,
                            RoleKnowledgeHandlers.RecordVillagerVillagerPublicFromDealObservation),
                        LogicStage(NightSubPhaseStage.NightStart, NightPhaseHandlers.StartNight),
                        HookStage(NightMainActionLoop),
                        NavigationEndStage(NightSubPhaseStage.NightEnd, FinishNightAndGoToDawn)
                    ],
                    possibleNextMainPhaseTransitions:
                    [
                        new(GamePhase.Dawn)
                    ])
            ]);

    private static IPhaseDefinition CreateDawnPhase()
        => new PhaseManager<DawnSubPhases>(
            entrySubPhase: DawnSubPhases.CalculateVictims,
            subPhaseList:
            [
                new(
                    subPhase: DawnSubPhases.CalculateVictims,
                    subPhaseStages:
                    [
						LogicStage(
							DawnSubPhaseStage.RequestElderRoleIdentification,
							DawnPhaseHandlers.RequestElderRoleIdentification),
						LogicStage(
							DawnSubPhaseStage.RecordElderRoleIdentification,
							DawnPhaseHandlers.RecordElderRoleIdentification),
                        NavigationEndStage(DawnSubPhaseStage.CheckForVictims, CalculateVictimsAndChooseDawnPath)
                    ],
                    possibleNextSubPhases:
                    [
                        DawnSubPhases.AnnounceVictims,
                        DawnSubPhases.Finalize
                    ]),
                new(
                    subPhase: DawnSubPhases.AnnounceVictims,
                    subPhaseStages:
                    [
                        CascadeStage(
                            DawnSubPhaseStage.ResolveEliminationCascade,
                            CreateDawnEliminationCascadeSeed,
                            ModeratorInstructionSemantic.AssignDawnVictimRoles,
                            CreateDawnEliminationAnnouncement),
                        NavigationEndStageSilent(DawnSubPhases.Finalize)
                    ],
                    possibleNextSubPhases:
                    [
                        DawnSubPhases.Finalize
                    ]),
                new(
                    subPhase: DawnSubPhases.Finalize,
                    subPhaseStages:
                    [
                        LogicStage(
                            DawnSubPhaseStage.EnsureVictoryFactsReady,
                            EnsureVictoryFactsReady),
                        HookStage(DawnMainActionLoop),
                        NavigationEndStageSilent(GamePhase.Day)
                    ],
                    possibleNextMainPhaseTransitions:
                    [
                        new(GamePhase.Day)
                    ])
            ]);

    private static IPhaseDefinition CreateDayPhase()
        => new PhaseManager<DaySubPhases>(
            entrySubPhase: DaySubPhases.Debate,
            subPhaseList:
            [
                new(
                    subPhase: DaySubPhases.Debate,
                    subPhaseStages:
                    [
                        NavigationEndStage(DaySubPhaseStage.Debate, StartDebateAndGoToVoteType)
                    ],
                    possibleNextSubPhases:
                    [
                        DaySubPhases.DetermineVoteType
                    ]),
                new(
                    subPhase: DaySubPhases.DetermineVoteType,
                    subPhaseStages:
                    [
                        NavigationEndStage(DaySubPhases.DetermineVoteType, GoToNormalVoting)
                    ],
                    possibleNextSubPhases:
                    [
                        DaySubPhases.NormalVoting,
                        DaySubPhases.Finalize
                    ]),
                new(
                    subPhase: DaySubPhases.NormalVoting,
                    subPhaseStages:
                    [
                        HookStage(OnVoteConducted),
                        LogicStage(
	                        DaySubPhaseStage.RequestVote,
	                        DayPhaseHandlers.RequestNormalVoteOutcome),
                        NavigationEndStage(DaySubPhaseStage.HandleVoteResponse, RecordNormalVoteAndChooseDayPath)
                            .RequiresInputType(ExpectedInputType.PlayerSelection)
                    ],
                    possibleNextSubPhases:
                    [
                        DaySubPhases.HandleNonTieVote,
                        DaySubPhases.ProcessVoteOutcome
                    ]),
                new(
                    subPhase: DaySubPhases.HandleNonTieVote,
                    subPhaseStages:
                    [
                        CascadeStage(
                            DaySubPhaseStage.ResolveEliminationCascade,
                            CreateCurrentVoteEliminationCascadeSeed,
                            ModeratorInstructionSemantic.AssignDayVoteTargetRole,
                            interceptBeforeCommit: InterceptVoteElimination,
                            createPostCommitInstruction:
                                CreateVoteEliminationAnnouncement),
                        NavigationEndStageSilent(DaySubPhases.ProcessVoteOutcome)
                    ],
                    possibleNextSubPhases:
                    [
                        DaySubPhases.ProcessVoteOutcome
                    ]),
                new(
                    subPhase: DaySubPhases.ProcessVoteOutcome,
                    subPhaseStages:
                    [
                        HookStage(OnVoteConcluded),
                        NavigationEndStage(DaySubPhaseStage.VoteOutcomeNavigation, ChoosePathAfterVoteConcluded)
                    ],
                    possibleNextSubPhases:
                    [
                        DaySubPhases.DetermineVoteType,
                        DaySubPhases.Finalize
                    ]),
	                new(
	                    subPhase: DaySubPhases.Finalize,
	                    subPhaseStages:
	                    [
	                        LogicStage(
	                            DaySubPhaseStage.EnsureVictoryFactsReady,
	                            EnsureVictoryFactsReady),
	                        LogicStage(
	                            DaySubPhaseStage
	                                .ExpireVoterEligibilityRestriction,
	                            DayPhaseHandlers
	                                .ExpireVoterEligibilityRestriction),
	                        NavigationEndStageSilent(GamePhase.Night)
                    ],
                    possibleNextMainPhaseTransitions:
                    [
                        new(GamePhase.Night)
                    ])
            ]);

    private static MainPhaseHandlerResult FinishNightAndGoToDawn(GameSession session, ModeratorResponse input)
        => TransitionPhase(NightPhaseHandlers.FinishNightActions(session, input), GamePhase.Dawn);

    private static MajorNavigationPhaseHandlerResult CalculateVictimsAndChooseDawnPath(GameSession session, ModeratorResponse input)
    {
        DawnPhaseHandlers.CalculateVictims(session, input);

        return DawnPhaseHandlers.HasVictimsToAnnounce(session)
            ? TransitionSubPhaseSilent(DawnSubPhases.AnnounceVictims)
            : TransitionSubPhaseSilent(DawnSubPhases.Finalize);
    }

    private static EliminationCascadeSeed CreateDawnEliminationCascadeSeed(
        GameSession session)
    {
        var determinedVictims =
            GameSessionQueries.GetPendingDawnEliminations(session);
        if (determinedVictims.Count == 0)
        {
            throw new InvalidOperationException(
                "Dawn entered an Elimination Cascade without a determined victim.");
        }

        return new EliminationCascadeSeed(
            $"Dawn:{session.TurnNumber}",
            determinedVictims[0].LogIndex,
            determinedVictims
                .Select(victim => new EliminationRequest(
                    victim.Player.Id,
                    victim.Reason))
                .ToArray());
    }

    private static string? CreateDawnEliminationAnnouncement(
        GameSession session,
        IReadOnlyCollection<EliminationRequest> eliminations)
    {
        if (eliminations.Any(elimination =>
                elimination.Reason == EliminationReason.RustySword))
        {
            return null;
        }

        var victimNames = string.Join(
            Environment.NewLine,
            eliminations.Select(elimination =>
                session.GetPlayer(elimination.PlayerId).Name));
        return GameStrings.MultipleVictimEliminatedAnnounce.Format(victimNames);
    }

    private static EliminationCascadeSeed
        CreateCurrentVoteEliminationCascadeSeed(GameSession session)
    {
        var currentVote =
            GameSessionQueries.GetCurrentDayVoteOutcome(session);
        if (currentVote is not
            {
                PlayerId: var targetId
            } ||
            targetId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A non-tied Vote Elimination Cascade requires the latest Vote target.");
        }

        return new EliminationCascadeSeed(
            $"Day:{session.TurnNumber}:Vote:{currentVote.Value.VoteOrdinal}",
            currentVote.Value.LogIndex,
            [
                new EliminationRequest(
                    targetId,
                    EliminationReason.DayVote)
            ]);
    }

    private static EliminationBatchCommitDecision InterceptVoteElimination(
        GameSession session,
        IReadOnlyCollection<EliminationRequest> eliminations)
    {
        if (eliminations.Count != 1 ||
            eliminations.First() is not
            {
                Reason: EliminationReason.DayVote
            } voteElimination)
        {
            return EliminationBatchCommitDecision.Proceed(eliminations);
        }

        var target = session.GetPlayer(voteElimination.PlayerId);
        if (target.State.CurrentRole is not { } currentRole ||
            !session.TryGetExistingListener<IVoteEliminationInterceptor>(
                Listener(currentRole),
                out var interceptor) ||
            !interceptor.TryInterceptVoteElimination(
                session,
                target,
                out var consequence))
        {
            return EliminationBatchCommitDecision.Proceed(eliminations);
        }

        return new EliminationBatchCommitDecision(
            Eliminations: [],
            consequence);
    }

    private static ModeratorInstruction? CreateVoteEliminationAnnouncement(
        GameSession session,
        IReadOnlyCollection<EliminationRequest> eliminations)
    {
        var voteElimination = eliminations.SingleOrDefault(
            elimination => elimination.Reason == EliminationReason.DayVote);
        if (voteElimination == default)
        {
            return null;
        }

        return new ConfirmationInstruction(
            ModeratorInstructionSemantic.AnnounceDayElimination,
            publicAnnouncement:
                GameStrings.SingleVictimEliminatedAnnounce.Format(
                    session.GetPlayer(voteElimination.PlayerId).Name));
    }

    private static SubPhaseHandlerResult StartDebateAndGoToVoteType(GameSession session, ModeratorResponse input)
        => TransitionSubPhase(DayPhaseHandlers.StartDebate(session, input), DaySubPhases.DetermineVoteType);

    private static SubPhaseHandlerResult GoToNormalVoting(GameSession session, ModeratorResponse input)
        => TransitionSubPhaseSilent(
	        DayPhaseHandlers.CanConductVote(session)
		        ? DaySubPhases.NormalVoting
		        : DaySubPhases.Finalize);

    private static MajorNavigationPhaseHandlerResult RecordNormalVoteAndChooseDayPath(GameSession session, ModeratorResponse input)
    {
        var selectedPlayerId = DayPhaseHandlers.RecordNormalVoteOutcome(session, input);

        if (selectedPlayerId == null)
        {
            return TransitionSubPhaseSilent(DaySubPhases.ProcessVoteOutcome);
        }

        return TransitionSubPhaseSilent(DaySubPhases.HandleNonTieVote);
    }

    private static SubPhaseHandlerResult ChoosePathAfterVoteConcluded(GameSession session, ModeratorResponse input)
        => TransitionSubPhaseSilent(ChoosePostVoteOutcomeSubPhase(
            DayVoteRules.ShouldConductConsecutiveVote(session)));

    internal static DaySubPhases ChoosePostVoteOutcomeSubPhase(
        bool shouldVoteRepeat)
        => shouldVoteRepeat
            ? DaySubPhases.DetermineVoteType
            : DaySubPhases.Finalize;
	#endregion

	#region Static Factory Methods

    /// <summary>
    /// Gets the initial instruction to bootstrap a new game session.
    /// This is a pure function that generates the startup instruction without creating any game state.
    /// </summary>
    /// <param name="rolesInPlay">The roles that will be used in this game.</param>
    /// <param name="gameId">The unique identifier for the game session.</param>
    /// <returns>The initial instruction prompting the moderator to confirm game start</returns>
    public static StartGameConfirmationInstruction GetInitialInstruction(List<MainRoleType> rolesInPlay, Guid gameId)
    {
        // Validate inputs
        ArgumentNullException.ThrowIfNull(rolesInPlay);
        if (!rolesInPlay.Any())
        {
            throw new ArgumentException("Role list cannot be empty", nameof(rolesInPlay));
        }

        return new StartGameConfirmationInstruction(gameId);
    }

    #endregion

	#region State Machine

	internal static ProcessResult HandleInput(
        GameSession session,
        ModeratorResponse input,
        IRoleAdmissionSource admissions)
    {
        var startingPhase = session.GetCurrentPhase();
        var startingInstruction = session.PendingModeratorInstruction;
        var startingListener = session.GetCurrentListener();
        var startingLogCount = session.GameHistoryLog.Count();
        ModeratorInstruction? nextInstructionToSend = null;

        while (nextInstructionToSend == null)
        {
            var oldPhase = session.GetCurrentPhase();
            var handlerResult = RouteInputToPhaseHandler(session, input);
            var newPhase = session.GetCurrentPhase();

            // A silent main-phase transition is a resolution boundary. Check victory
            // before routing any work owned by the phase that was just entered.
            if (TryGetVictoryInstructions(session, oldPhase, newPhase, out var victoryInstruction))
            {
                nextInstructionToSend = victoryInstruction;
                break;
            }

            nextInstructionToSend = handlerResult.ModeratorInstruction;
		}

        if (nextInstructionToSend == null)
        {
            throw new InvalidOperationException("HandleInput: null nextInstructionToSend");
        }

        // --- Update Pending Instruction ---
		session.SetPendingModeratorInstruction(Key, nextInstructionToSend);
        var endingPhase = session.GetCurrentPhase();
		if (ShouldAdvanceRecoveryBoundary(
                session,
                startingPhase,
                endingPhase,
                startingInstruction,
                nextInstructionToSend,
                startingLogCount))
		{
            var domainRecoveryCursor = CreateDomainRecoveryCursor(
                session,
                startingInstruction,
                input,
                nextInstructionToSend,
                startingLogCount,
                admissions);
			session.CaptureRecoveryBoundary(
                Key,
                domainRecoveryCursor == null
                    ? CreateAcceptedObservationRecoveryCursor(
	                    session,
                        startingInstruction,
                        startingListener,
                        nextInstructionToSend,
                        admissions)
                    : null,
                domainRecoveryCursor);
		}

		return ProcessResult.Success(nextInstructionToSend);
    }

    private static bool ShouldAdvanceRecoveryBoundary(
        GameSession session,
        GamePhase oldPhase,
        GamePhase newPhase,
        ModeratorInstruction? startingInstruction,
        ModeratorInstruction nextInstructionToSend,
        int startingLogCount)
    {
        if (oldPhase != newPhase)
        {
            return true;
        }

	        if (IsAcceptedObservation(startingInstruction))
	        {
	            return true;
	        }

	        if (nextInstructionToSend.Semantic is
	            ModeratorInstructionSemantic.ObserveStutteringJudgeSignal or
	            ModeratorInstructionSemantic.ChooseThiefOffer or
	            ModeratorInstructionSemantic.ChooseActorSetupCard)
	        {
		        return true;
	        }

        if (startingInstruction?.Semantic is
            ModeratorInstructionSemantic.RevealFoxResult or
            ModeratorInstructionSemantic.RevealSeerResult)
        {
            return true;
        }

			if (IsEliminationCascadeReactionInput(nextInstructionToSend))
			{
				return true;
			}

        if (HasNewOneUseRolePowerCommit(session, startingLogCount))
        {
            return true;
        }

        if (HasNewTargetPrivateRolePowerCommit(session, startingLogCount))
        {
            return true;
        }

		if (HasNewActorBorrowedRolePowerCommit(session, startingLogCount))
		{
			return true;
		}

        if (HasNewRecurringRolePowerCommit(session, startingLogCount))
        {
            return true;
        }

		if (HasNewActorRuntimeCommit(session, startingLogCount))
		{
			return true;
		}

		if (HasNewThiefOfferCommit(session, startingLogCount))
		{
			return true;
		}

		if (HasNewEliminationCascadeReactionCompletion(
			session,
			startingLogCount))
		{
			return true;
		}

		if (HasNewEliminationCascadeBatchResolution(
			session,
			startingLogCount))
		{
			return true;
		}

		if (HasNewEliminationCascadeCompletion(
			session,
			startingLogCount))
		{
			return true;
		}

        return newPhase == GamePhase.Night &&
               !session.GameHistoryLog.Any() &&
               nextInstructionToSend is ConfirmationInstruction
               {
                   PublicAnnouncement: var announcement
               } &&
               announcement == GameStrings.NightStartsPrompt;
    }

    private static bool HasNewOneUseRolePowerCommit(
        GameSession session,
        int startingLogCount) =>
        session.GameHistoryLog
            .Skip(startingLogCount)
            .OfType<OneUseRolePowerCommittedLogEntry>()
            .Any();

    private static bool HasNewRecurringRolePowerCommit(
        GameSession session,
        int startingLogCount)
    {
        var hasRecurringRolePowerCommit = session.GameHistoryLog
            .Skip(startingLogCount)
            .OfType<RecurringRolePowerCommittedLogEntry>()
            .Any();
        return hasRecurringRolePowerCommit ||
               GameSessionQueries.GetCommittedLoversPairsSince(
                       session,
                       startingLogCount)
                   .Count > 0;
    }

    private static bool HasNewTargetPrivateRolePowerCommit(
        GameSession session,
        int startingLogCount) =>
        session.GameHistoryLog
            .Skip(startingLogCount)
			.OfType<TargetPrivateRolePowerCommittedLogEntry>()
			.Any();

	private static bool HasNewActorBorrowedRolePowerCommit(
		GameSession session,
		int startingLogCount) =>
		session.GameHistoryLog
			.Skip(startingLogCount)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Any();

	private static bool HasNewActorRuntimeCommit(
		GameSession session,
		int startingLogCount) =>
		session.GameHistoryLog
			.Skip(startingLogCount)
			.Any(entry =>
				entry is ActorSetupCardSpendCommittedLogEntry or
					ActorBorrowedRolePowerActivationExpiredLogEntry);

	private static bool HasNewThiefOfferCommit(
		GameSession session,
		int startingLogCount) =>
		session.GameHistoryLog
			.Skip(startingLogCount)
			.Any(entry =>
				entry is ThiefOfferDeclinedLogEntry ||
				entry is PermanentRoleSwapCommittedLogEntry
				{
					ExpectedCurrentRole: MainRoleType.Thief
				});

	private static bool HasNewEliminationCascadeReactionCompletion(
		GameSession session,
		int startingLogCount) =>
		session.GameHistoryLog
			.Skip(startingLogCount)
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Any();

	private static bool HasNewEliminationCascadeBatchResolution(
		GameSession session,
		int startingLogCount) =>
		session.GameHistoryLog
			.Skip(startingLogCount)
			.OfType<EliminationCascadeBatchResolvedLogEntry>()
			.Any();

	private static bool HasNewEliminationCascadeCompletion(
		GameSession session,
		int startingLogCount) =>
		session.GameHistoryLog
			.Skip(startingLogCount)
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Any();

	private static bool IsCorrelatedActorBorrowedMarker(
		GameSession session,
		ActorBorrowedRolePowerCommittedLogEntry marker,
		IActorBorrowedRolePowerCommit privateCommit)
	{
		var coordinate = privateCommit.Coordinate;
		return coordinate.PublicMarkerLogIndex >= 0 &&
			coordinate.PublicMarkerLogIndex < session.GameHistoryLog.Count() &&
			session.GameHistoryLog.ElementAt(
				coordinate.PublicMarkerLogIndex) == marker &&
			marker.Timestamp == coordinate.Timestamp &&
			marker.TurnNumber == coordinate.TurnNumber &&
			marker.CurrentPhase == coordinate.CurrentPhase;
	}

	private static bool IsCorrelatedActorBorrowedWitchPotionUse(
		GameSession session,
		ActorBorrowedRolePowerCommittedLogEntry marker,
		ActorBorrowedWitchPotionUseCommit commit,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		ModeratorInstruction nextInstruction)
	{
		var actionType = ActorBorrowedWitchPotionUseCommit.GetActionType(
			commit.SpentResourceIdentity);
		var expectedStartingSemantic = actionType switch
		{
			NightActionType.WitchSave =>
				ModeratorInstructionSemantic.SelectWitchHealingTarget,
			NightActionType.WitchKill =>
				ModeratorInstructionSemantic.SelectWitchPoisonTarget,
			_ => ModeratorInstructionSemantic.Unspecified
		};
		var hasExpectedContinuation = actionType switch
		{
			NightActionType.WitchSave => nextInstruction.Semantic is
				ModeratorInstructionSemantic.SelectWitchPoisonTarget or
				ModeratorInstructionSemantic.PutRoleToSleep,
			NightActionType.WitchKill => nextInstruction.Semantic ==
				ModeratorInstructionSemantic.PutRoleToSleep,
			_ => false
		};
		return IsCorrelatedActorBorrowedMarker(session, marker, commit) &&
			session.GetCurrentPhase() == GamePhase.Night &&
			startingInstruction is SelectPlayersInstruction
			{
				CountConstraint: var countConstraint,
				RoleIdentification: null,
				AffectedPlayerIds: [var affectedPlayerId]
			} selection &&
			selection.Semantic == expectedStartingSemantic &&
			countConstraint == NumberRangeConstraint.SingleOptional &&
			affectedPlayerId == commit.PowerIdentity.ActingPlayerId &&
			selection.SelectablePlayerIds.Contains(commit.TargetPlayerId) &&
			input.InstructionId == startingInstruction.InstructionId &&
			input.SelectedPlayerIds is { Count: 1 } selectedPlayerIds &&
			selectedPlayerIds.Single() == commit.TargetPlayerId &&
			hasExpectedContinuation &&
			nextInstruction.AffectedPlayerIds is [var nextAffectedPlayerId] &&
			nextAffectedPlayerId == commit.PowerIdentity.ActingPlayerId;
	}

	private static bool IsCorrelatedActorBorrowedWitchPotionDecline(
		GameSession session,
		ActorBorrowedRolePowerCommittedLogEntry marker,
		ActorBorrowedWitchPotionDeclineCommit commit,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		ModeratorInstruction nextInstruction)
	{
		var actionType =
			ActorBorrowedWitchPotionDeclineCommit.GetOfferedActionType(
				commit.OfferedResourceIdentity);
		var expectedStartingSemantic = actionType switch
		{
			NightActionType.WitchSave =>
				ModeratorInstructionSemantic.SelectWitchHealingTarget,
			NightActionType.WitchKill =>
				ModeratorInstructionSemantic.SelectWitchPoisonTarget,
			_ => ModeratorInstructionSemantic.Unspecified
		};
		var hasExpectedContinuation = actionType switch
		{
			NightActionType.WitchSave => nextInstruction.Semantic is
				ModeratorInstructionSemantic.SelectWitchPoisonTarget or
				ModeratorInstructionSemantic.PutRoleToSleep,
			NightActionType.WitchKill => nextInstruction.Semantic ==
				ModeratorInstructionSemantic.PutRoleToSleep,
			_ => false
		};
		return IsCorrelatedActorBorrowedMarker(session, marker, commit) &&
			session.GetCurrentPhase() == GamePhase.Night &&
			startingInstruction is SelectPlayersInstruction
			{
				CountConstraint: var countConstraint,
				RoleIdentification: null,
				AffectedPlayerIds: [var affectedPlayerId]
			} selection &&
			selection.Semantic == expectedStartingSemantic &&
			selection.SelectablePlayerIds.Count > 0 &&
			countConstraint == NumberRangeConstraint.SingleOptional &&
			affectedPlayerId == commit.PowerIdentity.ActingPlayerId &&
			input.InstructionId == startingInstruction.InstructionId &&
			input.SelectedPlayerIds is { Count: 0 } &&
			hasExpectedContinuation &&
			nextInstruction.AffectedPlayerIds is [var nextAffectedPlayerId] &&
			nextAffectedPlayerId == commit.PowerIdentity.ActingPlayerId;
	}

	private static bool IsCorrelatedActorBorrowedJudgeObservation(
		GameSession session,
		ActorBorrowedRolePowerCommittedLogEntry marker,
		ActorBorrowedStutteringJudgeSignalObservationCommit commit,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		ModeratorInstruction nextInstruction)
	{
		var expectedOptionId = commit.SignalOccurred
			? StutteringJudgeSignalOptionIds.Occurred
			: StutteringJudgeSignalOptionIds.DidNotOccur;
		return IsCorrelatedActorBorrowedMarker(session, marker, commit) &&
			session.GetCurrentPhase() == GamePhase.Day &&
			session.GetSubPhase<DaySubPhases>() == DaySubPhases.NormalVoting &&
			GameSessionQueries.GetCurrentDayVoteOutcome(session) is null &&
			startingInstruction is SelectOptionsInstruction
			{
				Semantic:
					ModeratorInstructionSemantic.ObserveStutteringJudgeSignal,
				SelectionRange: var selectionRange,
				AffectedPlayerIds: [var affectedPlayerId]
			} signalInstruction &&
			selectionRange == NumberRangeConstraint.Single &&
			affectedPlayerId == commit.PowerIdentity.ActingPlayerId &&
			signalInstruction.Options
				.Select(option => option.Id)
				.SequenceEqual(
					[
						StutteringJudgeSignalOptionIds.Occurred,
						StutteringJudgeSignalOptionIds.DidNotOccur
					],
					StringComparer.Ordinal) &&
			input.InstructionId == startingInstruction.InstructionId &&
			input.Type == ExpectedInputType.OptionSelection &&
			input.SelectedOptionIds is [var selectedOptionId] &&
			StringComparer.Ordinal.Equals(selectedOptionId, expectedOptionId) &&
			nextInstruction is SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.RecordDayVote,
				CountConstraint: var countConstraint,
				RoleIdentification: null,
				AffectedPlayerIds: null
			} voteInstruction &&
			countConstraint == NumberRangeConstraint.SingleOptional &&
			voteInstruction.SelectablePlayerIds.SetEquals(
				session.GetPlayers()
					.WithHealth(PlayerHealth.Alive)
					.Select(player => player.Id));
	}

    private static DomainRecoveryCursor? CreateDomainRecoveryCursor(
        GameSession session,
        ModeratorInstruction? startingInstruction,
        ModeratorResponse input,
        ModeratorInstruction nextInstruction,
        int startingLogCount,
        IRoleAdmissionSource admissions)
    {
        var newCommittedEntries = session.GameHistoryLog
            .Skip(startingLogCount)
            .OfType<OneUseRolePowerCommittedLogEntry>()
            .ToArray();
        var newRecurringEntries = session.GameHistoryLog
            .Skip(startingLogCount)
            .OfType<RecurringRolePowerCommittedLogEntry>()
            .ToArray();
        var newTargetPrivateEntries = session.GameHistoryLog
            .Skip(startingLogCount)
            .OfType<TargetPrivateRolePowerCommittedLogEntry>()
            .ToArray();
		var newActorBorrowedEntries = session.GameHistoryLog
			.Skip(startingLogCount)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.ToArray();
		var newActorBorrowedSeerCommits = session
			.GetActorBorrowedSeerCheckCommits()
			.Where(commit => commit.PublicMarkerLogIndex >= startingLogCount)
			.ToArray();
		var newActorBorrowedDefenderCommits = session
			.GetActorBorrowedDefenderProtectionCommits()
			.Where(commit => commit.PublicMarkerLogIndex >= startingLogCount)
			.ToArray();
			var newActorBorrowedFoxCommits = session
				.GetActorBorrowedFoxCheckCommits()
				.Where(commit => commit.PublicMarkerLogIndex >= startingLogCount)
				.ToArray();
			var newActorBorrowedWitchPotionUseCommits = session
				.GetActorBorrowedWitchPotionUseCommits()
				.Where(commit => commit.PublicMarkerLogIndex >= startingLogCount)
				.ToArray();
			var newActorBorrowedWitchPotionDeclineCommits = session
				.GetActorBorrowedWitchPotionDeclineCommits()
				.Where(commit => commit.PublicMarkerLogIndex >= startingLogCount)
				.ToArray();
			var newActorBorrowedCupidCommits = session
				.GetActorBorrowedCupidLoversCommits()
				.Where(commit => commit.PublicMarkerLogIndex >= startingLogCount)
				.ToArray();
			var newActorBorrowedJudgeSetupCommits = session
				.GetActorBorrowedStutteringJudgeSignalSetupCommits()
				.Where(commit => commit.PublicMarkerLogIndex >= startingLogCount)
				.ToArray();
			var newActorBorrowedJudgeObservationCommits = session
				.GetActorBorrowedStutteringJudgeSignalObservationCommits()
				.Where(commit => commit.PublicMarkerLogIndex >= startingLogCount)
				.ToArray();
		var newActorSetupCardSpendEntries = session.GameHistoryLog
			.Skip(startingLogCount)
			.OfType<ActorSetupCardSpendCommittedLogEntry>()
			.ToArray();
        var newLoversPairEntries =
            GameSessionQueries.GetCommittedLoversPairsSince(
                session,
                startingLogCount);
        var domainCommitKinds =
            (newCommittedEntries.Length > 0 ? 1 : 0) +
            (newRecurringEntries.Length > 0 ? 1 : 0) +
            (newTargetPrivateEntries.Length > 0 ? 1 : 0) +
			(newActorBorrowedEntries.Length > 0 ? 1 : 0) +
			(newActorSetupCardSpendEntries.Length > 0 ? 1 : 0) +
            (newLoversPairEntries.Count > 0 ? 1 : 0);
        if (domainCommitKinds > 1)
        {
            throw new InvalidOperationException(
                "One accepted response cannot produce multiple domain commits.");
        }

		if (newActorSetupCardSpendEntries.Length > 0)
		{
			if (newActorSetupCardSpendEntries is not [_])
			{
				throw new InvalidOperationException(
					"One accepted response must produce exactly one correlated Actor setup-card spend.");
			}

			if (!ActorRole.TryValidateCommittedRecoveryBoundary(
				    session,
				    startingInstruction,
				    input,
				    nextInstruction,
				    out var activation) ||
			    activation is null)
			{
				throw new InvalidOperationException(
					"One accepted response must produce exactly one correlated Actor setup-card spend.");
			}

			return new DomainRecoveryCursor
			{
				Version = DomainRecoveryCursor.CurrentVersion,
				Kind = DomainRecoveryCursorKind.ActorSetupCardSpendCommit,
				CommittedActionType = NightActionType.Unknown,
				ActingPlayerId = activation.ActingPlayerId,
				SourceRole = activation.SourceRole,
				SourcePowerIdentifier = string.Empty,
				PowerInstanceId = Guid.Empty,
				PowerInstanceOrigin = null,
				OneUseResourceId = Guid.Empty,
				ActorSetupCardId = activation.SelectedCardId,
				ActorBorrowedActivationId = activation.ActivationId,
				CommittedTargetIds = [],
				NextInstructionSemantic = nextInstruction.Semantic,
				NextInstructionId = nextInstruction.InstructionId
			};
		}

        if (newLoversPairEntries.Count > 0)
        {
            if (newLoversPairEntries is not [var pair] ||
                !CupidRole.TryValidateCommittedRecoveryBoundary(
                    session,
                    startingInstruction,
                    input,
                    pair,
                    nextInstruction))
            {
                throw new InvalidOperationException(
                    "One accepted response must produce exactly one correlated private Lovers pair commit.");
            }

            var powerIdentity = pair.PowerIdentity;
            return new DomainRecoveryCursor
            {
                Version = DomainRecoveryCursor.CurrentVersion,
                Kind =
                    DomainRecoveryCursorKind.RecurringNativeRolePowerCommit,
                SourceRole = MainRoleType.Cupid,
                CommittedActionType = NightActionType.CupidLink,
                ActingPlayerId = powerIdentity.ActingPlayerId,
                SourcePowerIdentifier =
                    powerIdentity.SourcePowerIdentifier,
                PowerInstanceId = powerIdentity.PowerInstanceId,
                PowerInstanceOrigin =
                    powerIdentity.PowerInstanceOrigin,
                OneUseResourceId = Guid.Empty,
                CommittedTargetIds = pair.PlayerIds.ToList(),
                NextInstructionSemantic = nextInstruction.Semantic,
                NextInstructionId = nextInstruction.InstructionId
            };
        }

        if (newTargetPrivateEntries.Length > 0)
        {
            if (newTargetPrivateEntries is not [var targetPrivateEntry] ||
                targetPrivateEntry.TargetIds is { Count: > 0 } ||
                !RoleListenerDispatch
                    .TryValidateTargetPrivateCommittedRecoveryBoundary(
                    Listener(targetPrivateEntry.SourceRole),
                    admissions,
                    (id, factory) =>
                        session.GetOrCreateListener(id, factory),
					 session,
					 startingInstruction,
					 input,
					 TargetPrivateRolePowerRecoveryBoundary
						 .FromCommittedEntry(targetPrivateEntry),
					 nextInstruction))
            {
                throw new InvalidOperationException(
                    "One accepted response must produce exactly one correlated target-private Role Power commit.");
            }

            var powerIdentity = targetPrivateEntry.PowerIdentity;
            return new DomainRecoveryCursor
            {
                Version = DomainRecoveryCursor.CurrentVersion,
                Kind =
                    DomainRecoveryCursorKind.TargetPrivateRolePowerCommit,
                SourceRole = powerIdentity.SourceRole,
                CommittedActionType = targetPrivateEntry.ActionType,
                ActingPlayerId = powerIdentity.ActingPlayerId,
                SourcePowerIdentifier =
                    powerIdentity.SourcePowerIdentifier,
                PowerInstanceId = powerIdentity.PowerInstanceId,
                PowerInstanceOrigin =
                    powerIdentity.PowerInstanceOrigin,
                OneUseResourceId =
                    targetPrivateEntry.SpentResourceIdentity?
                        .OneUseResourceId ?? Guid.Empty,
                CommittedTargetIds = [],
                NextInstructionSemantic = nextInstruction.Semantic,
                NextInstructionId = nextInstruction.InstructionId
            };
        }

		if (newActorBorrowedEntries.Length > 0)
		{
				var typedCommitCount = newActorBorrowedSeerCommits.Length +
					newActorBorrowedDefenderCommits.Length +
					newActorBorrowedFoxCommits.Length +
					newActorBorrowedWitchPotionUseCommits.Length +
					newActorBorrowedWitchPotionDeclineCommits.Length +
					newActorBorrowedCupidCommits.Length +
					newActorBorrowedJudgeSetupCommits.Length +
					newActorBorrowedJudgeObservationCommits.Length;
			if (newActorBorrowedEntries is not [var marker] ||
				typedCommitCount != 1)
			{
				throw new InvalidOperationException(
					"One accepted response must produce exactly one correlated private Actor borrowed Role Power commit.");
			}

			if (newActorBorrowedSeerCommits is [var seerCommit])
			{
				if (!IsCorrelatedActorBorrowedMarker(
						 session,
						 marker,
						 seerCommit) ||
					!RoleListenerDispatch
						.TryValidateTargetPrivateCommittedRecoveryBoundary(
							Listener(seerCommit.PowerIdentity.SourceRole),
							admissions,
							(id, factory) =>
								session.GetOrCreateListener(id, factory),
							session,
							startingInstruction,
							input,
							TargetPrivateRolePowerRecoveryBoundary
								.FromActorBorrowedSeerCheckCommit(seerCommit),
							nextInstruction))
				{
					throw new InvalidOperationException(
						"One accepted response must produce exactly one correlated Actor borrowed target-private Role Power commit.");
				}

				var identity = seerCommit.PowerIdentity;
				return new DomainRecoveryCursor
				{
					Version = DomainRecoveryCursor.CurrentVersion,
					Kind = DomainRecoveryCursorKind.TargetPrivateRolePowerCommit,
					SourceRole = identity.SourceRole,
					CommittedActionType = NightActionType.SeerCheck,
					ActingPlayerId = identity.ActingPlayerId,
					SourcePowerIdentifier = identity.SourcePowerIdentifier,
					PowerInstanceId = identity.PowerInstanceId,
					PowerInstanceOrigin = identity.PowerInstanceOrigin,
					OneUseResourceId = Guid.Empty,
					ActorSetupCardId = seerCommit.ActorSetupCardId,
					ActorBorrowedActivationId = identity.PowerInstanceId,
					CommittedTargetIds = [seerCommit.TargetPlayerId],
					NextInstructionSemantic = nextInstruction.Semantic,
					NextInstructionId = nextInstruction.InstructionId
				};
			}

				if (newActorBorrowedFoxCommits is [var foxCommit])
			{
				if (!IsCorrelatedActorBorrowedMarker(
						session,
						marker,
						foxCommit) ||
					!RoleListenerDispatch
						.TryValidateTargetPrivateCommittedRecoveryBoundary(
							Listener(foxCommit.PowerIdentity.SourceRole),
							admissions,
							(id, factory) =>
								session.GetOrCreateListener(id, factory),
							session,
							startingInstruction,
							input,
							TargetPrivateRolePowerRecoveryBoundary
								.FromActorBorrowedFoxCheckCommit(foxCommit),
							nextInstruction))
				{
					throw new InvalidOperationException(
						"One accepted response must produce exactly one correlated Actor borrowed Fox check commit.");
				}

				var identity = foxCommit.PowerIdentity;
				return new DomainRecoveryCursor
				{
					Version = DomainRecoveryCursor.CurrentVersion,
					Kind = DomainRecoveryCursorKind.TargetPrivateRolePowerCommit,
					SourceRole = identity.SourceRole,
					CommittedActionType = NightActionType.FoxCheck,
					ActingPlayerId = identity.ActingPlayerId,
					SourcePowerIdentifier = identity.SourcePowerIdentifier,
					PowerInstanceId = identity.PowerInstanceId,
					PowerInstanceOrigin = identity.PowerInstanceOrigin,
					OneUseResourceId = foxCommit.SpentResourceIdentity?
						.OneUseResourceId ?? Guid.Empty,
					ActorSetupCardId = foxCommit.ActorSetupCardId,
					ActorBorrowedActivationId = identity.PowerInstanceId,
					CommittedTargetIds = [foxCommit.CenterPlayerId],
					NextInstructionSemantic = nextInstruction.Semantic,
					NextInstructionId = nextInstruction.InstructionId
					};
				}

				if (newActorBorrowedWitchPotionUseCommits is [var witchUse])
				{
					if (!IsCorrelatedActorBorrowedWitchPotionUse(
							session,
							marker,
							witchUse,
							startingInstruction,
							input,
							nextInstruction))
					{
						throw new InvalidOperationException(
							"One accepted response must produce exactly one correlated Actor borrowed Witch potion use.");
					}

					var identity = witchUse.PowerIdentity;
					return new DomainRecoveryCursor
					{
						Version = DomainRecoveryCursor.CurrentVersion,
						Kind = DomainRecoveryCursorKind
							.ActorBorrowedWitchPotionUseCommit,
						SourceRole = identity.SourceRole,
						CommittedActionType =
							ActorBorrowedWitchPotionUseCommit.GetActionType(
								witchUse.SpentResourceIdentity),
						ActingPlayerId = identity.ActingPlayerId,
						SourcePowerIdentifier = identity.SourcePowerIdentifier,
						PowerInstanceId = identity.PowerInstanceId,
						PowerInstanceOrigin = identity.PowerInstanceOrigin,
						OneUseResourceId = witchUse.SpentResourceIdentity
							.OneUseResourceId,
						ActorSetupCardId = witchUse.ActorSetupCardId,
						ActorBorrowedActivationId = identity.PowerInstanceId,
						CommittedTargetIds = [witchUse.TargetPlayerId],
						NextInstructionSemantic = nextInstruction.Semantic,
						NextInstructionId = nextInstruction.InstructionId
					};
				}

				if (newActorBorrowedWitchPotionDeclineCommits is
					[var witchDecline])
				{
					if (!IsCorrelatedActorBorrowedWitchPotionDecline(
							session,
							marker,
							witchDecline,
							startingInstruction,
							input,
							nextInstruction))
					{
						throw new InvalidOperationException(
							"One accepted response must produce exactly one correlated Actor borrowed Witch potion decline.");
					}

					var identity = witchDecline.PowerIdentity;
					return new DomainRecoveryCursor
					{
						Version = DomainRecoveryCursor.CurrentVersion,
						Kind = DomainRecoveryCursorKind
							.ActorBorrowedWitchPotionDeclineCommit,
						SourceRole = identity.SourceRole,
						CommittedActionType =
							ActorBorrowedWitchPotionDeclineCommit
								.GetOfferedActionType(
									witchDecline.OfferedResourceIdentity),
						ActingPlayerId = identity.ActingPlayerId,
						SourcePowerIdentifier = identity.SourcePowerIdentifier,
						PowerInstanceId = identity.PowerInstanceId,
						PowerInstanceOrigin = identity.PowerInstanceOrigin,
						OneUseResourceId = witchDecline.OfferedResourceIdentity
							.OneUseResourceId,
						ActorSetupCardId = witchDecline.ActorSetupCardId,
						ActorBorrowedActivationId = identity.PowerInstanceId,
						CommittedTargetIds = [],
						NextInstructionSemantic = nextInstruction.Semantic,
						NextInstructionId = nextInstruction.InstructionId
					};
				}

				if (newActorBorrowedJudgeSetupCommits is [var judgeSetupCommit])
				{
					if (!IsCorrelatedActorBorrowedMarker(
							session,
							marker,
							judgeSetupCommit))
					{
						throw new InvalidOperationException(
							"One accepted response must produce exactly one correlated Actor borrowed Stuttering Judge setup commit.");
					}

					return null;
				}

				if (newActorBorrowedJudgeObservationCommits is
					[var judgeObservationCommit])
				{
					if (!IsCorrelatedActorBorrowedJudgeObservation(
							session,
							marker,
							judgeObservationCommit,
							startingInstruction,
							input,
							nextInstruction))
					{
						throw new InvalidOperationException(
							"One accepted response must produce exactly one correlated Actor borrowed Stuttering Judge signal observation commit.");
					}

					if (!judgeObservationCommit.SignalOccurred)
					{
						return null;
					}

					var identity = judgeObservationCommit.PowerIdentity;
					var judgeResourceIdentity = judgeObservationCommit
						.SpentResourceIdentity!.Value;
					return new DomainRecoveryCursor
					{
						Version = DomainRecoveryCursor.CurrentVersion,
						Kind = DomainRecoveryCursorKind
							.ActorBorrowedStutteringJudgeSignalObservationCommit,
						SourceRole = identity.SourceRole,
						CommittedActionType = NightActionType.Unknown,
						CommittedDayActionType = DayPowerType.JudgeExtraVote,
						ActingPlayerId = identity.ActingPlayerId,
						SourcePowerIdentifier = identity.SourcePowerIdentifier,
						PowerInstanceId = identity.PowerInstanceId,
						PowerInstanceOrigin = identity.PowerInstanceOrigin,
						OneUseResourceId = judgeResourceIdentity.OneUseResourceId,
						ActorSetupCardId =
							judgeObservationCommit.ActorSetupCardId,
						ActorBorrowedActivationId = identity.PowerInstanceId,
						CommittedTargetIds = [],
						NextInstructionSemantic = nextInstruction.Semantic,
						NextInstructionId = nextInstruction.InstructionId
					};
				}

				if (newActorBorrowedCupidCommits is [var cupidCommit])
				{
					if (!IsCorrelatedActorBorrowedMarker(
							session,
							marker,
							cupidCommit) ||
						!CupidRole.TryValidateCommittedRecoveryBoundary(
							session,
							startingInstruction,
							input,
							cupidCommit,
							nextInstruction))
					{
						throw new InvalidOperationException(
							"One accepted response must produce exactly one correlated Actor borrowed Cupid Lovers commit.");
					}

					var identity = cupidCommit.PowerIdentity;
					return new DomainRecoveryCursor
					{
						Version = DomainRecoveryCursor.CurrentVersion,
						Kind = DomainRecoveryCursorKind.RecurringNativeRolePowerCommit,
						SourceRole = identity.SourceRole,
						CommittedActionType = NightActionType.CupidLink,
						ActingPlayerId = identity.ActingPlayerId,
						SourcePowerIdentifier = identity.SourcePowerIdentifier,
						PowerInstanceId = identity.PowerInstanceId,
						PowerInstanceOrigin = identity.PowerInstanceOrigin,
						OneUseResourceId = Guid.Empty,
						ActorSetupCardId = cupidCommit.ActorSetupCardId,
						ActorBorrowedActivationId = identity.PowerInstanceId,
						CommittedTargetIds = cupidCommit.PlayerIds.ToList(),
						NextInstructionSemantic = nextInstruction.Semantic,
						NextInstructionId = nextInstruction.InstructionId
					};
				}

				var defenderCommit = newActorBorrowedDefenderCommits.Single();
			if (!IsCorrelatedActorBorrowedMarker(
					session,
					marker,
					defenderCommit) ||
				!DefenderRole.TryValidateCommittedRecoveryBoundary(
					session,
					startingInstruction,
					input,
					defenderCommit,
					nextInstruction))
			{
				throw new InvalidOperationException(
					"One accepted response must produce exactly one correlated Actor borrowed Defender protection commit.");
			}

			var defenderIdentity = defenderCommit.PowerIdentity;
			return new DomainRecoveryCursor
			{
				Version = DomainRecoveryCursor.CurrentVersion,
				Kind = DomainRecoveryCursorKind.RecurringNativeRolePowerCommit,
				SourceRole = defenderIdentity.SourceRole,
				CommittedActionType = NightActionType.DefenderProtect,
				ActingPlayerId = defenderIdentity.ActingPlayerId,
				SourcePowerIdentifier =
					defenderIdentity.SourcePowerIdentifier,
				PowerInstanceId = defenderIdentity.PowerInstanceId,
				PowerInstanceOrigin = defenderIdentity.PowerInstanceOrigin,
				OneUseResourceId = Guid.Empty,
				ActorSetupCardId = defenderCommit.ActorSetupCardId,
				ActorBorrowedActivationId = defenderIdentity.PowerInstanceId,
				CommittedTargetIds = [defenderCommit.TargetPlayerId],
				NextInstructionSemantic = nextInstruction.Semantic,
				NextInstructionId = nextInstruction.InstructionId
			};
		}

        if (newCommittedEntries.Length == 0)
        {
            if (newRecurringEntries.Length == 0)
            {
                return null;
            }

            if (newRecurringEntries is not [var recurringEntry] ||
                recurringEntry.TargetIds is not { Count: > 0 } recurringTargetIds)
            {
                throw new InvalidOperationException(
                    "One accepted response must produce exactly one recurring Role Power commit.");
            }

            var recurringCommitCorrelated =
                recurringEntry.SourceRole switch
                {
                    MainRoleType.BigBadWolf =>
                        BigBadWolfRole.TryValidateCommittedRecoveryBoundary(
                            session,
                            startingInstruction,
                            input,
                            recurringEntry,
                            nextInstruction,
                            out _),
                    MainRoleType.Defender =>
                        DefenderRole.TryValidateCommittedRecoveryBoundary(
                            session,
                            startingInstruction,
                            input,
                            recurringEntry,
                            nextInstruction),
                    MainRoleType.WhiteWerewolf =>
                        WhiteWerewolfRole.TryValidateCommittedRecoveryBoundary(
                            session,
                            startingInstruction,
                            input,
                            recurringEntry,
                            nextInstruction),
                    MainRoleType.Piper =>
                        PiperRole.TryValidateCommittedRecoveryBoundary(
                            session,
                            startingInstruction,
                            input,
                            recurringEntry,
                            nextInstruction),
                    _ => false
                };
            if (!recurringCommitCorrelated)
            {
                throw new InvalidOperationException(
                    "The recurring native Role Power commit has no owning Role recovery contract.");
            }

            var powerIdentity = recurringEntry.PowerIdentity;
            return new DomainRecoveryCursor
            {
                Version = DomainRecoveryCursor.CurrentVersion,
                Kind =
                    DomainRecoveryCursorKind.RecurringNativeRolePowerCommit,
                SourceRole = powerIdentity.SourceRole,
                CommittedActionType = recurringEntry.ActionType,
                ActingPlayerId = powerIdentity.ActingPlayerId,
                SourcePowerIdentifier =
                    powerIdentity.SourcePowerIdentifier,
                PowerInstanceId = powerIdentity.PowerInstanceId,
                PowerInstanceOrigin =
                    powerIdentity.PowerInstanceOrigin,
                OneUseResourceId = Guid.Empty,
                CommittedTargetIds = recurringTargetIds.ToList(),
                NextInstructionSemantic = nextInstruction.Semantic,
                NextInstructionId = nextInstruction.InstructionId
            };
        }

        if (newCommittedEntries is not [var committedEntry] ||
            committedEntry.TargetIds is not { Count: 1 })
        {
            throw new InvalidOperationException(
                "One accepted response must produce exactly one atomic One-Use Resource commit.");
        }

        var committedTargetId = committedEntry.TargetIds[0];
        var roleOwnedCommitCorrelated =
            AccursedWolfFatherRole.TryValidateCommittedRecoveryBoundary(
                session,
                startingInstruction,
                input,
                committedEntry);
        if (!roleOwnedCommitCorrelated &&
            (startingInstruction is not SelectPlayersInstruction ||
             input.SelectedPlayerIds is not
                 { Count: 1 } selectedPlayerIds ||
             selectedPlayerIds.Single() != committedTargetId))
        {
            throw new InvalidOperationException(
                "A One-Use Resource commit must correlate to one accepted Player or semantic option selection.");
        }

        var resourceIdentity = committedEntry.ResourceIdentity;
        return new DomainRecoveryCursor
        {
            Version = DomainRecoveryCursor.CurrentVersion,
            Kind = DomainRecoveryCursorKind.OneUseRolePowerCommit,
            SourceRole = resourceIdentity.SourceRole,
            CommittedActionType = committedEntry.ActionType,
            ActingPlayerId = resourceIdentity.ActingPlayerId,
            SourcePowerIdentifier = resourceIdentity.SourcePowerIdentifier,
            PowerInstanceId = resourceIdentity.PowerInstanceId,
            PowerInstanceOrigin = resourceIdentity.PowerInstanceOrigin,
            OneUseResourceId = resourceIdentity.OneUseResourceId,
            CommittedTargetIds = [committedTargetId],
            NextInstructionSemantic = nextInstruction.Semantic,
            NextInstructionId = nextInstruction.InstructionId
        };
    }

    private static bool IsAcceptedObservation(ModeratorInstruction? instruction)
        => instruction?.Semantic is
            ModeratorInstructionSemantic.IdentifyRoleHolders or
            ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup or
            ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal or
            ModeratorInstructionSemantic.ObserveScapegoatHolderForTie or
            ModeratorInstructionSemantic.RevealScapegoatForTie or
            ModeratorInstructionSemantic.SelectScapegoatPermittedVoters or
            ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters or
            ModeratorInstructionSemantic.EstablishStutteringJudgeSignal or
            ModeratorInstructionSemantic.ObserveStutteringJudgeSignal or
            ModeratorInstructionSemantic.ChooseWolfHoundAlignment or
            ModeratorInstructionSemantic.ChooseThiefOffer or
			ModeratorInstructionSemantic.ChooseActorSetupCard or
            ModeratorInstructionSemantic.AnnounceDawnVictims or
            ModeratorInstructionSemantic.AssignDawnVictimRoles or
            ModeratorInstructionSemantic.AssignDayVoteTargetRole or
            ModeratorInstructionSemantic.AssignEliminationCascadeRoles or
            ModeratorInstructionSemantic.AnnounceBearTamerGrowl or
            ModeratorInstructionSemantic.RecognizeLovers;

		private static bool IsEliminationCascadeReactionInput(
			ModeratorInstruction instruction) =>
			instruction.Semantic is
				ModeratorInstructionSemantic.AnnounceDawnVictims or
				ModeratorInstructionSemantic.SelectHunterFinalShotTarget or
				ModeratorInstructionSemantic.ResolveDevotedServantVoteWindow or
				ModeratorInstructionSemantic.RecordDevotedServantAcquiredCard;

    private static AcceptedObservationRecoveryCursor?
        CreateAcceptedObservationRecoveryCursor(
	        GameSession session,
            ModeratorInstruction? startingInstruction,
            ListenerIdentifier? startingListener,
            ModeratorInstruction nextInstruction,
            IRoleAdmissionSource admissions)
    {
        if (session.GetCurrentPhase() != GamePhase.Night ||
            !IsNightStartSubPhase(session) ||
            !TryGetAcceptedObservationRole(
                startingInstruction,
                startingListener,
                out var acceptedObservationSemantic,
                out var observedRole))
        {
            return null;
        }

        var currentListener = session.GetCurrentListener();
        if (currentListener == null)
        {
            return null;
        }

        var continuationRole = (MainRoleType)currentListener;
		var cursor = new AcceptedObservationRecoveryCursor
		{
			Version = AcceptedObservationRecoveryCursor.CurrentVersion,
			AcceptedObservationSemantic = acceptedObservationSemantic,
			ObservedRole = observedRole,
			ContinuationRole = continuationRole,
			RetainedLittleGirlGuidanceDecision = null,
			NextInstructionSemantic = nextInstruction.Semantic,
			NextInstructionId = nextInstruction.InstructionId
		};
		if (RetainsLittleGirlGuidanceDecision(cursor))
        {
            if (!session.TryGetExistingListener<SimpleWerewolfRole>(
                    Listener(SimpleWerewolf),
                    out var werewolfListener))
            {
                throw new InvalidOperationException(
                    "The accepted observation requires its Simple Werewolf listener.");
            }

			cursor.RetainedLittleGirlGuidanceDecision =
                werewolfListener.LittleGirlGuidanceDecision;
        }

        ValidateAcceptedObservationRecoverySemantics(
            session,
            cursor,
            nextInstruction);
		var continuation = ResolvePendingInstructionContinuation(
			Listener(continuationRole),
			NightMainActionLoop,
			session,
			nextInstruction,
			admissions);
        if (continuation == null)
        {
            throw new InvalidOperationException(
                $"Unsupported accepted observation continuation '{acceptedObservationSemantic}:{observedRole}:{continuationRole}:{nextInstruction.Semantic}'.");
        }

		return cursor;
	}

    private static bool TryGetAcceptedObservationRole(
        ModeratorInstruction? startingInstruction,
        ListenerIdentifier? startingListener,
        out ModeratorInstructionSemantic acceptedObservationSemantic,
        out MainRoleType observedRole)
    {
        acceptedObservationSemantic = default;
        observedRole = default;
        if (!IsAcceptedObservation(startingInstruction))
        {
            return false;
        }

        acceptedObservationSemantic = startingInstruction!.Semantic;
        switch (startingInstruction)
        {
            case SelectPlayersInstruction
            {
                Semantic: ModeratorInstructionSemantic.IdentifyRoleHolders,
                RoleIdentification: { } identifiedRole
            }:
                observedRole = identifiedRole;
                return true;
            case SelectPlayersInstruction
            {
                Semantic:
                    ModeratorInstructionSemantic
                        .ObserveWerewolfFactionAgentGroup,
                RoleIdentification: null
            }:
                observedRole = SimpleWerewolf;
                return true;
            default:
                if (startingListener == null)
                {
                    return false;
                }

                observedRole = (MainRoleType)startingListener;
                return true;
        }
    }

    private static void ValidateAcceptedObservationRecoverySemantics(
        GameSession session,
        AcceptedObservationRecoveryCursor cursor,
        ModeratorInstruction pendingInstruction)
    {
        var continuationRole = cursor.ContinuationRole ?? cursor.ObservedRole;
        if (session.GetCurrentPhase() != GamePhase.Night ||
            !IsNightStartSubPhase(session))
        {
            throw new InvalidOperationException(
                $"Unsupported accepted observation continuation '{cursor.AcceptedObservationSemantic}:{cursor.ObservedRole}:{continuationRole}:{cursor.NextInstructionSemantic}'.");
        }

        if (pendingInstruction.InstructionId != cursor.NextInstructionId ||
            pendingInstruction.Semantic != cursor.NextInstructionSemantic)
        {
            throw new InvalidOperationException(
                "The Pending Instruction does not match the accepted observation continuation.");
        }

        var matchesCommittedObservation =
            cursor.AcceptedObservationSemantic switch
            {
                ModeratorInstructionSemantic.IdentifyRoleHolders =>
					 HasCommittedRoleIdentification(
						 session,
						 cursor.ObservedRole) &&
					 (cursor.ObservedRole is not
						  (WhiteWerewolf or MainRoleType.Piper or
						   MainRoleType.PrejudicedManipulator) ||
					  InitialBeneficiaryClosureRules
                         .HasConsistentInitialBeneficiaryClosure(session)),
                ModeratorInstructionSemantic
                    .ObserveWerewolfFactionAgentGroup
                    when cursor.ObservedRole == SimpleWerewolf &&
                         continuationRole == SimpleWerewolf =>
                    HasCommittedWerewolfAgentGroupObservation(
                        session,
                        pendingInstruction) &&
                    InitialBeneficiaryClosureRules
                        .HasConsistentInitialBeneficiaryClosure(session),
                ModeratorInstructionSemantic
                    .EstablishStutteringJudgeSignal
                    when cursor.ObservedRole == StutteringJudge =>
                    StutteringJudgeRole.HasValidEstablishedSignal(session),
                ModeratorInstructionSemantic.ChooseWolfHoundAlignment
                    when cursor.ObservedRole == WolfHound =>
                    HasCommittedWolfHoundAlignment(session),
				ModeratorInstructionSemantic.ChooseThiefOffer
					when cursor.ObservedRole == MainRoleType.Thief &&
						 continuationRole == MainRoleType.Thief &&
						 pendingInstruction is ConfirmationInstruction
						 {
							 Semantic:
								 ModeratorInstructionSemantic.PutRoleToSleep,
							 AffectedPlayerIds: [var thiefPlayerId]
						 } =>
					ThiefOfferRules.HasValidCommittedChoice(
						session,
						thiefPlayerId),
				ModeratorInstructionSemantic.ChooseActorSetupCard
					when cursor.ObservedRole == MainRoleType.Actor &&
						 continuationRole == MainRoleType.Actor =>
					ActorRole.HasExpectedDeclinedChoiceSleep(
						session,
						pendingInstruction),
                ModeratorInstructionSemantic.RecognizeLovers
                    when cursor.ObservedRole == Cupid &&
                         continuationRole == Cupid =>
                    CupidRole.HasExpectedCommittedPairSleep(
                        session,
                        pendingInstruction),
                _ => false
            };
        if (!matchesCommittedObservation)
        {
            throw new InvalidOperationException(
                "The accepted observation recovery cursor does not match its committed observation.");
        }

		if (!LittleGirlRole.HasValidRetainedGuidanceDecision(
				session,
				RetainsLittleGirlGuidanceDecision(cursor),
				cursor.RetainedLittleGirlGuidanceDecision))
		{
			throw new InvalidOperationException(
				"The accepted observation recovery cursor has an invalid retained Little Girl guidance decision.");
		}
	}

	private static bool HasCommittedRoleIdentification(
        GameSession session,
        MainRoleType observedRole)
    {
        var livingHolderIds = session.GetPlayers()
            .Where(player =>
                player.State.Health == PlayerHealth.Alive &&
                player.State.CurrentRole == observedRole &&
                player.State.ModeratorKnownRole == observedRole)
            .Select(player => player.Id)
            .ToHashSet();
        return livingHolderIds.Count > 0 &&
               session.GameHistoryLog
                   .OfType<RoleIdentificationLogEntry>()
                   .Any(entry =>
                       entry.TurnNumber == session.TurnNumber &&
                       entry.CurrentPhase == GamePhase.Night &&
                       entry.Role == observedRole &&
                       entry.PlayerIds.SetEquals(livingHolderIds));
    }

    private static bool HasCommittedWerewolfAgentGroupObservation(
        GameSession session,
        ModeratorInstruction pendingInstruction)
    {
        var observedPlayerIds =
            pendingInstruction.AffectedPlayerIds?.ToHashSet();
        if (observedPlayerIds == null)
        {
            return false;
        }

        var livingPlayerIds = session.GetPlayers()
            .Where(player => player.State.Health == PlayerHealth.Alive)
            .Select(player => player.Id)
            .ToHashSet();
        return session.GameHistoryLog
            .OfType<FactionFactsCommittedLogEntry>()
            .Any(entry =>
                entry.TurnNumber == session.TurnNumber &&
                entry.CurrentPhase == GamePhase.Night &&
                entry.Source.Kind ==
                    FactionFactSourceKind.ScheduledObservation &&
                entry.Source.Identifier ==
                    FactionFactSource
                        .WerewolfFactionAgentGroupObservationIdentifier &&
                entry.Facts.Length == livingPlayerIds.Count &&
                entry.Facts.All(fact =>
                    fact.Type == FactionFactType.Agent &&
                    fact.Faction == Faction.Werewolf &&
                    livingPlayerIds.Contains(fact.PlayerId)) &&
                entry.Facts
                    .Select(fact => fact.PlayerId)
                    .ToHashSet()
                    .SetEquals(livingPlayerIds) &&
                entry.Facts
                    .Where(fact =>
                        fact.AgentKnowledge ==
                        FactionAgentKnowledge.KnownAgent)
                    .Select(fact => fact.PlayerId)
                    .ToHashSet()
                    .SetEquals(observedPlayerIds));
    }

    private static bool HasCommittedWolfHoundAlignment(GameSession session)
    {
        var holders = session.GetPlayers()
            .Where(player =>
                player.State.Health == PlayerHealth.Alive &&
                player.State.CurrentRole == WolfHound)
            .ToArray();
        return holders is [var holder] &&
               WolfHoundRole.HasValidCommittedAlignment(
                   session,
                   holder.Id);
    }

    private static bool RetainsLittleGirlGuidanceDecision(
        AcceptedObservationRecoveryCursor cursor)
    {
        var continuationRole = cursor.ContinuationRole ?? cursor.ObservedRole;
        return cursor.AcceptedObservationSemantic ==
                   ModeratorInstructionSemantic
                       .ObserveWerewolfFactionAgentGroup ||
               continuationRole == SimpleWerewolf &&
               cursor.NextInstructionSemantic is
                   ModeratorInstructionSemantic
                       .ObserveWerewolfFactionAgentGroup or
                   ModeratorInstructionSemantic.WakeRole;
    }

    internal static void RestoreDurableContinuation(
        GameSession session,
        IRoleAdmissionSource admissions)
    {
        var domainCursor = session.GetDomainRecoveryCursor(Key);
        if (domainCursor != null)
        {
            RestoreDomainContinuation(session, domainCursor, admissions);
            return;
        }

        if (HasCursorlessWhiteWerewolfAttackBoundary(session))
        {
            throw new InvalidOperationException(
                "A committed White Werewolf attack requires its domain recovery cursor.");
        }

	        var cursor = session.GetAcceptedObservationRecoveryCursor(Key);
	        if (cursor == null)
	        {
		        RestorePendingHookListenerContinuation(session, admissions);
	            return;
	        }

        var continuationRole = cursor.ContinuationRole ?? cursor.ObservedRole;
        var pendingInstruction = session.PendingModeratorInstruction
            ?? throw new InvalidOperationException(
                "The accepted observation continuation requires one Pending Instruction.");
        ValidateAcceptedObservationRecoverySemantics(
            session,
            cursor,
            pendingInstruction);
		var continuation = ResolvePendingInstructionContinuation(
			Listener(continuationRole),
			NightMainActionLoop,
			session,
			pendingInstruction,
			admissions);
        if (continuation == null)
        {
            throw new InvalidOperationException(
                "The Pending Instruction does not match the accepted observation continuation.");
        }

        if (RetainsLittleGirlGuidanceDecision(cursor))
        {
            if (!session.TryGetExistingListener<SimpleWerewolfRole>(
                    Listener(SimpleWerewolf),
                    out var werewolfListener))
            {
                throw new InvalidOperationException(
                    "The active Simple Werewolf listener is unavailable for observation recovery.");
            }

            werewolfListener.RestoreLittleGirlGuidanceDecision(
                cursor.RetainedLittleGirlGuidanceDecision);
        }

	        session.RestoreTransientContinuation(
	            Key,
	            continuation.Value.ActiveSubPhaseStage,
	            continuation.Value.Listener,
	            continuation.Value.ListenerState);
	    }

	    private static void RestorePendingHookListenerContinuation(
		    GameSession session,
		    IRoleAdmissionSource admissions)
	    {
		    var pendingInstruction = session.PendingModeratorInstruction;
		    if (pendingInstruction == null)
		    {
			    return;
		    }

		    var continuation = ResolvePendingInstructionContinuation(
			    session,
			    pendingInstruction,
			    admissions);
		    if (continuation == null)
		    {
			    return;
		    }

		    session.RestoreTransientContinuation(
			    Key,
			    continuation.Value.ActiveSubPhaseStage,
			    continuation.Value.Listener,
			    continuation.Value.ListenerState);
	    }

	    private static bool HasCursorlessWhiteWerewolfAttackBoundary(
		    GameSession session) =>
		    session.GetCurrentPhase() == GamePhase.Night &&
		    GameSessionQueries.FindLogEntries<NightActionLogEntry>(
			    session,
			    NumberRangeConstraint.Exact(session.TurnNumber),
			    filter: entry =>
				    entry.ActionType ==
					    NightActionType.WhiteWerewolfVictimSelection ||
				    entry is RecurringRolePowerCommittedLogEntry recurring &&
				    recurring.SourceRole == MainRoleType.WhiteWerewolf &&
				    StringComparer.Ordinal.Equals(
					    recurring.SourcePowerIdentifier,
					    WhiteWerewolfRole.SoloAttackPowerIdentifier.Value))
			    .Any();

    private static void RestoreDomainContinuation(
        GameSession session,
        DomainRecoveryCursor cursor,
        IRoleAdmissionSource admissions)
    {
        var sourceRole = cursor.SourceRole
            ?? throw new InvalidOperationException(
                "The domain recovery cursor is structurally invalid.");
		if (cursor.Kind == DomainRecoveryCursorKind
			.ActorBorrowedStutteringJudgeSignalObservationCommit)
		{
			var expectedVoteInstruction =
				DayPhaseHandlers.CreateRecordDayVoteInstruction(session);
			if (sourceRole != MainRoleType.StutteringJudge ||
				cursor.CommittedActionType != NightActionType.Unknown ||
				cursor.CommittedDayActionType != DayPowerType.JudgeExtraVote ||
				session.GetCurrentPhase() != GamePhase.Day ||
				session.GetSubPhase<DaySubPhases>() !=
					DaySubPhases.NormalVoting ||
				session.PendingModeratorInstruction is not
					SelectPlayersInstruction pendingVoteInstruction ||
				pendingVoteInstruction.Semantic !=
					expectedVoteInstruction.Semantic ||
				!StringComparer.Ordinal.Equals(
					pendingVoteInstruction.PublicAnnouncement,
					expectedVoteInstruction.PublicAnnouncement) ||
				!StringComparer.Ordinal.Equals(
					pendingVoteInstruction.PrivateInstruction,
					expectedVoteInstruction.PrivateInstruction) ||
				(pendingVoteInstruction.AffectedPlayerIds is null) !=
					(expectedVoteInstruction.AffectedPlayerIds is null) ||
				pendingVoteInstruction.AffectedPlayerIds is { } affectedPlayerIds &&
					!affectedPlayerIds.SequenceEqual(
						expectedVoteInstruction.AffectedPlayerIds!) ||
				!pendingVoteInstruction.SoundEffects.SequenceEqual(
					expectedVoteInstruction.SoundEffects) ||
				!pendingVoteInstruction.SelectablePlayerIds.SetEquals(
					expectedVoteInstruction.SelectablePlayerIds) ||
				pendingVoteInstruction.CountConstraint !=
					expectedVoteInstruction.CountConstraint ||
				pendingVoteInstruction.RoleIdentification !=
					expectedVoteInstruction.RoleIdentification ||
				!StringComparer.Ordinal.Equals(
					pendingVoteInstruction.EmptySelectionOptionLabel,
					expectedVoteInstruction.EmptySelectionOptionLabel))
			{
				throw new InvalidOperationException(
					"The Actor borrowed Stuttering Judge recovery cursor does not match the canonical Record Day Vote instruction.");
			}

			return;
		}

        if (cursor.Kind is not
                (DomainRecoveryCursorKind.OneUseRolePowerCommit or
                 DomainRecoveryCursorKind.RecurringNativeRolePowerCommit or
                 DomainRecoveryCursorKind.TargetPrivateRolePowerCommit or
				 DomainRecoveryCursorKind.ActorBorrowedWitchPotionUseCommit or
				 DomainRecoveryCursorKind.ActorBorrowedWitchPotionDeclineCommit) ||
            session.GetCurrentPhase() != GamePhase.Night ||
            !IsNightStartSubPhase(session))
        {
            throw new InvalidOperationException(
                $"Unsupported domain continuation '{sourceRole}:{cursor.CommittedActionType}:{cursor.NextInstructionSemantic}'.");
        }

        if (cursor.Kind ==
            DomainRecoveryCursorKind.OneUseRolePowerCommit &&
            cursor.ResourceIdentity == null)
        {
            throw new InvalidOperationException(
                "The domain recovery cursor is structurally invalid.");
        }

        var pendingInstruction = session.PendingModeratorInstruction
            ?? throw new InvalidOperationException(
                "The committed domain continuation requires one Pending Instruction.");
        if (cursor.Kind ==
                DomainRecoveryCursorKind.TargetPrivateRolePowerCommit &&
            !RoleListenerDispatch
                .TryValidateTargetPrivateRecoveryCursorIdentity(
				session,
                Listener(sourceRole),
                admissions,
                (id, factory) =>
                    session.GetOrCreateListener(id, factory),
                cursor))
        {
            throw new InvalidOperationException(
                $"Unsupported target-private Role Power continuation '{sourceRole}'.");
        }

        if (cursor.Kind ==
            DomainRecoveryCursorKind.RecurringNativeRolePowerCommit)
        {
            switch (sourceRole)
            {
                case MainRoleType.BigBadWolf:
                    BigBadWolfRole.ValidateRecurringRecoveryCursorIdentity(
						session,
                        cursor);
                    break;
                case MainRoleType.Defender:
                    DefenderRole.ValidateRecurringRecoveryCursorIdentity(
						session,
                        cursor);
                    break;
                case MainRoleType.WhiteWerewolf:
                    WhiteWerewolfRole.ValidateRecurringRecoveryCursorIdentity(
						session,
                        cursor);
                    break;
                case MainRoleType.Piper:
					PiperRole.ValidateRecurringRecoveryCursorIdentity(
						session,
						cursor);
                    break;
                case MainRoleType.Cupid:
					CupidRole.ValidateRecurringRecoveryCursorIdentity(
						session,
						cursor);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported recurring Role Power continuation '{sourceRole}'.");
            }

			var hasMatchingRecurringCommit =
				sourceRole == MainRoleType.Cupid &&
				cursor.PowerInstanceOrigin == RolePowerInstanceOrigin.Borrowed
					? session.GetActorBorrowedCupidLoversCommits()
						.Any(commit =>
							commit.PowerIdentity == cursor.PowerIdentity &&
							commit.CurrentPhase == GamePhase.Night &&
							commit.TurnNumber == session.TurnNumber &&
							commit.PlayerIds.SequenceEqual(
								cursor.CommittedTargetIds))
					: sourceRole == MainRoleType.Defender &&
				cursor.PowerInstanceOrigin == RolePowerInstanceOrigin.Borrowed
					? session.GetActorBorrowedDefenderProtectionCommits()
						.Any(commit =>
							commit.PowerIdentity == cursor.PowerIdentity &&
							commit.CurrentPhase == GamePhase.Night &&
							commit.TurnNumber == session.TurnNumber &&
							cursor.CommittedTargetIds is
								[var committedTargetId] &&
							commit.TargetPlayerId == committedTargetId)
					: sourceRole == MainRoleType.Cupid
                    ? GameSessionQueries.GetCommittedLoversPair(session) is
                        { } entry &&
                            cursor.CommittedActionType ==
                                NightActionType.CupidLink &&
                            entry.PowerIdentity == cursor.PowerIdentity &&
                            entry.CurrentPhase == GamePhase.Night &&
                            entry.TurnNumber == session.TurnNumber &&
                            entry.PlayerIds.SequenceEqual(
                                cursor.CommittedTargetIds)
                    : session.GameHistoryLog
                        .OfType<RecurringRolePowerCommittedLogEntry>()
                        .Any(entry =>
                            entry.ActionType == cursor.CommittedActionType &&
                            entry.PowerIdentity == cursor.PowerIdentity &&
                            entry.CurrentPhase == GamePhase.Night &&
                            entry.TurnNumber == session.TurnNumber &&
                            entry.TargetIds is { Count: > 0 } targetIds &&
                            targetIds.SequenceEqual(
                                cursor.CommittedTargetIds));
            if (!hasMatchingRecurringCommit)
            {
                if (sourceRole != MainRoleType.BigBadWolf)
                {
                    throw new InvalidOperationException(
                        "The domain recovery cursor does not match the latest recurring native Role Power action.");
                }

                BigBadWolfRole.ValidateLegacyRecurringRecoveryBoundary(
                    cursor,
                    pendingInstruction);
                session.NormalizeLegacyRecurringRolePowerCommit(
                    Key,
                    cursor.CommittedActionType,
                    cursor.CommittedTargetIds.Single(),
                    cursor.PowerIdentity!.Value);
            }
        }

        var configuredContinuation = ResolveDomainContinuation(
            sourceRole,
            cursor.CommittedActionType,
            cursor.NextInstructionSemantic);
        if (sourceRole == Witch && configuredContinuation == null)
        {
            throw new InvalidOperationException(
                $"Unsupported domain continuation '{sourceRole}:{cursor.CommittedActionType}:{cursor.NextInstructionSemantic}'.");
        }

        var listenerContinuation = ResolvePendingInstructionContinuation(
            Listener(sourceRole),
            NightMainActionLoop,
            session,
            pendingInstruction,
            admissions);
        if (listenerContinuation != null)
        {
            if (listenerContinuation.Value.Listener !=
                Listener(sourceRole))
            {
                throw new InvalidOperationException(
                    "The committed domain continuation resolved to a different listener.");
            }

            session.RestoreTransientContinuation(
                Key,
                listenerContinuation.Value.ActiveSubPhaseStage,
                listenerContinuation.Value.Listener,
                listenerContinuation.Value.ListenerState);
            return;
        }

        if (cursor.Kind ==
            DomainRecoveryCursorKind.RecurringNativeRolePowerCommit)
        {
            throw new InvalidOperationException(
                $"Unsupported domain continuation '{sourceRole}:{cursor.CommittedActionType}:{cursor.NextInstructionSemantic}'.");
        }

        var continuation = configuredContinuation;
        if (continuation == null)
        {
            throw new InvalidOperationException(
                $"Unsupported domain continuation '{sourceRole}:{cursor.CommittedActionType}:{cursor.NextInstructionSemantic}'.");
        }

        if (!continuation.Value.Matches(session.PendingModeratorInstruction))
        {
            throw new InvalidOperationException(
                "The Pending Instruction does not match the committed domain continuation.");
        }

        session.RestoreTransientContinuation(
            Key,
            continuation.Value.ActiveSubPhaseStage,
            continuation.Value.Listener,
            continuation.Value.ListenerState);
    }

    private static bool IsNightStartSubPhase(GameSession session)
    {
        var subPhaseId = session.GetSubPhaseId();
        var nightSubPhase = session.GetSubPhase<NightSubPhases>();

        return (subPhaseId == null || nightSubPhase != null) &&
            (nightSubPhase ?? NightSubPhases.Start) == NightSubPhases.Start;
    }

    private static AcceptedObservationContinuation?
        ResolveDomainContinuation(
            MainRoleType sourceRole,
            NightActionType committedActionType,
            ModeratorInstructionSemantic nextInstructionSemantic)
        => (sourceRole, committedActionType, nextInstructionSemantic) switch
        {
            (Witch, NightActionType.WitchSave, ModeratorInstructionSemantic.SelectWitchPoisonTarget) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(Witch),
                    WitchRoleState.AwaitingPoisonSelection.ToString(),
                    AcceptedObservationInstructionShape.PlayerSelection),
            (Witch, NightActionType.WitchSave, ModeratorInstructionSemantic.PutRoleToSleep) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(Witch),
                    WitchRoleState.ReadyToSleep.ToString(),
                    AcceptedObservationInstructionShape.Confirmation),
            (Witch, NightActionType.WitchKill, ModeratorInstructionSemantic.PutRoleToSleep) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(Witch),
                    WitchRoleState.ReadyToSleep.ToString(),
                    AcceptedObservationInstructionShape.Confirmation),
            _ => null
        };

    private static bool TryGetVictoryInstructions(GameSession session, GamePhase oldPhase, GamePhase newPhase,
		out ModeratorInstruction? nextInstructionToSend)
    {
        nextInstructionToSend = null;
		// --- Post-Processing: Victory Check ---
		// Check victory ONLY at the starting point of Day and Night phases
		if (oldPhase != newPhase && newPhase is GamePhase.Day or GamePhase.Night)
        {
            var window = newPhase == GamePhase.Day
                ? VictoryCheckWindow.Dawn
                : VictoryCheckWindow.PreNight;
            var gameResult = CheckVictoryConditions(
                session,
                window,
                out var angelVictoryEligible);
            if (gameResult != null)
            {
                session.VictoryConditionMet(gameResult, window);
                AngelLifecycleRules.ExpireAfterResolvedWindow(
                    session,
                    window,
                    angelVictoryEligible);

                var finalInstruction = new FinishedGameConfirmationInstruction(
                    gameResult,
                    window);
                nextInstructionToSend = finalInstruction; // Override instruction
                return true;
            }

            AngelLifecycleRules.ExpireAfterResolvedWindow(
                session,
                window,
                angelVictoryEligible);
        }

		return false;
    }

    private static PhaseHandlerResult RouteInputToPhaseHandler(GameSession session, ModeratorResponse input)
    {
        var currentPhase = session.GetCurrentPhase();

        if (!PhaseDefinitions.TryGetValue(currentPhase, out var phaseDef))
        {
            throw new InvalidOperationException($"No phase definition found for phase: {currentPhase}");
        }

        var result = phaseDef.ProcessInputAndUpdatePhase(session, input);

        // Defensive check: null instructions should only bubble up from MainPhaseHandlerResult
        // during silent phase transitions (handled by HandleInput). If we get here with a null
        // instruction from any other result, something has gone wrong at the sub-phase or hook level.
        if (result is not MainPhaseHandlerResult && result.ModeratorInstruction == null)
        {
            throw new InvalidOperationException(
                $"Internal State Machine Error: Received null ModeratorInstruction from non-MainPhaseHandlerResult. " +
                $"Result type: {result.GetType().Name}, Current phase: {session.GetCurrentPhase()}");
        }

        return result;
    }

    private static void EnsureVictoryFactsReady(
        GameSession session,
        ModeratorResponse _) =>
        RequireLivingFactionBeneficiarySnapshot(session);

    private static LivingFactionBeneficiarySnapshot[]
        RequireLivingFactionBeneficiarySnapshot(
        GameSession session)
    {
        if (!InitialBeneficiaryClosureRules.HasCommitted(session))
        {
            throw new InvalidOperationException(
                "Required Faction facts are not ready.");
        }

		var committedLoverIds =
			GameSessionQueries.GetCommittedLoversPlayerIds(session)?
			.ToHashSet() ?? [];
        return session.GetPlayers()
            .WithHealth(PlayerHealth.Alive)
			.Select(player => new LivingFactionBeneficiarySnapshot(
				session.RequireKnownFactionBeneficiary(player.Id),
				player.State.HasStatusEffect(StatusEffectTypes.Charmed),
				player.State.DurableVotingPower,
				committedLoverIds.Contains(player.Id),
				player.Id))
            .ToArray();
    }

    private static GameResult? CheckVictoryConditions(
        GameSession session,
        VictoryCheckWindow window,
        out bool angelVictoryEligible)
    {
        var livingPlayers =
            RequireLivingFactionBeneficiarySnapshot(session);
		angelVictoryEligible = AngelLifecycleRules.IsVictoryEligible(
			session,
			window);
			var satisfiedFactions = FactionVictoryPredicates
				.Evaluate(livingPlayers, session.PublicGroupPartition)
			.Concat(angelVictoryEligible ? [Faction.Angel] : []);

        return GameResultSelection.Select(
            satisfiedFactions,
            allPlayersEliminated: livingPlayers.Length == 0);
    }

	#endregion
}
