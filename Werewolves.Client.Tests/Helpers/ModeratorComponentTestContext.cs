using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Client.Tests.Helpers;

public sealed class ModeratorComponentTestContext : BunitContext
{
	public static readonly CultureInfo PortugueseCulture = CultureInfo.GetCultureInfo("pt-PT");

	public ModeratorComponentTestContext()
	{
		CultureInfo.DefaultThreadCurrentCulture = PortugueseCulture;
		CultureInfo.DefaultThreadCurrentUICulture = PortugueseCulture;
		ClientStrings.Culture = PortugueseCulture;

		Services.AddSingleton<IHapticFeedbackService, NoOpHapticFeedbackService>();
		Services.AddSingleton<IInstructionAudioPlayback, NoOpInstructionAudioPlayback>();
		Services.AddSingleton<IGameSessionSaveStore, InMemoryGameSessionSaveStore>();
		Services.AddSingleton<IRecentSetupStore, InMemoryRecentSetupStore>();
		Services.AddSingleton<IScreenWakeLock, NoOpScreenWakeLock>();
		Services.AddSingleton<GameplayWakeLockController>();
		Services.AddSingleton<GameService>();
		Services.AddSingleton<LobbySetupMetadata>(sp =>
			sp.GetRequiredService<GameService>().GetLobbySetupMetadata());
		Services.AddSingleton<LobbySetupState>();
		Services.AddSingleton<ILocalTerminalLobbyCacheStore, InMemoryTerminalLobbyCacheStore>();
		Services.AddSingleton<ILobbyTerminalEvaluator>(_ => DisabledLobbyTerminalEvaluator.Instance);
		Services.AddSingleton(TimeProvider.System);
		Services.AddSingleton(sp => new LobbyEvaluationCoordinator(
			sp.GetRequiredService<LobbySetupState>(),
			sp.GetRequiredService<ILocalTerminalLobbyCacheStore>(),
			sp.GetRequiredService<ILobbyTerminalEvaluator>(),
			new LobbyEvaluationSettings(
				SimulatorCapability.FullProbability,
				LobbyEvaluationDepth.FullProbability),
			sp.GetRequiredService<TimeProvider>()));
		Services.AddSingleton<GameClientManager>();
	}

	public IRenderedComponent<TComponent> RenderModeratorComponent<TComponent>(
		Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null)
		where TComponent : IComponent =>
		parameters is null
			? Render<TComponent>()
			: Render(parameters);

	private sealed class NoOpHapticFeedbackService : IHapticFeedbackService
	{
		public void Click()
		{
		}

		public void LongPress()
		{
		}
	}

	private sealed class NoOpInstructionAudioPlayback : IInstructionAudioPlayback
	{
		public bool IsMuted => false;

		public Task ReconcileAsync(ModeratorInstruction? instruction, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task SetMutedAsync(
			bool isMuted,
			ModeratorInstruction? instruction,
			CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	private sealed class InMemoryGameSessionSaveStore : IGameSessionSaveStore
	{
		private string? _serializedSession;

		public string? Load() => _serializedSession;

		public void Save(string serializedSession) => _serializedSession = serializedSession;

		public void Clear() => _serializedSession = null;
	}

	private sealed class NoOpScreenWakeLock : IScreenWakeLock
	{
		public bool KeepScreenOn { get; set; }
	}
}
