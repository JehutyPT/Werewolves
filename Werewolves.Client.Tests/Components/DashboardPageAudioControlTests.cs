using FluentAssertions;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Xunit;

#pragma warning disable BL0006

namespace Werewolves.Client.Tests.Components;

public class DashboardPageAudioControlTests
{
	[Fact]
	public async Task StatusBar_RendersAudioToggleAsOperableMuteControl()
	{
		using var dashboard = new DashboardRenderFixture();
		await dashboard.RenderAsync();

		var toggle = dashboard.FindAudioToggleButton();

		toggle.Attributes.Should().Contain("type", "button");
		toggle.Attributes.Should().Contain("class", "ww-icon-button ww-audio-toggle");
		toggle.Attributes.Should().Contain("aria-label", ClientStrings.Dashboard_AudioMute);
		toggle.Attributes.Should().Contain("title", ClientStrings.Dashboard_AudioMute);
		toggle.Attributes.Should().Contain("aria-pressed", "false");
		toggle.TextContent.Should().BeEmpty();
		toggle.ClickEventHandlerId.Should().NotBe(0);
	}

	[Fact]
	public async Task StatusBar_AudioToggleClick_TogglesMuteStateAndUpdatesRenderedControl()
	{
		using var dashboard = new DashboardRenderFixture();
		await dashboard.RenderAsync();
		var toggle = dashboard.FindAudioToggleButton();

		await dashboard.ClickAsync(toggle);

		dashboard.Game.IsAudioMuted.Should().BeTrue();
		dashboard.AudioPlayback.MuteRequests.Should().Equal(true);

		var updatedToggle = dashboard.FindAudioToggleButton();
		updatedToggle.Attributes.Should().Contain("aria-pressed", "true");
		updatedToggle.Attributes.Should().Contain("aria-label", ClientStrings.Dashboard_AudioUnmute);
		updatedToggle.Attributes.Should().Contain("title", ClientStrings.Dashboard_AudioUnmute);
		updatedToggle.Attributes.Should().Contain("class", "ww-icon-button ww-audio-toggle ww-audio-toggle--muted");
		updatedToggle.TextContent.Should().BeEmpty();
	}

	private sealed class DashboardRenderFixture : IDisposable
	{
		private readonly DashboardTestRenderer _renderer;
		private readonly int _rootComponentId;
		private readonly ServiceProvider _serviceProvider;

		public DashboardRenderFixture()
		{
			AudioPlayback = new RecordingInstructionAudioPlayback();
			Game = new GameClientManager(
				new GameService(),
				AudioPlayback,
				new InMemoryGameSessionSaveStore());
			Game.StartGame(
				["Ana", "Bruno", "Catarina", "Diana", "Eduardo"],
				[
					MainRoleType.SimpleWerewolf,
					MainRoleType.Seer,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager
				]);

			var services = new ServiceCollection();
			services.AddSingleton(Game);
			services.AddSingleton<IHapticFeedbackService, NoOpHapticFeedbackService>();
			_serviceProvider = services.BuildServiceProvider();

			_renderer = new DashboardTestRenderer(_serviceProvider);
			_rootComponentId = _renderer.AttachRootComponent(typeof(DashboardPage));
		}

		public GameClientManager Game { get; }
		public RecordingInstructionAudioPlayback AudioPlayback { get; }

		public Task RenderAsync() =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.RenderRootAsync(_rootComponentId));

