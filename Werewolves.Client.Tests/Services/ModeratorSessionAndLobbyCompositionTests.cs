using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public sealed class ModeratorSessionAndLobbyCompositionTests
{
	[Fact]
	public void AddServices_NullLobbyStateFactoryIsRejectedImmediately()
	{
		var services = new ServiceCollection();

		var act = () => services.AddModeratorSessionAndLobbyServices(
			ServiceLifetime.Singleton,
			lobbyStateFactory: null!);

		act.Should().Throw<ArgumentNullException>()
			.WithParameterName("lobbyStateFactory");
	}

	[Fact]
	public void AddServices_ResolvesCompleteSingletonGraphFromExplicitDependencies()
	{
		var services = new ServiceCollection();
		var audio = DisabledInstructionAudioPlayback.Instance;
		var saveStore = DisabledGameSessionSaveStore.Instance;
		var recentSetupStore = DisabledRecentSetupStore.Instance;
		var clock = TimeProvider.System;
		var cacheStore = new InMemoryTerminalLobbyCacheStore();
		var evaluator = DisabledLobbyTerminalEvaluator.Instance;
		var settings = new LobbyEvaluationSettings(
			SimulatorCapability.FullProbability,
			LobbyEvaluationDepth.FullProbability);
		LobbySetupMetadata? receivedMetadata = null;
		LobbySetupState? createdState = null;
		services.AddSingleton<IInstructionAudioPlayback>(audio);
		services.AddSingleton<IGameSessionSaveStore>(saveStore);
		services.AddSingleton<IRecentSetupStore>(recentSetupStore);
		services.AddSingleton(clock);
		services.AddSingleton<ILocalTerminalLobbyCacheStore>(cacheStore);
		services.AddSingleton<ILobbyTerminalEvaluator>(evaluator);
		services.AddSingleton(settings);
		services.AddModeratorSessionAndLobbyServices(
			ServiceLifetime.Singleton,
			metadata =>
			{
				receivedMetadata = metadata;
				return createdState = new LobbySetupState(metadata);
			});
		using var provider = services.BuildServiceProvider(
			new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		var manager = provider.GetRequiredService<GameClientManager>();
		var coordinator = provider.GetRequiredService<LobbyEvaluationCoordinator>();
		var gameService = provider.GetRequiredService<GameService>();
		var metadata = provider.GetRequiredService<LobbySetupMetadata>();
		var state = provider.GetRequiredService<LobbySetupState>();

		manager.Should().BeSameAs(provider.GetRequiredService<GameClientManager>());
		coordinator.Should().BeSameAs(provider.GetRequiredService<LobbyEvaluationCoordinator>());
		gameService.Should().BeSameAs(provider.GetRequiredService<GameService>());
		metadata.Should().BeSameAs(provider.GetRequiredService<LobbySetupMetadata>());
		state.Should().BeSameAs(provider.GetRequiredService<LobbySetupState>());
		receivedMetadata.Should().BeSameAs(metadata);
		createdState.Should().BeSameAs(state);
		provider.GetRequiredService<IInstructionAudioPlayback>().Should().BeSameAs(audio);
		provider.GetRequiredService<IGameSessionSaveStore>().Should().BeSameAs(saveStore);
		provider.GetRequiredService<IRecentSetupStore>().Should().BeSameAs(recentSetupStore);
		provider.GetRequiredService<TimeProvider>().Should().BeSameAs(clock);
		provider.GetRequiredService<ILocalTerminalLobbyCacheStore>().Should().BeSameAs(cacheStore);
		provider.GetRequiredService<ILobbyTerminalEvaluator>().Should().BeSameAs(evaluator);
		coordinator.Capability.Should().Be(settings.Capability);
		coordinator.Depth.Should().Be(settings.Depth);
	}

	[Fact]
	public void AddServices_ScopedGraphIsSharedWithinScopeAndIsolatedAcrossScopes()
	{
		var services = CreateExternalServices();
		var createdStates = new List<(LobbySetupMetadata Metadata, LobbySetupState State)>();
		services.AddModeratorSessionAndLobbyServices(
			ServiceLifetime.Scoped,
			metadata =>
			{
				var state = new LobbySetupState(metadata);
				createdStates.Add((metadata, state));
				return state;
			});
		using var provider = services.BuildServiceProvider(
			new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
		using var firstScope = provider.CreateScope();
		using var secondScope = provider.CreateScope();

		var firstGraph = ResolveCommonGraph(firstScope.ServiceProvider);
		var firstGraphAgain = ResolveCommonGraph(firstScope.ServiceProvider);
		var secondGraph = ResolveCommonGraph(secondScope.ServiceProvider);

		firstGraph.GameService.Should().BeSameAs(firstGraphAgain.GameService);
		firstGraph.Metadata.Should().BeSameAs(firstGraphAgain.Metadata);
		firstGraph.State.Should().BeSameAs(firstGraphAgain.State);
		firstGraph.Coordinator.Should().BeSameAs(firstGraphAgain.Coordinator);
		firstGraph.Manager.Should().BeSameAs(firstGraphAgain.Manager);
		firstGraph.GameService.Should().NotBeSameAs(secondGraph.GameService);
		firstGraph.Metadata.Should().NotBeSameAs(secondGraph.Metadata);
		firstGraph.State.Should().NotBeSameAs(secondGraph.State);
		firstGraph.Coordinator.Should().NotBeSameAs(secondGraph.Coordinator);
		firstGraph.Manager.Should().NotBeSameAs(secondGraph.Manager);
		createdStates.Should().HaveCount(2);
		createdStates[0].Metadata.Should().BeSameAs(firstGraph.Metadata);
		createdStates[0].State.Should().BeSameAs(firstGraph.State);
		createdStates[1].Metadata.Should().BeSameAs(secondGraph.Metadata);
		createdStates[1].State.Should().BeSameAs(secondGraph.State);
	}

	[Theory]
	[InlineData(typeof(IInstructionAudioPlayback), typeof(GameClientManager))]
	[InlineData(typeof(IGameSessionSaveStore), typeof(GameClientManager))]
	[InlineData(typeof(IRecentSetupStore), typeof(GameClientManager))]
	[InlineData(typeof(TimeProvider), typeof(GameClientManager))]
	[InlineData(typeof(ILocalTerminalLobbyCacheStore), typeof(LobbyEvaluationCoordinator))]
	[InlineData(typeof(ILobbyTerminalEvaluator), typeof(LobbyEvaluationCoordinator))]
	[InlineData(typeof(LobbyEvaluationSettings), typeof(LobbyEvaluationCoordinator))]
	[InlineData(typeof(TimeProvider), typeof(LobbyEvaluationCoordinator))]
	public void AddServices_MissingExternalRegistrationFailsWhenAffectedGraphResolves(
		Type omittedService,
		Type affectedGraph)
	{
		var services = CreateExternalServices(omittedService);
		services.AddModeratorSessionAndLobbyServices(
			ServiceLifetime.Singleton,
			metadata => new LobbySetupState(metadata));
		using var provider = services.BuildServiceProvider(
			new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		var act = () => provider.GetRequiredService(affectedGraph);

		act.Should().Throw<InvalidOperationException>();
	}

	private static ServiceCollection CreateExternalServices(Type? omittedService = null)
	{
		var services = new ServiceCollection();
		if (omittedService != typeof(IInstructionAudioPlayback))
		{
			services.AddSingleton<IInstructionAudioPlayback>(
				DisabledInstructionAudioPlayback.Instance);
		}
		if (omittedService != typeof(IGameSessionSaveStore))
		{
			services.AddSingleton<IGameSessionSaveStore>(
				DisabledGameSessionSaveStore.Instance);
		}
		if (omittedService != typeof(IRecentSetupStore))
		{
			services.AddSingleton<IRecentSetupStore>(DisabledRecentSetupStore.Instance);
		}
		if (omittedService != typeof(TimeProvider))
		{
			services.AddSingleton(TimeProvider.System);
		}
		if (omittedService != typeof(ILocalTerminalLobbyCacheStore))
		{
			services.AddSingleton<ILocalTerminalLobbyCacheStore>(
				new InMemoryTerminalLobbyCacheStore());
		}
		if (omittedService != typeof(ILobbyTerminalEvaluator))
		{
			services.AddSingleton<ILobbyTerminalEvaluator>(
				DisabledLobbyTerminalEvaluator.Instance);
		}
		if (omittedService != typeof(LobbyEvaluationSettings))
		{
			services.AddSingleton(new LobbyEvaluationSettings(
				SimulatorCapability.FullProbability,
				LobbyEvaluationDepth.FullProbability));
		}

		return services;
	}

	private static CommonGraph ResolveCommonGraph(IServiceProvider provider) => new(
		provider.GetRequiredService<GameService>(),
		provider.GetRequiredService<LobbySetupMetadata>(),
		provider.GetRequiredService<LobbySetupState>(),
		provider.GetRequiredService<LobbyEvaluationCoordinator>(),
		provider.GetRequiredService<GameClientManager>());

	private sealed record CommonGraph(
		GameService GameService,
		LobbySetupMetadata Metadata,
		LobbySetupState State,
		LobbyEvaluationCoordinator Coordinator,
		GameClientManager Manager);
}
