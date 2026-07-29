using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class NativeLobbyEvaluationCompositionTests
{
	[Fact]
	public void Settings_RejectSafetyCapabilityWithFullProbabilityDepth()
	{
		var act = () => new LobbyEvaluationSettings(
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.FullProbability);

		act.Should().Throw<ArgumentException>()
			.WithParameterName("depth");
	}

	[Fact]
	public async Task ProductionComposition_ResolvesCoordinatorAndNativeAdapters()
	{
		var services = new ServiceCollection();
		services.AddSingleton(CreateSupportedLobby());
		services.AddNativeLobbyEvaluationServices();
		await using var provider = services.BuildServiceProvider(
			new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		provider.GetRequiredService<ILocalTerminalLobbyCacheStore>()
			.Should().BeOfType<FileTerminalLobbyCacheStore>();
		var settings = provider.GetRequiredService<LobbyEvaluationSettings>();
		settings.Capability.Should().Be(SimulatorCapability.SafetyScreening);
		settings.Depth.Should().Be(LobbyEvaluationDepth.DegenerateScreeningOnly);
		provider.GetRequiredService<ILobbyTerminalEvaluator>()
			.Should().BeOfType<AsyncTerminalLobbyEvaluator>();
		var coordinator = provider.GetRequiredService<LobbyEvaluationCoordinator>();
		coordinator.Capability.Should().Be(settings.Capability);
		coordinator.Depth.Should().Be(settings.Depth);
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);

		coordinator.Dispose();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
	}

	private static LobbySetupState CreateSupportedLobby()
	{
		var lobby = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager);
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