		public Task ClickAsync(ElementSnapshot element) =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.DispatchClickAsync(element.ClickEventHandlerId));

		public ElementSnapshot FindAudioToggleButton()
		{
			var element = EnumerateElements()
				.SingleOrDefault(element =>
					element.Name == "button" &&
			element.Attributes.TryGetValue("class", out var value) &&
			value is string className &&
			className.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("ww-audio-toggle"));

			element.Should().NotBeNull(ClientTestReferences.AssertionReasons.AudioToggleExposedInStatusBar);
			return element!;
		}

		public void Dispose()
		{
			_renderer.Dispose();
			_serviceProvider.Dispose();
		}

		private IEnumerable<ElementSnapshot> EnumerateElements()
		{
			foreach (var componentId in EnumerateComponentIds(_rootComponentId))
			{
				var frames = _renderer.GetFrames(componentId);

				for (var index = 0; index < frames.Count; index++)
				{
					var frame = frames.Array[index];
					if (frame.FrameType != RenderTreeFrameType.Element)
					{
						continue;
					}

					yield return CreateElementSnapshot(frames, index);
				}
			}
		}

		private IEnumerable<int> EnumerateComponentIds(int componentId)
		{
			yield return componentId;

			var frames = _renderer.GetFrames(componentId);
			for (var index = 0; index < frames.Count; index++)
			{
				var frame = frames.Array[index];
				if (frame.FrameType != RenderTreeFrameType.Component)
				{
					continue;
				}

				foreach (var childComponentId in EnumerateComponentIds(frame.ComponentId))
				{
					yield return childComponentId;
				}
			}
		}

		private static ElementSnapshot CreateElementSnapshot(
			ArrayRange<RenderTreeFrame> frames,
			int elementIndex)
		{
			var element = frames.Array[elementIndex];
			var attributes = new Dictionary<string, object?>();
			var text = new List<string>();
			var clickEventHandlerId = 0UL;
			var endIndex = elementIndex + element.ElementSubtreeLength;

			for (var index = elementIndex + 1; index < endIndex; index++)
			{
				var frame = frames.Array[index];
				switch (frame.FrameType)
				{
					case RenderTreeFrameType.Attribute:
						attributes[frame.AttributeName] = frame.AttributeValue;
						if (frame.AttributeName == "onclick")
						{
							clickEventHandlerId = frame.AttributeEventHandlerId;
						}
						break;
					case RenderTreeFrameType.Text:
						text.Add(frame.TextContent);
						break;
				}
			}

			return new ElementSnapshot(
				element.ElementName,
				attributes,
				string.Concat(text),
				clickEventHandlerId);
		}
	}

	private sealed class DashboardTestRenderer(IServiceProvider serviceProvider)
		: Renderer(serviceProvider, NullLoggerFactory.Instance)
	{
		public override Microsoft.AspNetCore.Components.Dispatcher Dispatcher { get; } =
			Microsoft.AspNetCore.Components.Dispatcher.CreateDefault();

		public int AttachRootComponent(Type componentType)
		{
			var component = InstantiateComponent(componentType);
			return AssignRootComponentId(component);
		}

		public Task RenderRootAsync(int componentId) => RenderRootComponentAsync(componentId);

		public ArrayRange<RenderTreeFrame> GetFrames(int componentId) =>
			GetCurrentRenderTreeFrames(componentId);

		public Task DispatchClickAsync(ulong eventHandlerId) =>
			DispatchEventAsync(eventHandlerId, default, new MouseEventArgs());

		protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

		protected override void HandleException(Exception exception)
		{
			throw new InvalidOperationException(
				ClientTestReferences.ExceptionMessages.ComponentRenderFailure("dashboard"), exception);
		}
	}

	private sealed record ElementSnapshot(
		string Name,
		IReadOnlyDictionary<string, object?> Attributes,
		string TextContent,
		ulong ClickEventHandlerId);

	private sealed class RecordingInstructionAudioPlayback : IInstructionAudioPlayback
	{
		public bool IsMuted { get; private set; }
		public List<bool> MuteRequests { get; } = [];

		public Task ReconcileAsync(ModeratorInstruction? instruction, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task SetMutedAsync(
			bool isMuted,
			ModeratorInstruction? instruction,
			CancellationToken cancellationToken = default)
		{
			IsMuted = isMuted;
			MuteRequests.Add(isMuted);
			return Task.CompletedTask;
		}
	}

	private sealed class InMemoryGameSessionSaveStore : IGameSessionSaveStore
	{
		private string? _serializedSession;

		public string? Load() => _serializedSession;

		public void Save(string serializedSession)
		{
			_serializedSession = serializedSession;
		}

		public void Clear()
		{
			_serializedSession = null;
		}
	}

	private sealed class NoOpHapticFeedbackService : IHapticFeedbackService
	{
		public void Click()
		{
		}

		public void LongPress()
		{
		}
	}
}
