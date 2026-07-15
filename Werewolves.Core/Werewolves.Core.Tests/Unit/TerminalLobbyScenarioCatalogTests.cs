using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public sealed class TerminalLobbyScenarioCatalogTests
{
	[Fact]
	public void EnumerateCurrentProfile_ReturnsCompleteSupportedIdentitySet()
	{
		var entries = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile();

		entries.Should().HaveCount(1_664);
		entries.Select(entry => entry.Identity).Should().OnlyHaveUniqueItems();
		entries.Select(entry => entry.Identity.ToString()).Should().BeInAscendingOrder(
			StringComparer.Ordinal);
		entries.Count(entry => entry.IsAlreadyDecided).Should().Be(832);
		entries.Count(entry => !entry.IsAlreadyDecided).Should().Be(832);

		foreach (var entry in entries)
		{
			var classification = SimulationScenarioClassifier.Classify(entry.Scenario);
			classification.RulesValidity.IsValid.Should().BeTrue();
			classification.AppSupport.Should().Match<AppSupportResult>(value => value.IsSupported);
			classification.SimulatorSupport.Should().Match<SimulatorSupportResult>(value => value.IsSupported);
			entry.Identity.Should().Be(new SimulationCompatibilityIdentity(
				entry.Scenario.ToCanonical(),
				SimulatorProfile.Active.Identity));
			entry.IsAlreadyDecided.Should().Be(
				classification.AlreadyDecided!.IsAlreadyDecided);
		}

		entries.Should().Contain(entry =>
			entry.Scenario.PlayerCount == 5
			&& entry.Scenario.RoleCompositionCards.Count(role => role == MainRoleType.SimpleWerewolf) == 1
			&& entry.Scenario.RoleCompositionCards.Count(role => role == MainRoleType.SimpleVillager) == 4);
		entries.Should().Contain(entry =>
			entry.Scenario.PlayerCount == 30
			&& entry.Scenario.RoleCompositionCards.Count(role => role == MainRoleType.Seer) == 1
			&& entry.Scenario.RoleCompositionCards.Count(role => role == MainRoleType.WildChild) == 1);
	}
}
