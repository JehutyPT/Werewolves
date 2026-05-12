using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using static Werewolves.Core.GameLogic.Models.InternalMessages.MainPhaseHandlerResult;
using static Werewolves.Core.GameLogic.Models.InternalMessages.SubPhaseHandlerResult;
using static Werewolves.Core.GameLogic.Models.StateMachine.NavigationSubPhaseStage;
using static Werewolves.Core.GameLogic.Models.StateMachine.HookSubPhaseStage;
using static Werewolves.Core.GameLogic.Models.StateMachine.LogicSubPhaseStage;
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
        [Listener(SimpleVillager)] = () => new SimpleVillagerRole()
    };

    internal static readonly Dictionary<GamePhase, IPhaseDefinition> PhaseDefinitions = new()
    {
        [GamePhase.Night] = new PhaseManager<NightSubPhases>(
        entrySubPhase: NightSubPhases.Start,
        subPhaseList: [
            new(
                subPhase: NightSubPhases.Start,
                subPhaseStages: [ 
                    LogicStage(NightSubPhaseStage.NightStart, HandleNightStart),
                    HookStage(NightMainActionLoop),
                    NavigationEndStage(NightSubPhaseStage.NightEnd, HandleNightActionLoopFinish)
                ],
                possibleNextMainPhaseTransitions:
                [ new (GamePhase.Dawn) ]
			)
        ]),

        
        [GamePhase.Dawn] = new PhaseManager<DawnSubPhases>(
        entrySubPhase: DawnSubPhases.CalculateVictims,
        subPhaseList: [
            new(
                subPhase : DawnSubPhases.CalculateVictims,
                subPhaseStages: [
                    NavigationEndStage(DawnSubPhaseStage.CheckForVictims, HandleDawnCalculateVictims)
                ],
                possibleNextSubPhases:[DawnSubPhases.AnnounceVictims, DawnSubPhases.Finalize]
			),
            new(
                subPhase : DawnSubPhases.AnnounceVictims,
                subPhaseStages: [
					LogicStage(DawnSubPhaseStage.AnnounceVictimsAndRequestRoles, HandleVictimsAnnounceAndRoleRequest),
					LogicStage(DawnSubPhaseStage.AssignVictimRoles, HandleVictimsAnnounceAndRoleResponse),
                    HookStage(PlayerRoleAssignedOnElimination),
                    NavigationEndStageSilent(DawnSubPhases.Finalize)
					],
                possibleNextSubPhases: [DawnSubPhases.Finalize]
			),
            new(
                subPhase: DawnSubPhases.Finalize,
                subPhaseStages: [
                    HookStage(DawnMainActionLoop),
                    NavigationEndStageSilent(GamePhase.Day)
                ],
                possibleNextMainPhaseTransitions: [
                    new(GamePhase.Day),
                ]
			)
        ]),

        [GamePhase.Day] = new PhaseManager<DaySubPhases>(
        entrySubPhase: DaySubPhases.Debate,
        subPhaseList: [
            new(
                subPhase: DaySubPhases.Debate,
                subPhaseStages: [ NavigationEndStage(DaySubPhaseStage.Debate, HandleDebate)],
                possibleNextSubPhases: [ DaySubPhases.DetermineVoteType ]
			),
            new(
                subPhase: DaySubPhases.DetermineVoteType,
                subPhaseStages: [ NavigationEndStage(DaySubPhases.DetermineVoteType, HandleVoteTypeSelection) ],
                possibleNextSubPhases:
                    [ DaySubPhases.NormalVoting 
                        //Additional voting sub-phases to be added here
                    ]
            ),
            new(
                subPhase: DaySubPhases.NormalVoting,
                subPhaseStages: [ 
                    LogicStage(DaySubPhaseStage.RequestVote, HandleDayNormalVoteOutcomeRequest),
                    NavigationEndStage(DaySubPhaseStage.HandleVoteResponse, HandleDayNormalVoteOutcomeResponse)
                        .RequiresInputType(ExpectedInputType.PlayerSelection),
                    
                    ],
                    
                possibleNextSubPhases: [
                    DaySubPhases.HandleNonTieVote,
                    DaySubPhases.ProcessVoteOutcome
                ]
			),
            // Additional sub-phases (AccusationVoting, FriendVoting) can be added here
            new(
                subPhase: DaySubPhases.HandleNonTieVote,
                subPhaseStages: [ 
                    NavigationEndStage(DaySubPhaseStage.VerifyLynchingOcurred, AssignRoleAndVerifyIfLynchingOcurred),
                ],
                possibleNextSubPhases:
                    [ DaySubPhases.ProcessVoteOutcome ]
            ),
            new(
                subPhase: DaySubPhases.ProcessVoteOutcome,
                subPhaseStages: [ 
                    
                    HookStage(OnVoteConcluded),
                    NavigationEndStage(DaySubPhaseStage.VoteOutcomeNavigation, AfterVoteConcludedNavigation)
                ],
                possibleNextSubPhases: [
                    DaySubPhases.DetermineVoteType,     // loops if i.e. stuttering judge triggers
                    DaySubPhases.ProcessVoteDeathLoop,   // if players were eliminated, we need to fire the on death trigger
                    DaySubPhases.Finalize               // proceed to finalize if no eliminations
                ]
            ),
            new(
                subPhase: DaySubPhases.ProcessVoteDeathLoop,
                subPhaseStages: [ 
                    HookStage(PlayerRoleAssignedOnElimination),
                    NavigationEndStageSilent(DaySubPhases.Finalize)
				],
                possibleNextSubPhases:
                    [ DaySubPhases.Finalize ]
			),
            new(
                subPhase: DaySubPhases.Finalize,
                subPhaseStages:
                    [ NavigationEndStageSilent(GamePhase.Night) ],
                possibleNextMainPhaseTransitions:
                    [ new(GamePhase.Night) ]
            )
        ]),

    };
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

	#region Night Phase Handler Methods

	/// <summary>
	/// Handles the Night.Start sub-phase: village goes to sleep, increment turn number.
	/// </summary>
	private static ModeratorInstruction HandleNightStart(GameSession session, ModeratorResponse input)
    {
        var instruction = new ConfirmationInstruction(
            publicAnnouncement: GameStrings.NightStartsPrompt,
            privateInstruction: GameStrings.ConfirmNightStarted
        );

		return instruction;
    }

    private static MainPhaseHandlerResult HandleNightActionLoopFinish(GameSession session, ModeratorResponse input)
    {
        var instruction = new ConfirmationInstruction(
            publicAnnouncement: "Night actions complete. Village wakes up."
        );

        return TransitionPhase(instruction, GamePhase.Dawn);
	}

    #endregion

    #region Dawn Phase Handler Methods

    /// <summary>
    /// Handles the Dawn.CalculateVictims sub-phase: process night actions to determine final victims.
    /// </summary>
    private static MajorNavigationPhaseHandlerResult HandleDawnCalculateVictims(GameSession session, ModeratorResponse input)
    {
        // 1. Delegate the heavy lifting to the static resolver
        // The Resolver loops through players, checks logs, and applies Eliminate/Status effects.
        NightInteractionResolver.ResolveNightPhase(session);

        // 2. Check the consequences (The Manager only cares about the RESULT, not the logic)
        var victimsExist = session.GetPlayersEliminatedThisDawn().Any();
        
        // 3. Route accordingly
        var nextSubPhase = victimsExist ? DawnSubPhases.AnnounceVictims : DawnSubPhases.Finalize;

        return TransitionSubPhaseSilent(nextSubPhase);
	}

    private static ModeratorInstruction HandleVictimsAnnounceAndRoleRequest(GameSession session, ModeratorResponse input)
    {
        var victimList = session.GetPlayersEliminatedThisDawn().ToImmutableHashSet();
        var victimNameList = string.Join(Environment.NewLine, victimList.Select(p => p.Name));
        var announcement = GameStrings.MultipleVictimEliminatedAnnounce.Format(victimNameList);

        // Check if any victims need role assignment
        var victimsNeedingRoles = victimList.Where(p => p.State.MainRole == null).ToImmutableHashSet();

        if (victimsNeedingRoles.Count == 0)
        {
            // All victims already have known roles - just announce
            return new ConfirmationInstruction(publicAnnouncement: announcement);
        }

        var unassignedRoles = session.GetUnassignedRoles();

        return new AssignRolesInstruction(
            publicAnnouncement: announcement,
            privateInstruction: GameStrings.RevealRolePromptSpecify,
            playersForAssignment: victimsNeedingRoles.Select(p => p.Id).ToImmutableHashSet(),
            rolesForAssignment: unassignedRoles
		);
    }

    /// <summary>
    /// Handles the Dawn.ProcessRoleReveals sub-phase: reveal roles for each eliminated player.
    /// </summary>
    private static void HandleVictimsAnnounceAndRoleResponse(GameSession session, ModeratorResponse input)
    {
        // Re-check if any victims need role assignment
        var victimList = session.GetPlayersEliminatedThisDawn();
        var victimsNeedingRoles = victimList.Where(p => p.State.MainRole == null).ToList();

        if (victimsNeedingRoles.Count == 0)
        {
            // All victims already have known roles - nothing to do
            return;
        }

        // Process role assignments from moderator response
        foreach (var entry in input.AssignedPlayerRoles!)
        {
            session.AssignRole(entry.Key, entry.Value);
        }
        
        //proceed to the next sub-phase state silently
    }

	#endregion

	#region Day Phase Handler Methods

    /// <summary>
    /// Handles the DayDebate.Confirm sub-phase: moderator confirms debate is complete.
    /// </summary>
    private static SubPhaseHandlerResult HandleDebate(GameSession session, ModeratorResponse input)
    {
        var voteInstruction = new ConfirmationInstruction(
            publicAnnouncement: GameStrings.DebateStartsPrompt,
            privateInstruction: GameStrings.DebateModeratorInstructions
        );
        
        return TransitionSubPhase(voteInstruction, DaySubPhases.DetermineVoteType);
    }

    private static SubPhaseHandlerResult HandleVoteTypeSelection(GameSession session, ModeratorResponse input)
    {
        // Currently only normal voting is implemented.
        return TransitionSubPhaseSilent(DaySubPhases.NormalVoting);
    }

    private static ModeratorInstruction HandleDayNormalVoteOutcomeRequest(GameSession session, ModeratorResponse input)
    {
        var alivePlayers = session.GetPlayers().WithHealth(PlayerHealth.Alive);

        var selectPlayerInstruction = new SelectPlayersInstruction(
            alivePlayers.ToIdSet(),
            NumberRangeConstraint.SingleOptional,
            publicAnnouncement: GameStrings.VoteStartsPublicInstruction,
            privateInstruction: GameStrings.VoteStartsModeratorInstruction);

        return selectPlayerInstruction;
    }

    /// <summary>
    /// Handles the DayVote.ProcessOutcome sub-phase: process vote outcome reported by moderator.
    /// </summary>
    private static SubPhaseHandlerResult HandleDayNormalVoteOutcomeResponse(GameSession session, ModeratorResponse input)
    {
        var selectedPlayer = input.SelectedPlayerIds!;

		if (selectedPlayer.Count == 0) //tie
        {
            session.PerformDayVote(null);

            return TransitionSubPhaseSilent(DaySubPhases.ProcessVoteOutcome);
		}
        else
        {
            var playerId = selectedPlayer.First();
            session.PerformDayVote(playerId);

            var votedPlayer = session.GetPlayer(playerId);

            // Check if the voted player already has a known role
            if (votedPlayer.State.MainRole != null)
            {
                // Role is already known - just confirm and proceed
                return TransitionSubPhaseSilent(DaySubPhases.HandleNonTieVote);
            }

            var availableRoles = session.GetUnassignedRoles();
            var instruction = new AssignRolesInstruction(
                [playerId],
                availableRoles,
                privateInstruction: GameStrings.RevealRolePromptSpecify
            );
            
			return TransitionSubPhase(instruction, DaySubPhases.HandleNonTieVote);
        }
    }

    private static SubPhaseHandlerResult AssignRoleAndVerifyIfLynchingOcurred(GameSession session, ModeratorResponse input)
    {
        // Get the voted player from the log, not from moderator input
        var lynchedPlayerId = session.GetCurrentVoteTarget()!.Value;
        var lynchedPlayer = session.GetPlayer(lynchedPlayerId);
        var lynchedPlayerState = lynchedPlayer.State;

        // Only process role assignment if player doesn't already have a known role
        if (lynchedPlayerState.MainRole == null)
        {
            var entry = input.AssignedPlayerRoles!.Single();
            var lynchedPlayerRole = entry.Value;
            session.AssignRole(lynchedPlayerId, lynchedPlayerRole);
        }

        if(lynchedPlayerState.IsImmuneToLynching)
        {        
            var announcement = lynchedPlayerState.LynchingImmunityAnnouncement;
            var instruction = new ConfirmationInstruction(publicAnnouncement: announcement!);
            
            session.ApplyStatusEffect(StatusEffectTypes.LynchingImmunityUsed, lynchedPlayerId);

            return TransitionSubPhase(instruction, DaySubPhases.ProcessVoteOutcome);
        }
        else
        {
            session.EliminatePlayer(lynchedPlayerId, EliminationReason.DayVote);
            
            var instruction = new ConfirmationInstruction(publicAnnouncement: GameStrings.SingleVictimEliminatedAnnounce.Format(lynchedPlayer.Name));
            return TransitionSubPhase(instruction, DaySubPhases.ProcessVoteOutcome);
        }

        
    }

    private static SubPhaseHandlerResult AfterVoteConcludedNavigation(GameSession session, ModeratorResponse input)
    {
        //if stuttering judge triggered, loop back to voting
        //else, check if players were eliminated
        //if players were eliminated, go to HandleVoteDeathLoop
        //otherwise go to Finalize

        var shouldVoteRepeat = session.ShouldVoteRepeat();

        if (shouldVoteRepeat)
        {
            return TransitionSubPhaseSilent(DaySubPhases.DetermineVoteType);
        }
        else if (session.GetPlayerEliminatedThisVote().Any())
        {
            return TransitionSubPhaseSilent(DaySubPhases.ProcessVoteDeathLoop);
        }
        else
        {
            return TransitionSubPhaseSilent(DaySubPhases.Finalize);
        }
    }

    #endregion
}
