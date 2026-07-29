using Microsoft.AspNetCore.Components;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.BrowserQaHost;

public sealed class BrowserQaScenarioTerminalLobbyCacheStore : ILocalTerminalLobbyCacheStore
{
	private static readonly ReadOnlyMemory<byte> DegenerateFixture = CreateDegenerateFixture();
	private readonly NavigationManager _navigation;
	private readonly InMemoryTerminalLobbyCacheStore _local = new();

	public BrowserQaScenarioTerminalLobbyCacheStore(NavigationManager navigation)
	{
		_navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
	}

	public async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (BrowserQaScenarioSelection.FromUri(_navigation.Uri) == BrowserQaScenario.Degenerate)
		{
			return DegenerateFixture.ToArray();
		}

		var local = await _local.ReadAsync(cancellationToken);
		return local is { IsEmpty: false } ? local : null;
	}

	public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
		ReadOnlyMemory<byte> bytes,
		CancellationToken cancellationToken = default) =>
		_local.StageWriteAsync(bytes, cancellationToken);

	private static ReadOnlyMemory<byte> CreateDegenerateFixture()
	{
		var scenario = new SimulationScenario(
			BrowserQaFixtures.DefaultPlayerNames.Count,
			BrowserQaFixtures.DefaultRoles);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
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
