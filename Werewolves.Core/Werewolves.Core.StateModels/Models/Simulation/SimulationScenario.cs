using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models.Simulation;

public readonly record struct SimulationRuleState(bool NewMoonEnabled = false)
{
	public static SimulationRuleState Default => new();
}

public sealed class SimulationScenario : IEquatable<SimulationScenario>
{
	private readonly MainRoleType[] _roleCompositionCards;
	private readonly ActorSetupCards _actorSetupCards;
	private readonly CanonicalSimulationScenario _canonical;

	public int PlayerCount { get; }

	public IReadOnlyList<MainRoleType> RoleCompositionCards { get; }

	public ActorSetupCards ActorSetupCards => _actorSetupCards;

	public SimulationRuleState RuleState { get; }

	public SimulationScenario(
		int playerCount,
		IEnumerable<MainRoleType> roleCompositionCards,
		ActorSetupCards? actorSetupCards = null,
		SimulationRuleState ruleState = default)
	{
		ArgumentNullException.ThrowIfNull(roleCompositionCards);

		_roleCompositionCards = roleCompositionCards.ToArray();
		if (_roleCompositionCards.Any(role => !Enum.IsDefined(role)))
		{
			throw new ArgumentOutOfRangeException(
				nameof(roleCompositionCards),
				"Role Composition contains an unknown Role identifier.");
		}

		var actorCards = actorSetupCards?.Cards.ToArray() ?? [];
		if (actorCards.Any(role => !Enum.IsDefined(role)))
		{
			throw new ArgumentOutOfRangeException(
				nameof(actorSetupCards),
				"Actor Setup Cards contain an unknown Role identifier.");
		}

		PlayerCount = playerCount;
		RoleCompositionCards = Array.AsReadOnly(_roleCompositionCards);
		_actorSetupCards = new ActorSetupCards(actorCards);
		RuleState = ruleState;
		_canonical = CanonicalSimulationScenario.Create(this);
	}

	public CanonicalSimulationScenario ToCanonical() => _canonical;

	public bool Equals(SimulationScenario? other) =>
		other is not null
		&& _canonical.Equals(other._canonical);

	public override bool Equals(object? obj) =>
		obj is SimulationScenario other && Equals(other);

	public override int GetHashCode() => _canonical.GetHashCode();
}
