using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Models.StateMachine;

/// <summary>
/// Interprets ordered stages and declared destinations for one explicit Main Phase.
/// The Game Session kernel remains the sole execution-state mutation authority.
/// </summary>
/// <typeparam name="TSubPhaseEnum">The enum type defining the sub-phases for this phase.</typeparam>
internal class PhaseManager<TSubPhaseEnum> : IPhaseDefinition where TSubPhaseEnum : struct, Enum
{
    private class PhaseManagerKey : IPhaseManagerKey {}
    private static readonly PhaseManagerKey Key = new();
    private class SubPhaseStageCacheKey : ISubPhaseManagerKey {}
    private static readonly SubPhaseStageCacheKey StageEntryKey = new();

	private readonly Dictionary<TSubPhaseEnum, SubPhaseManager<TSubPhaseEnum>> _subPhaseDictionary;
    private readonly TSubPhaseEnum _entrySubPhase;
    private readonly GamePhase _ownedPhase;

    /// <summary>
    /// Creates an interpreter from the centrally declared owning phase and sub-phases.
    /// </summary>
    /// <param name="ownedPhase">The Main Phase this declaration belongs to.</param>
    /// <param name="subPhaseList">The ordered stages and allowed destinations for each sub-phase.</param>
    /// <param name="entrySubPhase">The default entry sub-phase when no sub-phase state is cached.</param>
    public PhaseManager(GamePhase ownedPhase, TSubPhaseEnum entrySubPhase, List<SubPhaseManager<TSubPhaseEnum>> subPhaseList)
    {
        _ownedPhase = ownedPhase;
        _entrySubPhase = entrySubPhase;

        // Each sub-phase must have exactly one declaration.
        var duplicateSubPhases = subPhaseList.GroupBy(s => s.StartSubPhase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateSubPhases.Count > 0)
        {
            throw new ArgumentException($"Duplicate sub-phase declarations found for: {string.Join(", ", duplicateSubPhases)}");
        }

        _subPhaseDictionary = subPhaseList.ToDictionary(s => s.StartSubPhase);
    }

    /// <summary>
    /// Interprets declared work until an instruction is ready or the Main Phase exits.
    /// The kernel owns the cursor; this interpreter only requests its transitions.
    /// </summary>
    public PhaseExecutionResult ProcessInputAndUpdatePhase(GameSession session, ModeratorResponse input)
    {
        RequireOwningPhase(session);

        while (true)
        {
            var execution = session.Execution;
            var subPhaseState = execution.GetSubPhase<TSubPhaseEnum>();
            if (execution.SubPhaseId != null && subPhaseState == null)
            {
                throw new InvalidOperationException("The active sub-phase does not belong to this declaration.");
            }

            var currentSubPhase = subPhaseState ?? _entrySubPhase;
            if (!_subPhaseDictionary.TryGetValue(currentSubPhase, out var subPhase))
            {
                throw new InvalidOperationException($"No declaration exists for sub-phase '{currentSubPhase}'.");
            }

            var stage = subPhase.SubPhaseStages.FirstOrDefault(
                candidate => session.TryEnterSubPhaseStage(StageEntryKey, candidate.Id))
                ?? throw new InvalidOperationException(
                    $"No declared stage can execute in sub-phase '{currentSubPhase}'.");
            var result = stage.Execute(session, input);
            RequireOwningPhase(session);
            switch (result)
            {
                case StayInSubPhaseHandlerResult stay:
                    if (!stay.StageComplete && stay.ModeratorInstruction == null)
                    {
                        throw new InvalidOperationException("A paused stage requires an instruction.");
                    }
                    if (stay.StageComplete)
                    {
                        session.CompleteSubPhaseStage(Key);
                    }
                    break;

                case SubPhaseHandlerResult navigation:
                    if (navigation.SubGamePhase is not TSubPhaseEnum destination ||
                        subPhase.PossibleNextSubPhases?.Contains(destination) != true ||
                        !_subPhaseDictionary.ContainsKey(destination))
                    {
                        throw new InvalidOperationException("The requested sub-phase destination is not declared.");
                    }
                    session.TransitionSubPhase(Key, destination);
                    break;

                case MainPhaseHandlerResult navigation:
                    if (subPhase.PossibleNextMainPhaseTransitions?.Contains(
                            new PhaseTransitionInfo(navigation.MainPhase)) != true)
                    {
                        throw new InvalidOperationException("The requested Main Phase destination is not declared.");
                    }
                    session.TransitionMainPhase(navigation.MainPhase);
                    return new PhaseExecutionResult.PhaseExited(
                        _ownedPhase, session.Execution.CurrentPhase, navigation.ModeratorInstruction);

                default:
                    throw new InvalidOperationException("The stage returned an unsupported outcome.");
            }

            if (result.ModeratorInstruction is { } instruction)
            {
                return new PhaseExecutionResult.InstructionReady(instruction);
            }
        }
    }

    private void RequireOwningPhase(GameSession session)
    {
        if (session.Execution.CurrentPhase != _ownedPhase)
        {
            throw new InvalidOperationException(
                $"Phase '{_ownedPhase}' cannot execute while '{session.Execution.CurrentPhase}' is active.");
        }
    }
}
