using Werewolves.Core.StateModels.Enums;

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

public sealed class SimulationStartState : IEquatable<SimulationStartState>
{
	private readonly SimulationPlayerRoleAssignment[] _roleAssignments;

	public SimulationCompatibilityIdentity CompatibilityIdentity { get; }

	public CanonicalSimulationScenario CanonicalScenario => CompatibilityIdentity.Scenario;

	public int PlayerCount => CanonicalScenario.PlayerCount;

	public IReadOnlyList<SimulationPlayerRoleAssignment> RoleAssignments { get; }

	internal SimulationStartState(
		SimulationCompatibilityIdentity compatibilityIdentity,
		SimulationPlayerRoleAssignment[] roleAssignments)
	{
		ArgumentNullException.ThrowIfNull(compatibilityIdentity);
		ArgumentNullException.ThrowIfNull(roleAssignments);
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

		RoleAssignments = Array.AsReadOnly(_roleAssignments);
	}

	public bool Equals(SimulationStartState? other) =>
		other is not null
		&& CompatibilityIdentity.Equals(other.CompatibilityIdentity)
		&& _roleAssignments.SequenceEqual(other._roleAssignments);

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

		return hash.ToHashCode();
	}
}
