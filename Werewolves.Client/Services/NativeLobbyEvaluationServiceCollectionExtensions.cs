using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Werewolves.Client.Services;

public static class NativeLobbyEvaluationServiceCollectionExtensions
{
	public static IServiceCollection AddNativeLobbyEvaluationServices(
		this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);
		services.TryAddSingleton<ITerminalLobbyCacheByteSource, MauiTerminalLobbyCacheByteSource>();
		services.TryAddSingleton<ILocalTerminalLobbyCacheStore>(
			FileTerminalLobbyCacheStore.CreateDefault());
		services.TryAddSingleton<ILobbyTerminalEvaluator, AsyncTerminalLobbyEvaluator>();
		services.TryAddSingleton<LobbyEvaluationCoordinator>();
		return services;
	}
}
