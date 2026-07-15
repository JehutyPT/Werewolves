using FluentAssertions;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class VersionedSimulationIdentityTests
{
	[Fact]
	public void DecisionStrategyIdentity_RoundTripsWithStructuralValueSemanticsAndReferenceOperators()
	{
		var first = new DecisionStrategyIdentity("baseline-random", "1-splitmix64");

		var parsed = DecisionStrategyIdentity.Parse(first.ToString());

		parsed.StrategyId.Should().Be("baseline-random");
		parsed.Version.Should().Be("1-splitmix64");
		parsed.Should().Be(first);
		parsed.GetHashCode().Should().Be(first.GetHashCode());
		(parsed == first).Should().BeFalse();
	}

	[Fact]
	public void SimulatorProfileIdentity_RoundTripsWithStructuralValueSemanticsAndValueOperators()
	{
		var first = new SimulatorProfileIdentity("core-simulator", "1");

		var parsed = SimulatorProfileIdentity.Parse(first.ToString());

		parsed.ProfileId.Should().Be("core-simulator");
		parsed.Version.Should().Be("1");
		parsed.Should().Be(first);
		parsed.GetHashCode().Should().Be(first.GetHashCode());
		(parsed == first).Should().BeTrue();
		(parsed != first).Should().BeFalse();
	}

	[Fact]
	public void VersionedSimulationIdentities_WithInvalidSerializedSyntax_RejectWithoutPartialValues()
	{
		string?[] invalidValues = [null, "", "@1", "identity@", "identity@1@extra", "invalid value@1"];

		foreach (var value in invalidValues)
		{
			DecisionStrategyIdentity.TryParse(value, out var strategy).Should().BeFalse();
			strategy.Should().BeNull();
			SimulatorProfileIdentity.TryParse(value, out var profile).Should().BeFalse();
			profile.Should().BeNull();
		}

		Action parseStrategy = () => DecisionStrategyIdentity.Parse("identity@");
		Action parseProfile = () => SimulatorProfileIdentity.Parse("identity@");
		parseStrategy.Should().Throw<FormatException>();
		parseProfile.Should().Throw<FormatException>();
	}

	[Fact]
	public void VersionedSimulationIdentities_WithInvalidParts_PreserveDomainParameterErrors()
	{
		Action invalidStrategyId = () => new DecisionStrategyIdentity("invalid value", "1");
		Action invalidStrategyVersion = () => new DecisionStrategyIdentity("strategy", "invalid value");
		Action invalidProfileId = () => new SimulatorProfileIdentity("invalid value", "1");
		Action invalidProfileVersion = () => new SimulatorProfileIdentity("profile", "invalid value");

		invalidStrategyId.Should().Throw<ArgumentException>()
			.Which.ParamName.Should().Be("strategyId");
		invalidStrategyVersion.Should().Throw<ArgumentException>()
			.Which.ParamName.Should().Be("version");
		invalidProfileId.Should().Throw<ArgumentException>()
			.Which.ParamName.Should().Be("profileId");
		invalidProfileVersion.Should().Throw<ArgumentException>()
			.Which.ParamName.Should().Be("version");
	}
}
