using System.Globalization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models.Simulation;

public sealed class CanonicalSimulationScenario : IEquatable<CanonicalSimulationScenario>
{
	private readonly MainRoleType[] _actorSetupCards;

	public int PlayerCount { get; }

	public CanonicalRoleComposition RoleComposition { get; }

	public IReadOnlyList<MainRoleType> ActorSetupCards { get; }

	public SimulationRuleState RuleState { get; }

	private CanonicalSimulationScenario(
		int playerCount,
		CanonicalRoleComposition roleComposition,
		MainRoleType[] actorSetupCards,
		SimulationRuleState ruleState)
	{
		PlayerCount = playerCount;
		RoleComposition = roleComposition;
		_actorSetupCards = actorSetupCards;
		ActorSetupCards = Array.AsReadOnly(_actorSetupCards);
		RuleState = ruleState;
	}

	public static CanonicalSimulationScenario Create(SimulationScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		return new CanonicalSimulationScenario(
			scenario.PlayerCount,
			CanonicalRoleComposition.Create(scenario.RoleCompositionCards),
			scenario.ActorSetupCards.Cards
				.OrderBy(role => role.ToString(), StringComparer.Ordinal)
				.ToArray(),
			scenario.RuleState);
	}

	public static CanonicalSimulationScenario Parse(string value)
	{
		if (!TryParse(value, out var scenario))
		{
			throw new FormatException("The value is not a canonical Simulation Scenario.");
		}

		return scenario;
	}

	public static bool TryParse(
		string? value,
		out CanonicalSimulationScenario scenario)
	{
		scenario = null!;
		if (value is null)
		{
			return false;
		}

		var parts = value.Split('|');
		if (parts.Length != 4
			|| !parts[0].StartsWith("players=", StringComparison.Ordinal)
			|| !parts[2].StartsWith("actor=[", StringComparison.Ordinal)
			|| !parts[2].EndsWith(']')
			|| !parts[3].StartsWith("rules=[", StringComparison.Ordinal)
			|| !parts[3].EndsWith(']'))
		{
			return false;
		}

		var playerCountText = parts[0]["players=".Length..];
		if (!int.TryParse(
				playerCountText,
				NumberStyles.AllowLeadingSign,
				CultureInfo.InvariantCulture,
				out var playerCount)
			|| !string.Equals(
				playerCountText,
				playerCount.ToString(CultureInfo.InvariantCulture),
				StringComparison.Ordinal)
			|| !CanonicalRoleComposition.TryParse(parts[1], out var roleComposition))
		{
			return false;
		}

		if (!TryParseActorSetupCards(parts[2], out var actorSetupCards)
			|| !TryParseRuleState(parts[3], out var ruleState))
		{
			return false;
		}

		scenario = new CanonicalSimulationScenario(
			playerCount,
			roleComposition,
			actorSetupCards,
			ruleState);
		return string.Equals(value, scenario.ToString(), StringComparison.Ordinal);
	}

	public override string ToString()
	{
		var actorCards = string.Join(',', _actorSetupCards.Select(role => role.ToString()));
		var ruleState = RuleState.NewMoonEnabled ? "NewMoonEnabled" : string.Empty;
		return $"players={PlayerCount.ToString(CultureInfo.InvariantCulture)}|{RoleComposition}|actor=[{actorCards}]|rules=[{ruleState}]";
	}

	public bool Equals(CanonicalSimulationScenario? other) =>
		other is not null
		&& PlayerCount == other.PlayerCount
		&& RoleComposition.Equals(other.RoleComposition)
		&& _actorSetupCards.SequenceEqual(other._actorSetupCards)
		&& RuleState == other.RuleState;

	public override bool Equals(object? obj) =>
		obj is CanonicalSimulationScenario other && Equals(other);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(PlayerCount);
		hash.Add(RoleComposition);
		foreach (var role in _actorSetupCards)
		{
			hash.Add(role);
		}

		hash.Add(RuleState);
		return hash.ToHashCode();
	}

	public static bool operator ==(
		CanonicalSimulationScenario? left,
		CanonicalSimulationScenario? right) => Equals(left, right);

	public static bool operator !=(
		CanonicalSimulationScenario? left,
		CanonicalSimulationScenario? right) => !Equals(left, right);

	private static bool TryParseActorSetupCards(
		string value,
		out MainRoleType[] actorSetupCards)
	{
		actorSetupCards = [];
		var body = value["actor=[".Length..^1];
		if (body.Length == 0)
		{
			return true;
		}

		var cards = new List<MainRoleType>();
		foreach (var roleIdentifier in body.Split(','))
		{
			if (!Enum.TryParse<MainRoleType>(roleIdentifier, out var role)
				|| !Enum.IsDefined(role)
				|| !string.Equals(roleIdentifier, role.ToString(), StringComparison.Ordinal))
			{
				return false;
			}

			cards.Add(role);
		}

		actorSetupCards = cards.ToArray();
		var sortedCards = actorSetupCards
			.OrderBy(role => role.ToString(), StringComparer.Ordinal)
			.ToArray();
		if (!actorSetupCards.SequenceEqual(sortedCards))
		{
			actorSetupCards = [];
			return false;
		}

		return true;
	}

	private static bool TryParseRuleState(
		string value,
		out SimulationRuleState ruleState)
	{
		var body = value["rules=[".Length..^1];
		if (body.Length == 0)
		{
			ruleState = SimulationRuleState.Default;
			return true;
		}

		if (string.Equals(body, "NewMoonEnabled", StringComparison.Ordinal))
		{
			ruleState = new SimulationRuleState(NewMoonEnabled: true);
			return true;
		}

		ruleState = default;
		return false;
	}
}
