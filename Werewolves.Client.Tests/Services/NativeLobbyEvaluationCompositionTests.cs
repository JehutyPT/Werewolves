using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class NativeLobbyEvaluationCompositionTests
{
	[Fact]
	public void Settings_RejectDepthNotDeclaredByCapability()
	{
		var capability = new SimulatorCapability(
			SimulatorCapability.FullProbability.Identity,
			[
				(MainRoleType.SimpleVillager, Faction.Villager, [])
			],
			supportedEvaluationDepths:
			[
				LobbyEvaluationDepth.DegenerateScreeningOnly
			]);
		var act = () => new LobbyEvaluationSettings(
			capability,
			LobbyEvaluationDepth.FullProbability);

		act.Should().Throw<ArgumentException>()
			.WithParameterName("depth");
	}

	[Fact]
	public void NativeEvaluationComposition_DoesNotRegisterCommonCoordinator()
	{
		var services = new ServiceCollection();
		var gameService = new GameService();
		services.AddSingleton(CreateSupportedLobby(
			gameService.GetLobbySetupMetadata()));
		services.AddNativeLobbyEvaluationServices();
		using var provider = services.BuildServiceProvider(
			new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		var act = () => provider.GetRequiredService<LobbyEvaluationCoordinator>();

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public async Task ProductionComposition_ResolvesCoordinatorAndNativeAdapters()
	{
		var services = new ServiceCollection();
		services.AddSingleton<IInstructionAudioPlayback>(
			DisabledInstructionAudioPlayback.Instance);
		services.AddSingleton<IGameSessionSaveStore>(
			DisabledGameSessionSaveStore.Instance);
		services.AddSingleton<IRecentSetupStore>(DisabledRecentSetupStore.Instance);
		services.AddNativeLobbyEvaluationServices();
		services.AddModeratorSessionAndLobbyServices(
			ServiceLifetime.Singleton,
			CreateSupportedLobby);
		await using var provider = services.BuildServiceProvider(
			new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		provider.GetRequiredService<ILocalTerminalLobbyCacheStore>()
			.Should().BeOfType<FileTerminalLobbyCacheStore>();
		var settings = provider.GetRequiredService<LobbyEvaluationSettings>();
		settings.Capability.Should().Be(SimulatorCapability.SafetyScreening);
		settings.Capability.HeadlessResponsePolicy.StrategyIdentity.Should().Be(
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity);
		settings.Depth.Should().Be(LobbyEvaluationDepth.DegenerateScreeningOnly);
		provider.GetRequiredService<ILobbyTerminalEvaluator>()
			.Should().BeOfType<AsyncTerminalLobbyEvaluator>();
		provider.GetRequiredService<GameClientManager>().Should().NotBeNull();
		var coordinator = provider.GetRequiredService<LobbyEvaluationCoordinator>();
		coordinator.Capability.Should().Be(settings.Capability);
		coordinator.Depth.Should().Be(settings.Depth);
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);

		coordinator.Dispose();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
	}

	private static LobbySetupState CreateSupportedLobby(LobbySetupMetadata metadata)
	{
		var lobby = new LobbySetupState(metadata);
		for (var index = 0; index < 5; index++)
		{
			lobby.AddPlayer($"Player {index + 1}");
		}
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		return lobby;
	}
}
