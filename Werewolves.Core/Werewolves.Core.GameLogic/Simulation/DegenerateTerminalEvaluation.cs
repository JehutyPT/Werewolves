using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public sealed record DegenerateTerminalEvaluation : TerminalLobbyEvaluation
{
	public SimulationResultEvidence ScreeningEvidence { get; }
	public DegenerateScreeningAggregate SupportingAggregate { get; }

	private DegenerateTerminalEvaluation(
		SimulationResultEvidence screeningEvidence,
		CompletedSimulationRun[] supportingRuns)
	{
		ScreeningEvidence = screeningEvidence;
		SupportingAggregate = new DegenerateScreeningAggregate(
			screeningEvidence.PossibleGameResults,
			supportingRuns);
	}

	internal static DegenerateTerminalEvaluation? TryCreate(
		SimulationResultEvidence evidence)
	{
		if (evidence.AttemptedRunCount !=
			TerminalLobbyEvaluator.GetScreeningAttemptCount(evidence.CanonicalScenario))
		{
			return null;
		}

		var policy = evidence.CanonicalScenario.ThiefOfferBranchPolicy;
		IEnumerable<IReadOnlyList<SimulationRun>> branches = policy is null
			? [evidence.Records]
			: policy.Branches.Select(branch =>
				(IReadOnlyList<SimulationRun>)evidence.Records
					.Where(record => policy.GetBranch(record.RunSeedMaterial.RunNumber) == branch)
					.ToArray());
		var supportingRuns = branches.FirstOrDefault(records =>
			records.Count == TerminalLobbyEvaluator.ScreeningAttemptCount
			&& records.All(record => record is CompletedSimulationRun { EndingTurn: 1 }));
		return supportingRuns is null
			? null
			: new DegenerateTerminalEvaluation(
				evidence,
				supportingRuns.Cast<CompletedSimulationRun>().ToArray());
	}
}

public sealed class DegenerateScreeningAggregate
{
	public IReadOnlyList<GameResultFrequency> GameResultFrequencies { get; }
	public IReadOnlyList<GameResultTurnWindowFrequency> GameResultFrequencyByTurn { get; }

	internal DegenerateScreeningAggregate(
		IReadOnlyList<GameResult> possibleGameResults,
		CompletedSimulationRun[] completedRuns)
	{
		GameResultFrequencies = Array.AsReadOnly(possibleGameResults
			.Select(result => new GameResultFrequency(
				result,
				completedRuns.Count(run => run.GameResult.Equals(result)),
				completedRuns.Length))
			.ToArray());
		GameResultFrequencyByTurn = Array.AsReadOnly(completedRuns
			.GroupBy(run => new { run.GameResult, run.EndingTurn, run.VictoryCheckWindow })
			.Select(group => new GameResultTurnWindowFrequency(
				group.Key.GameResult,
				group.Key.EndingTurn,
				group.Key.VictoryCheckWindow,
				group.Count(),
				completedRuns.Length))
			.OrderBy(cell => cell.EndingTurn)
			.ThenBy(cell => cell.VictoryCheckWindow)
			.ToArray());
	}
}
