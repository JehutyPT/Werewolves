using FluentAssertions;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;

#pragma warning disable BL0006

namespace Werewolves.Client.Tests.Components;

public class DashboardPageInteractionTests
{
	[Fact]
	public async Task InitialRender_ShowsConfirmButtonForStartGameInstruction()
	{
		using var fixture = new InteractionFixture();
		await fixture.RenderAsync();

		var confirmButton = fixture.FindButtonByText("Confirmar");
		confirmButton.Should().NotBeNull("the start game confirmation should be shown directly");
		confirmButton!.ClickEventHandlerId.Should().NotBe(0, "the confirm button should have a click handler");
	}

	[Fact]
	public async Task ClickConfirmButton_AdvancesGameToNextInstruction()
	{
		using var fixture = new InteractionFixture();
		await fixture.RenderAsync();

		var instructionBefore = fixture.Game.CurrentInstruction;
		instructionBefore.Should().BeOfType<StartGameConfirmationInstruction>();

		var confirmButton = fixture.FindButtonByText("Confirmar");
		await fixture.ClickAsync(confirmButton!);

		fixture.Game.CurrentInstruction.Should().NotBeSameAs(instructionBefore,
			"clicking confirm should advance to the next instruction");
	}

	[Fact]
	public async Task ClickConfirmButton_WhenHapticFeedbackFails_KeepsDashboardInteractive()
	{
		using var fixture = new InteractionFixture(new ThrowingHaptic());
		await fixture.RenderAsync();

		var instructionBefore = fixture.Game.CurrentInstruction;
		var confirmButton = fixture.FindButtonByText("Confirmar");

		await fixture.ClickAsync(confirmButton!);

		fixture.Game.CurrentInstruction.Should().NotBeSameAs(instructionBefore,
			"haptic feedback is optional and must not block game progression");

		var audioToggle = fixture.FindButtonByText(ClientStrings.Dashboard_AudioMute);
		await fixture.ClickAsync(audioToggle!);

		fixture.Game.IsAudioMuted.Should().BeTrue(
			"dashboard events should remain dispatchable after the game action");

		var rosterTab = fixture.FindButtonByText(ClientStrings.Dashboard_TabRoster);
		await fixture.ClickAsync(rosterTab!);

		var updatedRosterTab = fixture.FindButtonByText(ClientStrings.Dashboard_TabRoster);
		updatedRosterTab!.Attributes.Should().Contain("class", "ww-dashboard-tab ww-dashboard-tab--active");
	}

	[Fact]
	public async Task MultipleGameSteps_AllRenderWithoutException()
	{
		using var fixture = new InteractionFixture();
		await fixture.RenderAsync();

		var steps = new List<string>();
		for (var step = 0; step < 20; step++)
		{
			var instruction = fixture.Game.CurrentInstruction;
			if (instruction is null) break;

			var typeName = instruction.GetType().Name;
			steps.Add($"Step {step}: {typeName}");

			var buttons = fixture.FindAllButtons();
			var buttonLabels = string.Join(", ",
				buttons.Select(b => $"'{b.TextContent}' ({b.ClassName})"));

			buttons.Should().NotBeEmpty(
				$"step {step} ({typeName}) should render buttons. Found: [{buttonLabels}]");

			if (instruction is ConfirmationInstruction)
			{
				var confirm = fixture.FindButtonByText("Confirmar");
				if (confirm is null)
				{
					var reveal = buttons.FirstOrDefault(b =>
						b.ClassName.Contains("ww-btn-primary") && b.ClickEventHandlerId != 0);
					reveal.Should().NotBeNull(
						$"step {step}: ConfirmationInstruction should show a primary action button. " +
						$"Instruction: public='{instruction.PublicAnnouncement}', private='{instruction.PrivateInstruction}'. " +
						$"Buttons: [{buttonLabels}]");
					await fixture.ClickAsync(reveal!);

					confirm = fixture.FindButtonByText("Confirmar");
				}

				if (confirm is not null)
				{
					await fixture.ClickAsync(confirm);
				}
				else
				{
					break;
				}
			}
			else
			{
				break;
			}
		}

		steps.Should().HaveCountGreaterThan(1,
			"the game should have advanced through multiple instructions");
	}

	[Fact]
	public async Task ClickTabButton_SwitchesActiveTab()
	{
		using var fixture = new InteractionFixture();
		await fixture.RenderAsync();

		var rosterTab = fixture.FindButtonByText(ClientStrings.Dashboard_TabRoster);
		rosterTab.Should().NotBeNull("the Roster tab should be rendered");
		rosterTab!.ClickEventHandlerId.Should().NotBe(0, "the tab should have a click handler");

		await fixture.ClickAsync(rosterTab);

		var updatedRosterTab = fixture.FindButtonByText(ClientStrings.Dashboard_TabRoster);
		updatedRosterTab!.Attributes.Should().Contain("class", "ww-dashboard-tab ww-dashboard-tab--active");
	}

