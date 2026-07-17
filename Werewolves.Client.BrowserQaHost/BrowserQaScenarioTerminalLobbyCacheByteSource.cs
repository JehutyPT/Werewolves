using Microsoft.AspNetCore.Components;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.BrowserQaHost;

public sealed class BrowserQaScenarioTerminalLobbyCacheByteSource(
	NavigationManager navigation) : ITerminalLobbyCacheByteSource
{
	private static readonly ReadOnlyMemory<byte> ProbabilityFixture = CreateProbabilityFixture();
	private static readonly ReadOnlyMemory<byte> DegenerateFixture = CreateDegenerateFixture();

	public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
		string logicalName,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (logicalName != LobbyEvaluationCoordinator.BundledCacheLogicalName)
		{
			return ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);
		}

		var fixture = BrowserQaScenarioSelection.FromUri(navigation.Uri) switch
		{
			BrowserQaScenario.Probability => ProbabilityFixture,
			BrowserQaScenario.Degenerate => DegenerateFixture,
			_ => (ReadOnlyMemory<byte>?)null
		};
		return ValueTask.FromResult<ReadOnlyMemory<byte>?>(
			fixture);
	}

	private static ReadOnlyMemory<byte> CreateProbabilityFixture()
	{
		var scenario = new SimulationScenario(
			BrowserQaFixtures.DefaultPlayerNames.Count,
			BrowserQaFixtures.DefaultRoles);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorProfile.Active.Identity);
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var record = new ProbabilityTerminalCacheRecord(
			identity,
			[
				new(villager, 7_000, 10_000),
				new(werewolf, 3_000, 10_000),
				new(noWinner, 0, 10_000)
			],
			[
				new(villager, 1, VictoryCheckWindow.Dawn, 2_000, 10_000),
				new(villager, 1, VictoryCheckWindow.PreNight, 1_500, 10_000),
				new(villager, 2, VictoryCheckWindow.Dawn, 1_500, 10_000),
				new(villager, 2, VictoryCheckWindow.PreNight, 1_000, 10_000),
				new(villager, 3, VictoryCheckWindow.Dawn, 1_000, 10_000),
				new(werewolf, 1, VictoryCheckWindow.PreNight, 250, 10_000),
				new(werewolf, 2, VictoryCheckWindow.Dawn, 750, 10_000),
				new(werewolf, 2, VictoryCheckWindow.PreNight, 1_000, 10_000),
				new(werewolf, 3, VictoryCheckWindow.Dawn, 500, 10_000),
				new(werewolf, 4, VictoryCheckWindow.PreNight, 500, 10_000)
			]);
		return TerminalLobbyCache.Write(TerminalLobbyCache.CreateDocument([record]));
	}

	private static ReadOnlyMemory<byte> CreateDegenerateFixture()
	{
		var scenario = new SimulationScenario(
			BrowserQaFixtures.DefaultPlayerNames.Count,
			BrowserQaFixtures.DefaultRoles);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorProfile.Active.Identity);
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var record = new DegenerateTerminalCacheRecord(
			identity,
			[
				new(villager, 750, 1_000),
				new(werewolf, 250, 1_000),
				new(new NoWinnerGameResult(), 0, 1_000)
			],
			[
				new(villager, 1, VictoryCheckWindow.Dawn, 750, 1_000),
				new(werewolf, 1, VictoryCheckWindow.PreNight, 250, 1_000)
			]);
		return TerminalLobbyCache.Write(TerminalLobbyCache.CreateDocument([record]));
	}
}
