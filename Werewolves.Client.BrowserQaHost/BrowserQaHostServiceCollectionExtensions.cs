using Microsoft.Extensions.DependencyInjection.Extensions;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Client.BrowserQaHost;

public static class BrowserQaHostServiceCollectionExtensions
{
	public static IServiceCollection AddBrowserQaHostModeratorServices(this IServiceCollection services)
	{
		services.TryAddScoped<IAudioMap, AudioMap>();
		services.TryAddScoped<IAudioAssetLoader, BrowserSafeAudioAssetLoader>();
		services.TryAddScoped<IAudioPlayerFactory, BrowserSafeAudioPlayerFactory>();
		services.TryAddScoped<IInstructionAudioPlayback, BrowserSafeInstructionAudioPlayback>();
		services.TryAddScoped<IHapticFeedbackService, BrowserSafeHapticFeedbackService>();
		services.TryAddScoped<IScreenWakeLock, BrowserSafeScreenWakeLock>();
		services.TryAddScoped<IGameSessionSaveStore, BrowserQaInMemoryGameSessionSaveStore>();
		services.TryAddScoped<IRecentSetupStore, InMemoryRecentSetupStore>();
		services.TryAddSingleton(
			new LobbyEvaluationSettings(
				SimulatorCapability.SafetyScreening,
				LobbyEvaluationDepth.DegenerateScreeningOnly));
		services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);
		services.TryAddScoped<ILocalTerminalLobbyCacheStore, BrowserQaScenarioTerminalLobbyCacheStore>();
		services.TryAddScoped<ILobbyTerminalEvaluator, BrowserQaScreeningPassedLobbyTerminalEvaluator>();
		services.AddModeratorSessionAndLobbyServices(
			ServiceLifetime.Scoped,
			CreateSeededLobby);
		services.TryAddScoped<GameplayWakeLockController>();
		services.TryAddScoped<BenchmarkClientManager>();

		return services;
	}

	private static LobbySetupState CreateSeededLobby(LobbySetupMetadata metadata)
	{
		var lobby = new LobbySetupState(metadata);

		foreach (var playerName in BrowserQaFixtures.DefaultPlayerNames)
		{
			lobby.AddPlayer(playerName);
		}

		foreach (var role in BrowserQaFixtures.DefaultRoles)
		{
			lobby.IncrementRole(role);
		}

		return lobby;
	}
}

public static class BrowserQaFixtures
{
	public static IReadOnlyList<string> DefaultPlayerNames { get; } =
	[
		"Ana",
		"Bruno",
		"Catarina",
		"Diana",
		"Eduardo"
	];

	public static IReadOnlyList<MainRoleType> DefaultRoles { get; } =
	[
		MainRoleType.SimpleWerewolf,
		MainRoleType.SimpleWerewolf,
		MainRoleType.SimpleVillager,
		MainRoleType.SimpleVillager,
		MainRoleType.SimpleVillager
	];
}
