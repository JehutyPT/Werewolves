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
            Listener(BearTamer),
            Listener(Gypsy),
            Listener(TownCrier),
        ],

        [OnVoteConcluded] =
        [
            Listener(Scapegoat),            // in case of a tie, scapegoat ability triggers
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
			EliminationCascadeReactionIds.WildChildModelEliminated,
			EliminationCascadeReactionBoundary.Forced,
			Listener(WildChild)),
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
                        HookStage(DawnMainActionLoop),
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

    private static string CreateDawnEliminationAnnouncement(
        GameSession session,
        IReadOnlyCollection<EliminationRequest> eliminations)
    {
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
        if (!target.State.IsImmuneToLynching)
        {
            return EliminationBatchCommitDecision.Proceed(eliminations);
        }

        var immunityAnnouncement = target.State.LynchingImmunityAnnouncement!;
        session.ApplyStatusEffect(
            LynchingImmunityUsed,
            voteElimination.PlayerId);
        return new EliminationBatchCommitDecision(
            Eliminations: [],
            new ConfirmationInstruction(
                ModeratorInstructionSemantic.AnnounceLynchingImmunity,
                publicAnnouncement: immunityAnnouncement));
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

	internal static ProcessResult HandleInput(GameSession session, ModeratorResponse input)
    {
        var startingPhase = session.GetCurrentPhase();
        var startingInstruction = session.PendingModeratorInstruction;
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
                startingLogCount);
			session.CaptureRecoveryBoundary(
                Key,
                domainRecoveryCursor == null
                    ? CreateAcceptedObservationRecoveryCursor(
                        startingInstruction,
                        nextInstructionToSend)
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

	        if (nextInstructionToSend.Semantic ==
	            ModeratorInstructionSemantic.ObserveStutteringJudgeSignal)
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

    private static DomainRecoveryCursor? CreateDomainRecoveryCursor(
        GameSession session,
        ModeratorInstruction? startingInstruction,
        ModeratorResponse input,
        ModeratorInstruction nextInstruction,
        int startingLogCount)
    {
        var newCommittedEntries = session.GameHistoryLog
            .Skip(startingLogCount)
            .OfType<OneUseRolePowerCommittedLogEntry>()
            .ToArray();
        if (newCommittedEntries.Length == 0)
        {
            return null;
        }

        var selectedPlayerIds = input.SelectedPlayerIds;
        if (startingInstruction is not SelectPlayersInstruction ||
            selectedPlayerIds is not { Count: 1 })
        {
            throw new InvalidOperationException(
                "A One-Use Resource commit must correlate to one accepted Player selection.");
        }

        var committedTargetId = selectedPlayerIds.Single();
        if (newCommittedEntries is not [var committedEntry] ||
            committedEntry.TargetIds is not { Count: 1 } ||
            committedEntry.TargetIds[0] != committedTargetId)
        {
            throw new InvalidOperationException(
                "The accepted One-Use Resource selection did not produce its atomic domain commit.");
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
            CommittedTargetId = committedTargetId,
            NextInstructionSemantic = nextInstruction.Semantic,
            NextInstructionId = nextInstruction.InstructionId
        };
    }

    private static bool IsAcceptedObservation(ModeratorInstruction? instruction)
        => instruction?.Semantic is
            ModeratorInstructionSemantic.IdentifyRoleHolders or
            ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal or
            ModeratorInstructionSemantic.ObserveScapegoatHolderForTie or
            ModeratorInstructionSemantic.RevealScapegoatForTie or
            ModeratorInstructionSemantic.SelectScapegoatPermittedVoters or
            ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters or
            ModeratorInstructionSemantic.EstablishStutteringJudgeSignal or
            ModeratorInstructionSemantic.ObserveStutteringJudgeSignal or
            ModeratorInstructionSemantic.AnnounceDawnVictims or
            ModeratorInstructionSemantic.AssignDawnVictimRoles or
            ModeratorInstructionSemantic.AssignDayVoteTargetRole or
            ModeratorInstructionSemantic.AssignEliminationCascadeRoles;

	private static bool IsEliminationCascadeReactionInput(
		ModeratorInstruction instruction) =>
		instruction.Semantic is
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget;

    private static AcceptedObservationRecoveryCursor?
        CreateAcceptedObservationRecoveryCursor(
            ModeratorInstruction? startingInstruction,
            ModeratorInstruction nextInstruction)
    {
        if (startingInstruction is not SelectPlayersInstruction
            {
                Semantic: ModeratorInstructionSemantic.IdentifyRoleHolders,
                RoleIdentification: { } observedRole
            })
        {
            return null;
        }

        var continuation = ResolveAcceptedObservationContinuation(
            observedRole,
            nextInstruction.Semantic);
        if (continuation == null ||
            !continuation.Value.Matches(nextInstruction))
        {
            throw new InvalidOperationException(
                $"Unsupported Role Identification continuation '{observedRole}:{nextInstruction.Semantic}'.");
        }

        return new AcceptedObservationRecoveryCursor
        {
            Version = AcceptedObservationRecoveryCursor.CurrentVersion,
            AcceptedObservationSemantic =
                ModeratorInstructionSemantic.IdentifyRoleHolders,
            ObservedRole = observedRole,
            NextInstructionSemantic = nextInstruction.Semantic,
            NextInstructionId = nextInstruction.InstructionId
        };
    }

    internal static void RestoreDurableContinuation(
        GameSession session,
        IRoleAdmissionSource admissions)
    {
        var domainCursor = session.GetDomainRecoveryCursor(Key);
        if (domainCursor != null)
        {
            RestoreDomainContinuation(session, domainCursor);
            return;
        }

	        var cursor = session.GetAcceptedObservationRecoveryCursor(Key);
	        if (cursor == null)
	        {
		        RestorePendingHookListenerContinuation(session, admissions);
	            return;
	        }

        var continuation = ResolveAcceptedObservationContinuation(
            cursor.ObservedRole,
            cursor.NextInstructionSemantic);
        if (cursor.AcceptedObservationSemantic !=
                ModeratorInstructionSemantic.IdentifyRoleHolders ||
            session.GetCurrentPhase() != GamePhase.Night ||
            !IsNightStartSubPhase(session) ||
            continuation == null)
        {
            throw new InvalidOperationException(
                $"Unsupported Role Identification continuation '{cursor.ObservedRole}:{cursor.NextInstructionSemantic}'.");
        }

        if (!continuation.Value.Matches(session.PendingModeratorInstruction))
        {
            throw new InvalidOperationException(
                "The Pending Instruction does not match the accepted Role Identification continuation.");
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

    private static void RestoreDomainContinuation(
        GameSession session,
        DomainRecoveryCursor cursor)
    {
        var resourceIdentity = cursor.ResourceIdentity
            ?? throw new InvalidOperationException(
                "The domain recovery cursor is structurally invalid.");
        var continuation = ResolveDomainContinuation(
            resourceIdentity.SourceRole,
            cursor.CommittedActionType,
            cursor.NextInstructionSemantic);
        if (cursor.Kind != DomainRecoveryCursorKind.OneUseRolePowerCommit ||
            session.GetCurrentPhase() != GamePhase.Night ||
            !IsNightStartSubPhase(session) ||
            continuation == null)
        {
            throw new InvalidOperationException(
                $"Unsupported domain continuation '{resourceIdentity.SourceRole}:{cursor.CommittedActionType}:{cursor.NextInstructionSemantic}'.");
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

    private static AcceptedObservationContinuation?
        ResolveAcceptedObservationContinuation(
            MainRoleType observedRole,
            ModeratorInstructionSemantic nextInstructionSemantic)
        => (observedRole, nextInstructionSemantic) switch
        {
            (WildChild, ModeratorInstructionSemantic.SelectWildChildModel) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(WildChild),
                    StandardNightRoleState.AwaitingTargetSelection.ToString(),
                    AcceptedObservationInstructionShape.PlayerSelection),
            (SimpleWerewolf, ModeratorInstructionSemantic.SelectWerewolfVictim) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(SimpleWerewolf),
                    StandardNightRoleState.AwaitingTargetSelection.ToString(),
                    AcceptedObservationInstructionShape.PlayerSelection),
            (Seer, ModeratorInstructionSemantic.SelectSeerTarget) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(Seer),
                    ImmediateFeedbackNightRoleState.AwaitingTargetSelection.ToString(),
                    AcceptedObservationInstructionShape.PlayerSelection),
            (Seer, ModeratorInstructionSemantic.PutRoleToSleep) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(Seer),
                    ImmediateFeedbackNightRoleState.AwaitingSleepConfirmation.ToString(),
                    AcceptedObservationInstructionShape.Confirmation),
            (Witch, ModeratorInstructionSemantic.SelectWitchHealingTarget) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(Witch),
                    WitchRoleState.AwaitingHealingSelection.ToString(),
                    AcceptedObservationInstructionShape.PlayerSelection),
            (Witch, ModeratorInstructionSemantic.SelectWitchPoisonTarget) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(Witch),
                    WitchRoleState.AwaitingPoisonSelection.ToString(),
                    AcceptedObservationInstructionShape.PlayerSelection),
            (Witch, ModeratorInstructionSemantic.PutRoleToSleep) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(Witch),
                    WitchRoleState.ReadyToSleep.ToString(),
                    AcceptedObservationInstructionShape.Confirmation),
            (TwoSisters, ModeratorInstructionSemantic.RecognizeRoleHolders) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(TwoSisters),
                    CardinalityRoleHolderNightState.RecognitionConfirmation.ToString(),
                    AcceptedObservationInstructionShape.Confirmation),
            (TwoSisters, ModeratorInstructionSemantic.PutRoleToSleep) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(TwoSisters),
                    CardinalityRoleHolderNightState.SleepConfirmation.ToString(),
                    AcceptedObservationInstructionShape.Confirmation),
            (ThreeBrothers, ModeratorInstructionSemantic.RecognizeRoleHolders) =>
                new(
                    NightMainActionLoop.ToString(),
                    Listener(ThreeBrothers),
                    CardinalityRoleHolderNightState.RecognitionConfirmation.ToString(),
                    AcceptedObservationInstructionShape.Confirmation),
	            (ThreeBrothers, ModeratorInstructionSemantic.PutRoleToSleep) =>
	                new(
	                    NightMainActionLoop.ToString(),
	                    Listener(ThreeBrothers),
	                    CardinalityRoleHolderNightState.SleepConfirmation.ToString(),
	                    AcceptedObservationInstructionShape.Confirmation),
	            (StutteringJudge, ModeratorInstructionSemantic.EstablishStutteringJudgeSignal) =>
	                new(
	                    NightMainActionLoop.ToString(),
	                    Listener(StutteringJudge),
	                    StutteringJudgeRoleState.AwaitingSignalSetup.ToString(),
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
            var victoryCheckResult = CheckVictoryConditions(session);
            if (victoryCheckResult != null)
            {
                // Victory condition met!
                session.VictoryConditionMet(victoryCheckResult.Value.WinningTeam, victoryCheckResult.Value.Description);

                var finalInstruction = new FinishedGameConfirmationInstruction(victoryCheckResult.Value.Description);
                nextInstructionToSend = finalInstruction; // Override instruction
                return true;
            }
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

    private static (Team WinningTeam, string Description)? CheckVictoryConditions(GameSession session)
    {
        // Phase 1: Basic checks using assigned/revealed roles
        var aliveWerewolves = session.GetPlayers().WithHealth(PlayerHealth.Alive).FromTeam(Team.Werewolves).Count();
        int aliveNonWerewolves = session.GetPlayers().WithHealth(PlayerHealth.Alive).FromTeam(Team.Villagers).Count();

		// Villager win
		if (aliveWerewolves == 0 && aliveNonWerewolves > 0)
        {
            return (Team.Villagers, GameStrings.VictoryConditionAllWerewolvesEliminated);
        }

        // Werewolf win
        if (aliveWerewolves >= aliveNonWerewolves && aliveWerewolves > 0)
        {
            return (Team.Werewolves, GameStrings.VictoryConditionWerewolvesOutnumber);
        }

        return null;
    }

	#endregion
}
