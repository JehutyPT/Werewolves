using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class SimulatorTransportIdentityTests
{
	[Fact]
	public void BaselineTransportMigration_PreservesRoleProfileCacheAndCompatibilityIdentities()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.WildChild,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var compatibilityIdentity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorProfile.LegacyCore.Identity);
		var runSeedMaterial = new RunSeedMaterial(
			compatibilityIdentity,
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 0);

		BaselineRandomDecisionStrategy.Identity.Should().Be(
			new DecisionStrategyIdentity("baseline-random", "1-splitmix64"));
		SimulatorProfile.LegacyCore.Identity.Should().Be(
			new SimulatorProfileIdentity("core-simulator", "1"));
		SimulatorProfile.LegacyCore.SupportedRoles.Should().Equal(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager);
		TerminalLobbyCache.SchemaIdentifier.Should().Be("terminal-lobby-cache");
		TerminalLobbyCache.SchemaVersion.Should().Be(1);
		BuildTimeTerminalLobbyCacheGenerator.GeneratorIdentifier
			.Should().Be("terminal-lobby-cache-generator");
		BuildTimeTerminalLobbyCacheGenerator.GeneratorVersion.Should().Be("1");
		var compatibilityCatalog = TerminalLobbyScenarioCatalog.EnumerateLegacyCore();
		compatibilityCatalog.Should().HaveCount(1_664);
		compatibilityCatalog.Select(entry => entry.Identity).Should().OnlyHaveUniqueItems();
		runSeedMaterial.ToString().Should().Be(
			"profile=core-simulator@1|players=5|roles=[Seer=1,SimpleVillager=2,SimpleWerewolf=1,WildChild=1]|actor=[]|rules=[]|strategy=baseline-random@1-splitmix64|run=0");
	}
}
