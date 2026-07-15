namespace Werewolves.Core.StateModels.Models.Simulation;

public sealed class SimulationCompatibilityIdentity : IEquatable<SimulationCompatibilityIdentity>
{
	public CanonicalSimulationScenario Scenario { get; }

	public SimulatorProfileIdentity Profile { get; }

	public SimulationCompatibilityIdentity(
		CanonicalSimulationScenario scenario,
		SimulatorProfileIdentity profile)
	{
		Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
		Profile = profile ?? throw new ArgumentNullException(nameof(profile));
	}

	public static SimulationCompatibilityIdentity Parse(string value)
	{
		if (!TryParse(value, out var identity))
		{
			throw new FormatException("The value is not a simulation compatibility identity.");
		}

		return identity;
	}

	public static bool TryParse(
		string? value,
		out SimulationCompatibilityIdentity identity)
	{
		identity = null!;
		const string profilePrefix = "profile=";
		const string scenarioPrefix = "|players=";
		if (value is null || !value.StartsWith(profilePrefix, StringComparison.Ordinal))
		{
			return false;
		}

		var scenarioIndex = value.IndexOf(scenarioPrefix, StringComparison.Ordinal);
		if (scenarioIndex <= profilePrefix.Length)
		{
			return false;
		}

		if (!SimulatorProfileIdentity.TryParse(
				value[profilePrefix.Length..scenarioIndex],
				out var profile)
			|| !CanonicalSimulationScenario.TryParse(
				value[(scenarioIndex + 1)..],
				out var scenario))
		{
			return false;
		}

		identity = new SimulationCompatibilityIdentity(scenario, profile);
		return string.Equals(value, identity.ToString(), StringComparison.Ordinal);
	}

	public override string ToString() => $"profile={Profile}|{Scenario}";

	public bool Equals(SimulationCompatibilityIdentity? other) =>
		other is not null
		&& Scenario.Equals(other.Scenario)
		&& Profile.Equals(other.Profile);

	public override bool Equals(object? obj) =>
		obj is SimulationCompatibilityIdentity other && Equals(other);

	public override int GetHashCode() => HashCode.Combine(Scenario, Profile);

	public static bool operator ==(
		SimulationCompatibilityIdentity? left,
		SimulationCompatibilityIdentity? right) => Equals(left, right);

	public static bool operator !=(
		SimulationCompatibilityIdentity? left,
		SimulationCompatibilityIdentity? right) => !Equals(left, right);
}
