using Microsoft.Extensions.DependencyInjection.Extensions;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Client.BrowserQaHost;

public static class BrowserQaHostServiceCollectionExtensions
{
	public static IServiceCollection AddBrowserQaHostModeratorServices(this IServiceCollection services)
	{
		services.TryAddScoped<GameService>();
		services.TryAddScoped<LobbySetupMetadata>(sp =>
			sp.GetRequiredService<GameService>().GetLobbySetupMetadata());
		services.TryAddScoped<LobbySetupState>(sp =>
			CreateSeededLobby(sp.GetRequiredService<LobbySetupMetadata>()));
		services.TryAddScoped<IAudioMap, AudioMap>();
		services.TryAddScoped<IAudioAssetLoader, BrowserSafeAudioAssetLoader>();
		services.TryAddScoped<IAudioPlayerFactory, BrowserSafeAudioPlayerFactory>();
		services.TryAddScoped<IInstructionAudioPlayback, BrowserSafeInstructionAudioPlayback>();
		services.TryAddScoped<IHapticFeedbackService, BrowserSafeHapticFeedbackService>();
		services.TryAddScoped<IScreenWakeLock, BrowserSafeScreenWakeLock>();
		services.TryAddScoped<IGameSessionSaveStore, BrowserQaInMemoryGameSessionSaveStore>();
		services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);
		services.TryAddScoped<GameClientManager>();
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
