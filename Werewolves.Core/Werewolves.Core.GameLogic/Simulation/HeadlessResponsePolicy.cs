using System.Collections.Immutable;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

/// <summary>
/// Immutable declaration of the Moderator Instruction semantics a headless
/// decision strategy is allowed to answer.
/// </summary>
public sealed class HeadlessResponsePolicy
{
	private readonly ImmutableHashSet<ModeratorInstructionSemantic> _admittedSemantics;

	public DecisionStrategyIdentity StrategyIdentity { get; }

	public IReadOnlySet<ModeratorInstructionSemantic> AdmittedSemantics => _admittedSemantics;

	public HeadlessResponsePolicy(
		DecisionStrategyIdentity strategyIdentity,
		IEnumerable<ModeratorInstructionSemantic> admittedSemantics)
	{
		ArgumentNullException.ThrowIfNull(strategyIdentity);
		ArgumentNullException.ThrowIfNull(admittedSemantics);

		var snapshot = admittedSemantics.ToImmutableHashSet();
		if (snapshot.Any(semantic =>
			semantic == ModeratorInstructionSemantic.Unspecified ||
			!Enum.IsDefined(semantic)))
		{
			throw new ArgumentException(
				"Headless response policies can admit only defined, specified instruction semantics.",
				nameof(admittedSemantics));
		}

		StrategyIdentity = strategyIdentity;
		_admittedSemantics = snapshot;
	}

	public bool Admits(ModeratorInstructionSemantic semantic) =>
		_admittedSemantics.Contains(semantic);
}
