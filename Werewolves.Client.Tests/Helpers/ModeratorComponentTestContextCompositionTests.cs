using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Xunit;

namespace Werewolves.Client.Tests.Helpers;

public sealed class ModeratorComponentTestContextCompositionTests
{
	[Fact]
	public void CommonGraph_IsSingletonWithinContextBlankAndIsolatedAcrossContexts()
	{
		using var firstContext = new ModeratorComponentTestContext();
		using var secondContext = new ModeratorComponentTestContext();

		var first = ResolveCommonGraph(firstContext.Services);
		var firstAgain = ResolveCommonGraph(firstContext.Services);
		var second = ResolveCommonGraph(secondContext.Services);

		first.GameService.Should().BeSameAs(firstAgain.GameService);
		first.Metadata.Should().BeSameAs(firstAgain.Metadata);
		first.State.Should().BeSameAs(firstAgain.State);
		first.Coordinator.Should().BeSameAs(firstAgain.Coordinator);
		first.Manager.Should().BeSameAs(firstAgain.Manager);
		first.GameService.Should().NotBeSameAs(second.GameService);
		first.Metadata.Should().NotBeSameAs(second.Metadata);
		first.State.Should().NotBeSameAs(second.State);
		first.Coordinator.Should().NotBeSameAs(second.Coordinator);
		first.Manager.Should().NotBeSameAs(second.Manager);
		first.State.PlayerNames.Should().BeEmpty();
		second.State.PlayerNames.Should().BeEmpty();
		firstContext.Services.GetRequiredService<LobbyEvaluationSettings>().Should().BeEquivalentTo(
			new LobbyEvaluationSettings(
				SimulatorCapability.FullProbability,
				LobbyEvaluationDepth.FullProbability));
		firstContext.Services.GetRequiredService<ILobbyTerminalEvaluator>()
			.Should().BeSameAs(DisabledLobbyTerminalEvaluator.Instance);
		firstContext.Services.GetRequiredService<ILocalTerminalLobbyCacheStore>()
			.Should().BeOfType<InMemoryTerminalLobbyCacheStore>();
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