	[Fact]
	public async Task AllVisibleButtons_HaveRegisteredClickHandlers()
	{
		using var fixture = new InteractionFixture();
		await fixture.RenderAsync();

		var buttons = fixture.FindAllButtons();
		buttons.Should().NotBeEmpty("the dashboard should render buttons");

		foreach (var button in buttons)
		{
			var hasAnyHandler = button.ClickEventHandlerId != 0
				|| button.HasPointerHandlers
				|| button.IsDisabled;

			hasAnyHandler.Should().BeTrue(
				$"button '{button.TextContent}' (class={button.ClassName}) should have event handlers or be disabled");
		}
	}

	private sealed class InteractionFixture : IDisposable
	{
		private readonly InteractionTestRenderer _renderer;
		private readonly int _rootComponentId;
		private readonly ServiceProvider _serviceProvider;

		public InteractionFixture(IHapticFeedbackService? hapticFeedback = null)
		{
			Game = new GameClientManager(
				new GameService(),
				new NoOpAudioPlayback(),
				new InMemoryStore());
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
			services.AddSingleton(hapticFeedback ?? new NoOpHaptic());
			_serviceProvider = services.BuildServiceProvider();

			_renderer = new InteractionTestRenderer(_serviceProvider);
			_rootComponentId = _renderer.AttachRootComponent(typeof(DashboardPage));
		}

		public GameClientManager Game { get; }

		public Task RenderAsync() =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.RenderRootAsync(_rootComponentId));

		public Task ClickAsync(ButtonSnapshot button) =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.DispatchClickAsync(button.ClickEventHandlerId));

		public ButtonSnapshot? FindButtonByText(string text) =>
			FindAllButtons().FirstOrDefault(b =>
				b.TextContent.Equals(text, StringComparison.OrdinalIgnoreCase));

		public List<ButtonSnapshot> FindAllButtons()
		{
			var buttons = new List<ButtonSnapshot>();
			foreach (var componentId in EnumerateComponentIds(_rootComponentId))
			{
				var frames = _renderer.GetFrames(componentId);
				for (var index = 0; index < frames.Count; index++)
				{
					var frame = frames.Array[index];
					if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != "button")
						continue;

					buttons.Add(CreateButtonSnapshot(frames, index));
				}
			}
			return buttons;
		}

		public void Dispose()
		{
			_renderer.Dispose();
			_serviceProvider.Dispose();
		}

		private IEnumerable<int> EnumerateComponentIds(int componentId)
		{
			yield return componentId;
			var frames = _renderer.GetFrames(componentId);
			for (var index = 0; index < frames.Count; index++)
			{
				var frame = frames.Array[index];
				if (frame.FrameType != RenderTreeFrameType.Component)
					continue;
				foreach (var child in EnumerateComponentIds(frame.ComponentId))
					yield return child;
			}
		}

		private static ButtonSnapshot CreateButtonSnapshot(ArrayRange<RenderTreeFrame> frames, int elementIndex)
		{
			var element = frames.Array[elementIndex];
			var attributes = new Dictionary<string, object?>();
			var text = new List<string>();
			var clickHandlerId = 0UL;
			var hasPointerHandlers = false;
			var isDisabled = false;
			var endIndex = elementIndex + element.ElementSubtreeLength;

			for (var index = elementIndex + 1; index < endIndex; index++)
			{
				var frame = frames.Array[index];
				switch (frame.FrameType)
				{
					case RenderTreeFrameType.Attribute:
						attributes[frame.AttributeName] = frame.AttributeValue;
						if (frame.AttributeName == "onclick")
							clickHandlerId = frame.AttributeEventHandlerId;
						if (frame.AttributeName is "onpointerdown" or "onpointerup")
							hasPointerHandlers = true;
						if (frame.AttributeName == "disabled" && frame.AttributeValue is true)
							isDisabled = true;
						break;
					case RenderTreeFrameType.Text:
						text.Add(frame.TextContent);
						break;
				}
			}

			var className = attributes.TryGetValue("class", out var cls) && cls is string s ? s : "";
			return new ButtonSnapshot(
				className,
				string.Concat(text),
				clickHandlerId,
				hasPointerHandlers,
				isDisabled,
				attributes);
		}
	}

	internal sealed record ButtonSnapshot(
		string ClassName,
		string TextContent,
		ulong ClickEventHandlerId,
		bool HasPointerHandlers,
		bool IsDisabled,
		IReadOnlyDictionary<string, object?> Attributes);

	private sealed class InteractionTestRenderer(IServiceProvider serviceProvider)
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
				"Unhandled exception during dashboard rendering or event dispatch.", exception);
		}
	}

	private sealed class NoOpHaptic : IHapticFeedbackService
	{
		public void Click() { }
	}

	private sealed class ThrowingHaptic : IHapticFeedbackService
	{
		public void Click() => throw new InvalidOperationException("Synthetic haptic failure.");
	}

	private sealed class NoOpAudioPlayback : IInstructionAudioPlayback
	{
		public bool IsMuted { get; private set; }
		public Task ReconcileAsync(ModeratorInstruction? instruction, CancellationToken ct = default) => Task.CompletedTask;
		public Task SetMutedAsync(bool muted, ModeratorInstruction? instruction, CancellationToken ct = default)
		{
			IsMuted = muted;
			return Task.CompletedTask;
		}
	}

	private sealed class InMemoryStore : IGameSessionSaveStore
	{
		private string? _data;
		public string? Load() => _data;
		public void Save(string data) => _data = data;
		public void Clear() => _data = null;
	}
}
