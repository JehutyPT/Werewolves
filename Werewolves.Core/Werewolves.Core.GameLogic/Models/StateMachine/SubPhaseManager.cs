namespace Werewolves.Core.GameLogic.Models.StateMachine;

/// <summary>
/// Passive ordered stages and allowed destinations for one sub-phase.
/// </summary>
/// <typeparam name="TSubPhase">The enum type defining the sub-phases for the parent phase.</typeparam>
internal record SubPhaseManager<TSubPhase> where TSubPhase : struct, Enum
{
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
	internal IReadOnlyList<SubPhaseStage> SubPhaseStages { get; }

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

}
