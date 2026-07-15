namespace Werewolves.Core.StateModels.Models.Simulation;

/// <summary>
/// Minimal per-attempt source-record batch returned by simulation execution.
/// This is a precursor to, not the complete inventory-bearing Simulation Result Evidence
/// assembled by downstream terminal evaluation.
/// </summary>
public sealed class SimulationBatchSourceEvidence
{
	private readonly SimulationRun[] _records;

	public CanonicalSimulationScenario CanonicalScenario { get; }

	public SimulatorProfileIdentity SimulatorProfile { get; }

	public DecisionStrategyIdentity DecisionStrategy { get; }

	public IReadOnlyList<SimulationRun> Records { get; }

	public int CompletedRunCount { get; }

	public int IncompleteRunCount { get; }

	internal SimulationBatchSourceEvidence(
		CanonicalSimulationScenario canonicalScenario,
		SimulatorProfileIdentity simulatorProfile,
		DecisionStrategyIdentity decisionStrategy,
		IEnumerable<SimulationRun> records)
	{
		ArgumentNullException.ThrowIfNull(canonicalScenario);
		ArgumentNullException.ThrowIfNull(simulatorProfile);
		ArgumentNullException.ThrowIfNull(decisionStrategy);
		ArgumentNullException.ThrowIfNull(records);

		CanonicalScenario = canonicalScenario;
		SimulatorProfile = simulatorProfile;
		DecisionStrategy = decisionStrategy;
		_records = records.ToArray();
		var compatibilityIdentity = new SimulationCompatibilityIdentity(
			CanonicalScenario,
			SimulatorProfile);
		if (_records.Any(record => record is null)
			|| !_records.Select(record => record.RunSeedMaterial.RunNumber)
				.SequenceEqual(Enumerable.Range(0, _records.Length).Select(runNumber => (long)runNumber))
			|| _records.Any(record =>
				!record.RunSeedMaterial.CompatibilityIdentity.Equals(compatibilityIdentity)
				|| !record.RunSeedMaterial.DecisionStrategyIdentity.Equals(DecisionStrategy)))
		{
			throw new ArgumentException(
				"Simulation batch records must be complete, identity-matched, and ordered by ascending run number.",
				nameof(records));
		}

		Records = Array.AsReadOnly(_records);
		CompletedRunCount = _records.Count(record => record is CompletedSimulationRun);
		IncompleteRunCount = _records.Count(record => record is IncompleteSimulationRun);
		if (CompletedRunCount + IncompleteRunCount != _records.Length)
		{
			throw new ArgumentException(
				"Every batch record must be either a Completed or Incomplete Simulation Run.",
				nameof(records));
		}
	}
}
