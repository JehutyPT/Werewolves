using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Models.StateMachine;

/// <summary>
/// Generic phase definition that manages a declarative map of sub-phase stages.
/// Implements IPhaseDefinition to allow the main PhaseDefinitions dictionary to hold phase handlers for different sub-phase enums.
/// </summary>
/// <typeparam name="TSubPhaseEnum">The enum type defining the sub-phases for this phase.</typeparam>
internal class PhaseManager<TSubPhaseEnum> : IPhaseDefinition where TSubPhaseEnum : struct, Enum
{
    private class PhaseManagerKey : IPhaseManagerKey {}
    private static readonly PhaseManagerKey Key = new();

	private readonly Dictionary<TSubPhaseEnum, SubPhaseManager<TSubPhaseEnum>> _subPhaseDictionary;
    private readonly TSubPhaseEnum _entrySubPhase;
    private GamePhase? _cachedOwnedPhase;

    /// <summary>
    /// Determines which GamePhase this manager handles by finding itself in the PhaseDefinitions dictionary.
    /// The result is cached after the first lookup.
    /// </summary>
    private GamePhase GetOwnedPhase()
    {
        _cachedOwnedPhase ??= GameFlowManager.PhaseDefinitions
            .First(kvp => ReferenceEquals(kvp.Value, this))
            .Key;
        return _cachedOwnedPhase.Value;
    }

    /// <summary>
    /// Creates a new PhaseManager with the specified subPhaseList and entry point.
    /// </summary>
    /// <param name="subPhaseList">List of sub-phase subPhaseList that define the state machine.</param>
    /// <param name="entrySubPhase">The default entry sub-phase when no sub-phase state is cached.</param>
    public PhaseManager(TSubPhaseEnum entrySubPhase, List<SubPhaseManager<TSubPhaseEnum>> subPhaseList)
    {
        _entrySubPhase = entrySubPhase;
        
        // Validate that all subPhaseList are unique and build the lookup dictionary
        var duplicateStages = subPhaseList.GroupBy(s => s.StartSubPhase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
            
        if (duplicateStages.Count > 0)
        {
            throw new ArgumentException($"Duplicate sub-phase subPhaseList found for: {string.Join(", ", duplicateStages)}");
        }
        
        _subPhaseDictionary = subPhaseList.ToDictionary(s => s.StartSubPhase);
    }

	/// <summary>
	/// Processes input and updates the phase state according to the defined state machine rules.
	/// Loops internally until a ModeratorInstruction is produced.
	/// </summary>
	/// <param name="session">The current game session.</param>
	/// <param name="input">The moderator response to process.</param>
	/// <returns>A PhaseHandlerResult indicating the outcome of the processing.</returns>
	public PhaseHandlerResult ProcessInputAndUpdatePhase(GameSession session, ModeratorResponse input)
    {
        var ownedPhase = GetOwnedPhase();
        PhaseHandlerResult result;
        do
        {
            // 1. Determine the current sub-phase state, defaulting to the defined entry point
            var subPhaseState = session.GetSubPhase<TSubPhaseEnum>() ?? _entrySubPhase;

            // 2. Find the corresponding stage definition
            if (!_subPhaseDictionary.TryGetValue(subPhaseState, out var subPhase))
            {
                throw new InvalidOperationException(
                    $"Internal State Machine Error: No sub-phase stage definition found for phase '{session.GetCurrentPhase()}' and sub-phase '{subPhaseState}'.");
            }

            // 3. Execute the specific handler for this stage
            result = subPhase.Execute(session, input);

            // 4. Validate the resulting transition against the declarative rules of the stage
            AttemptTransition(session, subPhaseState, result, subPhase);
            
            // Exit if we're no longer the active phase (silent main phase transition occurred)
        } while (result.ModeratorInstruction == null && session.GetCurrentPhase() == ownedPhase);
        

        return result;
    }
    
    /// <summary>
    /// Validates that the transition result is allowed by the stage's declarative rules.
    /// </summary>
    private void AttemptTransition(
        GameSession session,
        TSubPhaseEnum currentSubPhase, 
        PhaseHandlerResult result, 
        SubPhaseManager<TSubPhaseEnum> subPhaseManager)
    {
        switch (result)
        {
            case SubPhaseHandlerResult subPhaseResult:
            {
                var nextSubPhase = (TSubPhaseEnum)subPhaseResult.SubGamePhase;
                var allowed = subPhaseManager.PossibleNextSubPhases;
                if (allowed == null || !allowed.Contains(nextSubPhase))
                {
                    throw new InvalidOperationException(
                        $"Internal State Machine Error: Illegal sub-phase transition from '{currentSubPhase}' to '{nextSubPhase}'. " +
                        $"Valid next sub-phases are: {(allowed == null ? "None" : string.Join(", ", allowed))}.");
                }

                session.TransitionSubPhaseCache(Key, subPhaseResult.SubGamePhase);
				break;
            }
            case MainPhaseHandlerResult mainPhaseResult:
            {
                var allowed = subPhaseManager.PossibleNextMainPhaseTransitions;
                var requested = new PhaseTransitionInfo(mainPhaseResult.MainPhase);
                if (allowed == null || !allowed.Contains(requested))
                {
                     throw new InvalidOperationException(
                        $"Internal State Machine Error: Illegal main-phase transition from '{currentSubPhase}' to '{requested.TargetPhase}'. " +
                        $"Valid main phase transitions are: {(allowed == null ? "None" : string.Join(", ", allowed))}.");
                }

                session.TransitionMainPhase(
                    mainPhaseResult.MainPhase);
				break;
            }
            case StayInSubPhaseHandlerResult stayInSubPhase:
				// Only mark the stage as complete if explicitly indicated.
				// Hook stages that pause mid-execution set StageComplete = false
				// to allow re-entry on the next input.
                if (stayInSubPhase.StageComplete)
                {
                    session.CompleteSubPhaseStageCache(Key);
                }
				break;
        }
    }
}
