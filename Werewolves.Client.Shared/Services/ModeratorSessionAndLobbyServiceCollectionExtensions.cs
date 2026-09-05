using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Services;

namespace Werewolves.Client.Services;

public static class ModeratorSessionAndLobbyServiceCollectionExtensions
{
	public static IServiceCollection AddModeratorSessionAndLobbyServices(
		this IServiceCollection services,
		ServiceLifetime serviceLifetime,
		Func<LobbySetupMetadata, LobbySetupState> lobbyStateFactory)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(lobbyStateFactory);

		services.TryAdd(new ServiceDescriptor(
			typeof(GameService),
			typeof(GameService),
			serviceLifetime));
		services.TryAdd(new ServiceDescriptor(
			typeof(LobbySetupMetadata),
			provider => provider.GetRequiredService<GameService>()
				.GetLobbySetupMetadata(),
			serviceLifetime));
		services.TryAdd(new ServiceDescriptor(
			typeof(LobbySetupState),
			provider => lobbyStateFactory(
				provider.GetRequiredService<LobbySetupMetadata>()),
			serviceLifetime));
		services.TryAdd(new ServiceDescriptor(
			typeof(LobbyEvaluationCoordinator),
			provider => new LobbyEvaluationCoordinator(
				provider.GetRequiredService<LobbySetupState>(),
				provider.GetRequiredService<ILocalTerminalLobbyCacheStore>(),
				provider.GetRequiredService<ILobbyTerminalEvaluator>(),
				provider.GetRequiredService<LobbyEvaluationSettings>(),
				provider.GetRequiredService<TimeProvider>()),
			serviceLifetime));
		services.TryAdd(new ServiceDescriptor(
			typeof(GameClientManager),
			provider => new GameClientManager(
				provider.GetRequiredService<GameService>(),
				provider.GetRequiredService<IInstructionAudioPlayback>(),
				provider.GetRequiredService<IGameSessionSaveStore>(),
				provider.GetRequiredService<TimeProvider>(),
				provider.GetRequiredService<LobbySetupState>(),
				provider.GetRequiredService<IRecentSetupStore>(),
				provider.GetRequiredService<LobbyEvaluationCoordinator>()),
			serviceLifetime));

		return services;
	}
}
