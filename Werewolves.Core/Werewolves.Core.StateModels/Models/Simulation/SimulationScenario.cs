using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Models.Simulation;

public readonly record struct SimulationRuleState(bool NewMoonEnabled = false)
{
	public static SimulationRuleState Default => new();
}

public sealed class SimulationScenario : IEquatable<SimulationScenario>
{
	private readonly MainRoleType[] _roleCompositionCards;
	private readonly MainRoleType[] _dealPoolCards;
	private readonly ActorSetupCards _actorSetupCards;
	private readonly CanonicalSimulationScenario _canonical;

	public int PlayerCount { get; }

	public IReadOnlyList<MainRoleType> RoleCompositionCards { get; }
	public IReadOnlyList<MainRoleType> DealPoolCards { get; }
	public MainRoleType? Offer1Role { get; }
	public MainRoleType? Offer2Role { get; }
	public ThiefOfferBranchPolicy? ThiefOfferBranchPolicy { get; }

	public ActorSetupCards ActorSetupCards => _actorSetupCards;
	public CanonicalPublicGroupPartition? PublicGroupPartition { get; }

	public SimulationRuleState RuleState { get; }

	public SimulationScenario(
		int playerCount,
		IEnumerable<MainRoleType> roleCompositionCards,
		ActorSetupCards? actorSetupCards = null,
		SimulationRuleState ruleState = default,
		CanonicalPublicGroupPartition? publicGroupPartition = null)
		: this(
			CreateUnpartitionedInput(roleCompositionCards),
			playerCount,
			actorSetupCards,
			ruleState,
			publicGroupPartition)
	{
	}

	public SimulationScenario(
		int playerCount,
		IEnumerable<MainRoleType> roleCompositionCards,
		IEnumerable<MainRoleType> dealPoolCards,
		MainRoleType? offer1Role,
		MainRoleType? offer2Role,
		ActorSetupCards? actorSetupCards = null,
		SimulationRuleState ruleState = default,
		CanonicalPublicGroupPartition? publicGroupPartition = null)
		: this(
			CreatePartitionedInput(
				roleCompositionCards,
				dealPoolCards,
				offer1Role,
				offer2Role),
			playerCount,
			actorSetupCards,
			ruleState,
			publicGroupPartition)
	{
	}

	public SimulationScenario(
		RoleLockIn roleLockIn,
		ActorSetupCards? actorSetupCards = null,
		SimulationRuleState ruleState = default,
		CanonicalPublicGroupPartition? publicGroupPartition = null)
		: this(
			roleLockIn?.PlayerCount ?? throw new ArgumentNullException(nameof(roleLockIn)),
			roleLockIn.RoleComposition.Select(card => card.PrintedRole),
			roleLockIn.DealPool.Select(card => card.PrintedRole),
			roleLockIn.Offer1?.PrintedRole,
			roleLockIn.Offer2?.PrintedRole,
			actorSetupCards,
			ruleState,
			publicGroupPartition)
	{
	}

	private SimulationScenario(
		PartitionInput partition,
		int playerCount,
		ActorSetupCards? actorSetupCards,
		SimulationRuleState ruleState,
		CanonicalPublicGroupPartition? publicGroupPartition)
	{
		_roleCompositionCards = partition.RoleCompositionCards;
		_dealPoolCards = partition.DealPoolCards;
		if (_roleCompositionCards.Any(role => !Enum.IsDefined(role)))
		{
			throw new ArgumentOutOfRangeException(
				nameof(partition),
				"Role Composition contains an unknown Role identifier.");
		}
		if (_dealPoolCards.Any(role => !Enum.IsDefined(role)))
		{
			throw new ArgumentOutOfRangeException(
				nameof(partition),
				"Deal Pool contains an unknown Role identifier.");
		}
		if (partition.Offer1Role is { } offer1Role && !Enum.IsDefined(offer1Role))
		{
			throw new ArgumentOutOfRangeException(nameof(partition));
		}
		if (partition.Offer2Role is { } offer2Role && !Enum.IsDefined(offer2Role))
		{
			throw new ArgumentOutOfRangeException(nameof(partition));
		}
		if ((partition.Offer1Role is null) != (partition.Offer2Role is null))
		{
			throw new ArgumentException(
				"A fixed Simulation Scenario partition requires both ordered offers or neither offer.",
				nameof(partition));
		}
		var partitionedCards = _dealPoolCards
			.Concat(partition.Offer1Role is { } offer1 ? [offer1] : [])
			.Concat(partition.Offer2Role is { } offer2 ? [offer2] : []);
		if (!CanonicalRoleComposition.Create(partitionedCards)
			.Equals(CanonicalRoleComposition.Create(_roleCompositionCards)))
		{
			throw new ArgumentException(
				"Deal Pool and ordered offers must partition the complete Role Composition.",
				nameof(partition));
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
		DealPoolCards = Array.AsReadOnly(_dealPoolCards);
		Offer1Role = partition.Offer1Role;
		Offer2Role = partition.Offer2Role;
		ThiefOfferBranchPolicy =
			partition.Offer1Role is { } branchOffer1 &&
			partition.Offer2Role is { } branchOffer2 &&
			_dealPoolCards.Count(role => role == MainRoleType.Thief) == 1
				? global::Werewolves.Core.StateModels.Models.Simulation
					.ThiefOfferBranchPolicy.Create(branchOffer1, branchOffer2)
				: null;
		_actorSetupCards = new ActorSetupCards(actorCards);
		if (publicGroupPartition is not null &&
			publicGroupPartition.PlayerCount != playerCount)
		{
			throw new ArgumentException(
				"The canonical Public Group Partition must cover the Simulation Scenario Player count.",
				nameof(publicGroupPartition));
		}
		PublicGroupPartition = publicGroupPartition;
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

	private static PartitionInput CreateUnpartitionedInput(
		IEnumerable<MainRoleType> roleCompositionCards)
	{
		ArgumentNullException.ThrowIfNull(roleCompositionCards);
		var cards = roleCompositionCards.ToArray();
		return new PartitionInput(cards, cards.ToArray(), null, null);
	}

	private static PartitionInput CreatePartitionedInput(
		IEnumerable<MainRoleType> roleCompositionCards,
		IEnumerable<MainRoleType> dealPoolCards,
		MainRoleType? offer1Role,
		MainRoleType? offer2Role)
	{
		ArgumentNullException.ThrowIfNull(roleCompositionCards);
		ArgumentNullException.ThrowIfNull(dealPoolCards);
		return new PartitionInput(
			roleCompositionCards.ToArray(),
			dealPoolCards.ToArray(),
			offer1Role,
			offer2Role);
	}

	private sealed record PartitionInput(
		MainRoleType[] RoleCompositionCards,
		MainRoleType[] DealPoolCards,
		MainRoleType? Offer1Role,
		MainRoleType? Offer2Role);
}
