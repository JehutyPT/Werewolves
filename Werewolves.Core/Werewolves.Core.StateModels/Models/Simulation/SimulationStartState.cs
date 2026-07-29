using System.Collections.ObjectModel;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Models.Simulation;

public sealed record SimulationPlayerRoleAssignment
{
	public int SeatNumber { get; }

	public MainRoleType Role { get; }

	public SimulationPlayerRoleAssignment(int seatNumber, MainRoleType role)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seatNumber);
		if (!Enum.IsDefined(role))
		{
			throw new ArgumentOutOfRangeException(nameof(role));
		}

		SeatNumber = seatNumber;
		Role = role;
	}
}

public sealed class SimulationPlayerFactionFacts
	: IEquatable<SimulationPlayerFactionFacts>
{
	private readonly IReadOnlyDictionary<Faction, FactionAgentKnowledge> _agents;

	public int SeatNumber { get; }

	public FactionBeneficiaryKnowledge Beneficiary { get; }

	public IReadOnlyDictionary<Faction, FactionAgentKnowledge> Agents => _agents;

	public SimulationPlayerFactionFacts(
		int seatNumber,
		FactionBeneficiaryKnowledge beneficiary,
		IReadOnlyDictionary<Faction, FactionAgentKnowledge> agents)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seatNumber);
		ArgumentNullException.ThrowIfNull(beneficiary);
		ArgumentNullException.ThrowIfNull(agents);
		if (!beneficiary.IsKnown || beneficiary.Faction is not { } beneficiaryFaction ||
		    !Enum.IsDefined(beneficiaryFaction))
		{
			throw new ArgumentException(
				"Simulation Faction facts require a known Beneficiary.",
				nameof(beneficiary));
		}

		var factions = Enum.GetValues<Faction>();
		if (agents.Count != factions.Length ||
		    agents.Keys.Any(faction => !Enum.IsDefined(faction)) ||
		    factions.Any(faction => !agents.ContainsKey(faction)) ||
		    agents.Values.Any(knowledge =>
			    !Enum.IsDefined(knowledge) ||
			    knowledge == FactionAgentKnowledge.Unknown))
		{
			throw new ArgumentException(
				"Simulation Faction facts require known Agent membership for every Faction.",
				nameof(agents));
		}

		SeatNumber = seatNumber;
		Beneficiary = beneficiary;
		_agents = new ReadOnlyDictionary<Faction, FactionAgentKnowledge>(
			agents.ToDictionary(pair => pair.Key, pair => pair.Value));
	}

	public FactionAgentKnowledge GetAgentKnowledge(Faction faction)
	{
		if (!Enum.IsDefined(faction))
		{
			throw new ArgumentOutOfRangeException(nameof(faction));
		}

		return _agents[faction];
	}

	public bool Equals(SimulationPlayerFactionFacts? other) =>
		other is not null &&
		SeatNumber == other.SeatNumber &&
		Beneficiary.Equals(other.Beneficiary) &&
		Enum.GetValues<Faction>().All(faction =>
			GetAgentKnowledge(faction) == other.GetAgentKnowledge(faction));

	public override bool Equals(object? obj) =>
		obj is SimulationPlayerFactionFacts other && Equals(other);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(SeatNumber);
		hash.Add(Beneficiary);
		foreach (var faction in Enum.GetValues<Faction>())
		{
			hash.Add(faction);
			hash.Add(GetAgentKnowledge(faction));
		}

		return hash.ToHashCode();
	}
}

public sealed class SimulationStartState : IEquatable<SimulationStartState>
{
	private readonly SimulationPlayerRoleAssignment[] _roleAssignments;
	private readonly SimulationPlayerFactionFacts[] _factionFacts;

	public SimulationCompatibilityIdentity CompatibilityIdentity { get; }

	public CanonicalSimulationScenario CanonicalScenario => CompatibilityIdentity.Scenario;

	public int PlayerCount => CanonicalScenario.PlayerCount;

	public IReadOnlyList<SimulationPlayerRoleAssignment> RoleAssignments { get; }

	public IReadOnlyList<SimulationPlayerFactionFacts> FactionFacts { get; }

	internal SimulationStartState(
		SimulationCompatibilityIdentity compatibilityIdentity,
		SimulationPlayerRoleAssignment[] roleAssignments,
		SimulationPlayerFactionFacts[] factionFacts)
	{
		ArgumentNullException.ThrowIfNull(compatibilityIdentity);
		ArgumentNullException.ThrowIfNull(roleAssignments);
		ArgumentNullException.ThrowIfNull(factionFacts);
		CompatibilityIdentity = compatibilityIdentity;
		_roleAssignments = roleAssignments.ToArray();
		if (_roleAssignments.Length != PlayerCount
			|| !_roleAssignments.Select(assignment => assignment.SeatNumber)
				.SequenceEqual(Enumerable.Range(1, PlayerCount))
			|| !CanonicalRoleComposition.Create(
					_roleAssignments.Select(assignment => assignment.Role))
				.Equals(CanonicalScenario.RoleComposition))
		{
			throw new ArgumentException(
				"Simulation Start State must assign every Role Composition card to exactly one Player seat.",
				nameof(roleAssignments));
		}

		_factionFacts = factionFacts.ToArray();
		if (_factionFacts.Length != PlayerCount ||
		    !_factionFacts.Select(facts => facts.SeatNumber)
			    .SequenceEqual(Enumerable.Range(1, PlayerCount)))
		{
			throw new ArgumentException(
				"Simulation Start State must provide complete Faction facts for every Player seat.",
				nameof(factionFacts));
		}

		RoleAssignments = Array.AsReadOnly(_roleAssignments);
		FactionFacts = Array.AsReadOnly(_factionFacts);
	}

	public bool Equals(SimulationStartState? other) =>
		other is not null
		&& CompatibilityIdentity.Equals(other.CompatibilityIdentity)
		&& _roleAssignments.SequenceEqual(other._roleAssignments)
		&& _factionFacts.SequenceEqual(other._factionFacts);

	public override bool Equals(object? obj) =>
		obj is SimulationStartState other && Equals(other);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(CompatibilityIdentity);
		foreach (var assignment in _roleAssignments)
		{
			hash.Add(assignment);
		}
		foreach (var facts in _factionFacts)
		{
			hash.Add(facts);
		}

		return hash.ToHashCode();
	}
}
