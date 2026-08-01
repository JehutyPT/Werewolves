using System.Globalization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models.Simulation;

public sealed class CanonicalSimulationScenario : IEquatable<CanonicalSimulationScenario>
{
	private readonly MainRoleType[] _actorSetupCards;

	public int PlayerCount { get; }

	public CanonicalRoleComposition RoleComposition { get; }
	public MainRoleType? Offer1Role { get; }
	public MainRoleType? Offer2Role { get; }
	public ThiefOfferBranchPolicy? ThiefOfferBranchPolicy { get; }

	public IReadOnlyList<MainRoleType> ActorSetupCards { get; }

	public SimulationRuleState RuleState { get; }

	private CanonicalSimulationScenario(
		int playerCount,
		CanonicalRoleComposition roleComposition,
		MainRoleType? offer1Role,
		MainRoleType? offer2Role,
		ThiefOfferBranchPolicy? thiefOfferBranchPolicy,
		MainRoleType[] actorSetupCards,
		SimulationRuleState ruleState)
	{
		PlayerCount = playerCount;
		RoleComposition = roleComposition;
		Offer1Role = offer1Role;
		Offer2Role = offer2Role;
		ThiefOfferBranchPolicy = thiefOfferBranchPolicy;
		_actorSetupCards = actorSetupCards;
		ActorSetupCards = Array.AsReadOnly(_actorSetupCards);
		RuleState = ruleState;
	}

	public static CanonicalSimulationScenario Create(SimulationScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		return new CanonicalSimulationScenario(
			scenario.PlayerCount,
			CanonicalRoleComposition.Create(scenario.DealPoolCards),
			scenario.Offer1Role,
			scenario.Offer2Role,
			scenario.ThiefOfferBranchPolicy,
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
		var hasOffers = parts.Length is 5 or 6;
		var hasThiefPolicy = parts.Length == 6;
		var actorPartIndex = hasThiefPolicy ? 4 : hasOffers ? 3 : 2;
		var rulesPartIndex = actorPartIndex + 1;
		if (parts.Length is not 4 and not 5 and not 6
			|| !parts[0].StartsWith("players=", StringComparison.Ordinal)
			|| (hasOffers && (!parts[2].StartsWith("offers=[", StringComparison.Ordinal)
				|| !parts[2].EndsWith(']')))
			|| (hasThiefPolicy && (!parts[3].StartsWith("thief=[", StringComparison.Ordinal)
				|| !parts[3].EndsWith(']')))
			|| !parts[actorPartIndex].StartsWith("actor=[", StringComparison.Ordinal)
			|| !parts[actorPartIndex].EndsWith(']')
			|| !parts[rulesPartIndex].StartsWith("rules=[", StringComparison.Ordinal)
			|| !parts[rulesPartIndex].EndsWith(']'))
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

		if (!TryParseOffers(
				hasOffers ? parts[2] : null,
				out var offer1Role,
				out var offer2Role)
			|| !TryParseThiefOfferBranchPolicy(
				hasThiefPolicy ? parts[3] : null,
				roleComposition,
				offer1Role,
				offer2Role,
				out var thiefOfferBranchPolicy)
			|| !TryParseActorSetupCards(parts[actorPartIndex], out var actorSetupCards)
			|| !TryParseRuleState(parts[rulesPartIndex], out var ruleState))
		{
			return false;
		}

		scenario = new CanonicalSimulationScenario(
			playerCount,
			roleComposition,
			offer1Role,
			offer2Role,
			thiefOfferBranchPolicy,
			actorSetupCards,
			ruleState);
		return string.Equals(value, scenario.ToString(), StringComparison.Ordinal);
	}

	public override string ToString()
	{
		var actorCards = string.Join(',', _actorSetupCards.Select(role => role.ToString()));
		var ruleState = RuleState.NewMoonEnabled ? "NewMoonEnabled" : string.Empty;
		var offers = Offer1Role is { } offer1 && Offer2Role is { } offer2
			? $"|offers=[{offer1},{offer2}]"
			: string.Empty;
		var thiefPolicy = ThiefOfferBranchPolicy is { } policy
			? $"|thief=[{policy}]"
			: string.Empty;
		return $"players={PlayerCount.ToString(CultureInfo.InvariantCulture)}|{RoleComposition}{offers}{thiefPolicy}|actor=[{actorCards}]|rules=[{ruleState}]";
	}

	public bool Equals(CanonicalSimulationScenario? other) =>
		other is not null
		&& PlayerCount == other.PlayerCount
		&& RoleComposition.Equals(other.RoleComposition)
		&& Offer1Role == other.Offer1Role
		&& Offer2Role == other.Offer2Role
		&& Equals(ThiefOfferBranchPolicy, other.ThiefOfferBranchPolicy)
		&& _actorSetupCards.SequenceEqual(other._actorSetupCards)
		&& RuleState == other.RuleState;

	public override bool Equals(object? obj) =>
		obj is CanonicalSimulationScenario other && Equals(other);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(PlayerCount);
		hash.Add(RoleComposition);
		hash.Add(Offer1Role);
		hash.Add(Offer2Role);
		hash.Add(ThiefOfferBranchPolicy);
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

	private static bool TryParseOffers(
		string? value,
		out MainRoleType? offer1Role,
		out MainRoleType? offer2Role)
	{
		offer1Role = null;
		offer2Role = null;
		if (value is null)
		{
			return true;
		}

		var identifiers = value["offers=[".Length..^1].Split(',');
		if (identifiers.Length != 2 ||
			!TryParseCanonicalRole(identifiers[0], out var offer1) ||
			!TryParseCanonicalRole(identifiers[1], out var offer2))
		{
			return false;
		}

		offer1Role = offer1;
		offer2Role = offer2;
		return true;
	}

	private static bool TryParseThiefOfferBranchPolicy(
		string? value,
		CanonicalRoleComposition roleComposition,
		MainRoleType? offer1Role,
		MainRoleType? offer2Role,
		out ThiefOfferBranchPolicy? policy)
	{
		policy = null;
		var requiresPolicy =
			offer1Role.HasValue &&
			offer2Role.HasValue &&
			roleComposition.Entries.Any(entry =>
				entry.Role == MainRoleType.Thief && entry.Count == 1);
		if (!requiresPolicy)
		{
			return value is null;
		}
		if (value is null ||
		    !ThiefOfferBranchPolicy.TryParse(
			    value,
			    offer1Role!.Value,
			    offer2Role!.Value,
			    out var parsed))
		{
			return false;
		}

		policy = parsed;
		return true;
	}

	private static bool TryParseCanonicalRole(
		string identifier,
		out MainRoleType role) =>
		Enum.TryParse(identifier, out role)
		&& Enum.IsDefined(role)
		&& string.Equals(identifier, role.ToString(), StringComparison.Ordinal);

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
