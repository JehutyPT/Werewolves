using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class NativeLobbyEvaluationCompositionTests
{
	[Fact]
	public async Task ProductionComposition_ResolvesCoordinatorAndNativeAdapters()
	{
		var services = new ServiceCollection();
		services.AddSingleton(CreateSupportedLobby());
		services.AddNativeLobbyEvaluationServices();
		await using var provider = services.BuildServiceProvider(
			new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		provider.GetRequiredService<ITerminalLobbyCacheByteSource>()
			.Should().BeOfType<MauiTerminalLobbyCacheByteSource>();
		provider.GetRequiredService<ILocalTerminalLobbyCacheStore>()
			.Should().BeOfType<FileTerminalLobbyCacheStore>();
		provider.GetRequiredService<ILobbyTerminalEvaluator>()
			.Should().BeOfType<AsyncTerminalLobbyEvaluator>();
		var coordinator = provider.GetRequiredService<LobbyEvaluationCoordinator>();
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
