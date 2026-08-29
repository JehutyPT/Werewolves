using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Werewolves.Core.GameLogic.Simulation;

namespace Werewolves.Client.Services;

public static class NativeLobbyEvaluationServiceCollectionExtensions
{
	public static IServiceCollection AddNativeLobbyEvaluationServices(
		this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.TryAddSingleton(
			new LobbyEvaluationSettings(
				SimulatorCapabilityRegistry.Production.SafetyScreening,
				LobbyEvaluationDepth.DegenerateScreeningOnly));
		services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);
		services.TryAddSingleton<ILocalTerminalLobbyCacheStore>(
			FileTerminalLobbyCacheStore.CreateDefault());
		services.TryAddSingleton<ILobbyTerminalEvaluator>(provider =>
			new AsyncTerminalLobbyEvaluator(provider.GetRequiredService<TimeProvider>()));
		services.TryAddSingleton(provider =>
			new LobbyEvaluationCoordinator(
				provider.GetRequiredService<LobbySetupState>(),
				provider.GetRequiredService<ILocalTerminalLobbyCacheStore>(),
				provider.GetRequiredService<ILobbyTerminalEvaluator>(),
				provider.GetRequiredService<LobbyEvaluationSettings>(),
				provider.GetRequiredService<TimeProvider>()));
		return services;
	}
}
