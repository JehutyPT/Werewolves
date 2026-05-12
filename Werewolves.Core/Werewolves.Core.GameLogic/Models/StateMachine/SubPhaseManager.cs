using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Models.StateMachine;

/// <summary>
/// Defines a single, validated sub phase within a main game phase's state machine.
/// </summary>
/// <typeparam name="TSubPhase">The enum type defining the sub-phases for the parent phase.</typeparam>
internal record SubPhaseManager<TSubPhase> where TSubPhase : struct, Enum
{
	private class SubPhaseStageCacheKey : ISubPhaseManagerKey {}
	private static readonly SubPhaseStageCacheKey Key = new();

	public SubPhaseManager(
		TSubPhase subPhase, 
		List<SubPhaseStage> subPhaseStages, 
		HashSet<TSubPhase>? possibleNextSubPhases = null, 
		HashSet<PhaseTransitionInfo>? possibleNextMainPhaseTransitions = null)
	{
		if (subPhaseStages.DistinctBy(stage => stage.Id).Count() != subPhaseStages.Count)
		{
			throw new InvalidOperationException(
				$"Attempted to create subphase stages with duplicate id's for subphase {subPhase.GetType().Name}:{subPhase}");
		}

		if (subPhaseStages.Last() is not NavigationSubPhaseStage)
		{
			throw new InvalidOperationException(
				$"Subphase {subPhase.GetType().Name}:{subPhase} has no navigation end stage");
		}

		StartSubPhase = subPhase;
		SubPhaseStages = subPhaseStages;
		PossibleNextMainPhaseTransitions = possibleNextMainPhaseTransitions;
		PossibleNextSubPhases = possibleNextSubPhases;
	}

	/// <summary>
    /// The specific sub-phase that triggers this stage.
    /// </summary>
    public TSubPhase StartSubPhase { get; init; }

	/// <summary>
	/// Sub-phase stages that make up the internal state machine for this sub-phase.
	/// These stages will be executed in order until one of them produces a result.
	/// There is no conditional/branching logic for sub phase stage sequence, if such is required
	/// then additional sub-phases should be implemented, with branching at the sub-phase level proper.
	/// </summary>
	private List<SubPhaseStage> SubPhaseStages { get; }

    /// <summary>
    /// A declarative set of all valid sub-phases that this stage is allowed to transition to.
    /// If null, any sub-phase transition is considered an error.
    /// </summary>
    public HashSet<TSubPhase>? PossibleNextSubPhases { get; init; }

    /// <summary>
    /// A declarative set of all valid main phase transitions that this stage is allowed to initiate.
    /// If null, any main phase transition is considered an error.
    /// </summary>
    public HashSet<PhaseTransitionInfo>? PossibleNextMainPhaseTransitions { get; init; }

	public PhaseHandlerResult Execute(GameSession session, ModeratorResponse input)
	{
		foreach (var stage in SubPhaseStages)
		{
			// Try to execute each sub-phase stage in order
			// They should only produce a result if they need to send an instruction to the moderator
			// Or if we reached the last stage: we need to have a transition defined, either to a sub-phase or a main phase
			if (session.TryEnterSubPhaseStage(Key, stage.Id))
			{
				return stage.Execute(session, input);
			}
		}
		
		throw new InvalidOperationException(
			$"Completed all stages for '{StartSubPhase.GetType().Name}:{StartSubPhase}' but had no final PhaseHandlerResult");
	}
}