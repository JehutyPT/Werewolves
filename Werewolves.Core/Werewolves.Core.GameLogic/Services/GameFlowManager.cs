using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using static Werewolves.Core.GameLogic.Models.InternalMessages.MainPhaseHandlerResult;
using static Werewolves.Core.GameLogic.Models.InternalMessages.SubPhaseHandlerResult;
using static Werewolves.Core.GameLogic.Models.StateMachine.HookSubPhaseStage;
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
            Listener(Fox),
            Listener(StutteringJudge),      //first night only
            Listener(Elder),                //first night only, required to enable disregarding wolf infection
            Listener(TwoSisters),
            Listener(ThreeBrothers),
            Listener(WildChild),            //first night only
            Listener(BearTamer),            //first night only
            Listener(Defender),
            Listener(WolfHound),            //first night only
			Listener(SimpleWerewolf),
            Listener(AccursedWolfFather),
            Listener(BigBadWolf),
            Listener(WhiteWerewolf),
			Listener(Seer),
            Listener(Witch),
            Listener(Gypsy),
            Listener(Piper),
            Listener(Charmed),
            Listener(KnightWithRustySword)  //should not wake the player, just check if the knight was killed the previous day by werewolves
                                            //and applies the effect if so
		],

        // To manage "death chains" (where one elimination triggers another, e.g., Hunter or Lovers) within a linear hook execution,
        // we utilize "Loop Unrolling" by duplicating the listener list. This ensures that upstream dependencies are resolved; 
        // for example, if a Hunter shoots a target at the end of the first pass, the second pass allows reactive roles (like Lovers) 
        // to process that new death.
        //
        // Two iterations are mathematically sufficient for the current ruleset because the "Single Hunter" constraint limits the 
        // maximum causal depth. A chain cannot extend beyond a secondary reaction (e.g., Hunter shoots Lover -> Partner dies, 
        // or Lover drags down Hunter -> Hunter shoots). The final victim in any such chain cannot trigger a third lethal event 
        // (as they cannot be a second Hunter), rendering a third iteration unnecessary.
		[PlayerRoleAssignedOnElimination] =
        [
            
                        // --- ITERATION 1 (Catches Primary Deaths) ---
            // allow the devoted servant to intercept role assignments before anything else happens, even before hunter.
            // they are able to swap roles with hunter before hunter's ability triggers
            Listener(DevotedServant),   
            Listener(Lovers),           // Kills partner if applicable
            Listener(Hunter),           // Shoots if dead
            Listener(WildChild),        // Transforms if Model died
            Listener(Elder),            // Lose lives/die
            Listener(Sheriff),          // Appoint successor
            Listener(Executioner),      // Nominate successor

            // --- ITERATION 2 (Catches Consequential Deaths) ---
            Listener(DevotedServant),
            Listener(Lovers),           // Catch partner if Hunter shot a Lover in Iter 1
            Listener(Hunter),           // Catch shot if Lover dragged Hunter down in Iter 1
            Listener(WildChild),        // Catch model death from Iter 1 shot
            Listener(Elder),
            Listener(Sheriff),          // Catch successor appointment from Iter 1 shot
            Listener(Executioner),
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
            Listener(StutteringJudge),      // power can only trigger once per game
        ],
	};

    /// <summary>
    /// Factory functions for creating listener instances. Each game session gets its own fresh instances.
    /// This ensures listener state machines are isolated between games (fixing test isolation bugs).
    /// </summary>
    internal static readonly Dictionary<ListenerIdentifier, Func<IGameHookListener>> ListenerFactories = new()
    {
        // Define listener factories here - each invocation creates a fresh instance
        [Listener(SimpleWerewolf)] = () => new SimpleWerewolfRole(),
        [Listener(Seer)] = () => new SeerRole(),
        [Listener(WildChild)] = () => new WildChildRole(),
        [Listener(SimpleVillager)] = () => new SimpleVillagerRole()
    };

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
                        LogicStage(DawnSubPhaseStage.AnnounceVictimsAndRequestRoles, DawnPhaseHandlers.AnnounceVictimsAndRequestRoles),
                        LogicStage(DawnSubPhaseStage.AssignVictimRoles, DawnPhaseHandlers.AssignVictimRoles),
                        HookStage(PlayerRoleAssignedOnElimination),
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
                        DaySubPhases.NormalVoting
                    ]),
                new(
                    subPhase: DaySubPhases.NormalVoting,
                    subPhaseStages:
                    [
                        LogicStage(DaySubPhaseStage.RequestVote, DayPhaseHandlers.RequestNormalVoteOutcome),
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
                        NavigationEndStage(DaySubPhaseStage.VerifyLynchingOcurred, ResolveNonTieVoteAndGoToVoteOutcome)
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
                        DaySubPhases.ProcessVoteDeathLoop,
                        DaySubPhases.Finalize
                    ]),
                new(
                    subPhase: DaySubPhases.ProcessVoteDeathLoop,
                    subPhaseStages:
                    [
                        HookStage(PlayerRoleAssignedOnElimination),
                        NavigationEndStageSilent(DaySubPhases.Finalize)
                    ],
                    possibleNextSubPhases:
                    [
                        DaySubPhases.Finalize
                    ]),
                new(
                    subPhase: DaySubPhases.Finalize,
                    subPhaseStages:
                    [
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

    private static SubPhaseHandlerResult StartDebateAndGoToVoteType(GameSession session, ModeratorResponse input)
        => TransitionSubPhase(DayPhaseHandlers.StartDebate(session, input), DaySubPhases.DetermineVoteType);

    private static SubPhaseHandlerResult GoToNormalVoting(GameSession session, ModeratorResponse input)
        => TransitionSubPhaseSilent(DaySubPhases.NormalVoting);

    private static MajorNavigationPhaseHandlerResult RecordNormalVoteAndChooseDayPath(GameSession session, ModeratorResponse input)
    {
        var selectedPlayerId = DayPhaseHandlers.RecordNormalVoteOutcome(session, input);

        if (selectedPlayerId == null)
        {
            return TransitionSubPhaseSilent(DaySubPhases.ProcessVoteOutcome);
        }

        var roleRevealInstruction = DayPhaseHandlers.RequestRoleRevealIfNeeded(session, selectedPlayerId.Value);

        return roleRevealInstruction == null
            ? TransitionSubPhaseSilent(DaySubPhases.HandleNonTieVote)
            : TransitionSubPhase(roleRevealInstruction, DaySubPhases.HandleNonTieVote);
    }

    private static SubPhaseHandlerResult ResolveNonTieVoteAndGoToVoteOutcome(GameSession session, ModeratorResponse input)
        => TransitionSubPhase(DayPhaseHandlers.ResolveNonTieVote(session, input), DaySubPhases.ProcessVoteOutcome);

    private static SubPhaseHandlerResult ChoosePathAfterVoteConcluded(GameSession session, ModeratorResponse input)
    {
        if (GameSessionQueries.ShouldVoteRepeat(session))
        {
            return TransitionSubPhaseSilent(DaySubPhases.DetermineVoteType);
        }

        return GameSessionQueries.GetPlayerEliminatedThisVote(session).Any()
            ? TransitionSubPhaseSilent(DaySubPhases.ProcessVoteDeathLoop)
            : TransitionSubPhaseSilent(DaySubPhases.Finalize);
    }
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
        var oldPhase = session.GetCurrentPhase();

        // --- Execute Phase Handler ---
        PhaseHandlerResult handlerResult = RouteInputToPhaseHandler(session, input);

        var newPhase = session.GetCurrentPhase();

		var nextInstructionToSend = handlerResult.ModeratorInstruction;

		if(TryGetVictoryInstructions(session, oldPhase, newPhase, out var victoryInstruction))
        {
            nextInstructionToSend = victoryInstruction;
		}

        if (nextInstructionToSend == null)
        {
            throw new InvalidOperationException("HandleInput: null nextInstructionToSend");
        }

        // --- Update Pending Instruction ---
		session.SetPendingModeratorInstruction(Key, nextInstructionToSend);

		return ProcessResult.Success(nextInstructionToSend);
    }

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
        PhaseHandlerResult result;
        do
        {
            var currentPhase = session.GetCurrentPhase();
            
            if (!PhaseDefinitions.TryGetValue(currentPhase, out var phaseDef))
            {
                throw new InvalidOperationException($"No phase definition found for phase: {currentPhase}");
            }

            result = phaseDef.ProcessInputAndUpdatePhase(session, input);
        } 
        while (result is MainPhaseHandlerResult { ModeratorInstruction: null });

        // Defensive check: null instructions should only bubble up from MainPhaseHandlerResult
        // during silent phase transitions (handled by the loop above). If we get here with a null
        // instruction, something has gone wrong at the sub-phase or hook level.
        if (result.ModeratorInstruction == null)
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
